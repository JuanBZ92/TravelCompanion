# Android diagnostic sample analysis

Capture review-build `YukuPerformance` logs separately for each controlled scenario. This analyzer only reads already captured files; it does not access a device or application:

```powershell
./docs/tools/analyze-native-performance.ps1 `
  -LogPath artifacts/review/performance.log `
  -Scenario online-250-entries `
  -BuildConfiguration Release -DeviceModel SM_S948B -AppVersion 1.0.0 -AppBuild 140 `
  -OutputDirectory artifacts/review/performance-analysis
```

Pass chronological, non-overlapping log files from one scenario. The analyzer accepts logcat lines tagged `YukuPerformance` and plain DiagnosticJournal JSONL. It keeps only the six measured Journal/Expense/photo/cleanup/timer event names and a strict whitelist of numeric fields. It never copies other log tags, exception records, payloads, URLs or source paths. `raw.jsonl` contains filtered metadata; `samples.csv` marks warmup/measured/extra rows. Missing or invalid numbers remain null (blank cells in CSV), including absent request/read counters on the timer.

For each operation, the first five events are warmups; the next thirty are the selected samples. Extras remain available in CSV/raw but do not affect the summary. `summary.json` reports coverage and nearest-rank percentiles. Certified `P50`/`P95` are null until thirty valid values are available for that metric; `ObservedP50`/`ObservedP95` describe partial captures and must not be presented as meeting the required sample count. Empty operations remain in the report with zero sample counts and null metrics. A genuine recorded zero duration/counter stays zero.

High-resolution elapsed ticks/frequency take precedence over integer milliseconds. Timings cover the instrumented operation interval, not whole-screen responsiveness or CPU usage. Most allocation/request/read counters cover the process interval and include overlapping background work. The schedule timer and initial preview cleanup use thread allocation counters instead. Missing cancellation/success metadata does not imply a successful operation. Keep native observations separate from the host comparison and from app startup measurements.

Validate the analyzer without a device using `./docs/tools/test-native-performance-analysis.ps1`. Synthetic fixtures cover five warmups, thirty measurements, extra samples, tick precision, UTC preservation, absent versus recorded zero counters, incomplete captures, malformed records and the metadata whitelist. Logs and assertions are retained under `artifacts/native-performance-analysis-tests` by default.
