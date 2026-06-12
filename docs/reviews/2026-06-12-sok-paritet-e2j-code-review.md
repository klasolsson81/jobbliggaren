# code-reviewer — Fas E2j sök-commit-modellen

**Status:** ✓ Approved
**Granskat:** 2026-06-12
**Auktoritet:** CLAUDE.md §2.1 (Clean Arch), §2.2 (DDD), §2.3 (CQRS), §3 (konventioner), §5 (anti-patterns), §7 (test-krav)
**Scope:** Backend (Application + Api) + Frontend (Next.js islands + lib) — 20 filer, ~932 insertions
**Underlag läst i sin helhet:** CTO-dom (`-cto.md`), architect-dom (`-architect.md`), ADR 0060-amendment, hela `git diff --cached`, on-disk `jobb-hero-search.tsx` (render-sentinel + commit-väg + form), `RecentJobSearchCaptureBehavior.cs`

**Verdict: Approved. Inga Blockers, inga Major.** Tre Minor (FYI). Samtliga sju load-bearing-punkter verifierade mot E2i-invarianterna och CTO-bindningarna. Mergeklar efter ev. Minor-städ.

---

## Verifiering av de sju load-bearing-punkterna

### 1. Render-sentinel skip-guard (`prevBase` → `lastCommitted`) — VERIFIERAD KORREKT

`jobb-hero-search.tsx:149–204`. Tre-vägs-ordningen är bevarad och korrekt:

1. **Own-roundtrip först** (`hitIndex >= 0`, rad 167–170): egen in-flight-commit landar, prune t.o.m. träffen, texten orörd, `adoptSortPageSize()`.
2. **Skip-guard** (`sameUrlState(base, lastCommitted)`, rad 171–182): bara icke-state-param (commit-flagga/sort/pageSize) skiftade → ingen resync.
3. **Extern divergens** (else, rad 183–202): text synkas, bokföring nollas.

**Strip-after-mount-skyddet håller:** `commit` ligger aldrig i `JobbUrlState`, så den strippade basen (`StripCommitParam` `router.replace` utan `commit=1`) är `===` det `next` som `commit()` satte i `lastCommitted` (rad 212). Den stripade basen träffar antingen own-roundtrip-grenen (om posten kvar i `recentCommits`) eller skip-guard-grenen (`sameUrlState(base, lastCommitted)` true) — aldrig extern-divergens. Texten serialiseras inte om. E2d/E2h-felklassen undviks.

**Bytet `prevBase` → `lastCommitted` är rätt val, inte en regression.** `lastCommitted` är hero:ns auktoritativa committade state; `prevBase` speglar senaste *props*-bas och kan släpa (props uppdateras inte synkront med egna `router.replace`). Verifierat mot extern "Rensa allt"-vägen: toolbar pushar `/jobb?commit=1` (tom state) → strippad/inkommande bas = tom; om användaren hade aktiv sökning är `lastCommitted` icke-tom → `sameUrlState(tom, icke-tom)` = false → faller korrekt till **extern-divergens** → texten resyncas till tomt. Skip-guarden miss-klassar alltså INTE en äkta extern clear som no-op. Kommentaren rad 178–181 dokumenterar exakt detta resonemang och det stämmer.

**Sekundär verifiering (toolbar-clear-dubbelreplace):** extern "Rensa allt" klassas extern-divergens (text resyncas, `setLastCommitted(base)`), därefter strippar `StripCommitParam` `?commit=1` → andra basen = nyss satta `lastCommitted` → skip-guard → ingen re-resync. Ren sekvens, ingen flimmer-risk i state-logiken.

### 2. `commit` strikt utanför state/serialize/href/resultsKey — VERIFIERAD

