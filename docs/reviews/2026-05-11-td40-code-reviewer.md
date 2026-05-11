# Code-review: TD-40 refine() leaf-path regression-test

**Status:** APPROVE
**Granskat:** 2026-05-11
**Auktoritet:** CLAUDE.md §2.4 (Testbart först), §3.x (TS strict), §4.1, §4.2
**Scope:** Frontend — testfil
`web/jobbpilot-web/src/lib/actions/resume-schemas.test.ts`
(ny `describe`-blok "resumeContentSchema – refine() leaf-path regression
(TD-40)", 3 it-block, ~110 rader)

---

## Sammanfattning

**Verdict: APPROVE.** Testen levererar exakt det de utger sig för. Ingen
Blocker, ingen Major. Två Minor + en Nit som inte hindrar merge — alla kan
adresseras i nästa naturliga touch (eller lämnas, motivering nedan).

---

## Granskning per fråga från Klas

### 1. Kontraktslås schemas ↔ pathToElementId — gör testet det det säger?

**Ja.** Testen verifierar tre saker som tillsammans utgör kontraktet:

- `experienceSchema.refine()` (rad 81–84 i `resume-schemas.ts`) raisar issue
  med `path: ["endDate"]` → Zod komponerar till array-leaf
  `["experiences", 0, "endDate"]` → `path.join(".")` ger
  `"experiences.0.endDate"` → `pathToElementId` matchar regex
  `/^experiences\.(\d+)\.(.+)$/` (rad 25 i `resume-path-routing.ts`) och
  returnerar `"exp-0-endDate"`.
- Samma kedja för `educationSchema` (rad 101–104) → `"edu-0-endDate"`.
- Test 3 låser **att index propageras** — `experiences.1.endDate` →
  `"exp-1-endDate"` bevisar att refinen inte hardcodar index 0 och att
  regex-grupperingen i routingen plockar rätt grupp.

Detta är **precis** den invariant som TD-40 ska bevaka: om någon tar bort
`path: ["endDate"]`-argumentet kommer Zod att emit:a issue på
`["experiences", 0]` (array-element-rot) — då matchar
`/^experiences\.(\d+)\.(.+)$/` inte (inget `.+` efter index), `pathToElementId`
returnerar `null`, focus-flytt skippas, `aria-invalid` flaggas inte. **Testen
fångar denna regression.**

Verifierad mot `ResumeContentForm`:
- `fieldA11y("experiences.<idx>.endDate")` (rad 334, 434) → producerar
  `id="exp-<idx>-endDate"` / `id="edu-<idx>-endDate"` — IDs som testen
  asserterar mot är de IDs som faktiskt renderas. Kontraktet är symmetriskt.

Kompletterar — duplicerar inte — `resume-path-routing.test.ts`. Den senare
testar `pathToElementId` med syntetiska strängar; TD-40 testar att Zod-output
för en *konkret refine-trigger* matchar formatet routingen förväntar sig. Två
olika invarianter, samma tema.

### 2. Test-namn + dokumentation — tydlig intention?

**Ja.** Kommentar på rad 276–281 förklarar varför testen finns (regression-
bevakning, inte fix) och beskriver hela skadekedjan: `path: ["endDate"]`
borttagen → fel hamnar på array-rot eller toppnivå → `pathToElementId`
returnerar null → ingen focus-flytt + missat `aria-invalid`. Future-self kan
återskapa hela tanken på 30 sekunder.

Describe-titeln innehåller TD-40-referens — bra för att hitta tillbaka när
TD-listan refereras.

It-titlar är beskrivande på svenska:
- "experiences refine pekar på leaf-path 'experiences.N.endDate' →
  pathToElementId mappar non-null"

Möjlig förbättring: `'experiences.N.endDate'` i namnet motsvarar
"`experiences.0.endDate`" i assert — N-notationen är inte exakt vad som
testas (det är alltid index 0 i test 1/2). Inte felaktigt — bara generaliserat
över de tre testen. **Nit, inte krav.**

### 3. Assertion-strategi — message-string match robust?

**Konditionellt OK.** Detta är den enda Minor som förtjänar diskussion.

Nuvarande mönster:
```typescript
const refineIssue = result.error.issues.find(
  (i) => i.message === "Slutdatum kan inte vara före startdatum."
);
```

**Risker:**

a) **Copy-ändring bryter testet utan att schemat eller routingen är trasig.**
   Om någon skriver om felmeddelandet till "Slutdatum får inte ligga före
   startdatum." (rimlig copy-fix per CLAUDE.md §10.3 civic-utility-ton) går
   testen rött trots att det invarianten bevakar fortfarande är intakt.

b) **Risk för dubblerat meddelande.** Om någon annan refine() introducerar
   samma exakta sträng (osannolikt men inte omöjligt) hittar `.find()` fel
   issue.

**Alternativ som vore mer robusta:**

- **Path-prefix-match** (rekommenderad):
  ```typescript
  const refineIssue = result.error.issues.find(
    (i) => i.path.join(".") === "experiences.0.endDate"
  );
  ```
  Detta testar **det testet faktiskt vill testa** — att path är leaf — utan
  att låsa copy-strängen som assert-nyckel. Sedan kan testet asserta
  `expect(refineIssue).toBeDefined()` + assert på message *separat* om
  meddelandet också är värt att låsa.

