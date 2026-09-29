# Compact itinerary and guided day review

## Implementation checklist

- The day selector precedes the compact action toolbar, next activity and hotel.
  The large city/date/Japanese header and D1/D2 labels are removed. Date, city,
  selection and Free locks remain visible.
- The itinerary keeps one warning card, without an inline conflict list. Both
  the card and its accessible arrow open the selected day's review.
- The dedicated page shows one issue at a time, its affected plans, exact dates
  and times, saved periods and time zones. Previous/next do not resolve an issue.
- Travelers choose the plan to edit. Ownership and Free permissions remain in
  force; other plans open their details with a read-only explanation.
- Save and Cancel return to the guided page when opened from that flow. Other
  editor entry points retain their existing destinations. System Back and the
  review toolbar return to the itinerary with the selected date.
- Returning recomputes the review from itinerary items. A stable issue key keeps
  the current issue after a time change; resolved issues advance to the next
  remaining issue. No automatic time or period changes are made.
- Trip-wide issue actions open the same guided page at the correct date/issue.
- Cached data is explicitly marked. Refresh remains available even when the
  cached review has no conflicts. Editing from the guided page requires a fresh
  online schedule. Session changes cancel loading and hide previous content.
- The existing editor retains its error handling for network and revision
  conflicts; unsuccessful saves do not mark any issue as resolved.
- No backend contracts or database migrations changed.

## Verification

190 mobile logic tests and 13 schedule review analyzer tests passed before
release. The cursor tests cover reordering, updated descriptions, multiple issue
types involving the same plans, resolved issues and an empty result. Android
Release compiles. Physical navigation, Free/3333/3334, enlarged text and screen
reader checks remain pending because no Android device was detected. Compilation
does not replace those checks.

Release 99 also includes the requested compact expense screens and trip-wide
review styling. These earlier local changes are preserved in this release.
