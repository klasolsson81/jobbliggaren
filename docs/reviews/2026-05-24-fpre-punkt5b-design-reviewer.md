# Design-reviewer — F-Pre Punkt 5b Gäst-mode utbyggnad

**Datum:** 2026-05-24
**Agent:** design-reviewer (agentId `ad6306e74bab2d0bd`)
**Status (initial):** NEEDS_REWORK (3 Major + 6 Minor)
**Status (post-fix):** APPROVED — alla 3 Major + relevanta Minor in-block-fixade

**FAS-DEFERRAL-MANIFEST (intryckt):** Rond utförd PRE-screenshot — statisk kod/paritets-rond mot DESIGN.md + jobbpilot-design-skills. Rendered-veto F4-F7-disciplin EJ tillämplig på initial rond. Post-deploy rendered-rond med separat FAS-DEFERRAL-MANIFEST-prefix kvarstår som öppen disciplin.

## Summary (post-fix)

**0 Block / 0 Major / 3 Minor (informativa) + 5 Praise**

## Resolution

### M1 — Status-pill mapping driftar från live
**Resolved:** `guest-application-detail.tsx` mappar nu `GuestApplicationStatus → ApplicationStatus` (`GUEST_TO_LIVE_STATUS`-konst) + använder `getStatusPillClass()` + `getStatusLabel()` från `lib/applications/status`. Rejected = danger (röd), Submitted = brand (navy), Interview = warning etc — identisk färgkodning live/gäst. Synk-disciplin per memory `project_crossref_badge_status` säkerställd.

### M2 — `<GuestJobAdCard>` rå source-literal
**Resolved:** `getJobSourceLabel(jobAd.source)` används istället för raw enum-värde. "Manual" → "Egen" via single source of truth (`lib/job-ads/status.ts`).

### M3 — Notiser-strukturen drifter från live `<NoticeList>`
**Resolved:** `guest-oversikt-page.tsx` refaktorerad att använda `<NoticeList>` + `NoticeData[]`. Action- vs info-grupp-rubriker ("Kräver åtgärd" / "Information") rendererade. Dismiss-knapp-kolumn intakt (state via localStorage — gäst-disciplin OK, ingen BE-mutation). Markup, ARIA, 6-kolumn-grid speglar live `(app)/oversikt` exakt.

### m1 — `source: "Manuell"` i mock
**Resolved:** Mock-data använder nu `"Manual"` (enum-värdet); `getSourceLabel` slår upp etiketten "Manuellt" via single source of truth (`lib/applications/status.ts`).

### m5 — STAMP_DATE inkonsistent med frozen TodayCard
**Resolved:** STAMP_DATE härleds från `GUEST_MOCK_REF_DATE` — hela demoöversikten är konsekvent frozen.

### m6 — Saknad SECTION-rubrik i GuestApplicationDetail
**Resolved:** "Om exempelansökningar"-rubrik tillagd med `SECTION_LABEL_STYLE` (typografisk paritet med live).

### m2, m3, m4 (informativa, ej fixade)
- m2 (disabled-CSS-tokens): polish, dark-mode-paritets-trade — kan adresseras nästa CSS-touch
- m3 (`<button disabled aria-disabled>` redundans): icke-blockerande, defense-in-depth-polish
- m4 (TodayCard googleSynced=false-copy): medveten paritets-trade

## Praise (utvalda)

- P1: `.jp-job`-CSS-chassi återanvänt från live (HANDOVER §5.3) — exakt rätt DRY
- P2: Intercepting-routes-paritet med live, ADR 0053-mönster en-till-en
- P3: Soft/hard-nav-symmetri (delade länkar funkar utan demo-context-tap)
- P4: Civic-utility-copy mönsterhantverk
- P5: A11y-grundläggning solid (aria-current, aria-label, skip-link, aria-describedby)

## Post-screenshot follow-up

Design-reviewer redo för rendered-rond med FAS-DEFERRAL-MANIFEST-prefix om visual-verify post-deploy avslöjar rendering-avvikelse från statisk analys.
