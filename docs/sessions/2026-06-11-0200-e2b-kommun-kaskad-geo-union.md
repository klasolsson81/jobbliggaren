---
session: Platsbanken sök-paritet Fas E2b — Län→Kommun-kaskad + geo-union (autonom natt-körning)
datum: 2026-06-11
slug: e2b-kommun-kaskad-geo-union
status: E2b byggd + reviewad (alla gates gröna), PR + automerge enligt natt-auktoritet; E2e/E2c följer i samma natt-session
commits:
  - 637f170 feat(jobads) geo-union region∪kommun i ApplyCriteria (CTO VAL 1 Variant D)
  - 597fd84 feat(web) Ort-pickern Län→Kommun-kaskad + ?municipality= atomiskt
  - d931b2f refactor(recentsearches) Ssyk-shim-borttagning
  - bc0ead1 test(jobads) pre-existing integ-tester → geo-union + slutgiltig DTO-form
  - 64e871a fix(web) code-reviewer minors
  - (docs-commit) ADR 0067 impl-notat E2b + ADR 0042-not + reviews + current-work + session-logg
---

# Fas E2b — Län→Kommun-kaskad + geografi-union

## Mål (Klas natt-prompt 2026-06-11)

Autonom natt-körning E2b → E2e → E2c med förauktoriserad automerge (gates:
design-reviewer 0 VETO, code-reviewer 0 Block/0 oåtgärdat Major, security
0 Crit/High, tsc/eslint/build/vitest/dotnet-test gröna). E2d = HÅRD STOPP
(chip/residual-bekräftelse-raden lämnades tom).

## Vad som gjordes

### Discovery + design (före kod)

1. **Förkrav:** HEAD `c43a9d8` (G4 mergad — current-work var stale på den
   punkten, korrigerad i denna PR), clean tree, Docker uppe, Api/FE 200.
2. **Discovery-fynd:** backend HELT klar för municipality sedan B1/C1/C2
   (param + STORED-kolumn + validator + recent-DTO-fält); FE-zod saknade
   `municipalities` OCH `municipalityList` (promptens "har redan" avsåg
   backend-DTO:n). DB-verifierat: 290 kommuner/21 län; 41 502/42 795 aktiva
   annonser har kommun; 1 293 saknar BÅDA geo-dimensionerna; ingen
   "Obestämd ort"-nod i snapshotten.