- **Issue-code (`i.code === "custom"`)** är vagt — alla refines emit:ar
  `"custom"`, så det skiljer inte två refines på samma aggregat.

**Verdict:** copy-baserad match är **inte fel** — det fungerar idag och alla
3 tester passerar. Men det binder två separata kontrakt (path + copy) till
samma assertion. **Rekommenderad åtgärd:** path-prefix-baserad search. Får
gärna lyftas in-block i nästa touch på filen, alternativt som TD om scope
inte tillåter just nu. Per CLAUDE.md §9.6 4h-regeln: detta är en 5-minuters
in-block-fix — bör fixas direkt om filen rörs igen.

**Klassning: Minor.** Testen blockerar inte merge — de bevakar invarianten
korrekt idag. Men robusthetsförbättringen är trivial att applicera.

### 4. Import-pattern `@/lib/forms/...` korsar lib-mappar

**OK pattern.** Verifierat mot 30+ existerande imports i `src/lib/**` —
`@/`-alias används rutinmässigt för cross-folder-imports inom samma `lib/`-
hierarki (t.ex. `@/lib/auth/session`, `@/lib/types/paged`, `@/lib/env`).
Test-filen följer befintligt mönster.

Att flytta testen vore fel — det är ett test om `resume-schemas.refine()`-
beteende som **bekräftar att outputen passar** `pathToElementId`. Att lägga
det under `lib/forms/` skulle missrepresent vad som testas (schemat). Att
lägga det under `lib/actions/` med cross-import är korrekt.

Sibling-testet `resume-path-routing.test.ts` (TD-46) testar routingen
isolerat med relativ import (`./resume-path-routing`). De två testen är
komplementära — separationen är medveten och bra.

### 5. CLAUDE.md-konformitet (TypeScript strict, ingen any, etc.)

**Pass.**
- Ingen `any`-typ använd (CLAUDE.md §4.1).
- Ingen `as`-cast utan kommentar.
- Non-null-assertion på `refineIssue!` (rad 311, 337, 371) — **legitim** för
  att hela testen redan asserterat `expect(refineIssue).toBeDefined()` på
  raden innan. Pattern är vanligt i Vitest-tester och alternativet
  (`if (!refineIssue) throw new Error(...)`) lägger till brus utan
  säkerhetsvärde här. **OK, ingen kommentar krävs.**
- Early-return `if (result.success) return;` (rad 304, 330, 364) är ett
  TypeScript-flow-narrow-trick — säkrare än `!`-cast på `result.error`.
  Snyggt.
- Inga `console.log`. Inga `useEffect`. Inga emoji/utropstecken i kommentarer.
- File-scoped imports korrekta, `describe`/`it`/`expect` från `vitest`.
- Filnamn `resume-schemas.test.ts` korrekt — co-lokaliserat med
  `resume-schemas.ts` (CLAUDE.md §4.2).
- Inget global state, ingen `localStorage`, inget DOM-anrop.

---

## Findings

### Blocker

Inga.

### Major

Inga.

### Minor

1. **Assert-nyckel: copy-sträng istället för path** (rad 306–308, 332–334,
   366–368) — binder två kontrakt (path + felmeddelande-copy) till samma
   `.find()`-nyckel. Copy-fix bryter testet utan att invarianten är
   regressed. **Rekommendation:** byt nyckel till `i.path.join(".") ===
   "experiences.0.endDate"` (resp. educations / index 1). Assert message
   separat om meddelandet också ska låsas. 5-min in-block-fix nästa touch.

### Nit

1. **It-titel "experiences.N.endDate"** generaliserar — i test 1/2 är N alltid
   0. Antingen exakt notation eller låt vara — saknar betydelse.

### Bra gjort

- **Kommentar-block på 7 rader** dokumenterar *varför* testen finns, vilken
  invariant den bevakar, och hela skadekedjan om invarianten bryts. Future-
  self / future-Claude kan rekonstruera intentionen utan att läsa TD-40.
- **Test 3 testar index-propagering** — utan detta skulle testen passera även
  om `pathToElementId` hardkodade index 0. Index-test är den specifika
  bevakning som fångar regex-grupperings-bugs i routingen.
- **Cross-modul-test-strategin** är genuint smart: en touch i `resume-schemas`
  utan motsvarande touch i `resume-path-routing` (eller vice versa) går rött
  → kontraktet kan inte tyst glida isär.
- **Komplement, inte duplikat** av `resume-path-routing.test.ts`. Routing-
  testet bevisar att `pathToElementId` hanterar förväntade format. TD-40-
  testet bevisar att Zod producerar de format routingen förväntar sig. Båda
  behövs.
- `if (result.success) return;` istället för `result.error!` — typsäker
  flow-narrowing.
- TD-40-referens i describe-titel — sökbar.

---

## Verdict

**APPROVE.**

Testen levererar TD-40:s syfte: kontraktslås mellan
`resume-schemas.refine()`-output och `pathToElementId`-input. 3/3 tester
passerar, 153/153 frontend-suite grön, ingen regression. Inga blockers,
ingen major, en Minor (assertion-strategi) som är trivial att förbättra i
nästa touch men inte hindrar nu.

Delegationer: ingen krävs.

Re-review: inte nödvändigt. Minor #1 kan följas upp i samband med att
filen rörs nästa gång — inget standalone-uppdrag.
