# Security-audit: Fas E2c — live facet-counts (`feat/sok-paritet-facet-counts-e2c`)

**Status:** ✓ APPROVED
**Granskat:** 2026-06-11
**Auktoritet:** CLAUDE.md §5.1/§5.2/§5.4, OWASP API4:2023 (CWE-400), Saltzer/Schroeder least common mechanism, ADR 0045 Beslut 1, ADR 0067 Beslut 4, CTO-dom `docs/reviews/2026-06-11-sok-paritet-e2c-cto.md`
**Granskade ytor:** Område 3 (auth), Område 6 (logging), Område 7 (attack-vektorer/DoS). Område 1/4/5 ej triggade — ingen PII-beröring (§4).

---

## BLOCKING-verdict — FacetCountsPolicy-talen (CTO VAL 1)

**VERDICT: 30 req/10s per UserId, fixed window, QueueLimit 0 — FASTSTÄLLS utan justering.**

**Kostnadssidan:** per-request GROUP BY på STORED, indexerad shadow-column över ~43k Active-rader, p95 ~27 ms NBomber-mätt = 9% av Class A-budgeten. Sustained max per användare 3 req/s × 27 ms ≈ 81 ms DB-tid/s (~8% av en core). Fixed-window-straddle värsta-fall 60 req/~10s ≈ 1,6 s DB-tid — absorberbart. Facet-ytan tillåter 3× fler anrop än ListRead men per-request-kostnaden är i samma klass, och **bulkhead-isoleringen är nettovinsten**. Amplifiering kräver konto-multiplicering (gated av AuthWrite 20/min/IP). `QueueLimit = 0` korrekt — fail-fast 429.

**Legitim-profilsidan:** CTO-aritmetiken verifierad — profil 20–40 req/min mot tak 180/min = ×4,5–9 headroom; Ort-popoverns 2 parallella requests = 15 toggle-events/10s-budget (1,5 toggles/s sustained, väl över mänsklig takt). Symmetrin med Suggest 30/10s legitim (samma debouncade klientprofil; NoLimiter-för-anonym säkert eftersom gruppen har `RequireAuthorization`). Identifierad benign kant: oavbruten ~2 toggles/s i ~7,5 s kan ge 429 — felmoden är degradering by design (counts=null utan retry, popovern användbar, listan opåverkad, fönster-reset ≤10 s). NBomber-omkalibreringen (15 req/10s) strikt under taket — fitness-funktionen mäter skeppad konfig.

## Critical / High / Medium

Inga.

## Low

1. **FE route-handler saknar lista-cap före backend-träff (defense-in-depth, ej krav):** forwardar obegränsat antal list-params; backend-validatorn bär gränsen (400 → Bad Request) + Kestrel 414. Ingen exploaterbar yta — kostnaden för angriparen är en proxad request som dör i validatorn inom samma rate-limit-budget. Ingen åtgärd krävd.
2. **`DEFAULT_RETRY_AFTER_SECONDS = 60` matchar inte 10s-fönstren** (`lib/dto/_helpers.ts`) — i praktiken irrelevant (backend sätter alltid Retry-After ur lease-metadata; facet-klienten retry:ar inte). Kosmetiskt; opportunistisk touch.
3. **NoLimiter-mönstrets gruppberoende (informationsnotering):** `GetNoLimiter("anonymous-facet-counts")` säkert ENDAST inom `RequireAuthorization`-gruppen — invariant för framtida route-flyttar.

## Praise

- Input-kedjan komplett och speglad: `IsInEnum()` mot numerisk out-of-range (integrationstestat 400-inte-500), 401-utan-auth, cap-överskridande — testdisciplin per Område 3/7.
- Ingen injektion möjlig: parametriserad EF `Contains` → `IN(@p...)`; `ShadowColumn` = sluten enum-switch (user input kan aldrig styra SQL-identifierare); regex med `\z` (newline-bypass stängd).
- DoS-frågan (bredare WHERE) korrekt besvarad: exkluderingen kan bara TA BORT predikat — dyraste konstruerbara query är exakt NBomber-scenariot (27 ms p95), billigare än list-ytans redan tillåtna ofiltrerade count + sorterad sida.
- Logging-hygien intakt: `Request.Path` exkluderar query string — varken q eller concept-ids når loggen.
- Ingen oavsiktlig persistens: `ICapturesRecentSearch` medvetet ej implementerat (dokumenterat).
- `Cache-Control: private, no-store`; degraderings-kontraktet läcker inget (redactIssues); total-count-store = ett icke-känsligt heltal.

## §4 — PII/GDPR

Endpointen returnerar aggregat över publika jobbannonser — inga per-annons-/användardata, ingen ny PII-kategori/sub-processor/AI-yta. Q-parametern samma klass som befintliga list/suggest. Ingen DPIA-relevans.

## Sammanfattning

0 Critical, 0 High, 0 Medium, 3 Low (2 opportunistiska, 1 notering). **BLOCKING-verdictet: 30/10s fastställs utan justering.** **Säkerhetsmässigt mergeklar.**
