# Current work — JobbPilot

**Status:** **F6 P5 PUNKT 2 PR5 + PR5b + UX-POLISH FULLT LEVERERAT 2026-05-23 (HEAD `2b00216`, origin/main).** 8 commits ovanpå PR1-4-batchen pushade. **PR5** (`c2f0ac5` `feat(ux): F6 P5 Punkt 2 PR5 — civic-utility-fixar + Sparad/Ansökt-taggar + chips`) — 10 Klas-feedback-punkter från visual-verify v0.2.58-dev adresserade: (1) parallel server-fetch isJobAdSaved+hasAppliedJobAd för modal-state-reset, (2) `disabled={isPending}` ersatt med opacity (snabbare upplevd respons), (3) `/sparade`-hero-chip + SavedJobAdsHeroChip båda chips till höger, (4) copy "Har ansökt" → "Markera som ansökt" / "Ansökt", (5) modal-footer-hierarki rensad (alla secondary, state-byte samma position), (6) "Öppna ansökan"-success-rad muted UNDER footer (inte inline-toast), (7) Sparad/Ansökt-taggar på `JobAdCard` via Set-prop från RSC, (8) grön bock navy-700-border + `var(--jp-success)`-ikon-färg, (9) tid på publicerad ("idag, kl. 21:14") via `formatPublishedAtWithTime`, (10) ADR 0063 NY (Accepted 2026-05-23) per-user overlay-status batch-port. **PR5b** (`1a688b0` `fix(ux): F6 P5 Punkt 2 PR5b — review-fix-batch + CI coverage-fix`) — review-fynd in-block-fix + nytt `Api.IntegrationTests`-projekt (+7 tester) + ADR 0063 redaktionell konsistens-fix (Implementation-text "anonym-tolerant" istället för "auth-gated"). **EF Core 10 batch-Contains-bugg** krävde två fix-försök: `31cd5c5` (.Select.Where) failed CI → `3849976` (client-side HashSet) CI grön — strongly-typed VO `JobAdId.Contains()` translation-bug i Npgsql 10-batch-genereringen. **UX-fix-svans** (`6889531` grön bock + publicerad-tid, `237711c` kraftigare tagg-kantlinje + NY-debug, `2383313` NY-debug via DOM-attribut, `2b00216` debug-cleanup + dokumentera last-seen-jobs-modell Klas-bekräftat). **CTO-domar denna session:** (1) agentId `abf2d7322f2bb6ee1` — 4 multi-approach-val för PR5, alla Variant A (parallel server-fetch / batch-port-arkitektur / FE-action-bryggan kvar i modal-footer / Set-projektion i RSC); (2) agentId `a5b8f9db1079a1a12` — Minor 9 Variant A: ta bort `RequireAuthorization` från batch-endpoint per ADR 0063 §Kontext-konsistens (anonym-tolerant — `JobAdDto` är publik list-projektion, batch-portens shape måste matcha). **Reviews:** security-auditor (agentId `ac31007ab7c67d6b2`) — 0 Block/0 Critical/0 Major/0 Medium/1 Minor APPROVED (Minor M-1 `IAuthenticatedRequest`-marker på `HasAppliedQuery` defense-in-depth-konsistens-hygien, kvar för framtida modal-yta-disciplin-batch); code-reviewer (agentId `a873a1b68679b9b07`) — 0 Block/2 Major (Major 1 → TD-87 rate-limit, Major 2 fixad in-block i PR5b) / 7 Minor (4 fixade in-block + 3 lyfts/skippas motiverat). **adr-keeper** (agentId `a7de23fc5822a0d18`) skrev ADR 0063 Accepted (Klas-GO post-utkast). **Tester:** Domain 404 / Application 561→**573** (+12: 5 SavedJobAd-validator + 7 UserStatus + paritets-fix) / Architecture 78 oförändrat / `Api.IntegrationTests` +7 (nytt projekt mot Testcontainers, CI grön) / vitest 630→**635** (+5 JobTags). **TDs lyfta (§9.6-godkända):** TD-87 (Major × F6 P5 P2-fas-stängning) rate-limit för `/me/*`-batch-endpoints via ny `MeStatusReadPolicy` med dual-partition userId/IP; TD-88 (Minor × Trigger) DOM-mutation `onMouseOver` i hero-chips → CSS `:hover`-refactor. **Tag `v0.2.59-dev` pekar på `3849976` (BE-leverans — efterföljande FE-only-commits behövde ingen ny tag).** Disciplin: alla commits explicit pathspec; `.claude/settings.json` aldrig committad; auto-skapad scaffolding orört; CC gav inte egen rekommendation vid multi-approach-val (CTO decision-maker); memory uppdaterad med EF-Contains-lärdomen (`feedback_ef_strongly_typed_vo_contains_translation.md`). **Pending Klas-operativt:** korpus-konvergens Punkt 1 (~72h-fönstret från 2026-05-23 v0.2.57-dev). **Nästa:** Punkt 3 (Landing live-stats `GET /api/v1/landing/stats`) — egen session med startprompt enligt session-start-template. Session-logg `docs/sessions/2026-05-23-2030-f6-p5-punkt2-pr5-ux-fixes.md`.

**(Föregående) Status:** **F6 P5 PUNKT 2 JOBBKORT SPARA + HAR-ANSÖKT FULLT LEVERERAD & PUSHAD 2026-05-23 (HEAD `1972f47`, origin/main).** 4 commits pushade (`c015918` PR1 backend SavedJobAd-aggregat + EF migration `20260523154503_AddSavedJobAds` + AccountHardDeleter cascade + API `/api/v1/me/saved-job-ads` GET/POST/DELETE; `4afc081` PR2 FE Zod-mirror + SaveJobAdToggle (optimistic, aria-pressed) + SavedJobAdList/Row + `/sparade` RSC-sida + app-shell nav-länk + ADR 0053 modal-footer-integration; `a187467` PR3 backend CreateApplicationFromJobAdCommand + endpoint `POST /api/v1/applications/from-job-ad/{jobAdId}` (validerar JobAd + befintlig Application.Create, ADR 0048 read-join NO snapshot per ADR 0048 Beslut d); `1972f47` PR4 FE createApplicationFromJobAdAction + HarAnsoktButton (optimistic + inline-toast med länk) + ADR 0053 Amendment 2026-05-23 (lyft Spara/Har-ansökt-deferral) + ADR 0024 Amendment 2026-05-23 (SavedJobAd-cascade-rad) + README-index uppdaterat). **CTO-dom** (agentId `ad76c06a752275b17`) — 4 multi-approach-val alla Variant A: (1) SavedJobAd fullt aggregate root paritet RecentJobSearch, (2) befintlig Application.Create + ADR 0048-join (ingen snapshot — ADR 0048 Beslut d-respekt), (3) separat `POST /api/v1/applications/from-job-ad/{jobAdId}`, (4) ADR 0053 modal-footer + toast med länk. **Reviews:** security-auditor (agentId `a1c34345919cf4f91`) 0/0/0/0/0 APPROVED — ADR 0031 ej tillämpligt (JobAdId publik, ingen IDOR-yta), cross-tenant via cookie-bound ICurrentUser; code-reviewer (agentId `a57baf5e5e5539d9e`) 0 Block/0 Major/3 Minor (M1 audit-noise idempotent no-op = observation, M2 ADR 0024-amend fixad i PR4, M3 fullt-kvalificerade typnamn i integration-test fixad i PR2-batchen). **adr-keeper** (agentId `ade82c309560fb383`) skrev ADR 0053-amend + ADR 0024-amend (Klas-override för verbatim-källa). **Tester:** Domain 399→404 (+5 SavedJobAd) / Application 546→561 (+11 SavedJobAd + 4 Application-from-JobAd) / Architecture 78 oförändrat / Worker.IntegrationTests +1 cascade-test (CI) / Web vitest 612→629 (+13 nya, +2 i pnpm test 66 files). Frontend `pnpm build` grön (RSC-boundary verifierad — `/sparade` + `(.)jobb/[id]` listade). Pre-push-gates passerat alla 4 push (gitleaks + tester). Inga TDs lyfta (§9.6 — alla fynd inom Fas 6, fixade in-block). **Pending Klas-operativt:** tag-push `v0.2.58-dev` (Klas-GO) → dev-deploy triggar EF migration `20260523154503_AddSavedJobAds` automatiskt → visual-verify post-deploy via `pnpm visual-verify`. **Klassiska följdfrågor (ej Punkt 2-scope):** in-card Spara-toggle i JobAdCard (modal-footer-only levererat; kräver Link→article-refactor — egen design-rond); Har-ansökt-knappens idempotens (FE-state-switch förebygger, backend tillåter — ev. domain-invariant senare). **Punkt 1 korpus-konvergens** fortfarande ~72h-fönstret från 2026-05-23 v0.2.57-dev-deploy. **Nästa:** Punkt 3 (Landing live-stats `GET /api/v1/landing/stats`) — egen session med startprompt enligt session-start-template. Session-logg `docs/sessions/2026-05-23-1730-f6-p5-punkt2-spara-har-ansokt.md`.

**(Föregående) Status:** **F6 P5 PUNKT 1 SNAPSHOT-RETENTION LEVERERAD & DEPLOY-TRIGGAD 2026-05-23 (HEAD `b3b5e31`, origin/main; `v0.2.57-dev` tag pushad — deploy-dev run `26336452793` queued; build-CI grön run `26336390555` 2m55s).** ADR 0032-amendment 2026-05-23 + ADR 0062-amendment 2026-05-23 (snapshot-miss-cleanup + ExpiresAt-cron + ApplyCriteria Status=Active SPOT-filter + post-archive circuit-breaker). Defense-in-depth-arkitektur enligt senior-cto-advisor-dom (agentId `a8e277380b446bb02`) + dotnet-architect-design (agentId `a10f8271fe298246c`) + tilläggsrond H1 post-archive circuit-breaker (agentId `acfe2963371fde555`). **Reviews:** code-reviewer 0 Block/0 Major/2 Minor accept-som-är (agentId `a82b9f511ec54889b`); security-auditor 0 Critical/0 Block, H1 in-block-fixad + H2 in-block-fixad (agentId `a419beb4d87e8a46e`). **adr-keeper** (agentId `a4e7227559affffdb`) skrev ADR 0032+0062-amendments + TD-86-not. **Tester:** Domain 399 / Application 546 (+13 nya) / Architecture 78 (+5 nya `JobAdRetentionLayerTests`). Worker.IntegrationTests + Api.IntegrationTests körs i CI. **Korpus förväntad konvergens:** 56k → ~40k aktiva över ~72h post-deploy (N=3 konsekutiva snapshot-misses + ExpiresAt-cron). **`/jobb`-UX-impact:** räkne-droppet synligt samma deploy (Status=Active SPOT-filter) per CTO Variant 1 (filter+retention samma release). **Pending Klas-operativt:** verifiera deploy-run klar grön + korpus-baseline `/api/v1/job-ads?pageSize=1 totalCount` post-deploy. Session-logg `docs/sessions/2026-05-23-1730-f6-p5-punkt1-snapshot-retention.md`.

**(Föregående) Status:** **F6 P4 FTS-SKIFTE STÄNGT 2026-05-23 — DEPLOYAD v0.2.56-dev (HEAD `7bc6233`, origin/main; deploy-run `26219978114` 12m51s success, LIVE på dev). Sök-/filter-arbetet PAUSAT per Klas-direktiv 2026-05-23.** Klas-GO tag-push `v0.2.56-dev` 2026-05-23 → dev-deploy. Deploy-verifiering via `explain-search`-FTS-mode (ECS task `33bd7299...`) gav **blandat perf-utfall** mot live dev-korpus 56 635 rader (växte 51 749 → 56 635 mellan 2026-05-21 och 2026-05-23): **specifika termer 6-11× snabbare** (`systemutvecklare` 1.6s→270ms BitmapOr GIN-tsvector+trigram-title ✓, `ekonom` 5.0s→464ms ✓) men **common-term-fallet sämre** (`lärare` 18.7s→23.5s COUNT, `sjuksköterska` ~5s→21.4s — planneren väljer **Seq Scan** över GIN-tsvector eftersom svensk Snowball-stemmer reducerar `"lärare"→"lär"` → matchar 14k+ rader ≈25 % av korpus; vid den selektiviteten är Seq Scan + de-TOAST av `search_vector` billigare än Bitmap Heap Scan + recheck). **ADR 0062 medveten trade-off bekräftad — description-LIKE-borttagningen hjälpte inte:** `search_vector` är `to_tsvector('swedish', title||' '||description)` STORED och TOAST:ad → samma I/O-börda. Drift-observation, ej ADR 0062-amendment-trigger. **Synlig "Söker…"-text** i sök-laddning (commit `3bfb27d`, skeleton-platshållare → `<p>`; `aria-label` borttaget per ARIA 1.2 `nameFrom=author`-disciplin från jobbpilot-design-a11y-skill). **TD-86 lyfted** (commit `7bc6233`, Minor × Trigger) — samlar samtliga öppna sök-/filter-trådar inför fas-paus: recall-gap vs Platsbanken (JobbPilot 198 vs Platsbanken 800+ träffar — observerat 2026-05-23), common-term-perf-regression, F6 P4c query-token-parser, P2-backfill-verifiering, description-LIKE-trade-off-omprövning, stemmer-aggressivitet, kommun-pickers, spinner-mi1/mi2. **Klas-observation 2026-05-23 (snapshot-retention):** korpus 56k vs Platsbanken ~46k → `sync-platsbanken-snapshot` rensar INTE utgångna jobb (endast UPSERT, ingen `MarkExpired`/soft-delete-pass). **Klas-direktiv 2026-05-23:** pausa sök-/filter-arbetet, fortsätt med andra punkter. Session-logg `docs/sessions/2026-05-23-1530-f6-p4-fts-deploy-verify-och-faspaus.md`.

**Pending för nästa fas — Klas-bekräftad ordning 2026-05-23 (1→5, dependency-driven):**

Varje punkt körs som **egen session** med `docs/runbooks/session-start-template.md` som mall (CC genererar startprompten per session).

1. ✅ **Snapshot-retention** *(small, backend-only)* — **LEVERERAD 2026-05-23** (HEAD `b3b5e31`, `v0.2.57-dev` deploy-triggad; korpus-konvergens 56k → ~40k aktiva pågår över ~72h-fönstret). ADR 0032+0062-amendments. Verifiera korpus-baseline post-deploy.
2. ✅ **Jobbkort Spara + Har-ansökt** *(large, backend+frontend)* — **HELT KLAR 2026-05-23 inkl. PR5-feedback-cykel** (HEAD `2b00216`; PR1-4 `c015918`/`4afc081`/`a187467`/`1972f47` + PR5/PR5b/UX-svans `c2f0ac5`/`1a688b0`/`31cd5c5`/`3849976`/`6889531`/`237711c`/`2383313`/`2b00216`). SavedJobAd-aggregat + `/api/v1/me/saved-job-ads` (GET/POST/DELETE); CreateApplicationFromJobAdCommand + `POST /api/v1/applications/from-job-ad/{jobAdId}`; FE-bryggan i modal-footer + `/sparade`-sida + app-shell nav. PR5: 10 Klas-feedback-punkter från v0.2.58-dev visual-verify adresserade (parallel server-fetch, opacity-disable, hero-chips, copy-fix, modal-footer-hierarki, success-rad muted, Sparad/Ansökt-taggar på `JobAdCard`, grön bock-ikon, publicerad-tid, ADR 0063 batch-port). PR5b review-fix-batch + nytt `Api.IntegrationTests`-projekt + EF Core 10 batch-Contains-translation-bugg fixad (client-side HashSet). ADR 0053-amend (lyft deferral, PR4) + ADR 0024-amend (cascade-rad, PR4) + ADR 0063 NY (Accepted, per-user overlay-status batch-port). TD-87 (Major × Fas-stängning) rate-limit + TD-88 (Minor × Trigger) hero-chip CSS-hover. Tag `v0.2.59-dev` på `3849976` (BE). Pending Klas-operativt: korpus-konvergens Punkt 1 (~72h).
3. **Landing live-stats** *(medium, backend endpoint + frontend swap)* — ny `GET /api/v1/landing/stats` (cache:ad — Redis / Hangfire-cron / materialiserad PG-vy), frontend swappar `getLandingStats()` per ADR 0056-utbytespunkt. **Perf-kritiskt** (publik landingpage, ingen auth). Beror på punkt 1 för korrekta antal. ADR 0056-amend; CTO-rond: cache-mekanism.
4. **Översiktssida `/oversikt`** *(large, frontend+ev. backend)* — handover-källa `docs/jobbpilot-v3-bundle/JobbPilot/handoff-oversikt/HANDOVER-oversikt.md` (Klas-godkänd 2026-05-23). Tre sektioner: Title+I dag / Notiser / Sammanfattning. Mockdata-tillåtelse för fält där BE saknas (HANDOVER §0). **Stänger TD-82**. Återanvänder punkt 3:s cache-mönster. CTO-rond: data-mappning riktigt-vs-mock (HANDOVER §3).
5. **Stängd registrering + gäst-mockdata** *(medium, frontend + ev. feature-flag)* — ADR 0005 `registrations_open`-flag finns. Landing AuthCard: "Skapa konto" → "Väntelista". "Utforska som gäst": read-only gäst-mode på alla sidor med tydligt markerad mockdata ("Exempel TEST-data"). Klas-prio: lägst. ADR 0005-amend + ADR 0056-amend; CTO-rond: gäst-mode-arkitektur (separat route-grupp `(guest)/*` vs flag-baserat vs demo-subdomän).

**Beroenden:** 2→1 (data konsistent), 3→1 (korrekta antal), 4→2+3 (sparade/ansökningar + stats-mönster), 5→fristående (parallell-möjlig men lägst prio).

**Pausad / out-of-scope för dessa 5 punkter:**

