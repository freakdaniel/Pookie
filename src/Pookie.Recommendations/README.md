# Local wave recommendations

The module contains the wave selection constraints, listening tracker, incremental
ranker, audio encoder and local journal. It takes public `WaveTrack` projections;
it does not reference SoundCloud, Electron, native UI, or audio playback.
`Pookie.App.Home.WaveTaste` converts transport models and preserves the original
track instances returned to the player. Learned predictions now replace the old
preference coefficients in the live queue. Existing account JSON profiles remain readable and are not converted
into fabricated listening events.

`ListeningObservation` records one playback identity, application-session identity,
start/end times, actual listened seconds, source, wave mode, end reason and algorithm
version. Intentional early skips of Wave and automatic continuation recommendations
teach a weak negative. Continuation has its own `Autoplay` listening source;
manual entries, errors and Stop never teach an implicit negative. Explicit selection of a track is distinguished from advancing the
queue or starting a wave/collection; source and selection are independent fields.
Natural end, stop, shutdown, account change and playback errors
are separate reasons. Like, unlike, block and reset-blocks are separate events.

On the first real account event, the app starts `ListeningJournal` at
`%LOCALAPPDATA%/Pookie/Wave/events.sqlite3` (or the corresponding application data
directory on other platforms). A single background consumer uses a bounded
256-entry channel and transactions of at most 64 events. An append completes after
commit; `(account_id, event_id)` makes retries idempotent. Shutdown waits for pending
appends before closing the writer. Failures propagate to callers and application
logs. SQLite operations run on the worker, not on UI or audio callbacks.

Raw listening retention is 50,000 events and 90 days per account. Raw preference
actions keep the last 5,000 events per account; current like/block flags live in a
separate table. Both limits apply after maintenance, including short-session close.
Events are learned and checkpoints commit in the same transaction before pruning.
Retries are deduplicated against raw rows and a separate receipt window (90 days,
100,000 receipts globally), including recent events already removed by compaction.
Old inactive unliked/unblocked rows are discarded, not current explicit preferences.

The database budget is 128 MiB via SQLite max_page_count. At 80% used space, the
writer removes processed raw observations while preserving model checkpoints and
explicit state. New databases use incremental auto-vacuum; old v1 databases receive
a one-time background VACUUM to enable it. Regular reclamation is 256 pages per
maintenance pass. WAL auto-checkpoints run at 256 pages, with a 4 MiB retained WAL
target. A stuck reader preventing truncation beyond 16 MiB causes an explicit write
error instead of indefinite WAL growth. Limits do not authorize deletion of current
explicit preferences to make room. Maintenance runs on writes/close, not an idle timer.

The audio-vector cache keeps 10,000 vectors globally, evicting vectors unused for
180 days. Each vector is 200 float32 values (800 bytes), stored as a BLOB with the
encoder/version/checksum identity. SQL computes exact cosine similarity using a
deterministic managed function and filters by account, model and block state. This
is bounded exact search, not an ANN index or a separate vector server.
The newer SQLitePCLRaw bundle is explicit because the older bundle pulled by
Microsoft.Data.Sqlite 10.0.0 contains a known vulnerable SQLite build.

## Audio inference and learning

MSD MusiCNN's frozen embedding head is bundled as a 3.1 MB ONNX model. The decoder
worker captures at most nine seconds of mono 16 kHz PCM from already decoded audio,
before playback gain. This playback tap makes no additional media downloads; PCM is not saved.
The sample is consumed after nine listened seconds so later uncommitted recommendations
can use it during playback, with end-of-listening capture as a fallback. Cached
compatible sound vectors avoid repeat inference, even when their mood metadata is
absent or obsolete. Production inference does not load the mood classifier.
The journal permits at most four pending audio jobs; excess samples defer analysis
but their events still commit. Inference has one CPU thread, no GPU and disabled
thread spinning. Inference sessions and batch PCM references are released after use.

