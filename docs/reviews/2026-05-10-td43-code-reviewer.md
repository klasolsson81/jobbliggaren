# Code-review: TD-43 — Komponent-test-baseline för forms

**Status:** Godkänd med mindre observationer (inga blockers, en större fundering)
**Granskat:** 2026-05-10
**Auktoritet:** CLAUDE.md §2.4 (testbarhet), §4 (TS/Next.js), §7 (testing-krav)
**Scope:** Frontend — tre nya Vitest + RTL-testfiler, setup.ts, package.json
**Spec:** `docs/tech-debt.md` TD-43 — rendering + happy submit + minst 1 felfall + a11y (aria-invalid + focus efter fail)

---

## Sammanfattning upfront

TD-43 är en **kvalitets-baseline med riktig substans** — inte vanity-tests.
Specen kräver fyra delar per form (rendering, happy submit, felfall, a11y),
och alla tre filer levererar alla fyra. 88/88 PASS, tsc clean, ingen `any`,
mocks är typade, ingen flakiness-yta. Detta är hantverk på rätt nivå.

**0 Blockers. 0 Större (med en seriös fundering om email-fältet, se nedan).
4 Mindre. 6 OK-noteringar.**

Mergeklar. Funderingen om email-fältet i `ResumeContentForm` är värd att
tänka igenom — men trade-offen är dokumenterad och försvarbar. Jag tar
inte skydd bakom "perfekt är fiendens till bra".

---

## Kritiska (block)

Inga.

---

## Större (bör övervägas, blockerar inte)

### S1. Email-fält i ResumeContentForm täcks inte av TD-15-testet — täcker det rätt risk?

**Fil:** `web/jobbpilot-web/src/components/resumes/resume-content-form.test.tsx:106–133`
**Bakgrund:** Du flyttade TD-15-testet från `email`-fältet till `fullName`-fältet
eftersom `<input type="email">` triggar browser-constraint-validation i jsdom som
blockerar submit. Du dokumenterade trade-offen i kommentar (rad 117–118).

**Min bedömning:** Trade-offen är **acceptabel som baseline**, inte optimal.

Här är resonemanget:

1. **Risken testet ska skydda mot är generisk** — `pathToElementId()` på
   `resume-content-form.tsx:142–157` är en switch-liknande mappning från
   schema-path till DOM-id. Den kan brytas på 11+ olika fältsorter
   (personalInfo.\*, experiences.N.\*, educations.N.\*, skills.N.\*, summary).
   Om mappningen är **rätt för fullName** är sannolikheten hög att resten
   också fungerar — det är samma kod-stig.

2. **Det testet inte fångar:** en regression där någon ändrar
   `pi-${path.slice("personalInfo.".length)}` till t.ex.
   `personalInfo-${...}` skulle bryta `pi-email` lika mycket som `pi-fullName`,
   och fullName-testet skulle fånga det. Så grundläggande mappnings-regression
   är skyddad.

3. **Det testet däremot inte fångar:** typ-specifik regression, som att
   email-inputen ändras till en custom-komponent som äter aria-invalid eller
   förlorar focus-handle. Det är teoretiskt — men inte abstrakt:
   du har redan en `Controller`-pattern på `skills.${index}.yearsExperience`
   (rad 510–526) som hanterar focus annorlunda. Om någon framtida fält flyttas
   till Controller-pattern skulle baseline-testet inte fånga det.

**Tre alternativ — välj efter smak:**

- **(a) Behåll som är.** Dokumentera explicit i TD-43:s tech-debt-rad eller
  i en kort kommentar i testfilen att TD-15-coverage är "happy path för
  fullName, och resten av fälten är inte enskilt täckta — schema-path-mappning
  delas". Detta är ärligt och minst kostsamt.

- **(b) Lägg till en parametriserad sub-test.** En `describe.each([...])` som
  loopar `["personalInfo.fullName", "experiences.0.company", "skills.0.name"]`
  och verifierar att schema-fel mappas till rätt DOM-id (utan att faktiskt
  submitta — bara unit-test:a `pathToElementId` direkt). Detta är **mer
  robust** och adresserar gren-coverage utan jsdom-strulet. Förmodligen 15–20
  rader till. **Min rekommendation om du orkar.**

- **(c) Workaround:** `formNoValidate`-attribut på submit-knappen i testet,
  eller mock:a `form.checkValidity` på element-prototype. Båda är fula och
  jag rekommenderar dem inte. jsdom-quirks är inte värda en hack-lösning
  i baseline-tests.

**Mitt råd:** alternativ (b) **eller** (a) med tydligare kommentar. Inte (c).
Detta är **inte en blocker** — TD-43:s spec säger "minst 1 felfall + a11y", och
ett felfall finns. Men du nämnde själv frågan, vilket säger mig att du anar att
det är värt att fundera på.

---

