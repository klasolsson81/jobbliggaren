---
session: E2j — sök-commit-modell
datum: 2026-06-12
slug: e2j-sok-commit-modell
status: PR öppen (automerge)
commits:
  - feat(web,jobads): Platsbanken sök-paritet Fas E2j — sök-commit-modell (commit-intent-capture)
  - docs: ADR 0060 amendment + ADR 0067-notat E2j + agent-reviews + current-work + session-logg
---

# Session E2j — sök-commit-modell (Klas rendered-feedback på E2i live-spegel)

## Bakgrund
Klas testade E2i (#53) renderat: live-sök visar jobb direkt utan Enter/Sök. Tre
kopplade problem: (1) "Senaste sökningar sparas inte som väntat", (2) native ×
rensar texten men inte filtren, (3) djup-fråga: behövs Sök-knapp + × när
resultat visas live? Modell-session (designbeslut, ej mekanik) → architect + CTO
INLINE + Klas-produktval.

## Discovery (empiriskt + web)
- **Empiriskt (dev-DB):** `recent_job_searches` full vid cap=20 för EN seeker,
  raderna synligt mellanstegsspam ("Systemutvecklare" ensam / +kommun /
  +yrkesgrupp som separata rader). Verifierade kedjan: live-`router.replace` per
  ord → RSC-render av `JobbResults` → `ListJobAdsQuery : ICapturesRecentSearch`
  → capture per mellansteg → evictar äkta committade sökningar. F3.3
  "acceptera+observera" (E2h CTO-uppskjuten) blev en defekt + data-minimerings-
  regression (Art. 5(1)(c)).
- **× ursprung:** native `::-webkit-search-cancel-button` (typeahead `type="search"`)
  — `onChange("",0)` utan avgränsar-keystroke → ingen delta-commit → filtren
  överlever i URL. WebKit/Blink-only (Firefox visar den aldrig).
- **Web (sök-UX-standard):** live-resultat + committad historik är komplementära;
  recent sparas vid explicit submit, inte per keystroke (Safari/Algolia/Google).
  `::-webkit-search-cancel-button` rensar bara value, inte filter — kan suppress:as
  + ersättas med kontrollerad knapp (MDN).

## Beslut
- **architect:** capture-på-commit kräver explicit FE→BE-signal (backend kan inte
  skilja replace/push). **Variant B (commit-flagga på befintlig list-query)** ≠
  ADR 0060:s avvisade Variant B (separat command) — de fyra avvisnings-grunderna
  (trust-flytt, dubbla round-trips, race, FE persisterar filter-shape) träffar
  inte. Native × MÅSTE bort. `commit` utanför state. ADR 0060-amendment krävs.
- **CTO (decision-maker):** VAL 1 = B · VAL 2 = amendment (ej ny ADR), Klas-GO
  på substansen · VAL 3 = behåll Sök-knapp · VAL 4 = ×-semantik (ii) · VAL 5 =
  väg 2 (`commit` strikt utanför state + strip-efter-mount) CTO-bestämt · VAL 6 =
  toolbar bär commit=1 · VAL 7 = security-auditor obligatorisk. Allt in-scope
  E2j, inga TD.
- **Klas (AskUserQuestion):** alla fyra produktval = CTO-rek (B + amendment-GO,
  behåll Sök, ×(ii), toolbar-commit=1).

## Levererat
- **Backend:** `ICapturesRecentSearch.Commit`; behavior commit-guard (additiv);
  `ListJobAdsQuery.Commit=false`; endpoint `?commit=`. Tester FÖRST (behavior +
  integration "live utan commit fångar inte").
- **FE:** `withCommitFlag`/`COMMIT_PARAM` (utanför `JobbUrlState`); commit-punkter
  bär `commit=1` (`onSubmitText` ALLTID, `onSelectSuggestion`, ny `onClear`=×(ii),
  toolbar); skip-guard (`sameUrlState(base, lastCommitted)`); native ×-suppress +
  `.jp-hero__clearbtn`; no-JS hidden `commit=1`; `StripCommitParam`-ö.
- **ADR 0060 amendment 2026-06-12** (a)–(d) + ADR 0067 impl-notat (E2j).

## Detours / beslutsdetaljer
- **Skip-guard `prevBase`→`lastCommitted`:** första implementationen jämförde
  `base` mot `prevBase` (stale vid mockad router) → en befintlig "Rensa allt"-test
  röd. Rättat till `lastCommitted` (hero:ns auktoritativa state) — strip-efter-mount
  + sort-only skyddade, äkta extern "Rensa allt" resyncas korrekt.
- **`onSubmitText` ALLTID-commit:** "Sök" måste bumpa recency även när filter-
  staten är oförändrad (re-sökning), inte vara no-op. Förslags-val likaså.
- **Popover-klick bär INTE commit=1:** medvetet — CTO:s commit-punkt-lista
  exkluderade popover; inkrementell komposition = live (som typing). Data-
  minimerings-konservativt. Bekräftat av security + code-review; flaggat för Klas.
- **Strip via separat ö (ej commit()-vägen):** CTO band "strip via commit()-vägen";
  jag löste samma invariant (ingen falsk text-resync) renare via skip-guarden
  (`sameUrlState(base, lastCommitted)`) + en fristående `StripCommitParam`-ö (SoC).
  Samma mål, robustare mekanism (oberoende av recentCommits-timing). Dokumenterat
  för reviewers; code-reviewer godkände ordningen explicit.

## Reviews
- security-auditor: **APPROVED 0/0/0** (a)–(e) verifierade; data-minimering stärkt.
- code-reviewer: **Approved 0 Block/0 Major/3 Minor** (soft-hyphen fixad in-block;
  onSubmitText/runDelta DRY + withCommitFlag-? = FYI).
- design-reviewer: **Approved 0 Blocker/0 Major/2 Minor** (WCAG 2.4.7-mönstret
  korrekt; FAS-DEFERRAL-MANIFEST för rendered; fokus-retur + 3×role=status = Minor).
- Gates: tsc/eslint rena, **837 vitest**, **pnpm build grön**, 750 Application-unit.

## Nästa session
- Klas rendered-test på /jobb: ×-knapp båda teman, fokus-retur efter clear,
  commit-flödet (Sök/Enter/förslags-val/toolbar → Senaste sökningar; live-typing
  sparar INTE). Popover-commit-scoping (medvetet live) — justeras på Klas-signal.
- Minus-operatorn (NOT) fortsatt Klas-pending (backend-fas).
- Pending sedan tidigare: spec-edit-hooken, de-grönings-domar, zod-drift-triage,
  re-ingest Klass 2.
