namespace Pookie.Audio;

internal static class HlsAttributes
{
    internal static Dictionary<string, string> Parse(string line)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var position = 0;
        while (position < line.Length)
        {
            var equal = line.IndexOf('=', position);
            if (equal <= position) throw Invalid();
            var name = line[position..equal].Trim(); position = equal + 1;
            string value;
            if (position < line.Length && line[position] == '"')
            {
                var end = line.IndexOf('"', ++position);
                if (end < 0) throw Invalid();
                value = line[position..end]; position = end + 1;
                if (position < line.Length && line[position] != ',') throw Invalid();
            }
            else
            {
                var end = line.IndexOf(',', position);
                if (end < 0) end = line.Length;
                value = line[position..end].Trim(); position = end;
            }
            if (name.Length == 0 || !result.TryAdd(name, value)) throw Invalid();
            if (position < line.Length) position++;
        }
        return result;
    }
    private static InvalidOperationException Invalid() => new("Некорректные атрибуты HLS.");
}