3. **KRITISKT architect-fynd:** `ApplyCriteria` AND:ade region×municipality
   sekventiellt → region=län-X + kommun-i-län-Y = noll träffar. Web-verifierat
   (JobTech GettingStartedJobSearchEN, architect + CTO oberoende):
   Platsbankens faktiska semantik är inkluderande union ("most local
   promoted"). Ingen ren FE-invariant kan ge TD-100:s 100%-paritet.
4. **CTO-dom (VAL 1–4, INGEN HALT):** Variant D (backend geo-union, ~10
   rader) + Variant A:s per-län-normalisering som UX-kosmetik. Triage:
   mekanik-konkretisering inom Accepted ADR 0067 (ADR 0042 Beslut B
   beslutade aldrig region×kommun — dimensionen fanns inte 2026-05-16);
   impl-notat, ej amendment, ej Klas-STOPP. VAL 2: "Obestämd ort/Utomlands"
   defer med payload-trigger. VAL 3: dual-axis-popover (ingen mode-flagga).
   VAL 4: E2c-facetten exkluderar HELA ort-dimensionen.

### Implementation (4 kod-commits + docs)

- **Backend:** union-gren när båda listorna icke-tomma; ensamma grenar
  orörda; 5 Testcontainers-tester (cross-län, intra-län, syntetisk
  region-only-annons = recall-bevis, enkel-gren-regression, ortogonal AND).
- **FE atomiskt:** taxonomy-zod (`municipalities` REQUIRED), recent-zod
  (`municipalityList`/`municipalityLabels`), `ort-selection.ts` (ren
  normaliserings-modul, 12 tester), buildJobbHref/buildPageHref/hidden
  inputs/Suspense-key/selectedConceptIds/toolbar-chips/recent-shim.
  Popover: `groupAxis`-kontrakt; "Hela länet" togglar ETT region-id;
  enkelkolumns-läget borttaget (noll konsumenter).
- **Shim-borttagning:** RecentJobSearchDto slutgiltig form; vakthund-test.

## Reviews + in-block-åtgärder

- **security-auditor:** APPROVED, 0 fynd (param-kedjan var redan härdad;
  unionen = parametriserad `= ANY(@p)`, BitmapOr över partial-index).
- **design-reviewer:** Approved 0 VETO/0 Major. Adjudikerade "Hela länet"
  (ärlig affordance — "Välj alla kommuner" hade lovat checkbox-bockning som
  inte sker). 2 Minor ("Hela {länsnamn}", dialogLabel-prop) → E2d-touchen.
- **code-reviewer:** 1 Block (RecentSearchesTests läste borttagna
  ssykList-fält — fixad innan rapporten ens landat, `bc0ead1`), 1 Major
  (ADR-docs saknades i diffen — skrivna, ingår i docs-committen), 3 Minor
  (cap-kommentar + stale kommentarer fixade `64e871a`; saved-searches-zod-
  drift = pre-existing → Klas-triage).
- **Full backend-svit:** 3 pre-existing tester föll (gamla AND-semantiken +
  ssykList-wire) → uppdaterade; 1771 gröna. 735 vitest, tsc/eslint/build OK.

## Beslut & avvägningar

- **Geo-union i query-lagret, inte FE-invariant:** Martin kap. 22 (policy i
  rätt lager); B′ (FE-hybrid) avvisad som "ACL upp-och-ner"; recall-luckan
  i B/B′ var latent falsk-klar (region-only-annonser ogaranterat noll).
- **"Most local promoted"-RANKNING replikeras inte** — paritet doms på
  resultatmängd (sort-ordningarna är explicita, ADR 0062).
- **"Hela länet" = ETT region-id:** 414-skydd (290 ids ≈ 7,3 KB query-string
  vs Kestrel 8 KB request-line) + en chip + recall (region-grenen).
- **Visual-verify:** auth-gated /jobb → rendered pending live-deploy per
  runbook (lokal http bär inte `__Host-`-cookien) — E2a-prejudikatet; Klas
  granskar Vercel post-merge.

## Detours

- Bygg-lås (Api/Worker körde) → stopp + rebuild + omstart × 2 (pre-commit
  bygger .NET när .cs stage:as). Stacken omstartad + /api/ready-verifierad.
- `pnpm build` clobbrade dev-serverns `.next` (känd fälla) → FE-dev omstart.
- Architect-rapportens cap-siffra (×2=800) var stale mot on-disk (×4=1600)
  — verifierad och korrigerad i kommentaren.

## E2e-leverans (samma natt-session, efter E2b-merge #46)

- E2b mergad `cb42575`; post-merge-ops körda (CodeQL-dispatch på main,
  branch raderad, stack verifierad).
- **Rensa = röd text-länk (rad 109):** `.jp-clearlink` (danger, underline,
  button) ersätter `.jp-popover__clear`; + "Rensa alla filter" i toolbaren
  (tre axlar nollas, q/sortBy/pageSize bevaras, gated på chips).
- **Sort-labels:** "Relevans / Datum (nyast) / Ansökningsdatum (sista
  ansökan)" — "(CV-match)"-faktafelet rättat (ts_rank ≠ CV-match);
  ExpiresAtAsc on-disk-verifierad (asc NULLS LAST).
- **Reviews:** code-reviewer Approved 0/0 (2 Minor — sortBy-test in-block;
  label-vs-ADR-ordalydelse dokumenterad i E2e-notatet), design-reviewer
  Approved 0/0 (3 Minor — kontrasttabell = spec-edit till Klas). 739 vitest.

## E2c-leverans (samma natt-session, efter E2e-merge #47)

