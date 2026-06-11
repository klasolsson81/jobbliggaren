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

## Nästa (samma natt-session)

1. E2e — Rensa-textlänkar + sortering (ExpiresAt-mappning verifieras).
2. E2c — FacetCounts query/handler/endpoint (VAL 4-semantiken) + FE
   "Visa N annonser" + NBomber observe-only (HALT vid >300ms p95).
3. E2d — HALT + morgonrapport (chip/residual-bekräftelse saknas).