Frozen neural weights are not retrained. A ten-feature adaptive logistic ranker
learns the user's reactions, bounded artist/genre affinities, up to three long-term
positive audio centers, up to three negative audio centers and a recent positive center.
The live application records all reactions in the general portrait (`WaveMood.Auto`);
it exposes only Balance, Favorites and Discoveries. Existing experimental mood
contexts remain readable but do not participate in selection or new app events.
Early skips start weak and gain confidence across different sessions;
skipping a liked track affects the session, not the general portrait. Session
effects do not carry into a new wave session. Explicit rejection
never also becomes a positive listening example.
Versioned features/predictions are captured before listening when a model snapshot
is available. At feedback time, training features are recomputed from the current
checkpoint and any audio obtained during that play, before its reaction updates
affinities or audio centers. Captured liked state still protects an early skip of
a liked track. The original selection-time prediction remains immutable and is
used for prequential error; the enriched training prediction is not substituted
into that metric. Repeated implicit plays
have at most three training updates per track/day within the bounded quota cache.
Unlike, unblock and reset are state changes, not negative listening labels. Weights and the event
cursor persist together; restarting does not replay previously learned events.

The learned ranker chooses the live queue and candidate-fetch seeds. SoundCloud
relationships supply candidates. Before 24 local learned reactions, a bounded
cold-start prior uses real likes, confirmed good plays, artist/genre affinity and
relationships to those positive seeds. The prior fades linearly to zero as the
personal model learns; it writes no fabricated events and does not mutate model
checkpoints. Without positive evidence, selection uses the learner's neutral
predictions and small random exploration. Removed likes and blocked seeds stop
contributing to the prior. Discoveries fetch personal seed candidates before
starting, even when Home already has enough tracks to fill a queue.
Modes, blocked works, reserved tracks, repetition cooldown and
artist diversity remain selection constraints. The live wave commits only three upcoming
recommendations. Published training checkpoints update the scores/candidate pool without
replacing those displayed entries. Advancing fills a free slot from the latest scores,
preserving remaining occurrence identities and the familiar/new balance across single-slot
refills. Explicit mode changes or rejection rebuild this horizon, retaining surviving
identities and the current/manual/forward entries; old refill responses are rejected.
Vector-search neighbors now enter the candidate pool with their cached audio
features. General taste centers are searched in one SIMD SQL pass, with up to 40
matches and a four-account cache invalidated by model/vector/preference changes.
Audio indexing grows from played tracks and bounded background candidate previews.
While Balance/Discoveries is playing, upcoming unclassified candidates are decoded
sequentially: at most three per batch, 20 seconds between batch starts, six seconds
per candidate, twelve seconds per batch and 24 attempts per wave session. All
batches share a 12 MiB budget for consumed CDN response bodies. No output device,
website playback, listening event, reward or SoundCloud history write is created.
Protected candidates are skipped and classified during normal playback instead.
Pause, playback preparation, Favorites, account changes and shutdown cancel the
batch; its decoder must finish disposal before another batch can start. Selection
never awaits a preview. Cached sound affects new recommendations beyond the committed
horizon, keeping displayed/current/manual/forward entries. A missing embedding leaves a track eligible;
there are no mood thresholds or unknown-audio quota. Existing experimental mood
centers are not searched by the base candidate retrieval.
Windows protected playback uses InfiniFrame's system WebView2 runtime. It opts
into an AudioWorklet tap before gain/master volume.
It downsamples to 16 kHz mono and transmits at most nine seconds in 1024-sample
chunks, bound to the current playback ID. Pauses and buffering suspend capture.
The normal browser EME session continues to own licenses/decryption. If a browser
does not expose PCM or returns silence, no vector is created; playback and
metadata learning continue.

