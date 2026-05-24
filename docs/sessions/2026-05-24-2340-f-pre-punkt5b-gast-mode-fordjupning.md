---
session: F-Pre Punkt 5b — Gäst-mode-fördjupning (post-deploy-feedback v0.2.70-dev)
datum: 2026-05-24
slug: f-pre-punkt5b-gast-mode-fordjupning
status: levererad
commits:
  - 65b6111 feat(web): F-Pre 5b commit 1 — gäst-jobb mock-sida + nav-länk + DEMO-banner
  - 08ff285 feat(web): F-Pre 5b commit 2 — gäst-modal-paritet (parallel-routes + interception)
  - a9b4941 feat(web): F-Pre 5b commit 3 — gäst-översikt TodayCard + utbyggd summary + fler notiser
  - 460192c fix(web): F-Pre 5b review-fix-batch — design-M1/M2/M3 + code-min1/min2 + sec-m-1
tags:
  - v0.2.71-dev (på `460192c`)
---

# F-Pre Punkt 5b — Gäst-mode-fördjupning (post-deploy-feedback v0.2.70-dev)

## Sammanfattning

Klas post-deploy-test av `v0.2.70-dev` (gäst-mode-första-leveransen från
F-Pre Punkt 5, HEAD `6db0c26`) gav tre konkreta feedback-punkter: "för
liten" (gäst-översikt saknade TodayCard + utbyggd summary), "saknar
Jobb-sida" (`/gast/jobb` var konverterings-CTA, inte mockdata-jobblista),
"vill ha modaler precis som live" (ansökningar + jobb klickbara → intercept).
Direkt-session efter F-Pre Punkt 5 (sekventiellt, ej parallellt med kommande
logo-arbete).

Fyra commits pushade på `origin/main`:

1. `65b6111` — gäst-jobb mock-sida + nav-länk + DEMO-banner (CTO Beslut
   2/3/4: ersätt CTA mot mockdata-jobb-sida + nav-länk tillbaka + banner)
2. `08ff285` — gäst-modal-paritet (CTO Beslut 1: paritet med ADR 0053 —
   `@modal/(.)ansokningar/[id]` + `@modal/(.)jobb/[id]` + fullsidor)
3. `a9b4941` — gäst-översikt TodayCard + utbyggd summary + fler notiser
   (CTO Beslut 5 Variant α — Klas "för liten"-feedback)
4. `460192c` — review-fix-batch (in-block-fix per §9.6: status-pill-paritet,
   source-label, NoticeList-reuse, ref-date-konsolidering, headless-h1-fix)

Tag `v0.2.71-dev` på `460192c` → deploy-dev-workflow triggad. Inga
BE-ändringar, inga migrations, inga nya dependencies.

## Mål

1. CTO-rond på utbyggnaden — Beslut 1-5 över alla tre Klas-feedback-punkter.
2. Implementera mockdata-jobb-sida + nav-länk + DEMO-banner.
3. Modal-paritet med ADR 0053 via Next.js parallel-routes + interception
   för `/gast/ansokningar/[id]` + `/gast/jobb/[id]` (intercepting + fullsidor).
4. TodayCard + utbyggd summary + fler notiser på `/gast/oversikt`.
5. Reviews → in-block-fix per §9.6.

## Fasindelning under sessionen

1. **Discovery** — kartlägga befintlig `/gast/*`-yta från Punkt 5,
   ADR 0053-modal-mönster i autentiserad `/ansokningar/[id]` + `/jobb/[id]`.
2. **STOPP A — CTO-rond Beslut 1-5** (modal-paritet, mockdata-jobb,
   nav-länk, banner, översikt-utbyggnad-variant α/β).
3. **Commit-batch 1** — mockdata-jobb-sida + nav + banner (`65b6111`).
4. **Commit-batch 2** — modal-paritet via parallel-routes (`08ff285`).
5. **Commit-batch 3** — översikt-utbyggnad (`a9b4941`).
6. **Reviews** — cto + code-reviewer + security-auditor + design-reviewer
   parallellt på batchen.
