---
session: F6 P4 FTS-deploy-verify + TD-86 lyft + Klas-direktiv fas-paus av sök-arbetet
datum: 2026-05-23
slug: f6-p4-fts-deploy-verify-och-faspaus
status: DEPLOYAD v0.2.56-dev (blandat perf-utfall — specifika termer 6-11× snabbare, common-term-fallet sämre); TD-86 lyfted; sök-/filter-arbetet PAUSAT per Klas-direktiv
commits:
  - "3bfb27d feat(web): F6 P4 — synlig 'Söker…'-text i sök-laddning"
  - "7bc6233 docs(tech-debt): TD-86 — sök/filter-hardening samlad"
deploys:
  - "v0.2.56-dev — tag-push 2026-05-23, run 26219978114, 12m51s, success — LIVE på dev"
adrs:
  - "Inga nya/amendade ADRs (ADR 0062 deploy-utfall = drift-observation, ej ADR-amendment-trigger)"
---

# F6 P4 FTS-deploy-verify + TD-86 lyft + Klas-direktiv fas-paus av sök-arbetet

**HEAD vid session-end:** `7bc6233` (origin/main). **Deploy:** `v0.2.56-dev` LIVE på dev (run `26219978114`, success). **build-CI:** grön.

## Mål

Pending från föregående session (2026-05-21, HEAD `81c6714`): Klas-GO för tag-push `v0.2.56-dev` → triggar dev-deploy → deploy-verifiering via `explain-search`-mode mot riktig korpus. Plus två polish-spår: synlig "Söker…"-text i sök-laddning (ARIA 1.2 nameFrom=author-disciplin) och TD-86-konsolidering av samtliga öppna sök-/filter-trådar inför fas-paus.

## Vad som gjordes

### 1. Tag-deploy v0.2.56-dev (Klas-GO 2026-05-23)

Klas körde `git push origin v0.2.56-dev` → triggade `deploy-dev`-workflow run `26219978114`. Total körtid 12m51s, end-to-end success. v0.2.56-dev LIVE på dev. ECS-services force-new-deployment passerade både api+worker.

### 2. Deploy-verifiering via explain-search-mode (ECS task 33bd7299...)

Permanent operativt verktyg från föregående session (ADR 0062 commit `6f2769b`) kördes mot live dev-korpus. EXPLAIN ANALYZE-rapport på FTS-query-planen. Korpus-storlek vid mättillfället: **56 635 rader** (växte 51 749 → 56 635 mellan 2026-05-21 och 2026-05-23, +4 886 rader).

**Perf-utfall — blandat:**

| Sökterm | Före (trigram) | Efter (FTS) | Förändring | Plan |
|---------|----------------|-------------|------------|------|
| systemutvecklare | 1.6s | 270ms | **6× snabbare** | BitmapOr GIN-tsvector + trigram-title ✓ |
| ekonom | 5.0s | 464ms | **11× snabbare** | BitmapOr GIN-tsvector + trigram-title ✓ |
| lärare | 18.7s | **23.5s COUNT (sämre)** | regression | **Seq Scan** över GIN-tsvector |
| sjuksköterska | ~5s | **21.4s (sämre)** | regression | **Seq Scan**, samma orsak |

**Rotorsak till common-term-regressionen:** Planneren väljer Seq Scan över GIN-tsvector eftersom den svenska stemmern reducerar `"lärare" → "lär"`, vilket matchar 14k+ rader (≈25 % av korpus). Vid den selektiviteten är Seq Scan + de-TOAST av `search_vector`-kolumnen billigare än Bitmap Heap Scan + recheck (recheck-cost är inte ett trigram-artefakt — `search_vector` är TOAST:ad och innehåller description-lexem, samma I/O-börda som trigram).