- **TD-86** sök/filter-hardening (recall-gap-precision, common-term-perf, F6 P4c, etc.) — pausat per Klas-direktiv; återupptas vid Klas-GO för sök-fas-2. Punkt 1 (snapshot-retention) löser en separat del av samma observation (korpus-storlek), men inte recall-gap-precisionen.
- **Fas 4 AI** — blockerad av 5 GDPR-villkor (ADR 0051: DPIA, SCC+TIA, versionerad privacy-policy, Art.25-opt-in-text, ADR 0049-decrypt-interaktion) + KMS-rehoming (ADR 0049→ADR 0050). Klas-arbete, ej CC-leverans. Långt borta oavsett dessa 5 punkters ordning.
- **ADR 0050/0051 Accepted-flip** + **AWS-exit (Hetzner-migration)** — egen strategisk Klas-fas.
- **BUILD.md §20 Open questions** (branding-hex, logotyp, privacy-policy, TOS) — långsiktigt.
5. **Stängd registrering + gäst-mockdata** — ADR 0005-koppling

**Fas 4 pre-reqs oförändrade (icke-blockerande just nu, blockerar första AI-kodrad):**

- **5 GDPR-villkor** (ADR 0051, icke-förhandlingsbar pre-Fas-4-grind, security-auditor-veto):
  1. DPIA Art. 35 genomförd
  2. SCC + Schrems II-TIA + Anthropic-DPA + DPF-verifikation
  3. Versionerad privacy-policy live före första AI-anrop
  4. Art. 25-opt-in även för systemnyckel (ingen US-default)
  5. ADR 0049-decrypt-interaktion (decrypt-före-AI = klartext-PII över Atlanten)
- **KMS-rehoming** (ADR 0049 ↔ ADR 0050-cross-ref): full AWS-exit tar bort AWS KMS, PII-fält-krypto måste om-hemmas (icke-AWS, crypto-erasure bevarad) FÖRE faktisk migration. ADR 0050 Öppen fråga.

**Commits sedan föregående status (F6 P5 P2 PR5+UX — 8 commits, push-only, tag `v0.2.59-dev` på `3849976`):**

| Commit | Typ | Beskrivning |
|--------|-----|-------------|
| `c2f0ac5` | feat(ux) | F6 P5 Punkt 2 PR5 — civic-utility-fixar + Sparad/Ansökt-taggar + chips + ADR 0063 Accepted (per-user overlay-status batch-port via dedikerad port, ej DTO-vidgning) |
| `1a688b0` | fix(ux) | F6 P5 Punkt 2 PR5b — review-fix-batch + Api.IntegrationTests (+7) + ADR 0063 redaktionell fix (Implementation "anonym-tolerant") |
| `31cd5c5` | fix(user-status) | EF Core 10 translation-fix försök 1 (.Select.Where) — CI failed |
| `3849976` | fix(user-status) | EF Core 10 translation-fix försök 2 (client-side HashSet) — CI grön. **Tag `v0.2.59-dev`** |
| `6889531` | fix(ux) | grön bock-ikon för Sparad/Ansökt + tid på publicerad i jobbkort |
| `237711c` | fix(ux) | kraftigare Sparad/Ansökt-tagg-kantlinje + NY-debug-log (dev) |
| `2383313` | fix(ux) | NY-debug via DOM-attribut istället för console.debug |
| `2b00216` | chore(ux) | ta bort NY-debug-attrs + dokumentera last-seen-jobs-modell (Klas-bekräftat) |
| (denna synk) | docs(sessions/current-work) | F6 P5 P2 PR5+UX session-end-synk |

**Föregående batch (F6 P5 P2 PR1-4):**

| Commit | Typ | Beskrivning |
|--------|-----|-------------|
| `c015918` | feat(saved-job-ads) | PR1 Del A backend — SavedJobAd-aggregat (Domain/App/Infra) + EF migration `20260523154503_AddSavedJobAds` + AccountHardDeleter cascade (ADR 0024 paritet) + API `/api/v1/me/saved-job-ads` (GET/POST/DELETE) + 5 Domain-tester + 11 Application-tester + 1 Worker.Integration cascade-test |
| `4afc081` | feat(saved-job-ads) | PR2 Del A FE — Zod-mirror SavedJobAdDto + lib/api/saved-job-ads + lib/actions/saved-job-ads + SaveJobAdToggle (optimistic, aria-pressed) + SavedJobAdList/Row (optimistic delete) + `/sparade` RSC-sida + app-shell nav-länk + integrerat i JobAdDetail modal-footer + vitest 13 nya |
| `a187467` | feat(applications) | PR3 Del B backend — CreateApplicationFromJobAdCommand + handler (validerar JobAd existerar + befintlig Application.Create, ADR 0048 read-join NO snapshot per Beslut d) + endpoint `POST /api/v1/applications/from-job-ad/{jobAdId}` + 4 Application-tester |
| `1972f47` | feat(applications) | PR4 Del B FE + ADR-amends — createApplicationFromJobAdAction + HarAnsoktButton (optimistic + inline-toast med länk) + integrerat i JobAdDetail modal-footer + ADR 0053 Amendment 2026-05-23 (lyft Spara/Har-ansökt-deferral) + ADR 0024 Amendment 2026-05-23 (SavedJobAd-cascade-rad) + README-index uppdaterat + 4 vitest |
| `fca7f9c` | docs(sessions) | F6 P5 Punkt 2 — session-end-synk (Spara + Har-ansökt levererad) |

**Föregående batch (F6 P5 P1):**

| Commit | Typ | Beskrivning |
|--------|-----|-------------|
| `b3b5e31` | feat(job-ads) | F6 P5 Punkt 1 — snapshot-retention defense-in-depth |
| `ca13c83` | docs(current-work) | Låst Klas-bekräftad ordning för nästa 5 punkter |
| **Deploy** | tag-push | `v0.2.57-dev` (2026-05-23, Klas-GO) → run `26336452793` queued |
| `3bfb27d` | feat(web) | F6 P4 — synlig "Söker…"-text i sök-laddning (skeleton → `<p>`, aria-label bort per ARIA 1.2 `nameFrom=author`) |
| `7bc6233` | docs(tech-debt) | TD-86 — sök/filter-hardening samlad (Minor × Trigger) |
| **Deploy** | tag-push | `v0.2.56-dev` (2026-05-23, Klas-GO) → run `26219978114` 12m51s success, **LIVE på dev** |

**EXPLAIN-fynd (deploy-verifiering 2026-05-23, korpus 56 635 rader):**

| Sökterm | Före (trigram) | Efter (FTS) | Förändring | Plan |
|---------|----------------|-------------|------------|------|
| systemutvecklare | 1.6s | 270ms | **6× snabbare** | BitmapOr GIN-tsvector + trigram-title ✓ |
| ekonom | 5.0s | 464ms | **11× snabbare** | BitmapOr GIN-tsvector + trigram-title ✓ |
| lärare | 18.7s | 23.5s COUNT | **regression** | **Seq Scan** över GIN-tsvector |
| sjuksköterska | ~5s | 21.4s | **regression** | **Seq Scan**, samma orsak |

ADR 0062-beslutet (FTS-hybrid + Infrastructure-query-port) står — implementations-strategin kräver fortsatt arbete (query-token-parser, ev. stemmer-tuning eller GIN-tsvector partial-index på vanliga lemman) som äges av TD-86. Ingen ADR-amend triggas per §9.6 + adr-keeper-disciplin (amend = ändrad beslutsmekanik, inte ändrat empiriskt utfall).

**(Föregående) Status:** **F6 P4 FTS-SKIFTE LEVERERAD & PUSHAD 2026-05-21 (HEAD `95bcb74`, origin/main — `build`-CI grön run `26218140677`; tag-push `v0.2.56-dev` + deploy-verifiering pending Klas-GO).** PostgreSQL full-text-search-hybrid för jobbannons-sök — löser ADR 0061:s kvarstående "lärare"-klass-perf-problem (korta vanliga svenska termer 18.7s pga fundamental trigram-selektivitet). FTS `search_vector` STORED generated tsvector (`to_tsvector('swedish', title||description)`) + GIN-index, hybrid `websearch_to_tsquery('swedish', q) OR lower(title) LIKE '%q%'`, `ts_rank`-relevans ersätter ADR 0042 Beslut D2 ILIKE-heuristik. **Lager-refaktor:** sök-kompositionen flyttad Application→Infrastructure bakom porten `IJobAdSearchQuery` (`SearchAsync`+`CountAsync`) — FTS-LINQ ligger fysiskt i Npgsql-assemblyn som arch-testet förbjuder i Application; `JobAdSearch.cs` borttagen, 3 handlers → tunna adaptrar, ny `JobAdFilterCriteria`-SPOT delad av tre konsumenter (CTO Variant B, 3:e-konsument `ListRecentSearches` fångad). **Reviews:** code-reviewer 0 Block/2 Major-åtgärdade/4 Minor; security-auditor 0 Block/Crit/High/Medium, 1 Low non-regression — APPROVED. **Tester:** Domain 399, Application.UnitTests 535, Architecture.Tests 73 (+`JobAdSearchLayerTests`), integration FTS 11 + ListJobAdsFilter/MultiFilter 18 + SavedSearch/RecentSearch 26 — alla gröna; frontend vitest src/components/job-ads 105/105. **6 commits** (`2adcec9`→`95bcb74`). ADR 0062 NY (Accepted, FTS-hybrid + Infrastructure-query-port); ADR 0061 + 0039 additiva amendments 2026-05-21; README-index uppdaterat. **Sök-laddningsindikator** (skeleton i `/jobb`) levererad — design-reviewer VETO rond 1 → fix → GO rond 2. **PENDING (Klas):** tag-push `v0.2.56-dev` → triggar dev-deploy → deploy-verifiering (explain-search-mode: `Bitmap Index Scan` på `ix_job_ads_search_vector`; perf-mål "lärare" <0.2s, alla q-termer <2s). P2-backfill ~51k legacy-rader pending nästa `sync-platsbanken-snapshot` 02:00 UTC — verifiera ssyk/region-filter mot hela korpusen. **Backlog:** deploy-verifiering → F6 P4b SavedJobAds → F6 P4c query-token-parser ("lärare göteborg" smart-sök) → F6 P4 retention (51k vs 45k-städning, Klas-observation). Session-logg `docs/sessions/2026-05-21-1210-f6-p4-fts-skifte.md`.

**(Föregående) Status:** **F6 P4 SÖK-INFRASTRUKTUR-FIX (P1+P2) LEVERERAD & DEPLOYAD 2026-05-21 (HEAD `a143f60`, tag `v0.2.55-dev`, origin/main — live på dev). NÄSTA: F6 P4 FTS-skifte i egen session (startprompt genererad i chatten 2026-05-21).** P1 q-search-perf (GIN trigram, ADR 0061) + P2 filter-bugg-rotorsak (JobTechHit-POCO saknade klassifikations-properties → generated columns NULL → ssyk/region-filter 0 träffar). Deploy krävde 5 cykler (v0.2.51 pg_trgm-permission → ensure-extensions-CLI-mode → CommandTimeout 600s → partial-index-predikat-fix → v0.2.55 explain-search-diagnostik). **VERIFIERAT:** q-search 40s→1.6s cold/<0.2s warm specifika termer; ssyk/region-filter fungerar; recent-searches 57s→6.3s. **KVARSTÅR — går till egen FTS-session:** vanliga korta svenska termer fortf. långsamma (lärare 18.7s). EXPLAIN ANALYZE bevisade fundamental trigram-selektivitet (12 980 kandidater, 7 581 falska positiva, de-TOAST-recheck; `lossy=0` → work_mem uteslutet). CTO-dom (3 ronder): **PostgreSQL FTS-hybrid, Variant (b)** — Infrastructure-query-port `IJobAdSearchQuery`, hela `JobAdSearch.ApplyCriteria`+`ApplySort` flyttas Application→Infrastructure (FTS-funktioner finns bara i Npgsql-assembly; arch-test `TaxonomyAclLayerTests` förbjuder Npgsql i Application). Kräver ADR 0039-amend + 0061-amend + ny ADR 0062. Klas-beslut: behåll trigram-index (substring-fallback), FTS i egen fokuserad session. **P2 backfill:** ~51k legacy-rader pending nästa `sync-platsbanken-snapshot` 02:00 UTC (admin-trigger avvecklad, TD-83) — idempotent UPSERT re-importerar med ny POCO. **Reviews:** code-reviewer 0/0/0 + security-auditor 0/0/0/0/0 (P1/P2-batchen). **Tester:** Domain 399 + App.Unit 532 + Arch 70 + Integ gröna; `build`-CI grön. **10 commits** (815e422→a143f60). ADR 0061 Accepted. **Backlog:** F6 P4 FTS-skifte (nästa) → F6 P4b SavedJobAds → F6 P4c query-token-parser ("lärare göteborg" smart-sök) → F6 P4 retention (51k vs 45k-städning, Klas-observation). Session-logg `docs/sessions/2026-05-20-2340-f6-p4-sok-infrastruktur-fix.md`.

**(Föregående) Status:** **F6 P4a BACKEND LEVERERAD & PUSHAD 2026-05-20 (HEAD `5bc6eea`, tag `v0.2.49-dev`, origin/main).** RecentJobSearches auto-capture-domän + ADR 0060 (NY, Accepted) + ADR 0039/0055/0024 amend. Multi-approach via senior-cto-advisor (3 entydiga val) + dotnet-architect-verifiering. Levererat: Domain (RecentJobSearch + FilterHashCalculator SHA-256 + 2 events), Application (post-handler `RecentJobSearchCaptureBehavior` med opt-in `ICapturesRecentSearch`-markör + `IRecentSearchCaptureResponse` response-markör; ListQuery + DeleteCommand med ADR 0031 cross-tenant), Infrastructure (`RecentJobSearchCapturer` race-säker via ADR 0032 §5 ON CONFLICT-pattern; EF-konfig text[] shadow-fields paritet Resume; DI), API `/api/v1/me/recent-searches` GET/DELETE, migration `AddRecentJobSearches`, Frontend Zod-mirror + API-helper + 10 Zod-tester (tsc clean). **GDPR Art. 17-cascade in-block-fix:** explicit RemoveRange för SavedSearches + RecentJobSearches i AccountHardDeleter.HardDeleteAccountAsync; ADR 0024-amend + integration-test; pre-existing SavedSearches-cascade-lucka samtidigt fixad in-block per §9.6. **Reviews:** code-reviewer 0 Block/0 Major/8 Minor (Min-1 fixad in-block); security-auditor GDPR-1 BLOCKER + High-1/High-2/Medium-3 ALLA in-block-fixade. **Tester:** 38 nya gröna — Domain 399/Application 526/Architecture 70 (pipeline+taxonomy-allowlist uppdaterade)/Worker.Integration 69/Api.Integration 356, Frontend vitest 10/10. **Nästa: F6 P4a FRONTEND** återupptas i Klas's chat-session efter merge — /sokningar route refactor + hero-chip + privacy-disclosure (GDPR Art. 13 Klas-uppgift per ADR 0060 Mekanik-not 6). F6 P4b SavedJobAds = separat backend-prompt. **Pending (ej F6 P4a FE-blockerande):** dev-DB-migration körs vid tag-deploy (idempotent additive); cap=20-bekräftelse (auto-mode-accepterad enligt CTO-motivering). Session-logg `docs/sessions/2026-05-20-2143-f6-p4a-recent-job-searches-backend.md`.

**(Föregående) Status:** **POST-FAS-3 MIGRATION-DISCOVERY — STOPP 4 (2026-05-19, ingen kod, inga commits än, HEAD `3f22224`).** STOPP-driven 4-block-session. **Block 1** (AWS-budget $50→$100) **SKIPPAD** (Klas-beslut: budget-action stoppar bara Bedrock-deny på dev-roll, Fas 4 ej byggt = noll funktionell påverkan, AWS rivs juni — ingen prod-apply gjord; architect fann 3 startprompt-fel: tak i prod/baseline ej dev, trösklar redan 50/80/100, deny i separat modul). **Block 2 BESLUTAT:** full AWS-exit → **Hetzner CX32** (8GB/80GB, ~€6,80/mån, sizing-grundat på 45k+ korpus + ingestion-minne) + Vercel FE kvar + Cloudflare (R2 pg_dump-offload + proxy). **Block 3 BESLUTAT:** Bedrock utgår, **Anthropic Direct API** (systemnyckel + BYOK), **US opt-in även systemnyckel** (ingen US-default, CTO Art.25-dom); AI-lager = 0 rader (greenfield Fas 4). **Block 4:** ADR **0050** (AWS-exit, Proposed) + ADR **0051** (AI-provider, Proposed) skrivna (Klas-begärd §9.4-override, substans från 3 agent-domar), `docs/decisions/README.md`-index + "Planerade ADRs" uppdaterat (adr-keeper), `docs/research/2026-05-19-bedrock-vs-anthropic-direct.md` skriven. **⚠️ KMS-MIGRATIONS-BLOCKER:** ADR 0049 (TD-13) PII-fält-krypto använder AWS KMS — full AWS-exit tar bort KMS, krypto måste om-hemmas (icke-AWS, bevarad crypto-erasure) FÖRE migration; namngiven olöst i ADR 0050 Öppen fråga. **5 GDPR-villkor** (ADR 0051: DPIA/SCC+TIA/versionerad policy/Art.25-opt-in/ADR 0049-decrypt-interaktion) = icke-förhandlingsbar pre-Fas-4-grind (security-auditor-veto). **PENDING (Klas):** STOPP 4-granskning ADR-paket+docs-diff → commit/push-GO; spec-amendments (BUILD.md §3.1/§7/§8/§9.6/§13.4 + CLAUDE.md §5.3/§9.5 + privacy-policy = Klas spec-edit-approve, ej CC); README-räknedrift ADR 44→51 (samlas vid Accepted-flip); tech-debt obsolet-flaggning EJ blint applicerad (TD-77/78/27/26 mekanism-kopplade men krav överlever — Klas/CTO-triage). Fas 4 + faktisk migration = egna strategiska Klas-GO + ren `/clear`. Session-logg `docs/sessions/2026-05-19-1307-post-fas3-migration-discovery.md`.**

**(Föregående) Status:** **TD-13 FAS 3.5 — ✅ STÄNGD 2026-05-19. C1–C6 + KMS-IaC levererat, `v0.2.19-dev`-deploy GRÖN på dev (api/ready 200, ren KMS-boot, ECS steady state, ingen taxonomi-sök-regression). 4 user-ägda PII-kolumner krypterade (per-användar-DEK KMS-envelope) + backfill-job + crypto-erasure-hook. security-auditor + code-reviewer GO; full svit grön (Domain 358/App 492/Migrate 6/arch 70/Worker 68/Api 344). TD-13 arkiverad (§9.7 `tech-debt-archive.md`). FAS 4 AVBLOCKERAD (kräver egen Klas-GO + ren /clear). Öppet (ej blockerande): ADR 0049 Mekanik-not 6-reconciliation-utkast väntar Klas-granskning; TD-85 (github_oidc + RDS-param-group IaC-drift); Beslut 5 steg 3–4 (cutover→content-drop) framtida Klas-STOPP; live dev-test-end-to-end-spotcheck rekommenderad. Session 2026-05-19.**