7. **Review-fix-batch** — in-block-fix per §9.6 (`460192c`).
8. **STOPP B — Klas-GO commit + push** (4 separata commits per CLAUDE.md §1.5).
9. **STOPP C — Klas-GO tag-push v0.2.71-dev på `460192c`**.

## Klas-STOPP-val

- **STOPP A:** CTO Beslut 1 (modal-paritet) + 2 (mockdata-jobb-sida) +
  3 (nav-länk) + 4 (banner-text) + 5 Variant α (TodayCard + utbyggd
  summary + fler notiser) — alla GO.
- **STOPP B:** GO commit + push, 4 separata commits.
- **STOPP C:** GO tag-push `v0.2.71-dev` på `460192c`.

## Beslut

### CTO-dom — 5 beslut

`docs/reviews/2026-05-24-fpre-punkt5b-cto.md`. Motivering per beslut:

1. **Modal-paritet:** följ ADR 0053-mönstret 1:1 — `@modal/(.)ansokningar/[id]`
   + `@modal/(.)jobb/[id]` + fullsidor under `(guest)/gast/`-route-grupp.
   Konsistens-värde med autentiserad yta > ny lösning för gäst.
2. **Mockdata-jobb-sida:** ersätt CTA-mot-registrera-sida i `/gast/jobb`
   med faktisk mockdata-lista (paritet med `/jobb`). CTA finns redan via
   demo-banner + welcome-modal.
3. **Nav-länk:** addera `/gast/jobb` i gäst-shell nav-listan (parallell med
   `/gast/oversikt`, `/gast/ansokningar`, `/gast/cv`).
4. **DEMO-banner:** texten "Exempel TEST-data" säkerställs på alla 4 sidor
   inkl. nya `/gast/jobb`.
5. **Översikt Variant α** (TodayCard + utbyggd summary + fler notiser) över
   Variant β (bara fler notiser) — Klas "för liten"-feedback indikerar
   yta-knapphet, inte signal-knapphet. Mockdata-tillåtelse per HANDOVER §0.

### Reviews-fixar in-block (`460192c`)

- **design-M1:** status-pill-paritet med autentiserad `/ansokningar` (samma
  färg-pill-mönster, inte separat gäst-stil).
- **design-M2:** source-label på mockdata-jobblistan ("Källa: Exempel-data")
  för disclosure-konsistens.
- **design-M3:** headless h1 (sr-only heading) på modaler för
  screen-reader-paritet med `/ansokningar/[id]`-fullsida.
- **code-min1:** NoticeList-reuse i gäst-översikt (ingen duplicering av
  notis-komponenten).
- **code-min2:** ref-date-konsolidering — `today_ref` som enskild källa
  istället för spridda `new Date()`-anrop i mockdata-aggregering.
- **sec-m-1:** ingen ny PII-yta exponerad (mockdata-strängar är fiktiva,
  ingen cookie-yta utöver existerande `httpOnly`-cookien från Punkt 5).

## Leverans

### Nya routes (4 nya `/gast/*`)

- `web/jobbpilot-web/src/app/(guest)/gast/ansokningar/[id]/page.tsx`
  — fullsida (deep-link-fallback)
- `web/jobbpilot-web/src/app/(guest)/gast/ansokningar/@modal/(.)ansokningar/[id]/page.tsx`
  — intercepting modal
- `web/jobbpilot-web/src/app/(guest)/gast/jobb/[id]/page.tsx`
  — fullsida
- `web/jobbpilot-web/src/app/(guest)/gast/jobb/@modal/(.)jobb/[id]/page.tsx`
  — intercepting modal

### Ändrade FE-filer

- `web/jobbpilot-web/src/app/(guest)/gast/jobb/page.tsx` — ersatt CTA med
  mockdata-jobblista (paritet med `/jobb`)
- `web/jobbpilot-web/src/app/(guest)/gast/oversikt/page.tsx` — TodayCard
  + utbyggd summary + fler notiser
- `web/jobbpilot-web/src/components/guest/guest-shell.tsx` — nav-länk till
  `/gast/jobb` + `aria-current` på aktiv route
