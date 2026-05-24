# CTO-dom — F-Pre Punkt 5b Gäst-mode-fördjupning

**Datum:** 2026-05-24
**Agent:** senior-cto-advisor (agentId `a41208f22f042fd7b`)
**Trigger:** Klas post-deploy-feedback efter v0.2.70-dev — gäst-mode "för liten", saknar jobb-sida, saknar modal-paritet
**HEAD vid dom:** `a00a74b` (main)
**Nästa tag:** `v0.2.71-dev`
**Föregående:** `docs/reviews/2026-05-24-fpre-punkt5-cto.md` (Beslut 3 Alt 2 supersederas av 5b Beslut 2)

---

## TL;DR

1. **Modal-strategi:** Variant A — parallel-routes + interception i `(guest)/gast/@modal/(.)*` (paritet med ADR 0053 / live)
2. **`/gast/jobb`:** byt CTA mot riktig mock-jobb-sida (Variant 1) — säkerhetsrisken som motiverade Alt 2 i föregående dom försvinner när vi mockar (ingen `RequireAuthorization`-drop, inga BE-anrop)
3. **`/gast/jobb`-länken:** tillbaka i `GUEST_NAV` (CCP + WCAG 2.4.5)
4. **DEMO-banner:** PÅ `/gast/jobb` när mockdata — konsekvent tillämpning av "DEMO = ej din riktiga data"-regeln
5. **`/gast/oversikt`-utbyggnad:** Variant α — `<TodayCard>` + utöka summary + 4-5 notiser (Klas sa "för liten", inte "fel" → YAGNI)
6. **Komponentåteranvändning:** adapter `lib/guest/mock-adapters.ts` för shape-mapping; återanvänd `<TodayCard>`, `<SummaryRow>`, `<NoticeRow>`, `<ApplicationModalShell>`, `<JobAdModalShell>`, `<ApplicationDetail>`, `<JobAdDetail>`; egna `<Guest*>`-rader/cards där shape divergerar >20%
7. **Scope-batching:** 3 commits, samma session, en STOPP-rapport efter alla tre push:ade

**Inget av detta kräver Klas-GO innan implementation** — alla beslut entydigt motiverade mot principer + Klas-feedback-text.

---

## Beslut 1 — Modal-strategi (Variant A)

Parallel-routes + interception i `(guest)/gast/@modal/(.)*` paritet med `(app)/@modal/(.)*`.

**Motivering:**
- **ADR 0053 + Next.js 16 Intercepting Routes:** kanon-mönster för "modal som inte tappar context + shareable URL + browser-back fungerar"
- **WCAG 2.4.5 Multiple Ways:** shareable URLs, "öppna i ny flik", browser-back = basal navigerbarhet
- **REP/CCP (Martin 2017 kap. 13):** samma pattern återanvänds för samma reason
- **Klas-citat:** "modal kommer upp precis som 'live'" — Variant B (client-state) och C (bara full-page) levererar inte detta

**Trade-offs accepterade:** 4-6 nya filer + `(guest)/gast/layout.tsx` utökas med `modal`-slot-prop. Adapter mockdata→DTO skrivs en gång i `lib/guest/mock-adapters.ts` (SRP).

## Beslut 2 — `/gast/jobb` riktig mock-sida (Variant 1)

Ersätt nuvarande konverterings-CTA med klon av `(app)/jobb`-strukturen, mockad data.

**Motivering:**
- **Klas-feedback supersederar tidigare CTO-val medvetet** (CLAUDE.md §9.6 Klas-override-praxis)
- **Föregående Beslut 3 Alt 2** valde hide:ad CTA pga `RequireAuthorization`-drop-risk. Mockdata har **ingen** av de riskerna — inga BE-anrop till `/api/v1/job-ads`, ingen anonym yta exponeras
- **YAGNI:** Klas bad om det NU. Behöll CTA-sida som "deferred LIVE" hade inget kvarvarande syfte

## Beslut 3 — `/gast/jobb` tillbaka i `GUEST_NAV`