- **Architect-spec + CTO-dom:** endpoint-form entydig (egen route); CTO VAL 1
  = FacetCountsPolicy 30/10s (least common mechanism — delad ListRead-budget
  hade svält LISTAN; löste samtidigt D1:s NBomber-kalibrerings-fel
  strukturellt); VAL 2 = A (per-option-counts + "Visa N annonser"-knapp på
  PagedResult.TotalCount — aldrig facett-summa). Ingen Klas-HALT (fördelegerat
  + implementerar Accepted rad 109).
- **Backend:** GetFacetCountsQuery (residual-parser-konsistens; ej
  ICapturesRecentSearch; ingen Total) + IsInEnum-skydd + VAL 4-ort-
  exkludering + 22 nya/uppdaterade tester.
- **NBomber FÖRE FE-wiring (Beslut 4-gaten):** p95 26,8/25,0 ms ≪ 300 ms
  (×11-marginal, 0 fails, dev-korpus ~43k) — fallback-trappan obehövd.
- **FE:** counts i popover-raderna (tre-tillstånds: känd nolla ≠ okänt),
  "Hela länet"-region-count, Visa N annonser-knapp via total-count-store
  (useSyncExternalStore över streaming-ö-gränsen).
- **Reviews in-block:** security APPROVED + 30/10s BLOCKING-fastställt;
  design 0 VETO/1 Major (singular "Visa 1 annons") + 1 Minor (13px);
  code 0 Block/2 Major (fel-gren 200+{} → 502 — "(0)"-desinformation;
  abort i effect-cleanup) + 3 Minor. Alla åtgärdade (85dee89).
- **Detour:** NUL-byte smög in i en teststräng → filen git-binär →
  strippad (e980046). FE-dev `.next` clobbrad av pnpm build × 2 → rensad
  + omstartad.

## E2f-leverans (samma session — Klas rendered-feedback på morgonen)

- Klas tre fynd mot Platsbanken-screenshots: (1) höger kaskad-kolumn TOM
  tills vänster-val (ingen auto-vald första grupp); (2) kommun-rader
  renderas MARKERADE vid helläns-val + "hela länet minus en kommun"
  (region-id bort, övriga materialiseras ≤48 — preciserar CTO VAL 1 till
  PÅ-toggling; komplettering kollapsar tillbaka); (3) de-gröna rubriker
  (CC-rekommendation "grönt = interaktion, inte information" — Klas-GO):
  nav/drawer-aktiv → ink + accent-bar, jobb-/app-titlar/räknar-tal/
  peek-titlar/översikts-datum → ink.
- Ny `toggleMunicipalityInRegion` + `groupAxis.onToggleItem`-kontrakt;
  7 nya/omskrivna unit-fall + flödes-tester uppdaterade (klick vänster
  först). 755+ vitest, tsc/eslint/build gröna.
- Reviews: code Approved 0/0 (4 Minor — m1 denorm-state-fix + m2 kant-
  fall-tester + m4 stale kommentar in-block; m3 opportunistisk), design
  0 VETO/1 Major spec-sync (globals.css-kommentar + ADR 0068-notat
  in-block; DESIGN.md + tokens-skill = Klas approve-spec-edit) + 4 Minor
  (3 de-grönings-rester = Klas-dom; tri-state mixed = E2d).
- ADR 0067 impl-notat (E2f) + ADR 0068 impl-notat (accent-text-rollen).

## Nästa

1. E2d — HALT (chip/residual-bekräftelse saknas; promptens bekräftelse-
   rad var tom). E2d-touchen ärver: "Hela {länsnamn}"-label, dialogLabel,
   tri-state aria-checked="mixed".
2. Spec-edits till Klas approve-hook: DESIGN.md accent-rollraden +
   tokens-skillen (accent-700-texten + kontrasttabellens dark-danger-par).
3. Klas-dom: tre de-grönings-rester (land-top is-active, land-feature__key,
   summary-highlight-värdet) + saved-searches-zod-drift-triage.