- `web/jobbpilot-web/src/lib/guest/mock-data.ts` — utbyggd med mockdata
  för jobblista + summary-fält + ref-date-konsolidering

## Tester

- vitest **703/703 PASS** (oförändrat — inga befintliga tester rörda,
  inga nya tester lyfta eftersom mockdata-utbyggnaden är FE-orkestrering
  utan ny domän-logik).
- pnpm build PASS — alla 4 nya routes registrerade
  (`/gast/ansokningar/[id]` + `(.)ansokningar/[id]`-modal +
  `/gast/jobb/[id]` + `(.)jobb/[id]`-modal).
- Inga .NET-tester rörda (inga BE-ändringar).

## Commits

1. `65b6111` `feat(web): F-Pre 5b commit 1 — gäst-jobb mock-sida + nav-länk
   + DEMO-banner` (CTO Beslut 2/3/4).
2. `08ff285` `feat(web): F-Pre 5b commit 2 — gäst-modal-paritet
   (parallel-routes + interception)` (CTO Beslut 1, paritet med ADR 0053).
3. `a9b4941` `feat(web): F-Pre 5b commit 3 — gäst-översikt TodayCard +
   utbyggd summary + fler notiser` (CTO Beslut 5 Variant α).
4. `460192c` `fix(web): F-Pre 5b review-fix-batch — design-M1/M2/M3 +
   code-min1/min2 + sec-m-1` (in-block-fix per §9.6).

## Inga TDs lyfta. Inga nya ADRs. Inga BE-ändringar.

Per CLAUDE.md §9.6 — alla fynd pressade mot fas-regeln och fixade in-block.
Modal-paritet följer existerande ADR 0053-mönster, ingen ny ADR motiverad.

## Reviews

- `docs/reviews/2026-05-24-fpre-punkt5b-cto.md` — 5 beslut.
- `docs/reviews/2026-05-24-fpre-punkt5b-code-reviewer.md` — Approved + 2 Minor
  (båda in-block-fixade i `460192c`).
- `docs/reviews/2026-05-24-fpre-punkt5b-security-auditor.md` —
  APPROVED 0/0/0/0/0/2 Minor.
- `docs/reviews/2026-05-24-fpre-punkt5b-design-reviewer.md` —
  NEEDS_REWORK → APPROVED efter 3 Major + 3 Minor in-block.

## Disciplin

- CC gav inte egen rekommendation vid multi-approach-val (CTO decision-maker
  per §9.6).
- Klas-STOPP-kedja A (Beslut 1-5) + B (GO commit + push, 4 separata commits)
  + C (GO tag-push v0.2.71-dev) — alla väntade GO.
- Docs-commit separat från feature-commits (per CLAUDE.md §1.5).
- Direkt-session efter F-Pre Punkt 5 baserat på post-deploy-feedback,
  inte parallell med kommande logo-arbete.

## Pending för Klas

1. Verifiera deploy-dev-workflow stable för `v0.2.71-dev`.
2. Post-deploy visual-verify:
   - `/gast/oversikt` (TodayCard + 4 notiser + utbyggd summary)
   - `/gast/jobb` (mock-lista, paritet med `/jobb`)
   - `/gast/ansokningar` (klickbar → modal-intercept)
   - `/gast/jobb/[id]`-modal (klickbar → intercept)
   - `/gast/cv` (oförändrad från Punkt 5)
3. design-reviewer öppen FAS-DEFERRAL-MANIFEST-prefix för rendered-rond
   post-deploy om något avviker från statisk analys.

## Nästa session

Logo-arbete (separat session per Klas-direktiv), eller kvarstående
val från Punkt 5:

- **Alt 1:** /jobb LIVE-vertikal för gäst (egen session, lägre prio,
  kräver ADR 0005-amendment + drop `.RequireAuthorization()` på
  job-ads-GET + JobAdsPublicReadPolicy + security/architect-rondar).
- **Alt 2:** TD-94 + TD-95 från grunden per F6 P5 P4 svans-direktivet
  2026-05-24 1050.