- `JobbUrlState` (search-params.ts) oförändrad — ingen `commit`-property.
- `buildJobbHref` emitterar aldrig `commit` (explicit testat, `search-params.test.ts:30–33`).
- `withCommitFlag` är ren sträng-suffix *ovanpå* en redan byggd href (rad 43–47) — rör inte state.
- `sameUrlState`/`serializeSearchText` orörda — `commit` når dem aldrig.
- `resultsKey`/chip-state (jobb-results.tsx): `commit` skickas till `getJobAds` men ingår inte i nyckeln. Korrekt.

CTO binding #1 + #8 uppfyllda. Inget läckage.

### 3. `onSubmitText` committar ALLTID — VERIFIERAD, ingen redundant-nav-bugg

`jobb-hero-search.tsx:299–314`. `onSubmitText` kör nu `commit(..., true)` direkt utan `sameUrlState`-gate (till skillnad från `runDelta` rad 229 som behåller gaten för live-delta). Avsiktligt per CTO VAL 3: "Sök" = "kör/spara den här sökningen", re-sök på samma filter ska bumpa recency. Eftersom `withCommitFlag` gör URL:en skild från nuvarande är det inte en no-op-navigering. Announce-strängen blir tom när inget ändrades, men `commit()` rad 220 guardar `if (announce)`. Efter strip återgår basen till `lastCommitted` → skip-guard → texten orörd. Korrekt, ingen dubbel-render-loop.

### 4. Strip-island `window.location` i `useEffect` — ACCEPTABELT, ingen loop-risk

`strip-commit-param.tsx`. Detta är INTE §5.2-DOM-manipulation (`document.getElementById`/manuell DOM-mutation) — det är läsning av `window.location.href` för att bygga en ren URL och sedan `router.replace` (React/Next-styrd navigering). Standard-Next-mönster för transient-param-städning. **Ingen infinite-loop:** effekten guardar på `active` (server-känt: `commit===true`) OCH `url.searchParams.has(COMMIT_PARAM)`; efter `router.replace` utan flaggan blir `commit===false` på nästa render → `active` false → effekten no-op:ar. `scroll: false` bevarar scroll-position. `[active, router]`-deps korrekta.

### 5. Popover-filter committar INTE — KONSISTENT OCH FÖRSVARBART

CTO:s enumererade commit-punkter (VAL 5 punkt 3) listar Enter/Sök, förslags-val, ×-clear, toolbar — popover är medvetet exkluderad. Detta är konsistent, inte en inkonsekvens vs toolbar:

- **Toolbar-handlingar** (`removeChip`/`clearAllFilters`/`onSortChange`) är diskreta, avslutade `router.push` — varje klick är en färdig avsikt.
- **Popover-interaktion** är pågående komposition (användaren bockar i flera kommuner innan stängning) — live-`replace`, samma klass som hero-fältets live-typing. Att committa per popover-bock vore exakt den mellanstegsspam E2j eliminerar.

Den naturliga commit-punkten för en popover-session är att användaren sedan trycker Sök/Enter (som committar) eller justerar via toolbar. Linjen "diskret avslutad handling = commit / pågående komposition = live" är konsekvent dragen. Försvarbart, inget omtag behövs.

### 6. DI / same-commit-disciplin — VERIFIERAD, inget broken intermediate state

