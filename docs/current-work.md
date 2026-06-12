# Current work — JobbPilot

**Status:** **FAS B2-DISCOVERY 2026-06-12 — premiss inverterad: B2:s DATA-LAGER redan komplett+mergat på main; query-wiringen återstår och är ADR-gated bakom re-ingest. CTO-dom + Klas-GO (AskUserQuestion): RE-INGEST FÖRST, query-wiring i efterföljande PR mot sann data. Aktivt steg = Klas kör `backfill-klass2`. Ingen kod denna session (ren discovery + docs-sync).** Föregående: CC-STRUKTUR COLD REVIEW (Batch A #55, B #56, C CLAUDE.md-prune #57 `fe5041c` — komplett). PLATSBANKEN SÖK-PARITET FAS E2j MERGAD #54 (`741727a`). E2j + äldre faser arkiverade i `docs/current-work-archive.md`.

**Levererat denna session 2026-06-12 (Fas B2-discovery + re-ingest-beslut — ingen kod):**

- **Discovery (inga ändringar, CLAUDE.md §9.4):** verifierat on-disk att B2:s data-lager är komplett och mergat — STORED-migration `20260608205054_F6P7JobAdKlass2SearchColumns` (employment_type + worktime_extent + partial-index), JobTechHit-POCO (`JobTechEmploymentType`/`JobTechWorkingHoursType`), sanitizer-allowlist, JobAdConfiguration shadow-mapping (`EmploymentTypeConceptId`/`WorktimeExtentConceptId`), `BackfillJobAdKlass2Job` + `POST /backfill-klass2`, generated-columns-tester. Promptens premiss ("kolumnerna saknas") motbevisad. Full rapport: `docs/research/2026-06-12-fas-b2-state-discovery.md`.
- **Faktisk lucka = query-wiringen:** `JobAdFilterCriteria`, `ApplyCriteria`-grenar, `ListJobAdsQuery`+validator, `?employmentType=`/`?worktimeExtent=`-bindning, `SearchCriteria`-VO-fält, `FacetDimension`-värden — alla medvetet utelämnade per ADR 0067 (Beslut 6 + C1/C2/D1/D2-notat: "följer query-wiring post re-ingest" / "i samma PR som re-ingestens data").
- **CTO-triage (decision-maker, §9.6) — sekvens-/fas-fråga mot Accepted-ADR-mekanik:** (1) re-ingest FÖRST, wira efteråt mot sann data — bygg INTE wiringen nu; (2) "FE-gated + Testcontainers-syntetisk" är otillräcklig falsk-klar-uppfyllelse — VO-ripplet till SavedSearch riskerar tyst-noll-bevakningar (ADR rad 41/115); (3) ingen legitim liten batch (CCP, Martin kap. 13); (4) Klas-GO krävs (prompt skriven under fel premiss). **Klas-GO: "Re-ingest först (CTO-rek)".**
- **Operativt till Klas:** `POST http://localhost:5049/api/v1/admin/job-ads/backfill-klass2` (admin-auth, ~2,5h) → verifiera `emp_pop`/`wt_pop ≫ 0` (se research-rapporten + pending punkt 5).

**Levererat denna session 2026-06-12 (CC-struktur cold review — token-effektivisering):**

- **Cold review genomförd** (webb-verifierad mot code.claude.com 2026-06-12): VS Code-extensionen behålls (officiellt rekommenderad, samma tokenkostnad som CLI); Froject avvisad (GTM-målgrupp, `C:\DOTNET-UTB\froject-setup` raderad per Klas-GO); svenska→engelska för nya docs/commits (UI-copy + Klas-dialog förblir svenska).
- **Batch A (PR #55, automerge):** ADR 0002 Amendment 2026-06-12 — tier-alias (`opus`/`sonnet`/`haiku`) i agent-frontmatter ersätter explicita IDs (pinned ID kvar i runtime-config). Alla 13 agenter fick `model:`-fält (0/13 hade det on-disk): 8× opus, 3× sonnet, 2× haiku (test-runner + docs-keeper — mekaniskt arbete, 1/5 Opus-pris). `.claude/README.md` synkad (11→13 agenter). Memory `project_agent_model_field_drift` stängd.
- **Batch B (denna PR):** `session-start-template.md` omskriven 12→4 sektioner på engelska (~2–3k tokens sparas per session-start; duplicering av auto-laddad kontext struken). 5 största agent-filerna trimmade 16–21 KB → 5,5–6,6 KB styck (security-auditor, nextjs-ui-engineer, ai-prompt-engineer, code-reviewer, design-reviewer — roll/kriterier/veto/rapportformat bevarade; exempel och CLAUDE.md-duplicering strukna; agent-kroppen är subagentens systemprompt per dispatch). current-work.md trimmad (E2i–D2-block → arkivet).
- **Batch C (pågående):** TD-livscykel-skill + session-protokoll-runbook + CLAUDE.md-prune ≤3,5k tokens på engelska — **kräver Klas `approve-spec-edit.sh`**.

**Levererat session 2026-06-12 (Fas E2j — sök-commit-modell, MERGAD #54 `741727a`):**

- **Klas rendered-feedback på E2i live-spegel (modell-session, EJ mekanik):** tre kopplade problem — (1) recent-search "sparas inte som väntat" (**empiriskt bekräftat i dev-DB:** `recent_job_searches` full vid cap=20 för en seeker, fylld av live-`router.replace`-mellanstegsspam som evictade äkta committade sökningar — F3.3 "acceptera+observera" från E2h blev en defekt), (2) native `::-webkit-search-cancel-button` rensade texten men committade ingen delta → filtren överlevde, (3) djup-fråga: behövs Sök-knapp + × när resultat visas live?
- **architect + CTO INLINE (`docs/reviews/2026-06-12-sok-paritet-e2j-architect.md` + `-cto.md`):** capture-på-commit kräver explicit FE→BE-signal (backend kan inte skilja `router.replace` från `router.push`). Variant B (commit-flagga på befintlig list-query) är **materiellt skild** från ADR 0060 Beslut 3:s avvisade Variant B (separat command) — de fyra avvisnings-grunderna prövade verbatim, ingen träffar. → **ADR 0060 amendment 2026-06-12** (preciserar Beslut 3, ej ny ADR).
- **Klas-produktval (AskUserQuestion 2026-06-12, alla = CTO-rek):** (1) capture-trigger **B** (commit-flagga) + amendment-GO · (2) **behåll** Sök-knappen · (3) ×-semantik **(ii)** rensa text + de filter texten claimat, popover-val kvar · (4) toolbar-handlingar **bär commit=1**.
- **Backend (commit-guard):** `ICapturesRecentSearch.Commit` (markör-property); `RecentJobSearchCaptureBehavior` no-op:ar vid `Commit==false` (additiv till browse-/anonym-/invalid-guarderna); `ListJobAdsQuery.Commit=false`; endpoint binder `?commit=`. Test-writer FÖRST: behavior-tester (true/false/browse-additiv) + integration "live-sök utan commit fångar inte". 750 Application-unit gröna.
- **FE (`commit` strikt utanför `JobbUrlState`/`sameUrlState`/`buildJobbHref` — CTO väg 2):** `withCommitFlag`/`COMMIT_PARAM` (search-params.ts); commit-punkter (`onSubmitText` ALLTID, `onSelectSuggestion`, ny `onClear` = ×-semantik ii via `applyClaimsDelta(EMPTY_CLAIMS)`, toolbar) bär `?commit=1`; live-`onFieldChange` aldrig. **Render-sentinel skip-guard** (`else if sameUrlState(base, lastCommitted)`) skyddar own-roundtrip-detektorn + strip-efter-mount från falsk text-resync. Native × suppress:ad (CSS) + kontrollerad `.jp-hero__clearbtn`. No-JS statiskt hidden `commit=1`. Ny `StripCommitParam`-ö strippar `?commit=1` efter mount. **Popover-filter-klick bär medvetet INTE commit=1** (inkrementell komposition = live; CTO:s commit-punkt-lista exkluderade popover; data-minimerings-konservativt — bekräftat av security + code-review).
- **Reviews (alla Approved, inga blockers):** security-auditor **APPROVED 0/0/0** (PII-insamlingsväg-ändring verifierad (a)–(e); data-minimering Art. 5(1)(c) STÄRKT) · code-reviewer **Approved 0 Block/0 Major/3 Minor** (skip-guard-ordning verifierad; soft-hyphen-Minor fixad in-block) · design-reviewer **Approved 0 Blocker/0 Major/2 Minor** (WCAG 2.4.7 ×-fokusring-mönstret korrekt återanvänt; FAS-DEFERRAL-MANIFEST för rendered på auth-gated /jobb).
- **Gates:** tsc/eslint rena, **837 vitest** (+7 från 830-baslinjen), **pnpm build grön** (RSC — `/jobb` Dynamic), 750 Application-unit. ADR 0060 amendment + ADR 0067 impl-notat (E2j) skrivna.
- **CI-fångad fix (`47d60f1`):** `?commit=1` → `?commit=true` — ASP.NET `bool`-binding tar inte "1" (hade 400:at list-queryn). 8/8 RecentSearches-integrationstester verifierade lokalt mot Testcontainers efter fix. Lärdom: kör integration lokalt, inte bara unit/vitest.
- **Pending Klas:** rendered-granskning på auth-gated /jobb (×-knapp i båda teman, fokus-retur efter clear, commit-flödet Sök→Senaste sökningar live). Popover-commit-scoping (medvetet live) kan justeras på Klas-signal.

**Commit-kedja (squash-merge-SHA på main, sök-paritet-bågen):**

| SHA | PR | Beskrivning |
|---|---|---|
| `01a6039` | #29 | Fas A — ADR 0067 + ADR 0043-amendment (design) |
| `154fb07` | #30 | Fas B1 — Klass 1 data-layer (kommun/yrkesgrupp STORED) |
| `1fd9600` | #31 | Fas B2 — Klass 2 data-layer (anställningsform/omfattning + re-ingest-trigger) |
| `bc54a84` | #32 | Fas C1 — query/filter-layer + yrke-nivåbyte |
| `481e1f0` | #33 | Fas C2 — VO-expansion + reverse-lookup-migration + jsonb-bakåtkompat |
| `cefa60f` | #34 | chore/editor-baseline — .editorconfig + .vscode + docs-drift-fix |
| `e06c678` | #35 | docs(spec) — CLAUDE.md §1.6-rad current-work-archive |
| `06b7840` | #36 | docs(design) — handoff-bundles + agent-roster-CTO-rapport |
| `ed959c0` | #37 | Fas D1 — facet-counts + utökad typeahead-suggest |
| `13eb0af` | #38 | Fas D2 — ISearchQueryParser residual-fritext |
| `86b61ae` | #39 | Fas E1b — typeahead-suggest FE-kontrakt SuggestionDto[] |
| `f860ddf` | #40 | Fas E1a — /jobb-hero varm papperston-canvas (Papperskontoret) |
| `2922a25` | #41 | Fas E2a — yrke-nivå-skifte → yrkesgrupp (ssyk-level-4) |
| `7140c6b` | #42 | Fas G1 — grön accent-identitet + F4-banner (ADR 0068) |
| `74a25a9` | #43 | Fas G2 — banner-konsekvens (Sök jobb, 1136-alignment, F4-platta-rollout) |
| `08abb7b` | #44 | Fas G3 — konsekvensfixar (Sök jobb top-left, vit pagehero-CTA, a:hover-rotfix) |
| `c43a9d8` | #45 | Fas G4 — landing-redesign (produkt-forward ljus hero, login → topbar) |
| `cb42575` | #46 | Fas E2b — Län→Kommun-kaskad + geo-union region∪kommun |
| `0a4f48d` | #47 | Fas E2e — Rensa-röda-textlänkar + sorterings-labels |
| `65c3c8f` | #48 | Fas E2c — facet-counts-endpoint + NBomber-gate + FE live-counts |
| `e4071fe` | #49 | Fas E2f — tom kaskad-kolumn + markerade kommun-rader + de-gröna rubriker |
| `c3a8b57` | #50 | Fas E2g — hero-ö-state-synk (useOptimistic) + recent-search-labels |
| `1061bc2` | #51 | Fas E2d — typeahead-chip-komponist |
| `5f4e1cc` | #52 | Fas E2h — chips-i-sökfältet (UI ersatt av E2i) |
| `438a770` | #53 | Fas E2i — spegel-sökfält |
| `741727a` | #54 | Fas E2j — sök-commit-modell |

---

## Pending operativt för Klas

1. **Post-merge-granskning E2b (ADR 0065 automerge):** PR-diffen + Vercel-rendering av Ort-pickern (Län→Kommun, "Hela länet"-label) — design-reviewer godkände mot kod/diff; rendered-verifiering var pending live-deploy per runbook (auth-gated /jobb). Notera ram-utvidgningen: ~10 backend-rader geo-union (CTO-dom — Platsbanken-semantik var union, inte AND; full motivering i `docs/reviews/2026-06-11-sok-paritet-e2b-cto.md`).
2. **KLAS-STOPP — chip/residual-kombinationssemantik (ADR 0067 Beslut 5):** krävs INNAN E2d wirar chip+residual. Bekräfta `(dim-predikat) AND (FTS ∨ title-LIKE ∨ synonym)`. Natt-promptens bekräftelse-rad lämnades tom → E2d HALT.
3. **Klas-triage — `saved-searches.ts`-zod-drift (pre-existing, code-reviewer Minor):** FE-schemat kräver `ssyk`/`ssykLabels` men backend `SavedSearchDto` bär OccupationGroup/Municipality/Region sedan C2 — latent hård zod-fail för första FE-konsument av sparade sökningar. Egen touch innan saved-search-FE-ytan byggs.
4. **Logo-översyn (separat, Klas-ägd):** guld `#FFCD00` vs handoffens `#E8C77B` + og/twitter-wordmark — tas när du vill.
5. **Re-ingest Klass 2 — AKTIVT STEG** (`POST http://localhost:5049/api/v1/admin/job-ads/backfill-klass2`, admin-auth, ~2,5h). Sekvens-Klas-GO givet 2026-06-12 (CTO-rek "re-ingest först"); själva körningen är din operativa åtgärd. Populerar `employment_type_concept_id`/`worktime_extent_concept_id` (NULL för 100% av raderna tills körd). Verifiera efter: `SELECT count(*) FILTER (WHERE employment_type_concept_id IS NOT NULL) AS emp_pop, count(*) FILTER (WHERE worktime_extent_concept_id IS NOT NULL) AS wt_pop, count(*) AS total FROM job_ads;` → emp_pop/wt_pop ≫ 0. **Query-wiringen** (filter + VO + facet) byggs i efterföljande PR mot sann data (ADR 0067 Beslut 6/7-sekvens; test-writer FÖRST). Detalj: `docs/research/2026-06-12-fas-b2-state-discovery.md`.
6. **CLAUDE.md §11.3-drift** (`make dev`/`pnpm dev:up` finns ej) — skapa-vs-stryk-beslut vid nästa spec-touch (kvarstår).
7. **"Obestämd ort/Utomlands"** — deferrad med payload-verifierings-trigger (ADR 0067 impl-notat E2b); explicit rest mot TD-100-stängningen.
8. **Rendered-granskning E2j** (×-knapp båda teman, fokus-retur, commit-flödet) — se E2j-blocket.

---

## Historik

All tidigare session-historik (Fas E2i, E2h, E2d, E2c, E2e, E2b, E2a, E1a, E1b, D2, D1, editor-baseline, Fas C2 och bakåt): **`docs/current-work-archive.md`** (omvänd kronologi) + per-session-loggar i **`docs/sessions/`**.