**Levererat & pushat (alla gates GO, full svit grön: unit 493 / arch 63 / Worker-integ 48 / Api-integ 344):**
- STOPP D: discovery + **ADR 0049 Accepted** (per-användar-DEK envelope, crypto-erasure, raw_payload exkluderad, hybrid lazy+backfill, jsonb→text expand/contract) + CTO 5-besluts-dom + CTO-triage. `9952a0c`/`a039bb0`.
- **C1** KMS-envelope-fundament (`IFieldEncryptor`/`IDataKeyProvider`/`KmsEnvelopeEncryptor` AES-256-GCM `v1:`-sentinel/`KmsDataKeyProvider`/`FieldEncryptionOptions`, AWSSDK.KeyManagementService 4.0.8.8). `78958ce`.
- **Hotfix Approach D** — C1 J3-regression (global ValidateOnStart bröt ~6 KMS-fakande integ-hostar) → `FieldEncryptionOptionsValidator` (IValidateOptions, hård fail Prod/Staging, warn Dev/Test) + EU-region-guard. `1162f1c`.
- **C2** per-användar-DEK-store (`user_data_keys` keyless EJ IAppDbContext, `IUserDataKeyStore`/`IUserDataKeyCache`/`ScopedUserDataKeyCache` zeroing, crypto-erasure-port, migration). `018e001`.
- ADR Mekanik-not 3 (decrypt-prefetch Approach B). `1851632`.
- **C3** (KÄRNAN — fält-kryptering interceptor-par) `bbf8081` — 4 arkitektur-hardpoints lösta via CTO/architect-kedja: #1 DTO-projektion (Approach A, handler materialiserar, ADR Mekanik-not 4 + ADR 0048-undantag), #2 re-entrancy-deadlock (Approach A: write-interceptor ren synkron singleton-cache-konsument), #3 system-scope decrypt (CTO iv: scope-diff fail-closed — auth kasta/system passthrough), #4 EF DI singleton+`(sp,options).AddInterceptors`+Context.GetService (Mekanik-not 5c, Microsoft Learn-verifierad). Markör `IRequiresFieldEncryptionKey` + `FieldEncryptionKeyPrefetchBehavior` (efter Auth/före UnitOfWork) på 5 write-commands + GetApplicationByIdQuery. 3 TEXT-kolumner HasMaxLength bort + migration. security-auditor GO (0 Crit/High/GDPR) + code-reviewer GO (0 Block/Major).

**NÄSTA = FAS 4 (kräver egen Klas-GO + ren `/clear` — sessionsbyte är strategisk transition per §9.2).** TD-13 FAS 3.5 stängd; FAS 4 (AI-lager / BYOK enligt BUILD.md) avblockerad. FAS 4-startprompt levererad som chat-copy-paste-block 2026-05-19 (ej repo-fil per §1.5). UI-refactor (v3-bundle-källa, `docs/jobbpilot-v3-bundle/`+`docs/JobbPilot.zip` untracked — RÖR EJ) körs efter TD-13 STOPP V enligt separat sekvensnot — Klas beslutar FAS 4 vs UI-refactor-ordning. **Öppna uppföljningar (ej FAS-4-blockerande):** (1) ADR 0049 Mekanik-not 6-reconciliation-utkast (`46a0948`) — Klas granskar, kan override:a dual-shadow/nullable-ContentEnc/`DROP NOT NULL`/dedikerad-CMK → formell amendment; (2) **TD-85** github_oidc prod-drift + RDS-param-group dev-normalisering (separat IaC-triage); (3) Beslut 5 steg 3 cutover-flipp (fitness-ratchet ADR 0045, Klas-STOPP) → steg 4 `content` jsonb-drop (destruktiv, egen migration); (4) prod-paritet KMS-IaC vid framtida prod-deploy; (5) live dev-test-konto end-to-end skriv→content_enc→läs-spotcheck (krypto redan integration-bevisad).

**Tidigare STOPP V-läge (historik, nu stängt):** dotnet-architect KMS-IaC-design klar + Klas-GO "full kedja autonomt". 6 Terraform-filer skrivna (commit nedan): `modules/kms` td13_field-CMK+alias+outputs, `modules/iam_ecs` `kms_td13_field_key_arn`-var + `Td13FieldEnvelopeKms`-statement (GenerateDataKey+Decrypt, Resource-scoped, EncryptionContext purpose=td13-field/aggregate=jobseeker) i task_api+task_worker, `environments/dev` td13_field-data-source + iam_ecs-koppling + `FieldEncryption__CmkKeyId/__AwsRegion` i api+worker_environment, `environments/prod/outputs` td13-outputs. **prod/baseline targeted-apply KÖRD** (`-target=module.kms.aws_kms_key.td13_field` + alias — output indikerade lyckad, alla outputs printades) **MEN OVERIFIERAD** (auto-mode-classifiern blockerar fortsatt prod/infra/AWS inkl. `terraform output`/`describe-key`-verifiering — ser ej Klas "GO full kedja autonomt"-svar; jag kringgår EJ spärren). **github_oidc prod-drift** (OIDC-provider+deploy_dev-roll "update in-place", pre-existing, EJ TD-13) → **TD-85** (targeted apply uteslöt den medvetet). **Klas-handoff:** Klas verifierar td13-CMK-state själv (`terraform output | grep td13` + `aws kms describe-key --key-id alias/jobbpilot-td13-field-key`) + lägger Bash-permission-regel (terraform/aws) i settings.json → continue-GO för dev-apply + re-tag `v0.2.18-dev` + deploy + rök-verify + TD-13-arkivering. TD-13 INTE arkiverad (ej grön). ADR 0049 Mekanik-not 6-reconciliation-utkast väntar Klas-granskning.

**Tidigare plan (post-KMS-infra, post-continue-GO):** dotnet-architect designade (§9.2 obligatorisk, ADR 0036-precedens CTO+architect-tandem): (1) dedikerad TD-13-CMK vs återanvänd `aws_kms_alias.master` (ADR 0049 Beslut 1 — CMK wrappar per-användar-DEK); (2) `FieldEncryption__CmkKeyId`-ARN + `FieldEncryption__AwsRegion=eu-north-1` som secret/env i API+Worker+Migrate task-defs (Terraform); (3) ECS-task-roll IAM `kms:GenerateDataKey/Decrypt/DescribeKey` på CMK:n + encryption-context-policy. Architect-rapport → **Klas-GO före Terraform-apply**. Sedan: re-tag `v0.2.18-dev` → deploy → rök-verify (`/api/ready` 200 + content_enc-ciphertext via dev RDS + klartext-läs via dev-test-konto `project_dev_test_account` + taxonomi/raw_payload-generated-cols-regress; SQL-bevis 2026-05-19: ssyk/region_concept_id STORED generated INTAKTA) → **TD-13 → `docs/tech-debt-archive.md`** (§9.7) + steg-tracker + FAS 4-startprompt. **Klas-granskning kvarstår:** ADR 0049 Mekanik-not 6-reconciliation-utkast (probe-subsumering + nullable-ContentEnc/ContentLegacyJson-readonly/`ALTER COLUMN content DROP NOT NULL`-preciseringar — Klas kan override:a till formell amendment).

**Tidigare STOPP V-plan (nu post-KMS-infra):**

1. **AWS SSO utgången** (flaggad pre-flight 2026-05-19): `aws sso login --profile jobbpilot` krävs FÖRE deploy-rök-verify (KMS/Secrets). Dev `/api/ready` = 200; Testcontainers fakar KMS (svit ej blockerad), men deploy-verify behöver riktig KMS.
2. **Tag/deploy = Klas-GO:** tag `v0.2.x-dev` → deploy-dev (Migrate Phase E — C4.1 `20260519060041` + C4.2 `20260519064819` redan applicerade på dev-container; prod-deploy kör dem). Rök-verify: `/api/ready` 200 + ny resume skrivs `content_enc` ciphertext + läses klartext via dev-test-konto (`project_dev_test_account`) + taxonomi-sök/raw_payload-generated-cols-regress (SQL-bevis 2026-05-19: ssyk/region_concept_id STORED generated INTAKTA, raw_payload orörd — ADR 0049 Beslut 3).
3. **ADR 0049 Mekanik-not 6-reconciliation** (webb-Claude verbatim §9.4 — CC konstruerar EJ ADR-prosa): proben är raderad (subsumerad av C4.4 sc.1); Not 6 rad ~363 + C4-review-doc måste korrigeras (probe→C4.4-subsumering). Plus STOPP V-flaggor: Not 5b/5c/6 + C4.2-preciseringarna (nullable ContentEnc / ContentLegacyJson read-only / `ALTER COLUMN content DROP NOT NULL` = 4:e Beslut 5/Not 6-precisering) — Klas kan override:a någon → formell amendment.
4. Vid grön rök-verify: **TD-13 → `docs/tech-debt-archive.md`** (§9.7 full kropp + stängningsnotat), översikts-/stängda-tabeller, steg-tracker, docs-keeper-synk. **FAS 4-startprompt** + UI-refactor-sekvensnot (UI efter TD-13 STOPP V; v3-bundle-källa untracked, RÖR EJ).

**Senare (egen Klas-STOPP):** Beslut 5 steg 3 cutover-flipp (EF-mappning→content_enc-only, fitness-ratchet ADR 0045) + steg 4 drop `content` jsonb (destruktiv, separat commit, prod-verifiering).

