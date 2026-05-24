# CTO-rond — F6 P5 Punkt 4 `/oversikt`

**Datum:** 2026-05-24
**Agent:** senior-cto-advisor
**AgentId:** `ac1dbfa14aa599e65`
**Kontext:** Sessionsstart-CTO-rond för Punkt 4 `/oversikt` (post-login-landningsvy per HANDOVER-oversikt.md). 6 frågor: data-aggregations-mönster (D1), cache-headers (D2), composer-handler-design (D3), notiser-mix (D4), ADR-yta (D5), default-route-byte-tajming (D6).

## Beslut (sammanfattning)

| Fråga | Beslut | Klas-STOPP? |
|---|---|---|
| D1 — Data-aggregations-mönster | **Variant A** — direkt RSC `Promise.all` mot 5-6 endpoints | Nej |
| D2 — Cache-Control | `Cache-Control: private, no-store` + Next.js `dynamic = 'force-dynamic'` | Nej |
| D3 — Composer-handler-design | N/A (Variant B avvisad) | Nej |
| D4 — Notiser-mix | **Acceptera HANDOVER §3.3 mix** (3 riktig + 3 mock) | Nej |
| D5 — ADR-yta | **Ingen ny ADR.** Implementations-not + ADR 0064-skiljelinje | Nej |
| D6 — Default-route-byte | **Defer** route-bytet → additivt först (nav + route), substitutivt sen | **JA** (post-leverans) |

CC kör D1–D5 non-stop per Auto Mode + memory `feedback_nonstop_with_pr_reports`. D6 lyfts till Klas i STOPP-rapport efter PR1.

## Motivering — D1 Variant A (RSC `Promise.all`)

**Avvisade alternativ:**

- **Variant B** (ny `GET /api/v1/me/overview` composer-endpoint): bryter YAGNI (Beck 2004), SRP/Bounded Contexts (Martin 2017 kap. 7, Evans 2003 kap. 14) + CLAUDE.md §2.3 ("en handler gör en sak"). En composer skulle orkestrera 4 bounded contexts (Application/Resume/JobAd/SavedJobAd/RecentSearch). Ny port-yta för single consumer = REP/CCP-brott (Martin 2017 kap. 13).
- **Variant C** (Worker-precomputed per-user Redis-cache `overview:user:{userId}:v1`): ADR 0064-mönstret är publik-aggregat-only. Per-user-precompute kräver Worker-iteration över alla aktiva users → kvadratisk skalning. Fel mönster för fel data-klass.
- **Variant D** (hybrid med `getLandingStats().activeCount`): **dataklass-blandning**. Landing-stats är publik anonym 5-min-fördröjd med Floor-fallback. Att låta `/oversikt`:s "Aktiva annonser totalt" läsa samma cache betyder att inloggad användare ser samma fördröjda värde som anonym. Funktionell defekt maskerad som perf-vinst. Saltzer & Schroeder 1975 — least common mechanism applicerad på cache-buckets.

**Vald Variant A motivering:**

- ADR 0048 Beslut (b) säger: extern/översatt/context-korsande → port; intern/enkel/samma-DbContext → in-handler-join. Per-user overview matchar **ingen** av de fyra etablerade port-axlarna (0043/0062/0063/0064) → föredra direkt aggregering i konsument.
- Operational visibility (12-Factor §XI): `LoggingBehavior` ger granulär per-query-signal. Composer-aggregator döljer 5-6 queries bakom en handler.
- Trade-offs: 5-6 BE-rundresor från SSR per `/oversikt`-hit. Acceptabelt eftersom (1) auth-gated lågvolym, (2) `Promise.all` parallelliserar → max-latency ej sum, (3) RSC server-side → noll klient-impact.

## Motivering — D2 `private, no-store` + force-dynamic

- GDPR + Saltzer & Schroeder 1975 fail-safe defaults: per-user-data får ALDRIG hamna i delad cache.
- ADR 0045 klass (a) 300 ms p95 för auth-gated read-paths. `Promise.all` av 5-6 individuellt-budgetade queries klarar det. `LoggingBehavior` ger signalen — vid regression: optimera den långsamma queryn, inte gå till Variant B.

## Motivering — D4 Acceptera HANDOVER §3.3-mix

- HANDOVER §0 är explicit Klas-godkänd mock-direktiv. Memory `feedback_v3_designspec_veto_scope` säger veto-regler gäller INTE Klas egna nyare regler.
- §3.7 OVERSIKT_MOCK är medvetet statisk → dynamiska rader = handling, statiska = info-platshållare; mixen blir begriplig, inte smutsig.
- CLAUDE.md §9.6: full-mock-deferral skulle skapa TD som "scope-disciplin" — bryter `feedback_td_lifting_discipline`.

## Motivering — D5 Ingen ny ADR

- YAGNI + ADR-disciplin (Nygard 2011 "Documenting Architecture Decisions"): ADR skrivs när en beslutsregel etableras. Variant A är frånvaron av mönster (ingen port, ingen cache, ingen aggregator). ADR 0048 Beslut (b) täcker redan regeln.
- Avgränsning mot ADR 0064 dokumenteras som **implementations-not** i `current-work.md`/session-log: *"Punkt 4 använder INTE ADR 0064-mönstret. Skäl: per-user auth-gated, ej publik anonym aggregat. Per Variant A — direkt RSC-aggregering (CTO-dom 2026-05-24, agentId `ac1dbfa14aa599e65`). ADR 0064 förblir explicit publik-anonym-only."*

## Motivering — D6 Defer default-route-byte

- Operational risk (Nygard 2018 kap. 5): substitutiv user-visible change kombinerad med synlig mockdata = sida ser ofärdig ut för 100 % inloggade.
- Reversibility (Fowler 2018): brand-länk + nav-item = additiva (Översikt blir åtkomligt, inte default). Login-redirect-bytet = substitutivt. Additiva tidigt, substitutiva sent.
- Klas är produktägare. HANDOVER §7 skrevs innan mock-omfattning var känd. Klas-STOPP-fråga: *"OK att additivt leverera /oversikt + nav + brand-länk i PR1; default-redirect-bytet i separat commit efter pixel-verifiering?"*

## Referenser

- Robert C. Martin, *Clean Architecture* (2017) kap. 7 (SRP), 11 (DIP), 13 (REP/CCP/CRP), 23 (CQRS)
- Eric Evans, *Domain-Driven Design* (2003) kap. 14 (Bounded Contexts)
- Kent Beck, *Extreme Programming Explained* 2nd ed. (2004) — YAGNI
- Michael Nygard, *Release It!* 2nd ed. (2018) kap. 5, *Documenting Architecture Decisions* (2011)
- Martin Fowler, *Refactoring* 2nd ed. (2018) — reversibility
- Saltzer & Schroeder (1975) — least common mechanism
- ADR 0048 Beslut (b), ADR 0064 (publik-anonym-only-skiljelinje), ADR 0045 Beslut 1 klass (a)
- CLAUDE.md §2.3, §9.6
- Memory: `feedback_v3_designspec_veto_scope`, `feedback_td_lifting_discipline`, `feedback_nonstop_with_pr_reports`

---

*Sparad per CLAUDE.md §9.2 — agent-rapport bifogas STOPP-rapport.*
