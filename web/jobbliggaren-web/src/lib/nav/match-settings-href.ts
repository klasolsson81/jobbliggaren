/**
 * The canonical route the "set up matching" nudge links to.
 *
 * <para/> It lives in `lib/` rather than being exported from `company-watch-row.tsx`, where it was a
 * module-local const: that file is `"use client"`, and every export of a client module reaches a
 * Server Component as a client REFERENCE, not as its value — a server-side `href={MATCH_SETTINGS_HREF}`
 * would throw rather than render. A plain module is importable from both sides.
 *
 * <para/> Four other consumers (`job-ad-match-section`, `watch-filter-dialog`, `jobb-results-toolbar`,
 * `cv-complete-guide`) still inline the literal. Converting them is a separate change-reason and is
 * deliberately not done here.
 */
export const MATCH_SETTINGS_HREF = "/installningar#matchning";
