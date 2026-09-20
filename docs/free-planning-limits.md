# Free planning limits and retired planning features

- Trip setup retains the full chosen date range. Free editing and recommendation saves are limited to trip days 1–3; later days remain visible with an upgrade prompt.
- Free Assistant searches and Improve Day have separate limits of three useful results. Failed/empty results cancel their reservation. Paid access retains the shared daily limit.
- Improve Day leases use the server-owned `full-day:` operation prefix. Completed leases are the durable free improvement counter; do not purge them while a trial exists. Existing Assistant usage is retained; historical unclassified usage cannot safely be reassigned.
- The request operation ID is optional for older apps and is bound to a hash of its input. No schema migration is needed.
- Improve Day checks the editing window before navigation, before generation and after saving; an expired trial opens the paywall. API checks remain authoritative.
- Route/proposal mobile pages, navigation and actions were removed. Existing route/proposal endpoints return 410 once they reach the controller (access middleware may reject earlier). Historical itinerary data and schema are preserved.
- Generated cards enter with a 220 ms fade/translation; motion-disabled devices skip it. Today still receives one completed batch to prevent partial/stale itinerary snapshots.

Deploy the API before distributing the updated app. Validate the free day-four prompt, fourth attempt for each independent quota, expired editing, and animated card loading on a physical device. Automated tests do not establish physical-device UX or PostgreSQL concurrency.