**ADR 0062 medveten trade-off bekräftad — description-LIKE-borttagningen hjälpte inte:** Antagandet var att ta bort description-LIKE skulle reducera de-TOAST på description-texten. Men `search_vector` är `to_tsvector('swedish', title || ' ' || description)` STORED → innehåller description-lexem → samma TOAST-I/O vid recheck/de-TOAST. Den medvetna trade-offen i ADR 0062 (FTS-recall via stemmer + ts_rank > description-LIKE-substring-recall) håller principiellt men löser inte common-term-perf-fallet.

### 3. Synlig "Söker…"-text i sök-laddning (commit `3bfb27d`)

Skeleton-toolbar-platshållaren i `/jobb` byttes mot synlig `<p>Söker…</p>`-text. `aria-label` borttaget — ARIA 1.2 `nameFrom=author`-regel: ett element med synlig text exponerar texten som accessible name automatiskt; en `aria-label` ovanpå skapar dubbel-annotation och konflikt för screen readers. Disciplin från jobbpilot-design-a11y-skill.

### 4. TD-86 lyft (commit `7bc6233`) — sök/filter-hardening samlad

Inför fas-paus av sök-arbetet samlades samtliga öppna trådar i en enda TD för att inte tappa kontext:

- recall-gap vs Platsbanken (198 träffar JobbPilot vs 800+ Platsbanken — observerat 2026-05-23)
- common-term-perf-regression (lärare 23.5s, sjuksköterska 21.4s — Seq Scan-planneren)
- F6 P4c query-token-parser ("lärare göteborg" smart-sök)
- P2-backfill-verifiering (~51k → 56k legacy-rader genom snapshot-sync)
- description-LIKE-trade-off-omprövning (ADR 0062 Beslut C/D)
- stemmer-aggressivitet (svensk Snowball reducerar `lärare→lär`)
- kommun-pickers (ADR 0055-amend gjorde Ort-popover Län-only)
- spinner-mi1/mi2 (kvarvarande Minor från design-reviewer F6 P4-spinner)

**Klassificering:** Minor × Trigger (sektion Performance/Product quality/Search). Trigger-betingelse = användarsignal eller faktisk skala-mätning (recall-gap är observerad mot Platsbanken men inte ännu prioriterad av Klas).

### 5. Klas-observation 2026-05-23 — snapshot-retention saknas

Korpus 56 635 rader vs Platsbankens ~46 000 (löpande publicerade) → JobbPilot rensar INTE utgångna jobb i `sync-platsbanken-snapshot`. Snapshot är endast UPSERT, ingen `MarkExpired`/soft-delete-pass över rader som inte längre finns i feeden. Klas drog snapshot-retention som dedikerat nästa punkt (separat från TD-86 — det är en domän-feature, inte sök-hardening).

### 6. Klas-direktiv 2026-05-23 — pausa sök-/filter-arbetet

Med blandat deploy-utfall, recall-gap-observationen och korpus-retention som blockerare gav Klas följande direktiv:

> Pausa sök-/filter-arbetet. TD-86 är lyfted och behåller kontexten. Fortsätt med andra punkter.

**Nästa punkter dragna av Klas 2026-05-23 (sekvens ej låst):**

1. Snapshot-retention (rensa utgångna jobb i `sync-platsbanken-snapshot`)
2. Landing live-stats (ersätt hårdkodad `getLandingStats()` per ADR 0056-utbytespunkt)
3. Jobbkort-Spara/Har-ansökt (F6 P4b SavedJobAds backend-prompt — paus-rivs)
4. Översiktssida `/oversikt` (handoff i `docs/jobbpilot-v3-bundle/JobbPilot/handoff-oversikt/`)
5. Stängd registrering + gäst-mockdata (ADR 0005-koppling)

## Beslut och detours

**Deploy-utfall = drift-observation, inte ADR 0062-amendment.** Common-term-regressionen är en empirisk planner-observation på dev-korpus med en specifik storlek (56k) och en specifik stemmer-aggressivitet. ADR 0062-beslutet (FTS-hybrid + Infrastructure-query-port) står — det är *implementations-strategin* som kräver fortsatt arbete (query-token-parser, ev. stemmer-tuning eller GIN-tsvector partial-index på vanliga lemman). TD-86 äger fortsättningen; ingen ADR-amend triggas av denna session per §9.6 + adr-keeper-disciplin.

