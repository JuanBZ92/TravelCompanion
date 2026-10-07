# Local Journal/Expense performance comparison

Run from the repository root with .NET 10 and PowerShell:

```powershell
./docs/tools/measure-journal-host.ps1 -BaselineRef 51c9962 -OutputDirectory artifacts/journal-performance-host-20261006
```

The script extracts the seven production store/cache source files from the baseline using read-only `git show`, stages the current files and compiles both as Release console applications against the same Shared contracts and host adapters. It does not alter the checkout, connect to a backend, use a phone, or touch normal app data. Staged sources, encrypted synthetic cache files, full logs, individual samples and comparison JSON remain in the specified repository output directory. The run manifest records both Git revisions and staged-source SHA-256 fingerprints; the current variant includes uncommitted working-tree changes. Use a fresh output directory per run.

Each scenario has one separately recorded first sample, five warmups and thirty measured repetitions. Percentiles use nearest rank. Fixtures have 250 Journal memories (1,000 characters each), 250 expenses and six deterministic photos with 2 MiB full payloads and 8 KiB thumbnails. Both blocked-save scenarios hold a synthetic HTTP read for 100 ms and then fail it; the harness verifies the concurrent mutation remains pending and retains its exact text after synchronization. No retry/timeout is treated as successful synchronization.

Save latency measures only the local save while sync is in flight. Its allocation, disk-read and request counters cover the **whole overlapping sync plus save** so background work cannot disappear from the comparison. Fixture reset and correctness verification are outside measurement. Thumbnail latency/counters cover six encrypted thumbnail reads; normalization and native image decoding are excluded. File bytes count encrypted file sizes before production reads; API requests count controlled adapter invocations. Writes remain the production atomic encrypted implementation.

`SecureStorage` is an in-memory host adapter; photo normalization returns fixed byte arrays and HTTP uses a controlled signal. These results identify lock contention and combined-image payload overhead on this Windows host. They are **not Android frame times**, native memory measurements, realistic network latency, or app startup measurements. The first sample is before scenario warmup, but is not a cold application start. Device startup and native navigation require separate release measurements.

Review any changed production API signature or cache read primitive before updating adapters. Do not use absolute latency thresholds as CI assertions.