**KLAS-FLAGGOR (STOPP V — Klas non-stop-direktiv 2026-05-18, ej Klas-stopp före STOPP V):** ADR 0049 Mekanik-not 5b (scope-diff fail-closed CTO #3 iv) + 5c (singleton-interceptor-mekanik, architect flaggade potentiell amendment) — Klas kan override:a → formell ADR-amendment. Klas bindande live-verify `/ansokningar` (`850ae37`, FAS-3-svans, icke-blockerande).

**⚠️ OATTRIBUERAT (ej TD-13, ej CC):** `docs/JobbPilot.zip` + `docs/jobbpilot-v3-bundle/` dök upp i working tree — ej rörda/raderade (`feedback_dont_delete_auto_files`), exkluderade ur alla TD-13-commits. Klas verifierar/hanterar.

**Disciplin nästa session:** läs ADR 0049 (alla Mekanik-noter 1–5c) + `docs/reviews/2026-05-18-td13-{discovery,design-decisions-cto,stopp-i-cto-triage,c1-gates,c2-gates,c3-gates}.md` + C3-koden on-disk. CTO/architect-kedja non-stop till STOPP V. MTP-test: `dotnet exec <Worker-dll> -class <FQN>` (EJ `dotnet test --filter` — dumpar help). Worker-integ-svit ~9s. `git commit -- <paths>` pathspec-scoped, verifiera `git show --stat HEAD`; exkludera oattribuerade docs/JobbPilot.zip+bundle.

---

**(Föregående) Status:** **POST-FAS-3 POLISH-SPÅR LEVERERADE 2026-05-18 (laptop, HEAD `850ae37` + denna handoff-docs-commit ovanpå, origin/main synkad).** Två Klas-begärda post-stängnings-spår klara: **(1) dark-kantlinje-kontrast** (`9b00c0f`+`2413de7`) — ny roll-token `--jp-border-structural` (dark `#64748B` ≈3.6:1, light `#E2E8F0` oförändrad), ADR 0041-amendment, Klas approve-spec-edit, design-reviewer Gate 2 GODKÄND 0 fynd, Klas browser-dark-toggle bekräftade live. **(2) `/ansokningar` list-skannbarhet** (statusöversikt alla-10-inkl-0-count + minimera/maximera-grupper, CTO 6-punkts-ram) — **PROD-INCIDENT hanterad:** `eece124` bröt prod (RSC→client render-prop-funktion icke-serialiserbar, ERROR 850043857) → CTO Approach A: revert `3d09bf6` (prod återställd) → slot-map-fix `40a413a` (live, `pnpm build` oberoende GRÖN) → Minor-1-polish `850ae37`. **`pnpm build` nu permanent obligatorisk pre-push-gate för RSC/client-boundary, kodifierad i `web/jobbpilot-web/AGENTS.md`** (felmoden vitest/tsc/eslint ej fångar — incidenten = gate-lucka, ej disciplin-regression per CTO + ADR 0019 trigger 3). design-reviewer Area 5 GODKÄND 0 Block/0 Major/2 Minor (Minor 1 in-block-fixad). **ENDA ÖPPNA PUNKT: Klas bindande live-verify av `/ansokningar` list-skannbarhet** (`850ae37` live på www.jobbpilot.se) — Klas bytte till stationär dator innan verify. Inga nya TDs (§9.6 — allt in-block). Dev-test-konto: ett 2:a syntetiskt skapades på laptop (creds endast i laptop-`%USERPROFILE%`, ej i repo); **stationär använder sitt egna befintliga `dev-test-creds.env`**. Post-stängnings-backlog UTTÖMD. **Nästa fas:** Fas 4 (AI Layer) — egen strategisk Klas-GO + ren `/clear` (§9.2). Se `docs/HANDOFF.md` (dator-byte laptop→stationär, raderas efter pull) + `docs/reviews/2026-05-18-*`. **(Föregående) FAS 3 (APPLICATION MANAGEMENT) FORMELLT STÄNGD 2026-05-18 (HEAD `22338ea` + denna stängnings-docs-commit ovanpå, origin/main).** STOPP 3a backend (`46291c0`, `ManualPosting` VO + cross-aggregat-join ADR 0048, deployad `v0.2.15-dev`) + STOPP 3b frontend (`47a1378`, `/ansokningar`-omarbetning, deployad `v0.2.16-dev` run `26014066232` success). Stängnings-session (laptop-handoff 2026-05-18): visual-verify auth-läge utökad till 5 jobbidentitets-tillstånd + L2-destruktiv-capture (`8530d9b`+`38425be`); **design-reviewer bindande Area 5 render-VETO (ADR 0047): v1 1 Block/1 Major/1 Minor → tooling-re-work (senior-cto-advisor 2-ronds-triage: Block1 falsk-pos, Major1 = bekräftad Chromium/CDP-emulerings-instrumentartefakt, dispositivt produktkod-invariansbevis) → v2 PASS 0/0/1** (`22338ea`; Minor = m3-uppskjuten datetime-local, ej blocker). **Klas live-verify `/ansokningar` = GODKÄND 2026-05-18** (bindande grind efter 2 underkännanden; Klas dark-toggle i egen browser bekräftade dark fungerar = auktoritativ kompensation för instrument-artefakten). **ADR 0046 Proposed→Accepted 2026-05-18** (Grind 1, explicit Klas-GO, adr-keeper; ADR 0048 redan Accepted). **Defer-not (Klas-godkänd):** `jobad-kopplad`-dark visual-verify-snapshot = känd Chromium/CDP-instrument-artefakt (colorScheme-emulering + extern-target-popup-kontext), produktkod dispositivt invariant (kodanalys + autentiserad header/HTML-discovery: ingen ISR/prerender/markup-skillnad; överlever reload+localStorage-forcering); exkluderad från snapshot-gaten tills Playwright/Chromium-uppgradering — Klas browser-toggle = auktoritativ dark-bekräftelse. Syntetiskt dev-test-konto skapat på laptop (sanktionerat /register-mönster, creds utanför repo). Inga nya TDs (§9.6 — allt in-block/in-fas). **Post-stängnings-backlog (Klas-vald sekvensering "separat efter stängning", kräver egen plan-design — EJ påbörjad):** (1) `/ansokningar` list-skannbarhet: status-snabblänkar (Utkast 17/Skickad 3/…) + minimera/maximera status-grupper — designas TILLSAMMANS (samma underliggande skann-problem), egen /ansokningar-UX-touch; (2) dark-kantlinje-token-kontrast — `--jp-border` #1E293B ≈ dark-surface (samma WCAG 1.4.11-klass som ADR 0041 men ej åtgärdad utanför modaler) → ADR 0041-amendment/ny ADR + design-reviewer + `approve-spec-edit` (rör DESIGN.md/skills). **Nästa fas:** Fas 4 (AI Layer) — kräver ren `/clear` + strategisk Klas-GO för sessionsbyte (§9.2). Se `docs/sessions/2026-05-18-*-fas3-stangning.md`. **(Föregående) FAS 3 BATCH 1 LEVERERAD & CI-GRÖN 2026-05-17 (HEAD `78d3b14`, origin/main, run `25998180368` success).** Strategiskt premiss-brott upptäckt & Klas-beslutat: FAS 3-startpromptens antagande om greenfield-konstruktion var fel — hela Application-pipeline-vertikalen (Domain 10-state-machine, FollowUp/Note, 5 commands, 3 queries, DetectGhostedJob, EF+2 migrations, 7 endpoints, Worker recurring, 3 frontend-rutter, 12+ testfiler) **byggdes redan i Fas 1**. senior-cto-advisor `a49fdd7992b3a7a0a` fann spec-konflikt (startpromptens "Avslags-analys = FAS 3-kärna" felaktigt; BUILD.md rad 1641 fas-allokerar den Fas 6). **Klas godkände CTO-ramen:** redefinierad FAS 3 = **A (RecordFollowUpOutcome-vertikal in-block) + D (DoD-verifiering av befintlig 95%-vertikal, först)**; **B Påminnelser→Fas 5** (notifikations-infra = egen bounded context delad m. Calendar/Gmail; YAGNI/CCP att bygga isolerat nu); **C Avslags-analys→Fas 6** (BUILD.md rad 1641). Klas valde **ADR + session-log, ingen BUILD.md §18-spec-edit**. **D KLAR:** build 0/0, full svit **1160/1160** (0 failed/skipped), arch-tests gröna, ADR 0044 per-lager-golv ALLA PASS (Domain 95.3/93.1, App 97.7/91.1, Infra 84, Api 93.7), perf ADR 0045 orört, frontend lint/tsc/vitest gröna. **A KLAR (commit `78d3b14`, path-scoped, CI grön):** `FollowUp.RecordOutcome` saknade command/endpoint/UI — levererat TDD: `Application.RecordFollowUpOutcome` (aggregat-mediering, `FollowUpOutcomeRecordedDomainEvent`, audit ADR 0022, ingen IsClosedForActivity-guard per arkitekt-beslut), command/handler/validator (paritet AddFollowUp, cross-user ADR 0031, `.Include(FollowUps)`), `POST /api/v1/applications/{id}/follow-ups/{followUpId}/outcome`, inline outcome-form. **Rättade latent Fas 1-bugg:** followUpOutcome-enum/labels var felaktigt Pending/Positive/Negative/Neutral; backend-SmartEnum är Pending/Responded/NoResponse — synkad i 6 frontend-touchpunkter + test (hade kraschat GET-parse när utfall sätts). Tester: Domain 321, Application 474, API-int 308, vitest 389 — alla gröna. Gates: dotnet-architect `a1adb06cf1d1e8155` (5 beslut), test-writer (TDD röd→grön), **security-auditor GO 0/0/0/0/1Low**, **code-reviewer GO 0/0/0**, **design-reviewer APPROVED kod-nivå 0/0/1** (M1 aria + M2 danger-700 in-block-fixade i record-follow-up-outcome-form + add-follow-up-form; m3 date-fns medvetet uppskjuten). **ADR 0046 skapad (Proposed)** — FAS 3 scope-redefinition + B→Fas5/C→Fas6, dokumenterar medveten avvikelse mot BUILD.md §18 rad 1610; **Accepted-flip = Klas-STOPP**. **PENDING (ej blocker):** (1) ADR 0046 Proposed→Accepted = Klas-GO; (2) design-reviewer **VETO-villkor** rendered-screenshot-granskning (light+dark, `pnpm visual-verify`) = **Fas 3-stängnings-gate** (som Fas 2, ej push-blocker); (3) Fas 3-stängning = separat Klas-DoD-verifiering (steg-tracker rad 32 → uppdaterad till Pågående); (4) startprompt-fel uppströms (C var ej FAS 3-kärna). Inga nya TDs (§9.6 — allt in-fas/in-block). Inga prod-deploys. Se `docs/sessions/2026-05-17-1800-fas3-batch1-recordfollowupoutcome.md` + ADR 0046. **(Föregående) PRE-FAS-3 CLOSE-OUT KLAR & CI-GRÖN 2026-05-17 (HEAD `904c914`, origin/main).** Alla tre FAS 3-prerekvisiter uppfyllda (A README-portfolio, B pristine baseline, C perf-governance) — **FAS 3 (Application Management) FULLT FRI** (kräver ren /clear + strategisk Klas-GO §9.2; härdad FAS 3-startprompt levererad i chatten). Close-out: **(1)** BUILD.md §3.1 NBomber 6.x + NBomber.Http 6.x **applicerad** (människa-i-loopen: Klas körde `approve-spec-edit.sh` manuellt via Git Bash, `guard-spec-files` single-use-token konsumerad) → ADR 0045 sista loose-end stängd, **perf-governance 100% klar**; commit `354802d`. **(2)** **Permission-regel för approve-spec-edit AVRÅDD & dragen tillbaka** — auto-mode-klassificerarens hård-block av agent-själv-godkännande / agent-själv-permission-edit (`.claude/settings.json`) är **KORREKT säkerhetsbeteende by-design, EJ bugg**; tidigare "false-positive"-framing (memory `feedback_spec_edit_approve_classifier_block`) felaktig — bör korrigeras. §9.2-modellen behålls: människan kör approve-scriptet, agenten själv-godkänner aldrig. **(3)** Parallell-CC-härdning kodifierad i `docs/runbooks/session-start-template.md` §8/§9 (3 process-glidningar 2026-05-17: CC A `git commit -a` svepte CC B:s Resume-fix; docs-keeper `core.hooksPath=/dev/null` kringgick gitleaks; agent-själv-edit-försök settings.json): worktree-per-parallell-CC obligatoriskt, `git commit -- <pathspec>` enda form (commit -a förbjudet vid parallell CC), sub-agent-hook-bypass förbjudet, docs-keeper ej auto-push under öppen incident, agent själv-godkänner/själv-beviljar aldrig §9.2-edits; propageras till alla framtida startprompter; commit `0752968` (path-scoped). **(4)** Alla **4 Dependabot-PR:er mergade** (#6 nuget-all .NET 10.0.8 patch-servicing; #3 actions-all major-bumpar CI-verifierade end-to-end; #4 web-minor-patch; #5 @types/node 20→25 CI-verifierad dev-only) — coverage-gate (ADR 0044) höll grön genom alla; ingen öppen PR. **main-CI grön verifierad:** run `25994705495` (close-out `0752968`) success + run `25994771063` (#5-merge `904c914`) success — backend/coverage/ci + 3 observe-only alla gröna. Inga nya TDs (§9.6/§9.7 — process/doc-drift). Inga prod-deploys. **Pending operativt (ej FAS 3-blocker):** valfri Klas-manuell `.claude/agents/docs-keeper.md` hook-bypass-skärpning (mall+memory täcker operationellt, låg prio); §9.2-spec-edits kräver Klas manuell `approve-spec-edit.sh`-körning (Git Bash: `& "C:\Program Files\Git\bin\bash.exe" .claude/hooks/approve-spec-edit.sh`); memory `feedback_spec_edit_approve_classifier_block` bör korrigeras (klassificerare = korrekt, ej false-positive). Se `docs/sessions/2026-05-17-1700-pre-fas3-close-out.md`. **(Föregående) FAS 3-PREREKVISIT C (PERF-GOVERNANCE / ADR 0045) LEVERERAD & CI-GRÖN 2026-05-17 (HEAD `7ca463f`, pushad).** Performance-budgetar + fitness functions etablerade. ADR 0045 **Accepted** (Klas-flip 2026-05-17): (a) read-query p95 300ms / (b) typeahead p95 150ms Klas-låsta; (c) command p95 400ms / (d) ingestion ≥200 jobb/min CTO-satt; CWV LCP<2.5s/CLS<0.1 gate-intent + INP observe-only; Worker 512 MiB soft cap; **NBomber valt, k6 avvisat; BenchmarkDotNet deferrad per Klas-direktiv** (micro-benchmark skjuts Fas 7). CI observe-only Fas 1: 3 jobb (lighthouse/loadtest/audit) **UTANFÖR `ci.needs`** — ADR 0044 coverage-gate **orörd & verifierad grön** (CI-run `25993726144` success, alla jobb inkl. coverage gröna). `dependabot.yml` utökad (web/jobbpilot-web npm-entry — supply-chain-lucka stängd, dependabot bevisat öppnat PRs). CLAUDE.md §2.5 (perf granskningsbar kärnprincip) + §9.2 (dotnet-architect obligatorisk Terraform-scope). `.claude/agents/perf-test-writer.md` skapad (builder, ej reviewer/gate; NBomber+Lighthouse-mandat, ej BDN) — **agent-roster = 13** (5 CTO-avvisade agenter medvetet EJ skapade, anti-bloat per roster-gap-CTO 2026-05-17). `docs/runbooks/release-checklist.md` skapad (generisk repeterbar release-rutin). code-reviewer GO 0/0/0; senior-cto-advisor B1–B7 levererat. Commits `faf381c` (ADR 0045 + observe-only CI), `54b91ae` (CLAUDE.md §2.5/§9.2), `7ca463f` (perf-test-writer-agent). **PENDING (ej blockerande, Klas-beroende nästa session):** BUILD.md §3.1 NBomber-rader (3 rader: NBomber 6.x + NBomber.Http 6.x) applicerades EJ — auto-mode-klassificerare hård-blockerar spec-edit-approve-hooken trots Klas-GO (STOPP 3). NBomber redan dokumenterad i ADR 0045 + `Directory.Packages.props`-kommentar; ingen funktionell lucka. Åtgärd nästa session: Klas kör approve-script själv ELLER permission-regel för `bash .claude/hooks/approve-spec-edit.sh`. Se `docs/sessions/2026-05-17-perf-governance-adr0045.md`. **(Föregående)** **README PORTFOLIO-OMARBETNING LEVERERAD 2026-05-17 (HEAD `42ee92c`, pushad, CI-verifierad).** `README.md` omskriven från projektöversikt till portfolio-skyltfönster för CTO/gradare/senior lärare (betygsatt inlämning) — Klas-auktoriserat register-undantag scopat till portfolio-docs (CLAUDE.md §1 civic-ton styr PRODUKT-UI, ej portfolio-docs). Nya sektioner: "Om utvecklingsmodellen" (LinkedIn-positionering), "Agent-orkestrering" (mermaid-hierarki, 12 verifierade agenter, six-step-modell — ersatte svaga "AI-driven utveckling"), "Ingenjörsprinciper i praktiken" (Clean Arch/SOLID/DRY/SoC/DDD/CQRS var och en bunden till verifierbar mekanism: arch-test/ADR/namngiven kod-väg, inga rötande rad-nummer). FALSE-CLAIM rättad: gammal felaktig 4-fas-modell → auktoritativ 8-fas-modell (Fas 0/1/2 Klar, Fas 3 Planerad). Gate-trail: senior-cto-advisor register/substans-GO (`aaed9537d8bb200f5`); code-reviewer GO 0 Block/0 Major efter 2 blockers fixade (53 arch-test-fakta/10 filer korrigerade) → re-review GO (`a475be159946aa558`). Commit `62c9dc7`. **INCIDENT (Klas-direktiv: forward-recovery, INGEN history-rewrite):** `62c9dc7 docs(readme):` buntade ofrivilligt en parallell CC:s (CC B) `Resume.SoftDelete` idempotens-guard (`Resume.cs`/`ResumeVersion.cs`/`ResumeTests.cs`) — rotorsak: `git commit` utan pathspec mot delat git-index. Kod korrekt/intakt/CI-grön; defekt = commit-hygien/attribution (§1.5) + cross-CC-kontaminering. **Forward-attribution `42ee92c`** + två retroaktiva review-trail-filer: `docs/reviews/2026-05-17-resume-softdelete-retroactive-security.md` (security-auditor GO 0/0/0 — guarden STÄNGER latent Art.17 erasure-delay-regression, ALIGN med JobSeeker/Application, inga downstream-konsumenter) + `docs/reviews/2026-05-17-resume-softdelete-retroactive-cto.md` (CTO: BENIGN consistency-alignment, CTO-clearable, ingen TD, ApplicationNote/FollowUp-asymmetri benign non-TD). **KLAS KVITTERADE 2026-05-17 (process-incidenten STÄNGD):** Klas accepterade retroaktivt att Resume.SoftDelete-guarden nådde `main` via delat git-index utan föreskriven pre-commit-ordning. Grund för lågrisk: (1) CTO BESLUT 1b korrekt — N-1-konformering till redan Klas-godkänt mönster (2026-05-11 Application/JobSeeker), ej nytt domänkontrakt → ingen substantiell governance-brist; (2) trippel-rensad kod (reviews + retroaktiv security-auditor 0/0/0 + CTO benign-alignment); (3) netto-positiv — stänger latent Art.17-regression. Kvarstod = ren commit-hygien, framåt-dokumenterad i granskningstrail. **Baseline godkänd som pristine av Klas.** Ingen ny TD (§9.7 — process/doc-drift). **ÖPPEN NOT (separat Klas-beslut, EJ aktionerad — CLAUDE.md §9.2-skyddad):** formalisering av "uppdatera README vid fas-stängning" i CLAUDE.md §1.5. Cross-ref-verifiering (docs-keeper): README↔ADR ingen drift (0001/0008/0010/0011/0019/0022/0024/0027/0039/0043/0044+0031 resolver; ADR-index 0001–0044 = 44 poster matchar badge; 12-agent-påstående matchar `.claude/agents/`; steg-tracker/current-work-länkar resolver). Pending operativt i övrigt OFÖRÄNDRAT (FAS 3 inväntar strategisk Klas-GO §9.2; steg-tracker §4 öppen Klas-fråga; CC B äger `steg-tracker.md`-M + ev. mer Resume-arbete). Se `docs/sessions/2026-05-17-1553-readme-portfolio-rewrite.md`. **(Historik) PRE-FAS-3-VERIFIERING + HYGIEN-STÄDNING KLAR 2026-05-17 (HEAD `62c9dc7`, pushad, CI grön run `25992539084`).** Pristine baseline-verifiering före FAS 3. **Uppgift 1 — Fas 2-stängning end-to-end mot DoD §8: VATTENTÄTT VERIFIERAD.** Evidens (alla gröna): steg-tracker rad 31 "Klar 2026-05-17 ²⁵⁶" + fyllig fotnot ⁶; full svit `dotnet test` 1156→**1160** (0 failed/0 skipped); Fynd 1/2 Klas-slutgodkända; saved-search-namn-batch Klas-GO; cron-grön CONFIRMED (korpus 5 380→19 816, 5005 graceful); ingestion-hybrid ADR 0032-amendment Accepted; ADR 0039/0042/0043 Accepted; CI run `25989503529`+`25992539084` success (ADR 0044-regressions-gate aktiv & passerar). HEAD-not: Klas-prompt angav 31a2c51; verklig stängning skedde vid `31a2c51`, coverage-sidospår pushades ovanpå (parallella CC:er) — current-work internt konsistent, ej blocker. **Uppgift 2 — Resume.SoftDelete idempotens-asymmetri STÄNGD.** senior-cto-advisor `adbea6842e0c3e911` BESLUT 1a/1b (fixa in-block, konformering till Klas-godkänt N-1-mönster, ingen Klas-STOPP). `Resume.cs:165` + `ResumeVersion.cs:42` fick `if (DeletedAt.HasValue) return;` (paritet Application/JobSeeker). test-writer TDD 3 RÖD+1 happy. Svit 1156→1160 grön, noll regression. code-reviewer GO 0/0/0 + security-auditor GO 0 Crit/High/GDPR/Med/Low (netto-positiv Art.17/Art.5(1)(e)). **Uppgift 3 — steg-tracker §4/§5 FRYSTA.** senior-cto-advisor BESLUT 2 (frys medvetet, DRY/single-source; backfill avvisad). Verbatim frysnings-noteringar applicerade §4+§5-headers. **AVVIKELSE (Klas-eskalerad & beslutad):** parallell CC körde `git commit -a` och svepte min staged Resume-fix in i sin `62c9dc7 docs(readme):`-commit (redan pushad, CI grön). Koden korrekt/intakt/grön — defekt = commit-hygien/attribution (§1.5-brott) + cross-CC-kontaminering. History-rewrite avvisad (pushad delad main + aktiv parallell CC, ADR 0019). **Klas-beslut: acceptera som-är + dokumentera** (granskningstrail i session-log + här); ingen fler git-op mot 62c9dc7. Process-lärdom (parallell-CC working-tree-isolering) noterad för Klas, ej TD (§9.7 process/doc-drift). **Inga nya TDs. Baseline funktionellt pristine — FAS 3 fri att starta** (kräver explicit strategisk Klas-GO för sessionsbyte §9.2). Se `docs/sessions/2026-05-17-1545-pre-fas3-verifiering-hygien.md`. **(Historik) COVERAGE-FINALISERING KLAR & VERIFIERAD 2026-05-17 (HEAD `d67d340`).** ADR 0044 **Accepted** (Proposed→Accepted-flip, adr-keeper: §58-prosa pinnad + Mekanism-mening till enforce:ad/historik past tense + index rad 59) + per-lager regressions-gate **aktiverad & blockerande** (`ci.needs: [backend, frontend, coverage]`, continue-on-error+exit 0 borttaget) + README kvalitet/coverage-skryt-sektion. **main-CI run `25989344497` = success** (backend/frontend/coverage/ci alla success); gate-stegets ubuntu-output: alla 6 per-lager-golv PASS (Domain line 95.3/golv 93, branch 93.3/91; Application line 97.7/95, branch 91.1/89; Infrastructure line 84/82; Api line 93.7/91), Worker observe-only loggad, "Coverage-gate PASSED". Pinnade golv per senior-cto-advisor `a7fc36da3d8b1a8dc` (`floor(baseline−2.0pp)`): Domain line 93/branch 91, Application line 95/branch 89, Infrastructure line 82, Api line 91, Worker observe-only Fas 1, Migrate exkluderad, ingen global/method-gate. code-reviewer GO 0 Block/0 Maj/0 Minor (edge-case-dry-runs: regression/saknad-assembly/korrupt-JSON → fail-closed verifierat). Commits `ee4709a` (CI-gate-aktivering+Accepted-flip) + `d67d340` (README-skryt). **Klas-STOPP-flagga LÖST:** ADR 0044 Proposed→Accepted + gate-aktivering genomförd & CI-grön-verifierad. Inga TD lyfta (§9.6 — alla i-fas, in-block). Inga prod-deploys. **(Historik, levererat tidigare samma dag) TEST-COVERAGE-SIDOSPÅR (HEAD `472dbdb`).** Reproducerbar in-repo coverage-mekanism + ADR 0044 (då Proposed) + genuina luckor stängda. Suite 1139→**1156** (+17, 0 failed). First-party (denna mekanism): **Line 92.1% / Branch 84.5% / Method 90.2%** (Application 97.7%/91.1%, Domain 95.3%/93.3%). A: `Microsoft.Testing.Extensions.CodeCoverage` 18.6.2 (CPM) + `dotnet-reportgenerator-globaltool` 5.5.10 (`.config/dotnet-tools.json`) + `scripts/coverage.ps1`+`.sh` (rå cobertura ofiltrerad audit-trail, first-party-filter report-time → gitignorad `artifacts/coverage/`) + CI-jobb `coverage` PROPOSED (continue-on-error, ej i ci.needs). B2 (GDPR §5.4 HÖGSTA PRIO): DeleteAccountCommandHandler 71.8→**100%** branch, security-auditor **GO 0 Crit/High/GDPR** (cascade-completeness genuint bevisad). B1: ListInvitationsQueryHandler+DTO 0→**100%**. B3 (CTO Approach (a), ej Gemini extract-to-service): AuditLogRetentionJob→100%, SyncPlatsbankenSnapshotJob→98.1%, PurgeStaleRawPayloadsJob invalid-config-gren (rest=ExecuteUpdateAsync provider-bound = Worker.IntegrationTests-nivå, ej unit-lucka, dokumenterad). dotnet-architect (infra) + senior-cto-advisor (gate-modell + Hangfire-approach + ADR-granularitet) + test-writer×3 + security-auditor + code-reviewer×3 (alla GO 0 Block/0 Maj). Commits `2d262ee` (infra+ADR), `6768700` (B2), `472dbdb` (B1+B3). **Klas-STOPP-flagga LÖST 2026-05-17** (se status-header ovan): ADR 0044 `Proposed→Accepted`-flip + CI-gate-aktivering genomförd, main-CI grön run `25989344497`. **Flaggat för Klas/CTO-triage (ej fixat — utanför test-only-scope, security-auditor bekräftade ej GDPR-risk):** `Resume.SoftDelete` saknar idempotens-guard som `Application.SoftDelete`/`JobSeeker.SoftDelete` har — duplicate-domain-event/timestamp-hygien-inkonsistens, ej erasure-defekt (DeleteAccount-path korrekt via early-return). **README-skryt-omskrivning = separat senare Klas-uppgift** (denna session levererade grunden + siffrorna; §9.2-skyddad fil, skrivs ej utan explicit Klas-GO). **(Historik) PRIO-1 CI-FIX LEVERERAD 2026-05-17 (HEAD `b3772a3`, main-`build`-CI GRÖN run `25986194273`).** main var RÖD: `GetTaxonomyEndpointTests.GET_taxonomy_labels_resolves` IndexOutOfRange (tom regions). Committad handoff-diagnos ("saved-search singleton cache-poisoning") **empiriskt falsifierad** av test-coverage-CC §9.4 (GetTaxonomyEndpointTests failar ENSAM; single-variable-revert bevisade saved-search icke-kausal). Verklig rotorsak: `ApiFactory.InitializeAsync` `Services`-access triggar host-start → `IHostedService.StartAsync` FÖRE `MigrateAsync` (.NET 10-semantik, web-verif. dotnet/aspnetcore #60370) → TaxonomySnapshotSeeder+IdempotentAdminRoleSeeder bailar på 42P01 → oseeded hela delade collection-livstiden. Pre-existerande latent fixtur-defekt; prod opåverkad (Migrate kör DDL före trafik, ADR 0043 Beslut B). senior-cto-advisor Approach D/B (fix the cause, ej symptom): kör de två idempotenta seedrarna explicit EFTER migrations, riktat. **INGEN prod-kod, ingen ADR-amendment, ingen security-auditor, ingen Klas-STOPP** (entydigt mot Beck/Meszaros/Fowler/Martin). `src/` orört. code-reviewer GO 0/0/2. Full Release-svit 1139/1139 0 failed. Handoff-doc korrigerad (`docs/reviews/2026-05-17-ci-taxonomy-singleton-regression-handoff.md` — falsifiering + lösning + lärdom dokumenterad, originaltext bevarad som granskningstrail). **Nästa: återuppta TEST-COVERAGE-SIDOSPÅR** (CTO/architect-besluten redan tagna före CI-injektionen — se nedan). **(Historik) FYND 2 FULLT DEPLOYAD PÅ DEV 2026-05-17 — väntar Klas slutgodkännande av skärmbilder (HEAD `782414d` pushad, origin/main).** Klas-GO "allt enligt rek": ADR 0043 **Accepted** (commit `8c7e582`/`5075439`) + backend deployad (`v0.2.11-dev` run `25983313208` success, `/api/v1/job-ads/taxonomy` LIVE 200, 21 län/21 yrkesområden/2323 yrken seedade, ETag+private verifierad) + frontend pushad (`c79aace` namn-väljare, `1fc3b1b` död JobAdMultiSelect bort, `782414d` docs) → Vercel-deployad. **design-reviewer: kod-review APPROVED 0/0/0 + post-deploy skärmbilds-granskning APPROVED 0/0/2** (Klas kan slutgodkänna). visual-verify 56 shots live (`C:\tmp\jobbpilot-visual\20260517-0849`). concept-id (`MVqp_eS8_kDZ`/"OR-bevakning") HELT borta ur sök-ytan, ersatt av svenska hierarkiska väljare (Ort=Län enkelnivå, Yrke=Yrkesområde→Yrke), Platsbanken-paritet, light+dark verifierat. cron-grön CONFIRMED tidigare (5005 graceful + korpus 5 380→19 816, konvergens-trajektoria). **Klas slutgodkände skärmbilderna 2026-05-17 ("GO enligt rek") — Batch 6-grinden STÄNGD; Fynd 2 helt levererad & accepterad.** **SAVED-SEARCH-NAMN-BATCH KLAR (Klas-GO "enligt rek") — sista concept-id-läckan stängd:** CTO Approach A (server-side namn-berikning, ej bulk-endpoint — Beslut D-cap orörd). `ListSavedSearchesQueryHandler` injicerar `ITaxonomyReadModel` (in-process O(1), per sökning Ssyk/Region), `SavedSearchDto` += SsykLabels/RegionLabels (additiv; ADR 0039 orört), GetSavedSearch tomma labels (scopat). Frontend: /sokningar-listan visar svenska namn (font-mono bort), "SSYK-kod"→"yrke", e2e + visual-verify-skript (jobb-chip-filled→selectOption) uppdaterade. test-writer TDD (4c3b9f5 RÖD→GRÖN; test-arrange-fix q=x→xy), backend App 441/Arch 56 grön, vitest 31/31. CTO + test-writer + nextjs-ui-engineer + design-reviewer APPROVED 0/0/0 + security-auditor GO 0 Crit/High/GDPR (1 Minor doc-kommentar in-block-fixad). Commits `4c3b9f5` (tester) + `04b679e` (backend+frontend buntat — cohesivt feature) + `14662db` (doc-fix) + `6a29813` (docs). **Deployad `v0.2.12-dev` (run `25985349578` success), verifierad: `/api/ready` 200, `GET /api/v1/saved-searches` 200 med ny kod live.** Live-populerad-label-skärmbild ej tagen — dev-test-kontot har noll sparade sökningar (tomt-tillstånd oavsett); logiken bevisad av 441 gröna backend-tester (inkl. explicita label-tester m. mockad ITaxonomyReadModel) + design-reviewer APPROVED 0/0/0. **Nästa: TEST-COVERAGE-SIDOSPÅR** (startprompt levererad i chatten 2026-05-17 — reproducerbar in-repo coverage + stäng ListInvitations/DeleteAccount-GDPR/Hangfire-luckor; README-skryt = senare egen uppgift) FÖRE FAS 3. **Observation (ej krav, för Klas/framtid):** ingen per-JobSeeker count-cap + icke-paginerad saved-searches-list-query (pre-existerande, §9.6 saknad paginerings-domän). **(Historik) PÅGÅENDE (Klas-GO "enligt rek"):** saved-search-namn-batch — senior-cto-advisor-triage för bulk concept-id→namn (criteriaSummary + Spara-hjälptext) som överskrider ADR 0043 Beslut D-cap. **(Historik) PENDING KLAS-BESLUT:** (1) slutgodkänn skärmbilderna (Batch 6-grind); (2) saved-search-list `criteriaSummary` visar rå concept-id — bulk-namnuppslag överskrider ADR 0043 Beslut D reverse-lookup-cap (fan-out-DoS, ej designad) → §9.6 separat förhandlad batch/CTO-triage (samma copy även i Spara-sökning-hjälptext "SSYK-kod"); (3) visual-verify-skript `jobb-chip-filled` stale (`.fill()` mot `<select>`) → byt till `selectOption`, nextjs-ui-engineer/CC-uppföljning. Inga TD-lyft. **(Arkiv) POST-FAS-2 SÖK-YTA + cron-grön (autonom natt-session, lokala commits pushade).** Tidigare status: HEAD `75f0510` lokalt — EJ pushad, väntade Klas push-GO. Klas live-jämförde /jobb mot Platsbanken → 2 fynd. **Fynd 1 (PUSHAD `37338db`+`a4afa40`):** Sortering ut ur Filter-disclosure till egen alltid-synlig kontroll + tydligare etiketter ("Stänger snart/senare", enum oförändrad). design-reviewer APPROVED 0/0/0, vitest 358. CTO Fråga 2 = copy-only in-block. **Fynd 2 (LOKALT committat, EJ pushat — Klas push-GO + ADR Accepted-flip väntar):** Taxonomi-ACL (ADR 0043 Proposed) — JobTech concept-id (`MVqp_eS8_kDZ`) försvinner ur sök-ytans inmatning, ersätts av svenska namn-väljare. Backend KLART: `ITaxonomyReadModel`-port + committad embedded `taxonomy-snapshot.json` (21 län, 21 yrkesområden, 2323 yrken, kanoniskt dedupliserad) + idempotent version-medveten seeder + singleton retry-on-fault-cache + GET /taxonomy(ETag+private)/labels + TaxonomyReadPolicy 20/60s + migration F2TaxonomySnapshot. CTO Approach A + MAP-1/2/3 + scope-fork (Variant A: Län+Yrke, ej kommun = payload-trigger) + defekt-triage (#1 graf→dedup i generator, #2 validator-cascade, #3 fixtur-paritet RemoveStartupSeeders). dotnet-architect + senior-cto-advisor×4 + adr-keeper + db-migration-writer + test-writer (1130 grön, 0 failed) + security-auditor GO (0 Crit/High/GDPR). **SearchCriteria/JobAdSearch/shadow-props ORÖRDA (ADR 0043 Beslut E).** Frontend (hierarkiska väljare ersätter JobAdMultiSelect) = NÄSTA STEG, scopad för Klas (visual-verify kräver deploy = Klas-GO; FE-flagga från auditor: rendera labels som text). Lokala commits (ej pushade): `2e8e380`/`c86daca` ADR 0043, `0f46dad` migration, `67121d4`/`ac9e8da` tester, `75f0510` backend-feature, + docs-commit. **cron-grön CONFIRMED GRÖN:** 02:00 UTC-snapshot post-v0.2.9/10-dev: `[5401] startad` → `[5004]` trunkerad attempt 1/2 (fångad enumeration-boundary, ej ofångad storm) → **`[5005]` bounded retry uttömd efter 3, graceful avslut (36570 konverterade)**. Korpus 5 380→5 477→**19 816** (+14k från en graceful run, konvergerar mot ~40k+). Storm-borta + korpus-trajektoria + 5005 = ADR 0032-amendment gate-def **HELT UPPFYLLD**. **(Arkiv) FAS 2 FORMELLT STÄNGD 2026-05-17 (HEAD `31a2c51`).** Samlad session Batch 0–6: ingestion payload-trunkerings-hybrid-fix (ADR 0032-amendment Accepted; storm-borta CONFIRMED på dev; konvergens-risk medvetet accepterad, korpus-tillväxt-trajektoria = gate-def) + sök-yta-omdesign B–E (ADR 0042 Accepted + ADR 0039 Beslut 3 partiell supersession): B SearchCriteria single→multi (CTO Yta A3), C typeahead C1 (btree functional partial-index), D relevans D2-ILIKE, E IsNew/Since, A kollaps-filter + multi-select + live-typeahead frontend. Deployad `v0.2.9-dev` (Batch 1) + `v0.2.10-dev` (Batch 2–5 + 2 migrations Phase E applied) + Vercel (`31a2c51`). 7 Klas-STOPP; CTO×7/architect×3/security-auditor×3 PASS/code-reviewer×6 GO/db-migration-writer×3/test-writer/adr-keeper/design-reviewer APPROVED (VETO lyft run 0147). Klas hård input-regel 2026-05-17 (rena input-fält, ingen exempel-placeholder, hint via aria-describedby) tillämpad + kodifierad i jobbpilot-design-components/-copy; ADR 0038 placeholder-formulering upphävd. Svit 1083 backend + 357 frontend grön. Fas 2-TD-triage (Klas-direktiv): TD-13/27 Fas 2-defer Klas-bekräftad (EDPB CEF 2025 omverifierad 2026-05-17 — RDS KMS at-rest = Art. 32-standard, crypto-erasure ej krav); övriga "Fas 2"-TD = Trigger/skala (ej genuin skuld, etikett-städning separat docs-keeper-touch). **Klas verifierar rena auth-fält live** (fresh auth-korpus blockerad av Vercel Attack Challenge Mode — infra, ej kod; design-reviewer källgranskade input-regeln verbatim). **Pending operativt:** cron-grön async-followup (snapshot-graceful EventId 5005/5402 + korpus-trajektoria vid/efter 02:00 UTC — storm-borta CONFIRMED, gate-def uppfylld). **Fas 3 (Application Management) kräver explicit Klas-GO för sessionsbyte (§9.2).**

**(Arkiv) F2 INGESTION ROTORSAK-FIX (HYBRID) — BATCH 1 2026-05-16.** Samlad session (ingestion-fix + sök-omdesign B–E, 6 batchar). Batch 0-discovery (CloudWatch, dev `v0.2.8-dev`) verifierade rotorsak: `/v2/snapshot` >364 MB singel-GET termineras icke-deterministiskt mid-stream → ofångad `JsonException` vid enumeration → Hangfire-retry-storm; HttpClient.Timeout/MaxResponseContentBufferSize/Polly MOTBEVISADE (trunkering 87–442 s, 364 MB<500 MB-cap). senior-cto-advisor `ad8564aafc29be5a0` förkastade ren A2 efter web-verify (JobTech-doc: snapshot-först-pattern, ingen stream-only-backfill) → **hybrid**: snapshot bevaras + görs trunkerings-tålig (enumeration-boundary-catch + bounded retry, MA 3.1=A), stateless (MA 1.1=A), behåll job/id (MA 2.1=A), delad limiter (MA 4.1=A), drift=recurring inkrementell (Klas-GO, ingen timeout-höjning). **Batch 1 Part 1 levererad** (`PlatsbankenJobSource` resilient enumeration + regressionstest, svit 1043 grön, build 0/0, code-reviewer GO 0/0). **ADR 0032-amendment 2026-05-16 Accepted** (Klas-GO; CC-draft = medvetet §9.4-override, dokumenterat). Snapshot-paus-operatörsprocedur (Worker→desired-count 0) levererad till Klas. Konvergens-risk medvetet accepterad: ~40k+ tar dygn; STOPP 3 mäter korpus-tillväxt. Hybrid = ingen separat Part 2-kod (CTO: stream oförändrat mönster, §3 förtydligas ej supersederas). **Batch 6 KLAR (committad 5110b45, frontend):** ADR 0042 Beslut A–E frontend (nextjs-ui-engineer `ae8c96441b94d87ca`). A kollaps-filteryta (disclosure, resultat-först, civic regel 3/7). B multi-select taxonomi-chips (max 10, URL-driven, ersätter concept-id-fritext). C live-typeahead (CTO `a377901ce353b58e7` Variant A: self-contained debounce-hook ≥300ms/min 2/AbortController — EJ TanStack, YAGNI/§9.2; abort-on-unmount in-block). D snabbsortering inkl Relevance (disabled utan q). E Ny-badge (isNew, rullande 7-dygnsfönster, civic pill). F (CV-match) HÅRT OUT. vitest 357/357, tsc clean, lint 0 err. i18n: ingen messages/sv.json i repot (literala svenska strängar = on-disk-konvention, §9.1). **NÄSTA: STOPP 7 — backend tag-push v0.2.10-dev (Batch 1–5 + migrations F2SearchCriteriaMultiValue + F2SuggestTitlePrefixIndex, STOPP-5-godkända) + frontend Vercel (main-push auto) → auth-gated visual-verify full korpus → design-reviewer VETO mot bilder → Klas approve + since-fönster-bekräftelse → Fas 2 FORMELL STÄNGNING.**

**(Föregående) Batch 5 KLAR:** ADR 0042 Beslut C — C1 typeahead `SuggestJobAdTermsQuery` (lokal job_ads.Title ILIKE-prefix, distinkt, Active-only, Take-cap). CTO Variant A: btree functional partial-index `lower(title) text_pattern_ops WHERE status='Active' AND deleted_at IS NULL` (migration `F2SuggestTitlePrefixIndex`, ingen extension, raw-SQL F2P9-mönster). `LikePattern.EscapePrefix` + explicit 3-arg `EF.Functions.Like(...,ESCAPE '\')` (Clean Arch provider-agnostiskt). Ny `SuggestPolicy` per-user FixedWindow 30/10s IOptions-bound (least common mechanism, ej ListRead-återanvändning). Endpoint `GET /api/v1/job-ads/suggest` auth-gated. DoS-floor min-prefix≥2+Limit-cap pre-query. security-auditor PASS 0 Crit/High/GDPR (rate-limit 30/10s bekräftat, Title=publik metadata ej PII per ADR 0032 §8), code-reviewer GO 0/0/1 Minor FYI, db-migration-writer CTO-A-konform. Svit **1083 grön** (Domain 308/App 408/Arch 51/Api.Int 284/Worker 26/Migrate 6), build 0/0. STOPP 5+6 GO. **NÄSTA: Batch 6 (frontend B–E: kollaps-filter A, multi-select, typeahead, sort, IsNew-badge; nextjs-ui-engineer + design-reviewer VETO + visuell verifiering → STOPP 7) → Fas 2 formell stängning.**

**(Föregående) Batch 4 KLAR:** ADR 0042 Beslut E (`ListJobAdsQuery.Since`+`JobAdDto.IsNew`, runtime-ej-VO; RunSavedSearch/GetJobAd IsNew=false) + Beslut D (`JobAdSortBy.Relevance=4`, D2 ILIKE-heuristik exakt/prefix/contains via EF.Functions.Like+ToLower provider-agnostiskt; `ApplySort(source,sortBy,q)`-signatur; invariant Relevance-kräver-q i SearchCriteria.Create + ListJobAdsQueryValidator). code-reviewer GO 0/0/1 Minor FYI (pre-existing LIKE-konvention, ej in-block §9.6). Svit **1074 grön** (Domain 308/App 402/Arch 51/Api.Int 281/Worker 26/Migrate 6), build 0/0. Ingen Klas-STOPP (plan: code-reviewer+grön svit). **NÄSTA: Batch 5 (C typeahead C1 — architect INNAN kod + security-auditor BLOCKING + db-migration-writer index → STOPP 5/6).**

**(Föregående) Batch 3 KLAR:** SearchCriteria Ssyk/Region single→multi (ADR 0042 Beslut B, CTO Yta A3). IReadOnlyList + 4 invarianter + explicit Equals/GetHashCode (jsonb-dedupe-grund). Infra `SearchCriteriaConverters.cs` (System.Text.Json tolerant default-deny + EF ValueConverter/ValueComparer; Domain EF/serialiserings-fritt). `JobAdSearch.ApplyCriteria` list→IN(...). Migration `F2SearchCriteriaMultiValue` tom no-op (A3 — kolumn redan jsonb; Klas: behåll). test-writer FÖRST/TDD. security-auditor PASS 0 Crit/High/GDPR (M1 cap-paritet fixad in-block §9.6), code-reviewer GO 0/0, db-migration-writer A3-konform. Svit **1069 grön** (Domain 306/App 400/Arch 51/Api.Int 280/Worker 26/Migrate 6), build 0/0. STOPP 5+6 GO. **NÄSTA: Batch 4 (E `ListJobAdsQuery.Since`+DTO `IsNew` runtime-ej-VO; D `JobAdSortBy.Relevance` D2-ILIKE + ApplySort-signatur+q-invariant).**

**(Föregående) Batch 1** committad (`b9e757a` feature + `40e90b4` docs, pushad). **STOPP 3:** `v0.2.9-dev` tag-pushad (CC på Klas-GO), deploy in_progress (run `25970027351`); gate-def Klas-beslut = **grön = storm-borta + korpus-tillväxt-trajektoria** (ej literal ~40k+; ~40k+ konvergerar i bakgrunden över dygn) → Batch 2–6 non-stop. **Batch 2 KLAR:** ADR 0042 (sök-yta-IA A–F) Accepted + ADR 0039 Beslut 3 partiell supersession + README (STOPP 4 GO). **NÄSTA: Batch 3 (B SearchCriteria Ssyk/Region single→multi, test-writer FÖRST/TDD, dotnet-architect INNAN kod, security-auditor BLOCKING maxantal-cap, db-migration-writer om jsonb-shape→STOPP 5).** STOPP 5–7 enligt LÅST PLAN. Cron-grön verifieras async (rapporteras separat).

**(Föregående) F2 INGESTION-CRON-VERIFIERING RÖD — FAS 2 FORMELL STÄNGNING FÖRBLIR PAUSAD 2026-05-16 (HEAD `24f9dad` + docs-commits denna session). Snapshot-cron verifierad i CloudWatch (`/aws/ecs/jobbpilot-dev/worker`, deployad `v0.2.8-dev`): `SyncPlatsbankenSnapshotJob: startad [5401]` 7d=`60`, `klart [5402]` 7d=`0` — EXAKT samma "60 starts/0 completes"-symptom som rotorsaken FÖRE v0.2.6-dev, men NY rotorsak: fatal ofångad `System.Text.Json.JsonException: ...reached end of data` vid bytepos 26/41/47 MB → Platsbanken-snapshot-JSON kapas mitt i strömmen → dör före `LogCompleted` → Hangfire `AutomaticRetry`-loop. v0.2.6-dev:s child-scope-per-item fixade 23505-ackumulering men INTE payload-trunkering → defekten oadresserad (andra "falskt fixad"-mönstret i samma pipe). Sekundärt icke-fatalt: `Npgsql 23505` 46 760/24h (≈ hela ~47k-korpusen, child-scope fångar per item) + `Polly RateLimiterRejectedException`. Korpus (autentiserad API): ofiltrerad `/api/v1/job-ads` totalCount=`5 380` (förväntat ~40k+); `q=utvecklare`=`137` oförändrat → ingen full snapshot lyckats; endast `*/10 SyncPlatsbankenStreamJob` (inkrementell) fyller på. **Båda verifieringssteg RÖDA → Fas 2 kan EJ stängas (DoD CLAUDE.md §8 punkt 4).** senior-cto-advisor inline (agentId a5c2b2ca57caee056): (1) Fas 2 FÖRBLIR PAUSAD — mekanisk DoD-konsekvens, ej Klas-GO för pauseringen; (2) rotorsaks-fix = SEPARAT fix-session m/ obligatorisk dotnet-architect-rond + Klas-GO, **INGEN TD** (§9.6-pressad: ej annan fas/ej saknad dependency; Major/Fas-Nu → §9.7 förbjuder TD-kategori) — lever som STOPP-underlag + session-logg + kommande ADR 0032-amendment; (3) runbook-drift-fix gjord in-block (rad 120 `/ecs/jobbpilot-dev-migrate`→`/aws/ecs/jobbpilot-dev/migrate`, family-rader verifierat korrekta orörda); (4) Hangfire retry-storm = Klas-eskalering NU, CTO rekommenderar paus av `sync-platsbanken-snapshot` på dev tills fix (verkställs EJ av CC — Klas-GO + AWS-operatörsåtgärd, manuell trigger är 410 per ADR 0032 Amendment). Ingen egen ingestion-debug/fix påbörjad (Klas-STOPP-flagga + förbud). Se `docs/sessions/2026-05-16-1450-f2-ingestion-verify-red.md`. KLAS-ESKALERINGAR: (a) bekräfta Fas 2 pausad; (b) ingestion-fix egen session — när; (c) pausa snapshot-jobbet på dev nu?**

**(Föregående) F2 SAVED SEARCHES LIVE-VERIFIERAD + a11y ADR 0041 LEVERERAD 2026-05-16 (HEAD `64a6bf8`, deployad `v0.2.7-dev`+`v0.2.8-dev`/Vercel). Auth-gated visuell verifiering KLAR — denna sessions huvudleverans. Deploy `v0.2.7-dev` @ `29cd4ae` (migration `F2SavedSearches` applicerad, CloudWatch EventId 63, /api/ready 200). `visual-verify.ts` utökat med opt-in auth-läge (senior-cto-advisor Variant A): direkt backend-login, `__Host-`-cookie in-memory (aldrig disk, §5.4-risk eliminerad vid källan), temp-fixture-sökning, 3 vp × light/dark. Dedikerat dev-test-konto skapat (Variant C cred-plats `%USERPROFILE%\.jobbpilot\dev-test-creds.env`, utanför repot; runbook+MEMORY-pekare, aldrig creds). design-reviewer→nextjs-ui-engineer auktoritativ token-math→**WCAG 1.4.11 a11y-Blocker bekräftad** i delad `ui/dialog.tsx` (dark dialogyta=dimmad canvas, kant 1.35:1<3:1). senior-cto-advisor Alt 2 + Klas-GO: **ADR 0041 (Accepted)** — nytt semantiskt token `--jp-border-modal` (light `#E2E8F0`/dark `#64748B`=slate-500, ≈3.6:1) + `ui/dialog.tsx` `border-border`→`border-border-modal`. Deployad (Vercel main-push `64a6bf8` + backend `v0.2.8-dev`), live-verifierad: serverad CSS har tokenet, **design-reviewer re-review 0/0/0, Blocker RESOLVED, noll regression**, Klas slutgodkände bilderna. security-auditor PASS (0 Crit/High/Med, 2 Low informativa). Rök-test live grönt: login→create 201→list→**run 200 (paged, totalCount=137 för "utvecklare")**→scoping okänt-id 404 (ADR 0031)→delete 204→borttagen 404. Commits `12fc9e6` (a11y/ADR 0041) + `64a6bf8` (visual-verify auth-läge) pushade; docs-commit denna session. **FAS 2 FORMELL STÄNGNING PAUSAD** — gaten "(a) ingestion-cron verifierad" tillhör separat lokal session (Klas-beslut; EventId 5402 + ~40k+ korpus). `run`=137 träffar visar data finns men full cron/korpus-verifiering är separat spår. ADR 0005-observation: dev-test-kontot skapat via icke-flag-gejtat `/api/v1/auth/register` (kill-switch täcker bara waitlist/invite) — dokumenterad i runbook, CTO+auditor: ej formell TD, triageras i auth-fokuserad touch.**

**(Föregående) F2 SAVED SEARCHES LEVERERAD END-TO-END 2026-05-16 (HEAD `d602968`). Sista oimplementerade Fas 2-leverabeln — Fas 2-milstolpen "söka jobb på Platsbanken + spara sökningar" är FUNKTIONELLT KLAR (modulo ingestion-live-verifiering = separat spår + auth-gated visuell verifiering = pending live-deploy). ADR 0039 (Accepted, Klas-GO): SavedSearch AR + SearchCriteria VO + 6 endpoints JobSeeker-scoped + JobAdSearch delad SPOT-modul (Beslut 1) + run=query/last_run_at→Fas 5 (Beslut 2) + SortBy-i-VO (Beslut 3) + notification lagra-ej-dispatch→Fas 5 (Beslut 4). Klas mid-session-input "smart CV-filter" → ADR 0040 (Proposed, Fas 4+) + BUILD.md §18-backlog (CTO-vägd, gatear ej kod). Backend: 113 tester, Domain 293/App 398/Arch 51/Integration 268 gröna, build 0/0. Frontend: SaveSearchButton(/jobb) + /sokningar + /sokningar/[id] + DeleteSavedSearchDialog, 334 vitest/tsc 0/lint 0. dotnet-architect+CTO(×3) INNAN kod; code-reviewer 0 Block/0 Maj, security-auditor 0 Crit/High/Med, design-reviewer approved (Blocker+2 Minor in-block, re-review OK). OBSERVATION 1→TD-84 (CTO Alt B, projekt-brett, ingen ADR 0031-läcka). Commits: `b82e7cf` ADR 0039, `ae7a521` ADR 0040+BUILD, `b18074f` backend, `717dbd9` TD-84, `d602968` frontend — alla pushade. PENDING: visuell verifiering auth-gated → live-deploy (tag-push=Klas-GO); F2 ingestion-cron-verifiering = separat lokal session (AWS SSO).**

**(Föregående) F2 JOBB-INGESTION ROTORSAK FIXAD + KODKOMPLETT — Commit 1+2+3 + docs pushed 2026-05-16 (HEAD `d454d23`). Snapshot-jobbet 60 starts/0 completes på dev (CloudWatch) pga uncaught Npgsql 23505: hela ~47k-loopen i EN DI-scope → ackumulerad EF-tracker + UnitOfWorkBehavior-SaveChanges bröt ADR 0032 §5 per-command-isolering vid dubbletter. Korpus ~5k av ~47k. Fix: child-scope per item (CTO Variant B, Commit 1 `347b238`) + IAsyncEnumerable-streaming ~300MB OOM-defekt + rate-limiter bounded queue (Commit 2 `70a7c54`) + admin-endpoint avvecklad till 410 (CTO X4, Commit 3 `d454d23`). ADR 0032 §5-clarification + §9-amendment (Klas-GO). 929 tester gröna, build 0/0, code-reviewer 0 Blockers/Majors, CTO+dotnet-architect inline. Cadence: behåll */10 + 0 2 (CTO-rek, Klas-GO). **DEPLOYAD `v0.2.6-dev` (run 25956939801 success, /api/ready 200).** 410-copy korrigerad (ingen Hangfire-dashboard exponerad — Worker headless) + TD-83 lyft (operatörs-yta för Hangfire-jobb, Minor/Trigger). KVARSTÅR: ingen manuell trigger möjlig (ingen dashboard, admin-endpoint 410) → snapshot kör automatiskt via cron **02:00 UTC inatt**; CC verifierar imorgon (CloudWatch EventId 5402 första completionen + `job_ads`-count → ~40k+). HEAD efter copy-fix + docs.**
**(Föregående) UI-REFACTOR DESIGNSYSTEM v2 LEVERERAD 2026-05-16 — civic-utility slate-palett + dark mode (`data-theme`, no-flash, prefers-color-scheme auto), Shell Variant B (sektionerad sidebar, 4px brand-vänsterkant, ADMIN rollgejtad), civic landing, nya `.jp-*`-primitiv. DESIGN.md + 5 skills + 2 agenter → v2. ADR 0037 (Klas-GO). design-reviewer 2 Blockers + 3 Majors åtgärdade in-block. tsc/lint/313 vitest/next build gröna. Ej deployad (tag-push kräver Klas-GO). Öppen punkt: `.jp-h1`/display font-weight-drift jobbpilot.css(500/36px) vs tokens-spec(600/56px) — Klas-auktoritetsbeslut kvarstår.**
**Iteration 2:** broad-screen-centrering + dubbel-login + jobb-separation + post-login-redirect + visual-verify-rutin + TD-82.
**Iteration 3 (ADR 0038 — läsbarhets-omkalibrering):** Klas live-jämförde mot Platsbanken → v2 för litet/tunt. CTO+Klas-GO: GOV.UK-läsbarhetsgolv (brödtext 16px, lede 17, h1/h2/h3 vikt 600, mono data 13/secondary, input 44px, knapp 40, placeholder-exempel borttagna, text-tertiary endast dekorativt). Global token-fix, civic-ledger-form orörd. ADR 0038 (delvis supersession 0037, stänger jp-h1-driften). design-reviewer mot screenshots: ✓ approved 0 blockers.
**Senast uppdaterad:** 2026-05-23 (F6 P5 Punkt 2 PR5+PR5b+UX-polish FULLT LEVERERAT — ADR 0063 NY Accepted per-user overlay-status batch-port, TD-87/TD-88 lyfta, 10 Klas-feedback-punkter adresserade, EF Core 10 batch-Contains-translation-bugg fixad client-side; session-end docs-synk denna commit)
**HEAD:** `2b00216` (origin/main — chore(ux) NY-debug-cleanup + dokumentera last-seen-jobs-modell; ovanpå PR5-batchen `c2f0ac5`/`1a688b0`/EF-fix-paret `31cd5c5`+`3849976`/UX-svans `6889531`/`237711c`/`2383313`)
**Deploy:** **`v0.2.59-dev` pekar på `3849976`** (BE-leverans — efterföljande FE-only-commits behövde ingen ny tag). Föregående LIVE-tag `v0.2.58-dev` deployad. Korpus-konvergens Punkt 1 (~72h-fönstret från 2026-05-23 v0.2.57-dev) fortsätter i bakgrunden.
**Långsiktig bana:** `docs/steg-tracker.md`
**Tech debt:** `docs/tech-debt.md` (aktiva, +TD-80) + `docs/tech-debt-archive.md` (stängda)
**Prod-checklist:** `docs/runbooks/v0.2-prod-launch-checklist.md`

---

## Aktivt nu — F6 P4 FTS-skifte (levererad & pushad 2026-05-21)

Se `docs/sessions/2026-05-21-1210-f6-p4-fts-skifte.md` för full retrospektiv.

| Steg | Innehåll | Status |
|---|---|---|
| 1 | FTS `search_vector` STORED generated tsvector + GIN-index — migration `F6P4FtsSearchVector`, `JobAdConfiguration` shadow-property, `TestAppDbContextFactory` `IModelCustomizer`-strip (commit `1b04765`) | ✅ |
| 2 | Lager-refaktor: sök-komposition Application→Infrastructure bakom `IJobAdSearchQuery` (`SearchAsync`+`CountAsync`) + `JobAdFilterCriteria`/`JobAdSearchCriteria`, Infrastructure-impl `JobAdSearchQuery`, 3 handlers → tunna adaptrar, `JobAdSearch.cs` borttagen, DI, arch-test `JobAdSearchLayerTests` (commit `1eec29c`) | ✅ |
| 3 | `explain-search` FTS-mode för post-deploy-verifiering (commit `6f2769b`) | ✅ |
| 4 | ADR 0062 NY (Accepted) + ADR 0061/0039-amend + review-rapporter (commit `b70b548`) | ✅ |
| 5 | Sök-laddningsindikator (skeleton) i `/jobb` — design-reviewer VETO rond 1 → fix → GO rond 2 (commits `57feaac` + `95bcb74`) | ✅ |
| 6 | Reviews: code-reviewer 0 Block/2 Major-åtgärdade/4 Minor; security-auditor APPROVED 0 Block/Crit/High/Medium, 1 Low non-regression | ✅ |
| 7 | Tester gröna (Domain 399, App.Unit 535, Arch 73, integration FTS 11 + filter 18 + SavedSearch/RecentSearch 26, frontend vitest 105/105); `build`-CI grön run `26218140677` | ✅ |
| 8 | 6 commits pushade (`2adcec9`→`95bcb74`) + session-end docs-synk | ✅ |

**Commits (6, `2adcec9`→`95bcb74`):**

| Commit | Typ | Innehåll |
|---|---|---|
| `1b04765` | `feat(job-ads)` | FTS `search_vector` generated column + GIN-index — migration `F6P4FtsSearchVector`, `JobAdConfiguration` shadow-property, `TestAppDbContextFactory` `IModelCustomizer`-strip |
| `1eec29c` | `refactor(job-ads)` | Sök-komposition Application→Infrastructure bakom `IJobAdSearchQuery` — port + `JobAdFilterCriteria`/`JobAdSearchCriteria`, impl `JobAdSearchQuery`, 3 handlers → adaptrar, `JobAdSearch.cs` borttagen, DI, arch-test `JobAdSearchLayerTests` |
| `6f2769b` | `feat(migrate)` | `explain-search` FTS-mode för post-deploy-verifiering |
| `b70b548` | `docs` | ADR 0062 + ADR 0061/0039-amend + review-rapporter |
| `57feaac` | `feat(web)` | Sök-laddningsindikator (skeleton) i `/jobb` |
| `95bcb74` | `docs(reviews)` | Sök-spinner design-review-rapport |

**Agenter:** dotnet-architect (port-design + FTS-LINQ-API-verifiering + test-topologi), db-migration-writer (migration), senior-cto-advisor (Variant B port-kontrakt — `CountAsync` + `JobAdFilterCriteria`-split, 3:e konsument `ListRecentSearches`), test-writer (test-refaktor), code-reviewer, security-auditor, adr-keeper (ADR 0062 + amends), nextjs-ui-engineer + design-reviewer (spinner — VETO rond 1 → GO rond 2).

**PENDING (Klas):** tag-push `v0.2.56-dev` (Klas-GO) → triggar dev-deploy → deploy-verifiering: explain-search-mode EXPLAIN-bekräftelse (`Bitmap Index Scan` på `ix_job_ads_search_vector`; perf-mål "lärare" <0.2s, alla q-termer <2s). P2-backfill ~51k legacy-rader pending nästa `sync-platsbanken-snapshot` 02:00 UTC — verifiera ssyk/region-filter mot hela korpusen.

**Nästa:** deploy-verifiering → F6 P4b SavedJobAds (separat backend-prompt) → F6 P4c query-token-parser → F6 P4 retention.

---

## Aktivt nu (historik) — F2 live-verifiering + ADR 0041 a11y-fix (levererad 2026-05-16)

Se `docs/sessions/2026-05-16-1430-f2-live-verify-adr0041.md` för full retrospektiv.

| Steg | Innehåll | Status |
|---|---|---|
| 1 | Deploy `v0.2.7-dev` @ `29cd4ae` (Klas-GO) — migration `F2SavedSearches` applicerad (EventId 63), /api/ready 200 | ✅ |
| 2 | `visual-verify.ts` auth-läge (CTO Variant A) + runbook tre-nivå/env-kontrakt + https-guard | ✅ |
| 3 | Dedikerat dev-test-konto + cred-persistens Variant C (utanför repot) + runbook+MEMORY-pekare | ✅ |
| 4 | Auth-gated capture 48 shots × 3 vp × light/dark → design-reviewer | ✅ |
| 5 | a11y-Blocker (WCAG 1.4.11 dark dialog) → ADR 0041 Alt 2 (Klas-GO) → token + `ui/dialog.tsx` | ✅ |
| 6 | Deploy a11y-fix (`v0.2.8-dev` + Vercel) → re-capture live → design-reviewer re-review 0/0/0 RESOLVED | ✅ |
| 7 | security-auditor PASS + rök-test live grönt (create/list/run-137/scoping-404/delete) | ✅ |
| 8 | Commits `12fc9e6`+`64a6bf8` pushade + DESIGN.md-enradare (Klas approve) + docs | ✅ |

**Klas-godkänt:** auth-gated bilderna (`20260516-1424`) slutgodkända; ADR 0041-token-amendment; deploy v0.2.7/v0.2.8-dev; cred-Variant C; DESIGN.md-enradare.

**Fas 2 formell stängning — PAUSAD (medvetet, Klas-beslut):** gaten "(a) ingestion-cron verifierad" tillhör **separat lokal session** (AWS SSO, CloudWatch EventId 5402 + `job_ads`-korpus ~40k+). Auth-gated visuell verifiering (b) + rök-test (c) = **gröna denna session**. `run`=137 träffar bekräftar att data finns, men full cron/korpus-verifiering görs i det separata spåret innan steg-tracker Fas 2 → "Klar".

**Pending operativt:** F2 ingestion-cron-verifiering (separat session). ADR 0005-observation (dev-test-konto via icke-flag-gejtat /register) triageras i auth-fokuserad touch. ADR 0040 (smart CV-filter) detaljdesign vid Fas 4-start. TD-84 vid opportunistisk touch.

---

## Arkiv — Vercel-deploy 2026-05-14

### Levererat (5 commits, 1 Klas-cleanup)

| Commit | Innehåll | Effekt |
|---|---|---|
| `cbe4a10` | Vercel DNS-records (apex A 216.198.79.1 + www CNAME projekt-specifik + CAA Let's Encrypt) — Terraform applied i prod/baseline | DNS pekar mot Vercel ✅ |
| `25aa476` | Ta bort pnpm-workspace.yaml + flytta ignoredBuiltDependencies till package.json's pnpm-field | Hypotes-test (fel orsak) men hygienförbättring behållen |
| `9d0eae4` | next build/dev --webpack flag (force Webpack istället för Turbopack-default) | Hypotes-test (fel orsak) men säkerhetsmarginal behållen |
| `fcfe710` | **vercel.json med "framework": "nextjs"** | **LÖSNINGEN** ✅ |
| (Klas UI 00:50) | Dashboard Framework Preset = Next.js (defense-in-depth match) + radera oönskat `jobbpilot-web`-projekt | Cosmetic cleanup |

### Root cause — `framework: null` i Vercel project settings

Avslöjad av CTO-godkänd diagnos via lokal `vercel pull` + inspektera `.vercel/project.json`. När projektet skapades via "New Project"-flödet i UI valdes inte Application Preset = Next.js explicit (Klas noterade dropdown:n "försvann"). Vercel-platform-side hade `framework: null` → routing-tabellen registrerades inte som Next.js → ALLA URLs gav 404 NOT_FOUND oavsett auth/build-bundler/workspace-config.

### CTO-rond 2026-05-13 kväll — diagnos först (entydigt mot principer)

CTO valde Gemini-approach (systematisk diagnos) över ChatGPT (delete-project först). Motivering: Saltzer/Schroeder Fail-Safe Defaults + Beck TDD-spirit + CLAUDE.md §9.4 Discovery + YAGNI.

### End-to-end verifierat (Klas screenshots 00:50 2026-05-14)

| URL | Status | Fungerar |
|---|---|---|
| `jobbpilot.se` | 301 → www | ✅ |
| `www.jobbpilot.se/` | 200 LandingPage | ✅ (designsystem-demo, behöver login/register-CTA) |
| `www.jobbpilot.se/logga-in` | 200 | ✅ |
| `www.jobbpilot.se/mig` | 200 | ✅ Klas profil + Admin-roll |
| `www.jobbpilot.se/admin/granskning` | 200 | ✅ Audit-logg LIVE med System.JobAdsSynced cron-events |
| `www.jobbpilot.se/jobb` | 200 | ✅ **3391 jobbannonser från Platsbanken** |
| `www.jobbpilot.se/api/me` | 401 (utan auth) | ✅ Backend-koppling fungerar |

### Disciplinmissar + lärande

3 misslyckade hypoteser innan datadriven diagnos (auth, pnpm-workspace, Turbopack). ~2h Klas-tid på gissningar.

**Lärande:** `vercel pull` + inspektera `.vercel/project.json` är obligatorisk första-diagnos vid Vercel-konstigheter. Settings-mismatch mellan dashboard och vad CC ser från utsidan är osynlig utan det steget.

### TD-status

- **TD-81** lyft 2026-05-14 — Minor Trigger — middleware.ts → proxy.ts (Next.js 17-uppgradering). Källa: Vercel-deploy-session build-warning. Risk i nuläget noll, hanteras vid Next.js 17.

Aktiva: 22 (TD-13 Major Fas 2 + TD-26 Major Fas 4; resten Minor).

### Pending operativt för Klas

- **Landing-page-CTA** (Klas observation 00:48): `(marketing)/page.tsx` är design-system-demo, saknar "Logga in" + "Anmäl till väntelistan"-knappar. Civic-utility-MVP-krav.
- **Backend prod-stack-bring-up** (ADR 0036 D1) — Fas 7-prep, frontend pekar på dev-backend tills dess
- AWS SSO-token-livslängd, JobTech-API-key, BUILD.md §9.1 sync — kvarstår

### Nästa session — Klas-val

1. **Landing-page-CTA-fix** (snabb, civic-utility-MVP-blocker)
2. **F2-P11 / nästa Fas 2-feature** TBD
3. **v0.2-prod-tag-prep** (TD-13 PII-encryption är enda kvarstående Major Fas 2, CTO confirmed defer 2026-05-13)
4. **OIDC-drift-städning** (pre-existing 2 change-poster i prod/baseline-Terraform, fix opportunistiskt)

---

## Tidigare aktivitet — TD-80 STÄNGD (JobAd.Url scheme-whitelist)

### Levererat

| Område | Innehåll |
|---|---|
| `JobAd.cs` ValidateCore | Whitelist via `Uri.UriSchemeHttp`/`UriSchemeHttps`-konstanter (default-deny per Saltzer/Schroeder + OWASP A01:2021). Skydd genom alla 3 entry-points (Create/Import/UpdateFromSource) som delar `ValidateCore` |
| Tester FIRST (TDD) | 17 nya unit-tester (4 Theory-metoder med 13 InlineData-cases): http/https/uppercase positive + javascript/JAVASCRIPT/data/vbscript/file/ftp/gopher negative + UpdateFromSource state-bevarande post-fail |
| `UpsertExternalJobAdCommandHandler` | Ingen ändring krävdes — befintlig `Skipped`-flow (rad 53-57 + LogSkippedValidation) hanterar Import-failure rent. Worker sync-jobb propagerar `skipped++` i metrics |

### CTO-rond — skippad

Beslutet entydigt mot Saltzer/Schroeder 1975 default-deny + OWASP A01:2021 whitelist-rekommendation. Ingen multi-approach-fråga (whitelist > blacklist är etablerad princip; `Uri.UriSchemeHttp`-konstanter är idiomatisk .NET-form).

### Reviewers INLINE

| Reviewer | Verdict |
|---|---|
| security-auditor (re-audit av egen Blocker) | Approved 0/0/0 — defense-in-depth komplett, alla 3 entry-points skyddade, persistens säker via Worker `Skipped`-flow |
| code-reviewer | Approved 0/0/0 — typsäkra konstanter, korrekt nullable-flow, [Theory]+[InlineData] DRY, state-bevarande post-fail verifierat |

### Backend full svit grön

| Suite | Pre | Post | Delta |
|---|---|---|---|
| Domain.UnitTests | 225 | **242** | +17 |
| Application.UnitTests | 354 | 354 | 0 |
| Architecture.Tests | 50 | 50 | 0 |
| Api.IntegrationTests | 254 | 254 | 0 |
| Worker.IntegrationTests | 26 | 26 | 0 |
| Migrate.UnitTests | 6 | 6 | 0 |
| **Totalt** | **915** | **932** | **+17 grönt** |

### TD-status

- **TD-80** Major Fas 2 → **STÄNGD 2026-05-13** (flyttad till `tech-debt-archive.md`). Defense-in-depth FE Zod-refine (commit 70e1505) + BE Domain `ValidateCore`-whitelist.

Aktiva: 21 (TD-13 Major Fas 2 + TD-26 Major Fas 4; resten Minor). **0 Major Fas Nu, 0 Major Fas 1.**

---

## Tidigare aktivitet — F2-P10 frontend `/jobb`-katalog UI KOMPLETT

### Levererat (frontend-only batch)

| Område | Innehåll |
|---|---|
| ADR 0030 amendment 2026-05-13 | `rateLimited`-variant förstklassig i `ApiResult<T>` — RFC 9110 Retry-After, default 60s |
| `lib/dto/_helpers.ts` | `rateLimited`-kind + `parseRetryAfter` + `responseToResult` mappning av 429 |
| 5 konsument-pages | ansokningar, ansokningar/[id], cv, cv/[id], mig (renderProfile), admin/granskning — alla med rateLimited-case + civic-utility-copy |
| `lib/dto/job-ads.ts` | Zod-schemas: jobAdStatus/Source/SortBy/Dto + listJobAdsResult + jobAdFilters (regex-defense + URL-scheme http(s)-refine för XSS-skydd) |
| `lib/job-ads/status.ts` | Labels + variant-mappning (Aktiv/Utgången/Arkiverad + 4 sort-options + 4 source-labels) |
| `lib/api/job-ads.ts` | `getJobAds(query)` server-only fetcher → `ApiResult<ListJobAdsResult>` |
| `components/job-ads/` | StatusBadge + Card + List + Pagination (GOV.UK-numeric) + Filters (Client, RHF + manuell safeParse) |
| `app/(app)/jobb/page.tsx` | Server Component, async searchParams (Next.js 16), 6-fall switch + assertNever |
| `app/(app)/layout.tsx` | Nav-länk "Jobb" tillagd (första item) |
| `tests/e2e/jobb.spec.ts` | 7 Playwright-tester (auth-redirect + render + filter-submit + validation + reset + nav) |

### CTO-rond F2-P10 — 4 entydiga beslut

| Q | Beslut | Kort motivering |
|---|---|---|
| Q1 | **A** Utöka `ApiResult<T>` med `rateLimited` | CCP/REP, OCP via assertNever, Saltzer/Schroeder Economy of Mechanism |
| Q2 | **A** URL-driven server-state (router.push) | CLAUDE.md §4.3+§5.2, Fielding HATEOAS, Beck YAGNI |
| Q3 | **A** `JobAdStatusBadge` + `lib/job-ads/status.ts` | REP/CCP, SRP, codebase-konsekvens |
| Q4 | **A** Numeric pagination GOV.UK-stil | civic-utility-konvention, WCAG keyboard-direkthopp, Norman affordance |

### Reviewers INLINE

| Reviewer | Verdict |
|---|---|
| design-reviewer | Approved med 6 Minor (5 pre-existing patterns); Minor 1+2 (badge role=status, dubbel aria-live) fixade in-block |
| code-reviewer | Approved (0/0/3); M1 (kollaps-kommentar) + M2 (badge role=status) fixade in-block; M3 (Card focus-wrap) defererat — gäller framtida `/jobb/[id]` |
| security-auditor | **BLOCKER → fixad** XSS-vektor via `javascript:`-URL i `<a href={jobAd.url}>`. Zod-refine `^https?://` blockar FE-side. **TD-80 lyft** för BE Domain-tightening (annan fas per §9.6 punkt 1) |

### Tester

- vitest: **313/313 grönt** (+29 nya: 23 dto/status/filters/badge/card/list/pagination + 5 nya rateLimited i `_helpers.test.ts` + 1 uppdaterad assertNever-test + 8 URL-scheme-tester efter security-fix)
- `npx tsc --noEmit`: clean
- `pnpm lint`: 0 errors, 3 pre-existing warnings (audit-log-table.test, delete-account-dialog watch, applications.spec applicationId)

### TD-status

- **TD-80** lyft 2026-05-13 — Major Fas 2 — JobAd.Url scheme-whitelist (http/https) i Domain.ValidateInputs (security-auditor F2-P10 split)

Aktiva: 22 (TD-13 + TD-26 + TD-80 Major; resten Minor).

### Pending operativt för Klas

- **Vercel-deploy** för `/jobb` LIVE — egen Klas-op (DNS, env-vars för BACKEND_URL + auth-cookie-domain)
- **Lokal Lighthouse-pass + axe-DevTools** på `/jobb` mot dev-backend — Klas kör manuellt
- AWS SSO-token-livslängd, JobTech-API-key, BUILD.md §9.1 sync mot ADR 0032 §3 — kvarstår

---

## Tidigare aktivitet — D+A-session KOMPLETT (TD-79 + TD-70 stängda)

### Levererat Del A (TD-70 — F2-P9 search/filter)

| Commit | Innehåll |
|---|---|
| `d4294b6` | feat(jobads): F2-P9 search/filter-yta ?ssyk&?region&?q + ListReadPolicy rate-limit (TD-70) |
| Tag `v0.2.5-dev` | Triggered deploy run 25797979739 — 7m success, Phase E migration applied |

**Endpoint:** `GET /api/v1/job-ads?ssyk=<concept-id>&region=<concept-id>&q=<text>` (auth-gated + rate-limited 60/min per UserId)

**CTO-rond:** 11 entydiga beslut (Q1-Q11) + 1 follow-up-triage av security-auditor Major (in-block-rate-limit-fix).

**Reviewers:** dotnet-architect → senior-cto-advisor → db-migration-writer → test-writer → security-auditor (Major: rate-limit → CTO-triage in-block) → senior-cto-advisor (rond 2) → code-reviewer APPROVED 0/0/2/2.

**Tests:** Domain 225 + Application **354** (+31) + Architecture 50 + Api **254** (+14) + Worker 26 + Migrate 6 = **915 grönt (+45 nya)**.

### Levererat Del D (TD-79 pipeline-hygien)

| Commit | Innehåll |
|---|---|
| `94ec84a` | chore(infra): lifecycle.ignore_changes=[task_definition] på ECS api+worker services (TD-79) |

**Plan-output post-fix:**

| Resurs | Pre-fix plan | Post-fix plan |
|---|---|---|
| `aws_ecs_service.api.task_definition` | ~ update | ❌ no-op |
| `aws_ecs_service.worker.task_definition` | ~ :8 → :1 (rollback) | ❌ no-op |
| `aws_ecs_task_definition.api` | -/+ replace | ✓ apply genomförd (revision :13 ny, service ignorerar) |
| `aws_db_parameter_group.this` | ~ apply_method cosmetic | ~ kvarstår (pre-existing, ej TD-79-scope) |

**Live-state efter apply:**
- `jobbpilot-dev-api`: TaskDef `:13` (CI/CD-ägd revision behållen)
- `jobbpilot-dev-worker`: TaskDef `:8` (NOT rolled back to `:1`)
- `https://dev.jobbpilot.se/api/ready` → HTTP 200 OK
- 3 CloudWatch-alarms fortsatt i OK-state
- AdminBootstrap__InitialAdminEmail nu Terraform-ägd i task-def-content (env-var-ägarskap löst)

### CTO-rond 2026-05-13 (v0.2-prod-tag-readiness) — 5 beslut

1. **Q1 v0.2-definition:** Tolkning (c) — första prod-deploy-triggande tag oavsett feature-completeness. Frontend kommer i `v0.2.x`-patch-tags efter. Motivering: Continuous Delivery (Humble/Farley 2010), Fitness Functions (Ford/Parsons/Kua 2017).
2. **Q2 BUILD.md §14.4-alerts:**
   - JobTech-sync 3 consecutive failures → **In-block-fix FÖRE tag** (fas-relevant + observability)
   - Backend 5xx-rate > 1% / 5 min → **TD-77 Fas 8** (YAGNI vid 1-user-volym)
   - DB CPU > 80% / 10 min → **TD-78 Fas 8** (samma logik)
3. **Q3 SystemEventAuditor failure-alarm (EventId 5602) → In-block-fix FÖRE tag** (ADR 0035 §6 egen leveransspec; Art. 30 record-of-processing-kongruens)
4. **Q4 RDS backup-retention:** **14d för prod** (industry-common, EDPB CEF 2025 verifierad acceptans, KISS över 35d-max utan TD-13)
5. **Q5 TD-13 (PII-encryption + crypto-erasure):** **Defer Fas 2-stängning** (EDPB CEF 2025 verifierar standard practice räcker, fas-regel CLAUDE.md §9.6)

### Smoke-test 2026-05-13 — AUDIT-WIRE VERIFIERAD LIVE

CloudWatch Logs Insights mot `/aws/ecs/jobbpilot-dev/worker`:

| Cron-tick | Stream-result | audit_log INSERT |
|---|---|---|
| 08:21:55 UTC | fetched=1029, added=72, errors=0 | ✓ INSERT INTO audit_log (… payload …) |
| 08:30:47 UTC | fetched=1076, added=84, errors=0 | ✓ INSERT INTO audit_log (… payload …) |
| 08:40:41 UTC | (pågående vid query-tid) | ✓ INSERT INTO audit_log (… payload …) |

`SystemEventAuditor` skriver `System.JobAdsSynced` per cron-tick via
idempotens-check + insert. **0 EventId 5602 (Critical audit failure)** i
loggarna. TD-73 audit-wire fungerar i prod-flöde.

### Web-search-källor (CLAUDE.md §9.5, verifierade 2026-05-13)

- [AWS RDS Backup Retention](https://docs.aws.amazon.com/AmazonRDS/latest/UserGuide/USER_WorkingWithAutomatedBackups.BackupRetention.html) — default 7d console / 1d API, max 35d
- [EDPB CEF 2025 Report (PDF, 2026-02)](https://www.edpb.europa.eu/system/files/2026-02/edpb_cef-report_2025_right-to-erasure_en.pdf) — automatic overwrite cycles + live-radering acceptabelt; crypto-erasure inte krav
- [Terraform aws_cloudwatch_log_metric_filter](https://registry.terraform.io/providers/hashicorp/aws/latest/docs/resources/cloudwatch_log_metric_filter)
- [Terraform aws_cloudwatch_metric_alarm](https://registry.terraform.io/providers/hashicorp/aws/latest/docs/resources/cloudwatch_metric_alarm) — provider v6.30 stable

### TD-status

- **TD-77** lyft 2026-05-13 — Backend 5xx-rate-alarm, Fas 8 Klass-launch
- **TD-78** lyft 2026-05-13 — DB CPU > 80% alarm, Fas 8 Klass-launch
- **TD-13** Major Fas 2 — bekräftad ej launch-blocker per CTO Q5 + EDPB CEF 2025

Aktiva: 21 (TD-13 + TD-26 Major; resten Minor).

### Pending Klas-GO (in-block-fix-batch FÖRE v0.2-tag)

Per `docs/runbooks/v0.2-prod-launch-checklist.md` §9. Tre leveranser:

1. **CloudWatch-alarm: JobTech-sync 3 consecutive failures** — Terraform-utbyggnad i `modules/cloudwatch_security_alarms` (eller ny `cloudwatch_ops_alarms`-modul)
2. **CloudWatch-alarm: SystemEventAuditor failure (EventId 5602)** — stänger ADR 0035 §6-gap
3. **RDS backup-retention 7d → 14d** — prod-Terraform (dev oförändrad)

**Scope:** 2-3 commits, ~3-4h CC-tid.
**Klas-STOPP-territorium per CLAUDE.md §9.6 punkt 5:** v0.2-definition är strategisk + prod-Terraform-state + tag-push behöver explicit Klas-GO.

### Pending operativt för Klas (sedan tidigare)

- AWS SSO-token-livslängd (re-auth med `aws sso login --profile jobbpilot` vid behov)
- JobTech-API-key registrering (apirequest.jobtechdev.se nedlagd; v2 är open API)
- Frontend-deploy till Vercel (kommer i v0.2.x-patch efter v0.2)
- BUILD.md §9.1 sync mot ADR 0032 §3 — Klas-instruktion krävs

---

## Tidigare aktivitet (TD-73 prod-gating-batch — komplett)

### Tidigare commits

| Commit | Innehåll |
|---|---|
| `c13e1ce` | feat(jobads): TD-73 prod-gating — audit-wire α + right-to-erasure för rekryterar-PII |

### Granskningstrail

- `docs/sessions/2026-05-13-0730-td73-prod-gating.md` — session-log (skapas i denna session-end)
- Reviewers INLINE: dotnet-architect + senior-cto-advisor + code-reviewer + security-auditor
- Tidigare session: `docs/sessions/2026-05-13-0700-f2-p8c-hangfire-jobs.md`

### Leveranser

| Område | Innehåll |
|---|---|
| **Ny ADR 0035** | System-event audit-pipeline (bypass-port parallell till IAuditTrailEraser). EventType-konvention `System.<Event>`, AggregateType `System.<Aggregate>`. Idempotens-skydd vid Hangfire-retry. Best-effort-semantik vid audit-failure. |
| **ADR 0032 amendment** | §8 punkt 4 levererad: audit-wire via `ISystemEventAuditor` (inte domain-event), Email-only right-to-erasure, Name→TD-75, GIN-index→TD-76 |
| **ADR 0024 cross-ref-amendment** | Pekare till ADR 0035 + ADR 0032 §8 för rekryterar-PII-cascade (separat från ADR 0024 D6 user-cascade) |
| **Domain** | `AuditLogEntry.Payload` + `CreateSystemEvent`-factory (bevarar Guid.Empty-invariant) |
| **Application ports** | `ISystemEventAuditor`, `IRecruiterPiiPurger`, `SystemAuditEvent`-record-hierarki, `RedactRecruiterPiiCommand` (+ validator + enum) |
| **Infrastructure** | `SystemEventAuditor` (idempotens-check via (EventType, AggregateId)-lookup), `RecruiterPiiPurger` (`EF.Functions.JsonContains` + `ExecuteUpdateAsync`), EF-migration `AddAuditLogPayload` |
| **EF-config** | `AuditLogEntryConfiguration.Payload` jsonb-mapping |
| **Worker/Hangfire** | Audit-wire i `SyncPlatsbankenStreamJob` (finally med exception-mask-skydd), `SyncPlatsbankenSnapshotJob`, `PurgeStaleRawPayloadsJob` |
| **Admin endpoint** | `POST /api/v1/admin/job-ads/redact-recruiter-pii` med `RequireAuthorization(Admin)` + `JsonStringEnumConverter` |
| **Architecture-tester** | ISystemEventAuditor + IRecruiterPiiPurger konsumentlistor (Application + Infrastructure) |
| **Runbooks** | `recruiter-pii-erasure.md` (auto-flöde Email + manuell-flöde Name); `gdpr-processing-register.md` uppdaterad |

### Reviewers INLINE (CLAUDE.md §9.2)

| Reviewer | Tidpunkt | Verdict |
|---|---|---|
| dotnet-architect | INNAN kod | Design-skiss approved; 5 multi-approach → CTO |
| senior-cto-advisor | EFTER architect, INNAN kod | 13 beslut entydigt mot principer (Martin/Evans/Fowler/Beck/Saltzer-Schroeder/GDPR). **INGET Klas-STOPP** behövdes per CLAUDE.md §9.6 punkt 5 |
| code-reviewer | EFTER impl, INNAN commit | GO. 0 Blocker, 0 Major, 3 Minor (Minor-1 + Minor-2 in-block-fixade per §9.6; Minor-3 är planerad uppföljning) |
| security-auditor | EFTER impl, INNAN commit | APPROVED-WITH-CONDITIONS. 0 Critical, 0 GDPR-Blocker, 0 Major, 4 Sec-Min (acceptable as-is) |

### CTO-rond 2026-05-13 (TD-73 prod-gating) — 13 beslut

1. **Q1 AggregateId:** Per-run-Guid (via Hangfire jobId-pattern) — OCP-väg framåt
2. **Q2 Erasure-shape:** Total null-out via `SetProperty(_ => null)` — KISS + data-minimisation > debug-värde
3. **Q3 Audit-granularitet:** En aggregerad audit-rad per request — ADR 0024 D4-precedens
4. **Q4 RedactCmd.AggregateId:** Per-request-Guid (RequestId) — följer Q3
5. **Q5 GIN-index:** Defer till TD-76 — YAGNI vid F2-volym
6. **R-Risk1 Atomicitet:** Best-effort + Hangfire retry + idempotens-check + Critical log — Fowler 2018
7. **R-Risk2 Name-matching:** Email-only nu, Name som TD-75 — YAGNI + Art. 17 kräver inte name-identifier
8. **M1 ADR-shape:** Ny ADR 0035 + amendment till ADR 0032 §8 + cross-ref ADR 0024 — Ford/Parsons/Kua immutability
9. **M2 Klas-STOPP-buntning:** INGET Klas-STOPP — entydiga principer i alla 13 frågor
10. **M3 Snapshot-shim:** SyncPlatsbankenSnapshotCommand har redan inte IAuditableCommand — no-op
11. **M4 ICorrelationIdProvider:** Impl-validation räcker
12. **M5 SystemEventAuditor lifetime:** Scoped (matchar IAppDbContext)
13. **M6 Volym:** GIN-defer korrekt även vid sanity-check (5-15k INSERTs/dygn netto)

### Web-search-källor (CLAUDE.md §9.5, verifierade 2026-05-13)

- [Npgsql 10.0 Release Notes](https://www.npgsql.org/efcore/release-notes/10.0.html)
- [Trailhead Technology — EF Core 10 PostgreSQL Hybrid DB](https://trailheadtechnology.com/ef-core-10-turns-postgresql-into-a-hybrid-relational-document-db/)
- [GitHub Issue #3745](https://github.com/npgsql/efcore.pg/issues/3745) — Contains-regression
- [PostgreSQL Docs 18 — GIN Indexes](https://www.postgresql.org/docs/current/gin.html)
- [pganalyze — GIN Index The Good and Bad](https://pganalyze.com/blog/gin-index)

### Tester (full svit grön)

- Domain.UnitTests: 218 → **225** (+7: CreateSystemEvent-invarianter + Payload-default)
- Application.UnitTests: 307 → **323** (+16: SystemEventAuditor + RedactCommand + Validator)
- Architecture.Tests: 46 → **50** (+4: ISystemEventAuditor + IRecruiterPiiPurger konsumentlistor × Application + Infrastructure)
- Api.IntegrationTests: 234 → **240** (+6: AdminRedactRecruiterPiiTests end-to-end mot Postgres)
- Worker.IntegrationTests: 26 (oförändrat)
- Migrate.UnitTests: 6 (oförändrat)

Totalt backend: 837 → **870 grönt** (+33 nya).

### Disciplinmissar fångade + fixade

1. **Architect föreslog `EF.Functions.JsonContains` i Application-handler** — Clean Arch-brott (Npgsql i Application). Refactor: skapade `IRecruiterPiiPurger` Application-port + Postgres-impl. Samma mönster som `IAuditTrailEraser`.
2. **Architect+arch-test listade `RedactRecruiterPiiCommandHandler` som ISystemEventAuditor-konsument** — fel; handlern är `IAuditableCommand` + går via `AuditBehavior`. Fixad i arch-test + ADR 0035 §7 docs-not.
3. **Stream-job finally-block kunde maska originalexception vid audit-failure** (code-reviewer Minor-1). Fixad in-block med try/catch (CA1031-suppress) + Cwalina/Abrams §7.5-not.
4. **`JsonStringEnumConverter` saknades** för admin-endpoint enum-deserialisering — fixad via `[JsonConverter(typeof(JsonStringEnumConverter<>))]` på `RecruiterIdentifierType`.

### Tag-cykel + deploy

- `v0.2.4-dev` på `c13e1ce` → push 08:13 UTC → deploy run `25786909619`.
- Deploy completion: 08:20 UTC (~6m42s).
- Ready-probe: `https://dev.jobbpilot.se/api/ready` → **200 OK** verifierat efter deploy.

### Smoke-test status — väntar nästa cron-tick

**Pending verifikation:** Nästa stream-cron `*/10` (08:40 UTC) ska skriva
första `System.JobAdsSynced`-raden i `audit_log` via nya `ISystemEventAuditor`.
Verifikation via CloudWatch logs (Worker-task) eller psql mot dev-RDS:

```sql
SELECT event_type, aggregate_type, aggregate_id, occurred_at,
       payload->>'Source' as source,
       payload->>'Fetched' as fetched,
       payload->>'Added' as added
FROM audit_log
WHERE event_type LIKE 'System.%'
ORDER BY occurred_at DESC
LIMIT 5;
```

Förväntad rad: `event_type = 'System.JobAdsSynced'`, payload med counts.

### TD-status

- **TD-73** Major → **STÄNGD 2026-05-13** (flyttad till `tech-debt-archive.md`)
- **TD-75** Minor lyft — Name-baserad rekryterar-PII-radering (Trigger: första Name-begäran)
- **TD-76** Minor lyft — GIN-index på raw_payload jsonb (Trigger: latens >5s eller volym ×10)

Aktiva: 19 (TD-13 + TD-26 Major; resten Minor). **0 Major Fas Nu, 0 Major Fas 2 (gating blockerare borta).**

### Pending operativt (oförändrat sedan P8c)

- AWS SSO-token-livslängd (re-auth med `aws sso login --profile jobbpilot` vid behov)
- JobTech-API-key registrering (apirequest.jobtechdev.se nedlagd; v2 är open API)
- Frontend-deploy till Vercel
- BUILD.md §9.1 sync mot ADR 0032 §3 — Klas-instruktion krävs

---

## Nästa session — LÅST PLAN (Klas-GO för session-start = strategisk transition)

**Samlad session: ingestion payload-trunkerings-fix + F2 sök-yta-omdesign (Klas designbrief vs Platsbanken).** Klas §9.6 p.6-override av CTO-split: B (taxonomi-multiselect) + C (live-typeahead) ingår denna session. senior-cto-advisor (agentId a4318f13a645293cb) + dotnet-architect (a64f2ee9d89379046) plan-design klar. Fortfarande Fas 2 (ej Fas 3). **Fas 2 stängs vid B–E komplett** (Klas-val 2026-05-16 — en samlad stängning när hela sök-visionen live).

**6 linjära commit-batchar, reviewer-pass + STOPP per batch (samlad session ≠ samlad commit-batch):**

| # | Batch | ADR / grind |
|---|---|---|
| 0 | Discovery — verifiera ingestion-rotorsak (CloudWatch byte-offset-varians vs Polly/Timeout-hypotes) + kartlägg sök-kod | Discovery-rapport till Klas, ingen kod |
| 1 | Ingestion-fix (A1/A2/A3 in-session CTO efter Batch 0) | ADR 0032-amendment **STOPP** + deploy + **cron-grön (EventId 5402, korpus ~40k+) hård F2-DoD-gate** |
| 2 | ADR-batch, noll kod | ADR 0042 Accepted + ADR 0039 Beslut 3 superseded **STOPP** |
| 3 | B: `SearchCriteria` single→multi (VO collection-equality + maxantal-invariant + jsonb-datakompat) | architect+test-writer+code-reviewer + **security-auditor BLOCKING** + grön svit |
| 4 | E ("Ny"-tag, `Since`+`IsNew`) sedan D (relevans-sort) | code-reviewer + grön svit |
| 5 | C: typeahead (C1 lokal `job_ads` ILIKE-prefix) | **security-auditor BLOCKING** + db-migration-writer index→**Klas-STOPP** + grön svit |
| 6 | Frontend B–E (kollaps-filter A, multi-select, typeahead, sort, IsNew-badge) | design-reviewer VETO + Klas visuell verifiering |

**Låsta CTO-multi-approach-beslut:** C-källa = **C1** (lokal `job_ads` ILIKE-prefix; C2 JobTech-taxonomi-API avvisat). D-relevans = **D2** (ILIKE-heuristik; D1 tsvector = framtida skala-trigger, dokumenteras i ADR 0042 ej TD). Ingestion **A1/A2/A3 = in-session CTO-rond efter Batch 0-discovery** (A1 frikoppla hämtning/persistens via Infrastructure-buffrad NDJSON = default om timeout-rivning bekräftas).

**ADR-väg:** ingestion → ADR 0032-amendment (samma streaming-beslutsdomän). Sök-IA → **ny ADR 0042**. `SearchCriteria` single→multi → **supersession av ADR 0039 Beslut 3**, beslutet skrivs i ADR 0042 (ej egen ADR 0043). ADR 0039 Beslut 1 (delad JobAdSearch) hålls. ADR 0040 (F = CV-matchning "bra match") **hårt OUT**, ej ens visuell placeholder, endast korsrefererad.

**7 Klas-STOPP:** (1) ingestion-rotorsak+A-variant, (2) ADR 0032-amendment Accepted, (3) ingestion deploy+cron-grön, (4) ADR 0042+0039-supersession Accepted, (5) varje DB-migration (B jsonb om ändrad, C1-index, ev. `CREATE EXTENSION pg_trgm`), (6) security-auditor BLOCKING Batch 3+5, (7) frontend deploy+visuell verifiering. **BUILD.md §18 orörd** (ADR 0042 = beslutskälla).

**Förkrav-blockare innan Batch 1-kod:** ingestion-fix måste vara deployad + cron-verifierad (korpus ~40k+) INNAN B rör samma data-yta — B:s dedupe/identitet kräver riktig korpus, ej 5 380-stympad.

Se startprompt-block i chatten (2026-05-16, ingestion-verify-session-end) + `docs/sessions/2026-05-16-1450-f2-ingestion-verify-red.md`.

---

## Tidigare sessioner (kort)

- **2026-05-21** (denna): F6 P4 FTS-skifte — PostgreSQL FTS-hybrid + Infrastructure-query-port `IJobAdSearchQuery`. 6 commits (`2adcec9`→`95bcb74`), `build`-CI grön. ADR 0062 NY + 0061/0039-amend. Tag-push `v0.2.56-dev` + deploy-verifiering pending Klas-GO.
- **2026-05-20/21:** F6 P4 sök-infrastruktur-fix (P1 q-perf GIN trigram ADR 0061 + P2 filter-bugg JobTechHit-POCO). 10 commits, tag `v0.2.55-dev` live på dev.
- **2026-05-13 förmiddag:** TD-73 prod-gating-batch — audit-wire α (ADR 0035) + right-to-erasure (ADR 0032 §8 amendment). 1 commit `c13e1ce`, tag `v0.2.4-dev` deploy success. 33 nya tester. TD-73 stängd; TD-75 + TD-76 lyfta.
- **2026-05-13 morgon:** F2-P8c JobTech Hangfire-jobben + race-säker upsert + 30d-retention. 1 commit `81dfab6`, tag `v0.2.3-dev`. 43 nya tester.
- **2026-05-13 natt:** F2-P8b JobTech Infrastructure-leverans. 5 commits, tag `v0.2.2.1-dev`.
- **2026-05-12 kväll:** F2-P7 + P8a + bootstrap + aggregate-review. 17 commits, 3 nya ADRs.
