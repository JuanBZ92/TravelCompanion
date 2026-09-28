# Local mobile diagnostics

Release and Debug builds retain structured diagnostic events on the device. They
are not uploaded to the API or an analytics service. This is independent of the
optional product analytics setting.

To report a problem, reproduce it, reopen the app if necessary, then select
**Compartir diagnóstico** on the PIN screen or in Cuenta. Free sessions can open
Cuenta through Pase Japón → Opciones de acceso. The app explains the file contents
before opening the platform share sheet. Send the file to support together with
the approximate time and steps that triggered the problem.

## Contents and retention

- UTC timestamps, app version/build, device model and OS at startup.
- Window lifecycle and page class names, including modal pages.
- API area from a fixed allowlist, response status and elapsed milliseconds.
- Managed exception types, HRESULT and up to 16 method frames per exception,
  with at most four exceptions from the inner-exception chain.
- iOS memory warnings when delivered to the application delegate.

Messages, exception Data, source file paths, URLs, query strings, HTTP headers,
request/response bodies, PINs, account IDs and trip content are excluded. Existing
ILogger warnings/errors contribute only severity, numeric event ID and sanitized
exception details; formatted messages are never persisted.

Two rotating JSONL files are kept in app data, at most 256 KiB each. The previous
process history survives restart until rotation. Export creates one bounded cache
file which is overwritten on the next export. Diagnostic writes fail silently
if storage is unavailable; exception hooks do not suppress normal crash behavior.

## Limits

Install a build containing this feature before reproducing the problem. It cannot
recover diagnostics from earlier releases. Native crashes, startup failures before
MAUI initializes, watchdog terminations and memory kills may bypass managed hooks.
The last recorded events provide context, not proof of the cause. Obtain the iOS
`.ips` report and matching archive/dSYM for native crash analysis. A new
`process_start` does not by itself establish that the previous process crashed.

Before shipping iOS, validate on a physical Release/TestFlight build: export from
PIN and Cuenta, reopen after termination and confirm previous events remain, and
inspect the shared file for the expected build and absence of private content.
