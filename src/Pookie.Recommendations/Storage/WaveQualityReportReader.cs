using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Pookie.Recommendations.Storage;

public sealed record WaveQualityRow(long AccountId, string AlgorithmVersion, WaveMode Mode, bool? FamiliarAtSelection,
    int Plays, int Judged, int Interrupted, int EarlySkips, int LongListens, int NaturalEnds, int Rejections,
    int? Likes, int DistinctTracks, int DistinctArtists, int ScoredWithAudio)
{
    public double? EarlySkipRate => Judged == 0 ? null : EarlySkips / (double)Judged;
    public double? LongListenRate => Judged == 0 ? null : LongListens / (double)Judged;
    public double? LikeRate => Judged == 0 || !Likes.HasValue ? null : Likes / (double)Judged;
    public double? AudioCoverage => Plays == 0 ? null : ScoredWithAudio / (double)Plays;
}

// Aggregate retained, actual queue plays. Opening a report never trains, prunes or migrates the journal.
public static class WaveQualityReportReader
{
    public static WaveQualityRow[] Read(string path, DateTimeOffset from, DateTimeOffset until)
    {
        if (until < from) throw new ArgumentException("Invalid quality-report interval.");
        if (!File.Exists(path)) return [];
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder {
            DataSource = Path.GetFullPath(path), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            WITH feedback AS (
                SELECT account_id,json_extract(payload,'$.playback_id') playback_id,kind,
                    row_number() OVER (PARTITION BY account_id,json_extract(payload,'$.playback_id') ORDER BY sequence DESC) latest
                FROM events WHERE kind IN (1,2) AND occurred_at <= $until AND json_extract(payload,'$.playback_id') IS NOT NULL
            ), plays AS (
                SELECT account_id,json_extract(payload,'$.listening.playback_id') playback_id,
                    coalesce(json_extract(payload,'$.listening.algorithm_version'),'unknown') version,
                    json_extract(payload,'$.listening.mode') mode,
                    json_extract(payload,'$.listening.familiar_at_selection') familiar,
                    json_extract(payload,'$.listening.end_reason') reason,
                    json_extract(payload,'$.listening.listened_seconds') seconds,
                    json_extract(payload,'$.listening.track.duration') / 1000.0 duration,
                    json_extract(payload,'$.listening.track.id') track,
                    lower(trim(json_extract(payload,'$.listening.track.artist'))) artist,
                    coalesce(json_extract(payload,'$.listening.features.values[8]'),0) audio
                FROM events WHERE kind=0 AND occurred_at >= $from AND occurred_at <= $until
                    AND json_extract(payload,'$.listening.source')=0
                    AND json_extract(payload,'$.listening.selection')=2
            ), outcomes AS (
                SELECT p.*,coalesce(f.kind,0)=1 liked,
                    reason<>4 AND seconds>=15 AND duration>0 AND seconds>=duration*.6 long_listen
                FROM plays p LEFT JOIN feedback f ON f.account_id=p.account_id AND f.playback_id=p.playback_id AND f.latest=1
            ), judged AS (
                SELECT *,reason IN (0,1,2,4) OR long_listen OR liked evaluated FROM outcomes
            )
            SELECT account_id,version,mode,familiar,count(*),sum(evaluated),sum(NOT evaluated),
                sum(reason=2 AND seconds<min(20,duration*.25)),sum(long_listen),sum(reason=1),sum(reason=4),
                CASE WHEN version='learned-base-v2' THEN sum(liked) ELSE NULL END,
                count(DISTINCT track),count(DISTINCT nullif(artist,'')),sum(audio>0)
            FROM judged GROUP BY account_id,version,mode,familiar ORDER BY account_id,version,mode,familiar;
            """;
        command.Parameters.AddWithValue("$from", from.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$until", until.ToUniversalTime().ToString("O"));
        using var reader = command.ExecuteReader();
        var result = new List<WaveQualityRow>();
        while (reader.Read()) result.Add(new(reader.GetInt64(0), reader.GetString(1), (WaveMode)reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetBoolean(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6),
            reader.GetInt32(7), reader.GetInt32(8), reader.GetInt32(9), reader.GetInt32(10), reader.IsDBNull(11) ? null : reader.GetInt32(11),
            reader.GetInt32(12), reader.GetInt32(13), reader.GetInt32(14)));
        return result.ToArray();
    }
    public static string ToJson(WaveQualityRow[] rows) => JsonSerializer.Serialize(rows, JournalJson.Default.WaveQualityRowArray);
}