## Mindre (nice-to-fix, någon gång)

### M1. Mock-typning för Server Actions kan strykas hårdare i `me.ts`

**Fil:** `me-profile-form.test.tsx:9–11`

```ts
const updateMyProfileActionMock = vi.fn<
  (input: unknown) => Promise<ActionResult>
>();
```

Action har faktiskt typen `(input: UpdateMyProfileInput) => Promise<ActionResult>`
(`me.ts:22–24`). Att typa mock som `unknown` är **inte fel** — det är bara
löst. Skulle någon ändra signature skulle TS inte fånga det i testfilen.

**Föreslås:**
```ts
import type { UpdateMyProfileInput } from "@/lib/actions/me-schemas";
const updateMyProfileActionMock =
  vi.fn<(input: UpdateMyProfileInput) => Promise<ActionResult>>();
```

Och `vi.mock`-callback använder samma typ. Detsamma för `loginAction`
(redan korrekt typad — `prevState`+`FormData`) och `updateMasterContentAction`
(redan korrekt typad). Det är bara `me.ts`-mocken som glider till `unknown`.

**Kostnad:** 2 raders ändring. Värdet: TS fångar signature-drift i framtiden.

### M2. `ActionResult`-typ är duplicerad mellan testfiler

**Fil:** `me-profile-form.test.tsx:7`, `resume-content-form.test.tsx:7`

```ts
type ActionResult = { success: true } | { success: false; error: string };
```

Samma typ finns både i `lib/actions/me.ts:18–20` (exporteras) och
`lib/actions/resumes.ts:21` (exporteras också). Testfilerna re-deklarerar
istället för att importera. Inte fel, men: om `ActionResult` utökas i prod
(t.ex. ny `{success:false, error, code}`-variant) blir testfilerna falskt
gröna mot en lokal definition. Importera istället:

```ts
import type { ActionResult } from "@/lib/actions/me";
```

(samma för resumes-testet).

### M3. `pathToElementId(path: string)` är intern helper men inte exporterad — testbar i isolation?

**Inte ett test-fil-problem, en arkitekturobservation.**

`pathToElementId()` i `me-profile-form.tsx:30–43` och `resume-content-form.tsx:142–157`
är ren funktion utan komponent-koppling. Att testa den via fullständig RTL-render
+ submit-flöde är dyrt jämfört med en direkt unit-test. Om du går på alternativ
(b) under S1 → överväg att exportera `pathToElementId` (ev. via
`__internal`-namespace eller separat helper-fil) och testa den unit-style.

Inte heller blocker. Bara: när detta växer till 5+ forms är switch-statementet
en het regression-yta, och dedikerade unit-tests ger snabbare feedback.

### M4. `LoginForm`-testet täcker inte HTML5-required-vägen aktivt

**Fil:** `LoginForm.test.tsx:73–77`

```ts
it("requires email and password (HTML constraint)", () => {
  render(<LoginForm />);
  expect(screen.getByLabelText("E-postadress")).toBeRequired();
  expect(screen.getByLabelText("Lösenord")).toBeRequired();
});
```

Detta testar att **attributet finns**, inte att browsern faktiskt blockerar
submit. På backendside är det skillnad mellan "egenskap deklarerad" och
"effekt-test". Här är det milt — `required` är ett HTML-standardattribut
utan branchings — men det är ett strukturtest som heter "(HTML constraint)"
trots att det inte testar constraint:en.

**Föreslås:** byt namn till `it("declares email and password as required")` eller
ta bort testet. Det tillför nästan inget värde utöver vad TS+lint redan ger.

---

## OK-lista (det här gjordes rätt — bevara)

1. **Mock-strategi via `vi.mock("@/lib/auth/actions")` etc.** är **rätt skikt** —
   du mock:ar Server Action-modulen, inte `fetch`. Det betyder att tester
   inte är beroende av env-variabler, session-cookies eller backend-URL.
   Det betyder också att om någon ändrar transport (t.ex. fetch → tRPC) bryts
   tester inte falskt. Bra avgränsning.

2. **`useActionState` + `formAction`-flöde i LoginForm-testet** — korrekt
   förstått. Du injicerar `loginAction` via mock och låter React's
   action-pipeline driva anropet. Inte enkelt att få rätt; här fungerar det.

3. **`waitFor` + `findBy*`** är konsekvent använda för asynkrona
   assertions (`waitFor(() => expect(mock).toHaveBeenCalledTimes(1))` och
   `await screen.findByRole("alert")`). Inga `setTimeout`-hacks, ingen
   `act`-spridning. Stabilt.

4. **`beforeEach` med `mockReset` + `mockResolvedValue` default** — korrekt
   pattern för isolerade tester. Ingen läcka mellan it-block.

