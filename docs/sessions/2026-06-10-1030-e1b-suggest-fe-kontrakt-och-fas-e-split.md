---
session: Platsbanken sök-paritet Fas E — uppdelning + E1b suggest-kontrakt
datum: 2026-06-10
slug: e1b-suggest-fe-kontrakt-och-fas-e-split
status: E1b+E1a+E2a+G1+G2 MERGADE (#39-#43); G3 konsekvensfixar byggd (rubrik-align/vit-CTA/a:hover-rotfix), pending Klas-GO; landing-redesign väntar Klas-riktning
commits:
  - 5fee02c feat(jobads) E1b typeahead-suggest FE-kontrakt SuggestionDto[]
  - (docs-commit) ADR 0067 impl-note + current-work + session-log + reviews
---

# Fas E — design-grind: uppdelning + E1b-leverans

## Mål (Klas-prompt)

Fas E (FE-sökyta) med låst design-riktning A "Papperskontoret" + varm papperston
`#FAF9F6`. Klas-prompten rekommenderade E1 (design-grind + microcopy +
kontrakts-migration + docs-drift) som första session, med förväntad sub-splitt.

## Vad som gjordes

### Discovery + agent-domar (före kod)

1. **Förkrav verifierade:** HEAD `13eb0af`, status clean, docker uppe, Api
   `/api/ready` 200, FE 3000 200.
2. **dotnet-architect — varm-canvas-token:** /jobb-scoped ny `--jp-hero-canvas`
   (rör ej app-wide `--jp-canvas`), dark ärver `--jp-canvas` #0B1525, behåll
   `.jp-hero`-klassnamn, 12px→6px. (`docs/reviews/2026-06-10-sok-paritet-e1-architect.md`)
3. **senior-cto-advisor — split + radius:** radius 12→6 = cleanup (ingen ADR
   0052-amendment, Klas-prompt är GO); SPLITTA E1→E1b (plumbing, code-reviewer)
   först → E1a (design, design-reviewer VETO + Klas-GO).
4. **Verifierat backend-kontrakt on-disk:** list-endpoint tar `?occupationGroup=`
   (`?ssyk=` borttagen C2); `SuggestionDto = {kind, conceptId, label}`, kind =
   native enum utan converter → HELTAL på wire; `RecentJobSearchDto` har nya
   `occupationGroupList`/`municipalityList`/-Labels + deprecated tomma
   `ssykList`/`ssykLabels`.
5. **KRITISKT fynd — entanglement:** FE:s `taxonomy.ts` är stale (modellerar
   occupation-name, ej yrkesgrupp/kommun som backend `TaxonomyTreeDto` redan
   exponerar). Live Yrke-picker matar occupation-name-ids → `?ssyk=` →
   backend ignorerar = tyst no-op idag. `?ssyk=`→`?occupationGroup=`-rename utan
   picker-nivå-skifte → Yrke-filter regresserar till **noll träffar**.
6. **senior-cto-advisor — återtriage (Approach A):** E1b = `/suggest`-migration
   ENDAST; param-rename + recent-shim + picker-skifte → E2 (atomiskt block).
   **CTO flaggade Klas-GO** — omförhandlar Klas-promptens E1-lista.
   (`docs/reviews/2026-06-10-sok-paritet-e1-cto.md`)

### E1b-implementation (suggest-kontrakt)

- `lib/dto/job-ads.ts`: `suggestionDtoSchema` + `suggestionKindFromWire`
  (wire-heltal → namn via `SUGGESTION_KIND_ORDER`, defensivt int|string-union
  speglar `sortByFromWire`).
- `job-ad-typeahead.tsx`: konsumerar `SuggestionDto[]`, renderar `item.label`,
  `choose(item.label)`. kind/conceptId = kontraktsfält, chip-komposition E2.
- Tester: +7 schema-fall i `job-ads.test.ts`; fixtures migrerade i
  `job-ad-typeahead.test.tsx`. 45 vitest gröna, tsc/eslint rena, pnpm build grön.
- Reviews: code-reviewer 0 Block/0 Major/1 Minor (kommentar-precision, in-block);
  security-auditor APPROVED.

## Beslut & avvägningar

- **E1 split + sekvens E1b→E1a:** SRP-på-PR-nivå, olika gater, granskbar diff.
- **Radius cleanup ej amendment:** ADR 0052 Beslut 4 är permissiv tak-regel.
- **E1b = suggest-only:** entanglement-fyndet gör param-rename till icke-plumbing
  (regression-risk). Approach A undviker falsk-klar mot TD-100.
- **`JobAdTypeahead` ej wirad live:** migrationen är noll-regression.

## E1a-leverans (samma session, efter Klas Approach-A-GO)

- **Scope bekräftad:** Klas valde "Bekräfta CTO Approach A" (AskUserQuestion) → E1b=suggest-only
  (mergad #39), param-rename + recent-shim + picker → E2.
- **/jobb-hero "Papperskontoret":** navy-banner → varm papperston-canvas. Ny `--jp-hero-canvas`
  (#FAF9F6, /jobb-scoped). Regel-1-fixar (drop-shadow→border, 40px→28px H1; 12px redan compliant).
  Sök-knapp navy-800. Microcopy (H1 "Lediga jobb", ingen Platsbanken-verbatim). Dark: ljust sökfält.
- **design-reviewer VETO→APPROVED:** 2 Blockers (placeholder-kontrast light 3.5:1 / dark 2.41:1)
  åtgärdade via ny `--jp-placeholder`-token (#626B78, 5.39:1 / 4.89:1). Re-review 0 fynd.
- **Pending:** Klas rendered-GO (Vercel-preview) + docs-drift spec-edit (Klas `approve-spec-edit.sh`).
- **Detour:** visual-verify auth-mode kräver https (`__Host-`-cookie) → lokal auth-rendering blockerad;
  rätt källa = Vercel-preview. Registrerade dev-test-kontot lokalt (för login-test) — harmlös dev-artefakt.

## E2a-leverans (samma session, efter Klas GO E2 / Approach A)

- E1a-hero MERGAD av Klas (#40, rendered-GO). Klas "GO" → E2.
- **E2a = atomisk korrekthets-batch (20 filer, EN commit):** Yrke-pickern skiftar nivå
  occupation-name → yrkesgrupp (ssyk-level-4) för TD-100-paritet. FE-taxonomy-DTO
  `occupations`→`occupationGroups` (occupation-name droppad, ACL); `?ssyk=`→`?occupationGroup=`
  atomisk rename (Fowler, TS-säkrad); recent-shim; cap 10→400.
- **dotnet-architect** gav E2a-spec (drop occupations, municipality→E2b, atomisk rename, cap 400).
- **Reviews:** code-reviewer 0 Block/0 Major/1 Minor (in-block), security-auditor APPROVED,
  design-reviewer APPROVED. Empirisk backend-verifiering: 400 yrkesgrupper populerade.
- **Detour:** pnpm build clobbrade `pnpm dev`:s `.next` igen → FE-dev-server omstartad (bg).
- **Pending:** Klas rendered-GO (Vercel-preview PR #41).

## Fas G1 — grön accent-identitet + F4-banner (samma session, ADR 0068)

- E2a mergad (#41) av Klas. Klas levererade NY design-handoff (`docs/handoff-banner/`,
  Claude Design "F4 Hybrid"): bannern tillbaka (saknades — för tomt), accentbyte
  blå/navy→mörkgrön i HELA appen + landing; CC fick spec-fil-mandat.
- **CTO-triage:** neutraler A (v3 består — referensens slate = stale scaffolding);
  scope i–iii (accent app-wide nu, /jobb full F4, pagehero/landing färg-swap nu +
  F4-komponent senare); ny ADR 0068 (supersedar ADR 0052 Beslut 6 delvis + E1a-heron;
  absorberar pending docs-drift → skills direkt till grön kanon); fas-namn G1; E2b–E2e pausade.
- **Architect:** `--jp-accent-*`-ramp (hue-neutral namnrymd — logo-fällan: navy-700
  konsumeras av kompassen via currentColor), alias-flip, gradient-composite-token +
  solid ankare #14503A, fokus-property-scoping, fill/text-split i dark (800 ej skiftad /
  #6EE7A8 aldrig fill). 3 fynd: matchchip-grönkollision→neutral, shadcn-ring-WCAG-fix,
  referens-placeholder-AA (moot — Klas-regel: INGEN placeholder, memory uppdaterad).
- **Bygge (nextjs-ui-engineer):** globals.css (~550 rader), F4-platta på /jobb +
  guest-klon, mekanisk navy→accent-rename, E1a-hero riven (--jp-hero-canvas utgår).
  Jag kompletterade: land-hero hårdkodad #0A2647→gradient, auth-card inline-navy→accent,
  CTA→hero-bg, vit-fokus-scope + land-hero.
- **Spec-sync (Klas-mandat, approve-hook per edit):** ADR 0068 + index; CLAUDE.md §5.2
  gradient-undantag (scoped 4 ytor); DESIGN.md §1.2+§3 (grön kanon); principles-skill
  (regel 1-undantag + regel 5 grön); tokens-skill 5 filer (docs-keeper — full grön+v3-sync,
  docs-drift STÄNGD); components/a11y-skill-faktafel fixade.
- **Reviews:** design-reviewer 0 VETO / 2 Major (CLAUDE.md-scope-rad + landing-CTA
  leaf-på-grön→ghost) — åtgärdade in-block; code-reviewer 0 Block / 1 Major
  (components-skill-hover skulle producera #6EE7A8-fill i dark) — åtgärdad + minors
  (pill-token, WaitlistForm-fokus, placeholder-token-kommentar, radius-rad).
  Matchchip-mid→neutral GODKÄND. TD-108 lyft (pre-existing warning-bg 4.2:1 +
  border-strong 2.5:1 — G1 låser status/neutraler, in-block vore scope-brott).
  security-auditor EJ invokerad (ren CSS/copy/spec — ingen §9.2-yta).
- tsc/eslint/build gröna; 721 vitest gröna.

## Fas G2 — banner-konsekvens (samma session, Klas rendered-feedback på G1)

- G1 mergad (#42, Klas-GO). Klas tre rendered-fynd: AI-aktig H1, platta smalare än
  kort, kant-till-kant-banners + olika rubrik-typografi per sida.
- **Fixar:** H1 → "Sök jobb" (GOV.UK-mönster, inget utropstecken per §10.3 trots
  Klas-exemplets "!"); `.jp-page` shorthand→`padding-block` (rotorsak: kaskad-
  kollision med `.jp-container` — innehåll blev 1200 i st.f. 1136; Playwright-
  verifierad alignment 532→1668 på ALLA ytor efteråt); F4-platta-rollout
  tidigarelagd till pagehero (alla inre sidor, titel 44/800) + landing.
- **design-reviewer 0 VETO / 2 Major in-block:** M1 spec-trail (ADR 0068 G2-notat +
  DESIGN.md §4 + tokens-skill — display följer platta-komponenten, 1136-kanon);
  M2 dubbel-grön (empty--brand neutraliserad på pagehero-sidor + en-primary:
  pagehero-CTA ghost vid tom pipeline). + fokus-scope till inner-plattorna,
  typo "råd"→"ansökan". 721 vitest gröna.
- **CodeQL-incident (orelaterad):** transient GitHub-API-401 fällde csharp-analyze
  på #42 + första workflow-dispatch — båda löstes med rerun/retry.

## Fas G3 — konsekvensfixar (samma session, Klas rendered-feedback på G2)

- Tre fynd: "Sök jobb" centrerad (vill top-left), pagehero-CTA grön (vill vit),
  "Skapa första"-knapp grön text på hover (osynlig).
- Fixar: `.jp-hero__plate align-items: end→start`; pagehero-CTA alltid vit
  (`.jp-pagehero .jp-btn--primary`); **rotfix** `a`/`a:hover` → `a:not(.jp-btn)`
  (knapp-`<a>` ärvde länkfärg i hover pga specificitet 0,1,1 > 0,1,0 + knapp-hover
  satte bara bakgrund — lagar även latent secondary-`<a>`-bug). design-reviewer
  Approved 0 fynd. 721 vitest gröna.
- **Klas fynd #1 (landing "grön box ful")** → Fas G4 (separat); Klas valde CC-proposal.

## Fas G4 — landing-redesign (samma session)

- **CTO låste Riktning A** (produkt-forward ljus hero): grön box bort (Mercury "produkten
  ÄR gränssnittet"); login-formulär bort från hero (DRY — /logga-in finns) → topbar-
  "Logga in"-länk; statisk produkt-peek; grönt → accent. Features/footer orörda.
- **nextjs-ui-engineer byggde:** ljus `.jp-land-hero`, `.jp-land-peek*` (grön mini-banner-
  attrapp + 2 flat jobbkort), topbar-login, AuthCard+oauth-mark RADERADE (LoginForm orört),
  tester uppdaterade samma commit. design-reviewer APPROVED 0 VETO/0 Major/2 Minor FYI.
  Egen-verifierad light+dark (skärmdump). 716 vitest gröna. Rebasad på G3-main (08abb7b).

## Nästa session

1. **Klas rendered-GO på G4** (Vercel-preview light+dark) → automerge.
2. **Logo-översyn** (separat Klas-ägd): guld #FFCD00 vs #E8C77B + og/twitter-wordmark.
3. **E2b–E2e återupptas** (Klas-GO per split; byggs i grön identitet):
   E2b kommun-kaskad; E2c facet-count + NBomber; E2d chips (kräver chip/residual-
   semantik-GO); E2e Rensa/sortering. Re-ingest Klass 2 gated.

## Stack-status vid sessionsslut

Api 5049 / Worker / FE 3000 körande (ingen bygg-lås-stopp denna session — endast
FE-edits, ingen .NET-rebuild). `/api/ready` 200 verifierad vid start.