Memory `feedback_di_with_handlers_same_commit`. Behavior-gaten (`RecentJobSearchCaptureBehavior.cs:42–49`), query-property (`ListJobAdsQuery.cs`), interface-medlemmen (`ICapturesRecentSearch.cs`) och endpoint-bindningen (`JobAdsEndpoints.cs:49`) ligger alla i samma staged batch. `Commit` är en record-property som matchar `ICapturesRecentSearch.Commit` automatiskt — ingen ny DI-registrering krävs (CTO binding #7). Pipeline-ordningen orörd. Inget mellanläge där FE:s `commit=1` honoreras utan BE-gate eller vice versa.

### 7. Test-adekvans (§7) — UPPFYLLD

| Beteende | Test | Fil |
|---|---|---|
| `commit=false ⇒ no-op` | `Handle_CommitFalse_DoesNotCapture` | behavior unit |
| `commit=true ⇒ capture` | `Handle_CommitTrue_CapturesSearch` | behavior unit |
| commit-guard + browse-guard additiva | `Handle_CommitTrueButAllDimensionsEmpty_DoesNotCapture` | behavior unit |
| interface-shape (`Commit`) | `interface-shape`-test rad 346–347 | behavior unit |
| live-sök utan commit fångas ej (E2E-väg) | `Live_search_without_commit_flag_does_not_capture` | integration |
| commit=1 på alla capture-vägar | befintliga uppdaterade (`&commit=1`) | integration |
| Enter/Sök bär commit=1 | 2 tester | hero FE |
| live-typing (mellanslag) bär INTE commit=1 | `live-typing committar UTAN commit-intent` | hero FE |
| förslags-val bär commit=1 | 3 tester | hero FE |
| ×-clear synlighet + semantik (ii) + commit-intent | 3 tester | hero FE |
| no-JS hidden commit=1 | no-JS-test rad 420–423 | hero FE |
| toolbar bär commit=1 (alla 5 vägar) | uppdaterade | toolbar FE |
| `withCommitFlag` + `buildJobbHref`-isolering | 3 tester | search-params |

Backend behavior-test före produktionskod (CTO binding #6 / §2.4 TDD), commit-guard testad i båda riktningar, ×-clear + strip + no-JS täckta. FE 837 grön, backend Application 750 grön, tsc/eslint clean. Test-pyramiden respekterad (unit-tyngd, integration för E2E-väg). Inga luckor.

---

## Konventioner & anti-patterns (§3, §5)

**Backend:** file-scoped namespace, nullable on, `bool Commit = false` default-property (paritet `Since`/`Page`), `CancellationToken` propagerad (oförändrad kedja), `ConfigureAwait(false)` i behaviorn. `commit` param-namnet är konstant på FE-sidan (`COMMIT_PARAM`); på BE binds det via minimal-API-parameternamn (`bool commit`) vilket är ramverks-konventionen för query-binding, inte en magic string i §5.1-mening (samma mönster som `q`/`since`/`page`). CTO binding #8 uppfylld i anda — param-namnet är deklarerat en gång per sida.

**Frontend:** `"use client"` på strip-island med motiverande kommentar (router-effekt kräver klient). Inga `any`, inga `as`-cast utan kommentar, ingen `useEffect`-datahämtning (effekten gör navigering, inte fetch), inget `console.log`. Server Component default bevarad (page.tsx). `withCommitFlag`/`COMMIT_PARAM` är SPOT — ett ställe definierar param-namnet, FE-sidan konsumerar det överallt.

**Inga §5-träffar:** ingen `DateTime.Now` (behaviorn rör inte tid), inga magic strings, ingen Repository, inga secrets, ingen sync-over-async, ingen `dynamic`, ingen tom catch (behaviorns try/catch är best-effort med dokumenterad swallow per ADR 0060 — oförändrad).

---

## Minor (FYI — ingen merge-spärr)

1. **Soft-hyphen i XML-doc-kommentar.** `ICapturesRecentSearch.cs` — ordet `EN­DAST` (rad i `<summary>`) och `jobb-hero-search.tsx`-kommentar innehåller ett soft-hyphen-tecken (U+00AD) mitt i "ENDAST". Renderar osynligt men skräpar i docs-generering/grep. Föreslås: byt till rent `ENDAST`. Trivial, kan göras direkt.

2. **`onSubmitText` duplicerar `runDelta`-kroppen.** `jobb-hero-search.tsx:304–314` upprepar `parseSearchText`→`applyClaimsDelta`→`setPrevClaims`/`setLimitNotice`-sekvensen som `runDelta` (rad 224–238) har, men med ovillkorlig `commit(..., true)` istället för `sameUrlState`-gate. Läsbart men en parametriserad `runDelta(text, null, { forceCommit: true })` vore DRY:are. Bedömning: nuvarande explicit form är acceptabelt tydlig (de två har olid commit-semantik) — refaktor är smaksak, inte krav. Lämnas till Klas/nextjs-ui-engineer om önskat.

3. **`withCommitFlag` antar att `?` saknas i path-fragment.** `search-params.ts:43–47` använder `href.includes("?")` för att välja `?`/`&`. Robust för alla `buildJobbHref`-utdata (de har aldrig `?` i path), men om en framtida anropare skickar en href med `?` i ett path-segment (osannolikt) skulle det fela. Inte ett problem i nuvarande anropsgraf (endast `buildJobbHref`-utdata matas in). Notering, ingen åtgärd.

---

## In-scope-fix-rekommendation (§9.6)

Minor 1 (soft-hyphen) bör städas in-block i samma batch — trivial, hör till nuvarande fas. Minor 2 och 3 är smak/defensivt och behöver inte åtgärdas; ingen TD motiverad (§9.6: default in-block, TD endast vid annan fas/saknad dependency — inget av detta gäller).

---

## Bra gjort

- **E2i:s ömtåligaste maskineri respekterat.** Skip-guard-grenen är korrekt inskjuten mellan own-roundtrip och extern-divergens utan att rubba ordningen; `lastCommitted`-jämförelsen är den rätta auktoritets-källan och resonemanget i kommentaren är vattentätt.
- **`commit` hållen strikt som signal, inte tillstånd** — Separation of Concerns (Martin 2017 kap. 7) levererad i praktiken: noll läckage in i state/serialize/href, verifierat med dedikerat isolerings-test.
- **Backend open/closed-konformt:** ett predikat till markör-kontraktet + ett villkor i no-op-kedjan, ingen ny abstraktion/port/Domain-påverkan. `SearchCriteria`-VO + Capturer-invarianten orörda — exakt CTO binding #3.
- **Commit-guard additiv till browse-guard, inte ersättande** — `Handle_CommitTrueButAllDimensionsEmpty_DoesNotCapture` bevisar att tom sökning aldrig fångas även med commit=1 (Mekanik-not 2 består).
- **Test-writer-FÖRST-disciplinen följd** (§2.4): behavior-testerna bevisar beteende-ändringen i båda riktningar; FE-testerna täcker commit/no-commit-asymmetrin, ×-semantik och no-JS.
- **Dokumentationstäthet hög och korrekt** — varje icke-trivial rad bär ADR-/CTO-referens; en framtida läsare felklassar inte commit-flaggan som ADR 0060-brott (amendmentet + kommentarerna stänger fällan).
- **No-JS progressive enhancement intakt:** statiskt `commit=1` korrekt eftersom native submit per definition är commit; hydration-vägen interceptar via `preventDefault` så den hidden-inputen aldrig dubbel-applicerar. M4-namnlöshets-kontraktet (spegel-input) orört.

---

## Sammanfattning

**0 Blockers, 0 Major, 3 Minor (FYI).**
Alla sju CTO/architect-bindningar verifierade on-disk. E2i-invarianterna (own-roundtrip-detektor, skip-guard-ordning, extern-divergens, `commit` utanför state) skyddade. Test-täckningen är komplett över commit-guard (true/false/browse-additiv), ×-clear (ii), strip, no-JS och `withCommitFlag`-isolering.

**Mergeklar.** Rekommendation: städa Minor 1 (soft-hyphen) in-block; Minor 2–3 valfria.

Notering till PR-flödet: security-auditor är obligatorisk per CTO VAL 7 (PII-insamlingsväg ändras) — utanför code-reviewers scope, men dess rapport ska bifogas PR-body innan merge enligt §9.2. code-reviewer flaggar endast att den invocationen är ett krav, inte ett val.
