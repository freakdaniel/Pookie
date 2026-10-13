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
version. Only an explicit early skip of a wave recommendation counts as an implicit
negative signal. Explicit selection of a track is distinguished from advancing the
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
before playback gain. There are no additional media downloads and PCM is not saved.
The sample is consumed at the end of listening; cached vectors avoid repeat inference.
The journal permits at most four pending audio jobs; excess samples defer analysis
but their events still commit. Inference has one CPU thread, no GPU and disabled
thread spinning. Inference sessions and batch PCM references are released after use.

Frozen neural weights are not retrained. A ten-feature adaptive logistic ranker
learns the user's reactions, bounded artist/genre affinities, up to three long-term
positive audio centers, up to three negative audio centers and a recent positive center.
Four bounded mood profiles (calm, energetic, melancholic and aggressive) learn
context-specific reactions alongside the general portrait. Explicit mood skips
update that context and current-session direction without punishing the general
taste. Auto skips start weak and gain confidence across different sessions;
skipping a liked track affects the session, not the general portrait. Session
effects do not carry into a new wave session or mood selection. Explicit rejection
never also becomes a positive listening example.
Versioned features/predictions are captured before listening when a model snapshot
is available. Prequential prediction error is recorded. Repeated implicit plays
have at most three training updates per track/day within the bounded quota cache.
Unlike, unblock and reset are state changes, not negative listening labels. Weights and the event
cursor persist together; restarting does not replay previously learned events.

The learned ranker chooses the live queue and candidate-fetch seeds. SoundCloud
relationships supply candidates, not ranking weights. The empty model starts at
equal predictions with small random exploration; there is no fallback to the old
scoring formula. Modes, blocked works, reserved tracks, repetition cooldown and
artist diversity remain selection constraints. Published training checkpoints
rerank upcoming recommendations while preserving the current track and manual queue.
Vector-search neighbors now enter the candidate pool with their cached audio
features. All portrait centers are searched in one SIMD SQL pass, with up to 40
matches and a four-account cache invalidated by model/vector/preference changes.
Audio indexing grows from played tracks; unheard candidates without vectors use
the learned metadata features until they have been analyzed.
Windows protected playback uses InfiniFrame's system WebView2 runtime. It opts
into an AudioWorklet tap before gain/master volume.
It downsamples to 16 kHz mono and transmits at most nine seconds in 1024-sample
chunks, bound to the current playback ID. Pauses and buffering suspend capture.
The normal browser EME session continues to own licenses/decryption. If a browser
does not expose PCM or returns silence, no vector is created; playback and
metadata learning continue.

A 329 KB ONNX model combines MTG's relaxed, party, sad and aggressive classification
heads. It consumes the raw per-patch MusiCNN output before L2 normalisation; the
same audio pass produces both search vectors and independent mood probabilities.
Probabilities are cached in SQLite (version-2 caches migrate without losing
embeddings). Old vectors acquire mood on the next sufficiently long playback,
without background downloads. Mood selection is a soft ranking preference;
unknown mood remains neutral and eligible. The classifier observes a short audio
excerpt, not the user's emotional state or a guaranteed whole-song label.
The energetic option uses the party classifier as an approximation, not BPM.

Player Like and Dislike icons are mutually exclusive. Dislike works outside Wave;
only Wave advances automatically. Repeating Dislike cancels the work's exclusion,
and a later Like also restores it. Unblock/reset restore eligibility; they do not
reconstruct a counterfactual model with historical feedback removed.

On the development machine a synthetic three-second sample measured approximately
181 ms cold (both models loaded) and 58 ms warm, including mood classification. This is a local benchmark, not
a latency guarantee or a recommendation-quality result.
Exact SQL search of 10,000 vectors measured about 47 ms cold and 29 ms warm using
SIMD dot products. Database search and training stay on the journal worker;
live selection evaluates the small model from its published in-memory snapshot.
The C# frontend is checked against independent NumPy inputs; ONNX conversion is checked numerically against
the original TensorFlow model. Model attribution and CC BY-NC-SA 4.0 terms are in
../../NOTICE; the full license is in
../../licenses/CC-BY-NC-SA-4.0.txt. Python/TensorFlow are optional model-conversion
tools, described in ../../tools/audio-embeddings/README.md. They are not invoked
by build, run, tests, publication or inference. No Python interpreter is embedded.

Checks: `dotnet test tests/Pookie.Playback.Tests` covers ranking/queue regressions,
observation semantics, retries, isolation, retention, disk reclamation, budget pressure,
v1 migration, model checkpoints, real neural inference, vector search, failure propagation and legacy
profiles. The application `--home-ui-smoke-test` additionally verifies native
preference actions and observations reaching SQLite across an account switch.
`--wave-journal-ui-smoke-test` runs that integration independently of Home layout.