5. **`removeAttribute("required")` för att testa schema-level validation
   istället för HTML-required** — bra teknik, dokumenterad i kommentarer
   (rad 90–91 i me-profile-form.test, 117–118 i resume-content-form.test).
   Den här typen av kommentar är skillnaden mellan "tester som dokumenterar
   intent" och "tester som någon stirrar på i 20 minuter om 6 månader".

6. **Mockning av `next/navigation` i LoginForm-test** — nödvändig (jsdom
   saknar Next-router), korrekt scope:ad till bara `useSearchParams`.

7. **Setup-filen är minimal** — bara `@testing-library/jest-dom/vitest`-import.
   Inget globalt mockande, inget magiskt. Lätt att läsa, lätt att utöka.

8. **TS-strictness:** ingen `any`, type-imports markerade med `import type`,
   `vi.fn<...>()` typad. CLAUDE.md §4.1 efterlevd.

---

## Specifik svar på dina frågor

### "Mock-strategi (vi.mock med actions-modulen) — risk för 'passar tester men brister i prod'?"

Risken finns men är **låg och hanterbar**:

- **Vad mocken inte fångar:** ändringar i Server Action-implementation
  (t.ex. att `updateMyProfileAction` slutar anropa `revalidatePath` korrekt,
  eller att `loginAction` glömmer `safeRedirectPath`). Det är inte vad ett
  komponent-test ska fånga ändå.
- **Vad mocken fångar:** att komponenten **anropar action med rätt argument**,
  hanterar `{success: true|false, error}`-shapes korrekt, och visar UI-state
  (Sparat/role=alert/aria-invalid) baserat på respons.
- **Vad ska fånga action-internt-regression:** integration-tests på actions
  själv (separat fas, mot mock-fetch eller via MSW). Eller E2E. Inte detta
  lager.

Mock-skiktet är **rätt valt** för komponent-tests. Att strikt typa mocken
(M1) skulle stänga den lilla glipan som finns.

### "Saknas något viktigt test-case?"

För baseline — **nej**. Spec är uppfylld. Men om du tar TD-43 vidare:

- **MeProfileForm:** disable-state under `isPending` (knapp + alla inputs har
  `disabled={isPending}`). Inte testat. Risk: regression där disable-flödet
  går sönder.
- **ResumeContentForm:** `useFieldArray` add/remove för educations + skills
  (du testar bara experiences). Mest copy-paste-coverage; lågprioriterat
  för baseline men hör hemma långsiktigt.
- **ResumeContentForm:** "Ta bort"-knappen i en fieldset. Aria-label är
  parametriserat (`Ta bort erfarenhet ${index + 1}`) — om det bryts läcker
  det till screen-readers utan att synas visuellt. Bra a11y-test-kandidat.

Inget av detta blockerar TD-43-baseline. De är **kandidater för iteration 2**.

### "Test-stabilitet — flakiness-risk?"

Jag ser **ingen** flakiness-yta:

- `waitFor` används för async assertions
- `findBy*` används för element som visas async
- Inga timeouts, inga `setTimeout`, inga `act`-rop
- `beforeEach` resettar mocks → ingen state-läcka
- Inga datum/tid-beroenden (förutom `toLocaleTimeString` i `MeProfileForm`,
  och du testar `findByRole("status")` med regex `/Sparat/` — inte exakt
  tidssträng. Smart.)

Stabilt.

### "TS-strictness — `any` eller fel mock-typning?"

- Ingen `any` upptäckt.
- Mock-typning: tre av fyra mocks är korrekt typade. `updateMyProfileActionMock`
  glider till `unknown` (M1). Mindre.
- `vi.fn<(...)>()`-syntax används konsekvent. Bra.
- `import type` används där det ska. Bra.

### "Email-fält-trade-offen i ResumeContentForm — acceptabelt?"

Se S1. **Acceptabelt som baseline. Bättre att förstärka via parametriserad
unit-test av `pathToElementId` (alternativ b) vid nästa iteration.**

---

## Delegationer

Inga obligatoriska. Om du väljer att adressera S1+M1+M2+M4 nu kan du göra det
direkt — det är trivial omarbetning av existerande testfiler. Om du parkerar
det: lägg en rad i `docs/tech-debt.md` under TD-43-noteringen att "iteration 2
adresserar Controller-fält-coverage + ActionResult-import-konsolidering".

---

## Slutsats

Mergeklar. Det här är hederligt hantverk. Spec är uppfylld, mocks är på
rätt skikt, tester är stabila, TS är strict. Trade-offen för email-fältet
är dokumenterad — och det är **just** den dokumentationen som skiljer
"baseline med insikt" från "baseline med blint öga". Du noterade själv
spänningen, vilket är rätt instinkt.

Ta detta till commit, använd `LoginForm.test.tsx` som mall för framtida
forms, och kom tillbaka till S1+M1+M2 vid nästa form (eller vid en lugnare
session). Inga blockers.
