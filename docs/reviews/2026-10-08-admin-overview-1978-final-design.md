charter=.claude/agents/design-reviewer.md bytes=6862

## Design-review: Admin overview #1978 (PR #2059)

**Status:** ⛔ Blocked

**Authority:** DESIGN.md §§1.1, 4, 5; ADR 0150; canonical design-a11y skill §9.

**Reviewed HEAD:** `ba06465bb594f08f750741da1cd043974c84b8b1`.

All 31 current overview renders were inspected, alongside relevant access/deletion dialog states and interaction evidence. Dark rendering is skipped because `theme-provider.tsx:37` disables it; both theme token definitions were inspected.

### Blockers / Major / Minor

1. **Blocker — New drill-down links have undersized targets.**

   **File:** `web/jobbliggaren-web/src/components/admin/admin-overview.tsx:435`, `:445`; `src/app/(admin)/admin.css:192`.

   **Current:** All seven secondary registration/status links measure **20 px high at both 1280 and 640 CSS px**, with inline display, zero padding and zero minimum height. Evidence: `C:/tmp/admin-overview-1978-url-repro-extra.log`.

   **Required:** Give these links non-overlapping hit areas of at least **32×32 CSS px**, increasing to **44×44 at ≤768 px**. Cover the other introduced overview links in the same fix: today/total values, including single-digit zero, and attention links at `admin-overview.tsx:463` and `:467`. The attention links sit outside `.jp-adminattention`, so its existing mobile rule does not apply. Measure all introduced targets after correction.

   **Reason:** DESIGN.md §5 and design-a11y §9 explicitly apply these floors to links. Passing axe does not close this measured failure.

2. **Major — Long audit codes collapse the aggregate-reference column.**

   **File:** `web/jobbliggaren-web/src/components/admin/admin-overview.tsx:483`; `src/app/(admin)/admin.css:924`.

   **Current:** `auto auto minmax(0, 1fr)` lets `JobSeeker.FollowedCompanyNotificationConsentUpdated` consume the event track. In `long-stored-event-1280.png` and `-3440.png`, the aggregate reference becomes roughly two characters wide, producing tall vertical text and stretching the neighboring attention card.

   **Required:** Constrain both text tracks so they wrap at readable widths, or stack event and aggregate details beneath the timestamp within the existing Variant A card. Preserve complete event codes and aggregate IDs. Re-render long codes at 1024, 1280 and 3440 px.

   **Reason:** DESIGN.md §§1.1, 4, 5 require readable information density; this is weak composition.

### Praise

- Real zero, unavailable, stale and retained-failure states remain clearly distinguishable.
- Current keyboard, native 200% zoom and shared-dialog evidence support the intended interaction paths.

### Summary

**1 Blocker, 1 Major, 0 Minor.** Route both fixes through the driving session. After the committed batch, request one same-issuer, report-only recheck scoped to these corrections and their rendered evidence under CLAUDE.md §9.6.