**ARIA 1.2 nameFrom=author > aria-label-redundans.** Synlig text på ett `<p>` *är* dess accessible name. `aria-label` ovanpå skapar konflikt — många screen readers prioriterar aria-label och ignorerar därmed den faktiska visuella texten. Disciplin från jobbpilot-design-a11y-skill, kodifierad här som mönster för framtida skeleton/laddnings-text.

**Inga ADRs amendade trots deploy-utfall.** ADR 0062-amend skulle kräva adr-keeper-pass och webb-Claude-verbatim-prosa (§9.4) — drift-observationer som inte ändrar beslutsmekanik tillhör session-loggen + TD-listan, inte ADR-historiken (ADR-amend-disciplin: amend = ändrad mekanik/beslut, inte ändrat utfall).

## Reviews

- **design-reviewer:** Söker-text-fix (`3bfb27d`) — APPROVED (a11y-disciplin direkt motiverad av jobbpilot-design-a11y-skill).
- **code-reviewer / security-auditor:** Ej invokerade — båda commits trivial-scope (ren UI-text, docs-only TD-lyft).

## Tester

| Svit | Status |
|------|--------|
| Backend full svit | Ej kört denna session (deploy-only + UI-text + docs) |
| Frontend vitest (sök-relaterat) | Påverkas ej av `3bfb27d` — synlig text byter inte rendering-logik |

`build`-CI grön (post-`3bfb27d` push).

## Commits

2 commits, `81c6714`→`7bc6233`:

- `3bfb27d` feat(web): F6 P4 — synlig "Söker…"-text i sök-laddning
- `7bc6233` docs(tech-debt): TD-86 — sök/filter-hardening samlad

## Agenter

docs-keeper (denna synk — current-work / steg-tracker / session-logg / ADR-index-konsistens-check). Inga andra agenter invokerade — deploy + UI-text-polish + TD-lyft + fas-paus-direktiv är inte agent-scope-utlösande.

## Pending (Klas)

1. **Klas-valt nästa spår** (5 punkter ovan, sekvens ej låst — Klas drar nästa i kommande session).
2. **Fas 4 pre-reqs kvar oförändrade** (ej blockerade av denna session):
   - 5 GDPR-villkor (ADR 0051): DPIA Art. 35 / SCC+Schrems II-TIA+Anthropic-DPA+DPF / versionerad privacy-policy live / Art. 25-opt-in / ADR 0049-decrypt-interaktion
   - KMS-rehoming (ADR 0049/0050-cross-ref) — full AWS-exit tar bort AWS KMS, krypto måste om-hemmas före faktisk migration

## Nästa session

Klas-vald punkt ur 5-listan. Sannolik kandidat (Klas-observation 2026-05-23): snapshot-retention först eftersom den löser både korpus-drift och en del av recall-gap-observationen indirekt (utgångna jobb i index sänker recall mot färska Platsbanken-resultat).

## Disciplin-noter

- senior-cto-advisor EJ invokerad — inga multi-approach-val, ingen TD-skapande-validering nödvändig (TD-86 = ren konsolidering av kända öppna trådar, inte ny TD-väg).
- adr-keeper EJ invokerad — drift-observation ≠ ADR-amend (se "Deploy-utfall" ovan).
- Inga TDs stängda; **TD-86 NY** (Minor × Trigger).
- Pre-existing oparsade ändringar (`.claude/settings.json`, `.claude/scheduled_tasks.lock`, `docs/jobbpilot-v3-bundle/`, `docs/reviews/2026-05-17-agent-roster-gap-cto.md`) RÖRDA EJ.
- docs-keeper-synk (denna fil + `docs/current-work.md`) committas pathspec-scoped av CC i en separat docs-commit per stående praxis.