**Motivering:**
- **CCP:** sidor som hör till samma bounded context och är likvärdiga primär-destinationer ska samexistera i nav
- **WCAG 2.4.5 Multiple Ways:** primär-destinationer utan nav-länk bryter findability
- Kommentar i `guest-shell.tsx` ska dokumentera reverseringen: `// F-Pre Punkt 5b — /gast/jobb tillbaka i nav (CTO 2026-05-24 5b Beslut 3); föregående hide motiverad av LIVE-deferral, mockdata-väg har inte den risk-profilen.`

## Beslut 4 — DEMO-banner PÅ `/gast/jobb`

**Motivering:**
- Konsekvent tillämpning av "DEMO = denna data är inte din riktiga data"-principen — föregående exklusion gällde LIVE-data, mockdata kräver banner
- GDPR / användartransparens — användaren måste alltid kunna avgöra mockad vs riktig korpus

## Beslut 5 — `/gast/oversikt`-utbyggnad (Variant α)

`<TodayCard>` återanvänds med mockad `OversiktTodayEvent[]` (2-3 demo-events). Summary utökas med extra grupp eller fler rader. Notiser ökar från 3 till 4-5.

**Motivering:**
- Klas-feedback var "för liten", inte "fel" → utvidgning på existerande linjer, inte redesign
- `<TodayCard>` redan ren presentational RSC med typed prop-interface — trivial återanvändning
- Beck small-batches: lös det Klas sa

## Beslut 6 — Komponentåteranvändning

**Adapter-strategi:**
- `lib/guest/mock-adapters.ts` — `toApplicationDto(mock)`, `toJobAdDto(mock)` etc, single yta
- **Återanvänd:** `<TodayCard>`, `<SummaryRow>`, `<NoticeRow>`, `<ApplicationDetail>`, `<JobAdDetail>`, `<ApplicationModalShell>`, `<JobAdModalShell>`
- **Egna** `<GuestApplicationRow>`, `<GuestJobAdCard>` om shape-skillnaden är >20% (avgör vid implementations-touch — default försök adapter först)
- **Skip:** muterande footer-actions (`<WithdrawApplicationButton>` etc) — gäst får inte mutera per Klas-direktiv §F

**Motivering:**
- Evans 2003 Bounded Contexts — gäst och app är olika kontexter; tvinga inte dual-shape på `<ApplicationRow>` (skulle bryta SRP)
- Presentational + chrome-komponenter återanvänds när shape matchar trivialt (knowledge-piece-DRY)

## Beslut 7 — Scope-batching (3 commits, en STOPP-rapport)

1. `feat(web): F-Pre 5b commit 1 — gäst-jobb mock-sida + nav-länk + DEMO-banner` (Beslut 2 + 3 + 4)
2. `feat(web): F-Pre 5b commit 2 — gäst-modal-paritet (parallel-routes + interception för ansökningar/jobb)` (Beslut 1 + 6 adapter)
3. `feat(web): F-Pre 5b commit 3 — gäst-översikt TodayCard + utbyggd summary + fler notiser` (Beslut 5)

En STOPP-rapport efter alla tre commits pushed. Visual-verify mot alla 4 gäst-routes. `pnpm build` mandatory pre-push.

**Motivering:**
- Beck (XP 1999) + Fowler 2018 small-batches: atomicitet per logisk enhet
- Klas-direktiv (MEMORY `feedback_nonstop_with_pr_reports`): STOPP bara efter PR

---

## Referenser

Martin 2017 (kap. 7 SRP, kap. 13 REP/CCP), Evans 2003 (Bounded Contexts), Beck XP 1999 + Fowler 2018 (small-batches + YAGNI), Hunt/Thomas 1999 (DRY knowledge-piece), ADR 0053 (intercepting modal-mönster), Next.js 16 docs, WCAG 2.1 AA SC 2.4.5 + 2.4.8, CLAUDE.md §1 + §1.5 + §9.6, MEMORY (rendered-veto FAS-DEFERRAL-MANIFEST, non-stop med PR-rapporter, DEMO-banner-konsistens).