The retained experimental 411 KB ONNX graph combines MTG's relaxed, party, sad and aggressive classifiers
with DEAM arousal/valence regression. It consumes raw per-patch MusiCNN outputs
before L2 normalisation. It is used only when explicitly requested by diagnostics
or classifier tests, not by the production listening journal. Energy uses arousal, with relaxed/aggressive evidence and a small party
contribution. Arousal and valence are normalised from [1,9] to [0,1]. These are
estimates of activation and emotional tone, not calibrated mood probabilities or BPM.
Mood cache version `musicnn-moods-arousal-v2` is preserved for diagnostic comparisons.
Changing those scores cannot reorder or exclude tracks in the base wave. Future
mood experiments must be validated independently of recommendation preference;
the classifier observes a short excerpt, not the user's emotional state or a
guaranteed whole-song label. `ListeningJournal(analyzeMoods: true)` explicitly
enables diagnostic mood-cache refresh; the default analyzes sound embeddings only.

Player Like and Dislike icons are mutually exclusive. Dislike works outside Wave;
only Wave advances automatically. Repeating Dislike cancels the work's exclusion,
and a later Like also restores it. Unblock/reset restore eligibility; they do not
reconstruct a counterfactual model with historical feedback removed.

## Quality measurements

`learned-base-v2` observations record whether a work was familiar BEFORE listening.
Confirmed like/unlike actions carry their playback identity, so a reaction cannot
be attributed to another play of the same track. Preview jobs write only vectors.
`WaveQualityReportReader` opens SQLite read-only and aggregates retained automatic
wave plays by account, algorithm version, mode and familiar/new/unknown cohort.
It reports early skips, listening >=60% (at least 15 seconds, excluding rejection),
natural ends, explicit rejections, likes remaining after unlike, unique tracks/
artists and the fraction whose prediction had audio features. Short interrupted
plays (shutdown, pause/stop, playback error, account change, Previous) do not count
as failures; they remain visible in the interrupted count. A confirmed like or
sufficient listening makes such an observation evaluable. Like rates for older
versions are null because their actions did not carry a playback identity.

Use `dotnet run --project tests/Pookie.App.SmokeTests -- --wave-quality-report 7`
to print JSON for the last seven days (1-90 days supported). This makes no network
requests and does not train, migrate or prune the journal. Reports cover retained
observations/feedback, not deleted history, and are descriptive comparisons rather
than a randomized A/B experiment or proof of improved subjective recommendations.

On the development machine a synthetic three-second sample measured approximately
181 ms cold (both models loaded) and 58 ms warm, including mood classification. This is a local benchmark, not
a latency guarantee or a recommendation-quality result.
Exact SQL search of 10,000 vectors measured about 47 ms cold and 29 ms warm using
SIMD dot products. Database search and training stay on the journal worker;
live selection evaluates the small model from its published in-memory snapshot.
The C# frontend is checked against independent NumPy inputs; ONNX conversion is checked numerically against
the original TensorFlow model. Model attribution and CC BY-NC-SA 4.0 terms are in
../../REUSE.toml; the full license is in
../../licenses/CC-BY-NC-SA-4.0.txt. Python/TensorFlow are optional model-conversion
tools, described in ../../tools/audio-embeddings/README.md. They are not invoked
by build, run, tests, publication or inference. No Python interpreter is embedded.

Checks: `dotnet test tests/Pookie.Playback.Tests` covers ranking/queue regressions,
observation semantics, retries, isolation, retention, disk reclamation, budget pressure,
v1 migration, model checkpoints, real neural inference, vector search, failure propagation and legacy
profiles. The application `--home-ui-smoke-test` additionally verifies native
preference actions and observations reaching SQLite across an account switch.
`--wave-journal-ui-smoke-test` runs that integration independently of Home layout.

Availability is separate from taste: fresh metadata confirming a missing track
excludes its ID for 30 days, access restrictions for one hour. These deadlines are
persisted with the account profile and survive stale recommendation imports and
resetting dislikes. Neither status creates a skip/dislike observation. Expired
CDN URLs, verification challenges, rate limits and network errors do not establish
that a track is missing. Successful explicit playback clears an exclusion.
