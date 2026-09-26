# 2026-09-13 — #1717 / PR #1722 — dotnet-architect

> ⚠ **Transkriberad av sessionen ur agentens svar.** Innehållet är hans. Kapad till hans charters
> Output format (§9.2): max 25 rader per fynd, inget metodavsnitt — rapporten är fyndlistan.

## Arkitektur-analys

### Scope — uttrycklig avgränsning

Deltat är **rent frontend**: 13 filer, alla under `web/jobbliggaren-web/`. Verifierat:
`git diff f987c62d..44806e0d --name-only | grep -v '^web/jobbliggaren-web/'` → tom. Ingen C#, inget
under `src/`, ingen `.csproj`, ingen `Directory.Packages.props`, inga nya paket.

**Min charters kärnområden har noll yta här** — Clean Architecture-lagergränser, DDD, CLAUDE.md §5
backend-listan, Mediator.SourceGenerator-pipelinen och EF Core 10. Inget av det rörs. Jag är inkallad
på ">5 filer"-raden och svarar på de tre frågor som ligger inom räckhåll för allmän kontraktsdesign;
resten är `code-reviewer`s och `design-reviewer`s bord och jag graderar inte i deras skalor.

Kontrollerat och **klarar sig**: `style={{ margin: 0 }}` är inte ett uppfunnet mönster —
`notice-list.tsx:76` och `notice-section.tsx:214` i samma mapp bär samma form, och 37 filer i `src/`
gör det. Inget fynd.

### Fynd

**[Nice-to-have]** `src/components/oversikt/application-summary.tsx:55`

**Vad:** `ApplicationSummary.heading` är en obligatorisk prop vars **enda producerbara värde är
`null`** — båda anropsställena skickar `null`, alltid. Grenen `headingNode !== null` är därmed onåbar
från produktion, och den kan inte heller pinnas: en fixtur som skickar en rubrik dit hade varit ett
produktionsfaktum påstått ur en premiss produktionen inte kan framställa. Sviten **vet** det —
`application-summary.test.tsx` renderar aldrig den grenen, med kommentar om varför. En gren som varken
produktionen kan nå eller sviten får mäta bärs av ingenting.

**Varför:** CLAUDE.md §9.1 — *"check existing patterns (reuse, don't invent)"*. Grannskapet har redan
avgjort exakt den här formen, åt andra hållet: `CriteriaSummary` fick sin `linkHref` **borttagen** och
ersatt av modulkonstanten `CATALOGUE_HREF`, med motiveringen ordagrant i `criteria-summary.test.tsx:88`
— *"Not a prop any more — the component owns it, because every consumer is an authenticated surface and
**there was never a second value**."* Regeln repot skrev åt sig självt är alltså *ett producerbart
värde → inte en prop*. Meddelandekatalogen säger samma sak: deltat lägger till `companySummary.heading`
och `criteriaSummary.heading`, men **ingen** `summary.heading`.

**Föreslagen åtgärd:** ta bort `heading` från `ApplicationSummaryProps` och de två `heading={null}` vid
anropsställena; docblockets motivering flyttar till komponentens egen docblock, där den är ett påstående
om komponenten i stället för om en parameter.

**Avgränsning:** gäller **bara** `ApplicationSummary`. `CompanySummary.heading` producerar båda värdena
och där är obligatorisk-utan-default rätt av precis det skäl docblocket anger. `CriteriaSummary.heading`
har ett värde men försvaras av SSOT-ledningen; bara dess `heading === null`-gren är död, och den kostar
tre rader.

### Rekommendation

**1. Är en obligatorisk prop vars enda producerade värde är `null` rätt kontrakt?** Nej för
`ApplicationSummary`. Jämförelsen med `linkHref` håller **inte** symmetriskt: `linkHref` har ett äkta
värderum per komponent och propen är obligatorisk för att en default hade varit *fel* på en yta.
`heading` har ett äkta värderum på **en** av tre komponenter. Argumentet "syskonen delar en grammatik"
är ett argument om läsbarhet, inte om kontrakt: ett kontrakt formas av sitt värderum, inte av
syskonsymmetri.

**2. Duplicering mot abstraktion — fyrar Rule of Three?** Nej, och §3.6 når inte hit. §3.6:s kalibrering
triggar på **identitet** hos det som återanvänds, inte på likhet hos formen. De tre paren är samma
*form* över tre olika strängkällor, och två av tre är strukturellt degenererade. Att extrahera en
`SummaryBlock` hade abstraherat över N=1 levande konsument och flyttat **rotelementets tagg** in i en
delad modul — och rottaggen är bärande för `.jp-appsummary:has(+ .jp-appsummary)`. Den kopplingsytan är
större än dupliceringen den tar bort. Andra ledet: den tvågrenade formen finns **bara** för att hålla
rubriklösa anropsställen byte-identiska, vilket är `design-reviewer` B4 — den har därför inget
självständigt svar i min skala och ska inte graderas av mig alls.

**3. SSOT — rätt riktning att läsa nyckeln vid anropsstället?** Ja. Skiljelinjen är **vem som äger
värdet**, inte vem som äger strängen. `t("unavailable")` och ankarets pluralform är egenskaper hos
komponentens rendering — samma på varje yta. Rubriken är en egenskap hos **anropsställets layout**:
hela poängen är att den varierar per yta. Läste komponenten `t("companySummary.heading")` själv skulle
gästytans *ingen rubrik* ändå behöva en andra prop, och ett beslut vore utspritt på två propar. Samma
distinktion som `linkHref` (varierar → prop) mot `CATALOGUE_HREF` (varierade aldrig → komponenten äger).

### Sammanfattning

**0 kritiska, 0 viktiga, 1 nice-to-have.** Deltat bryter ingen lagergräns och rör inget i min charters
tabeller. Den renderade formen är `design-reviewer`s bindning och ifrågasätts inte.

---

## SKOPAD OMKONTROLL (rapport-läge) — fix-deltat `44806e0d..d7a40753`

**Delta:** 10 filer, alla under `web/jobbliggaren-web/`. Min charters kärnområden har fortsatt **noll
yta**.

### Sammanfattning

**Mitt Nice-to-have är STÄNGT.** Den stängande mätningen är mekanisk och jag körde om den själv i
stället för att lita på rapporten. **Inga nya fynd i min skala — 0 kritiska, 0 viktiga, 0 nice-to-have.**

### Stängning — egen mätning

```
git diff f987c62d d7a40753 -- .../oversikt/application-summary.tsx   →  0 rader
grep -n "heading" .../oversikt/application-summary.tsx                →  0 träffar
```

Komponenten är byte-identisk med basen. Det är starkare än vad jag föreslog: jag föreslog att propen
skulle tas bort, och resultatet är att hela filen är återställd till bas. Det stänger både propen och
den onåbara grenen, och det gör stängningen självmätande — nästa läsare behöver inte tro på en mening,
bara köra diffen.

Testfilen är `+36/-0` mot basen, och de två överlevande pinnarna är legitima: de mäter **basens** form,
inte den borttagna propen. Ingen fixtur framkallar längre ett värde produktionen inte producerar.

### Min fence — hur den överlevde

`CompanySummary` behåller propen: två anropsställen, båda värdena producerbara. Min fence, orörd.

`CriteriaSummary` behåller propen på `design-reviewer`s grund (ett blocknamn är ett
kompositionspåstående, en href är det inte) och inte på värderumsgrunden. Den diskrimineringen är
**hennes att göra och den är koherent** — `CATALOGUE_HREF`-precedensen jag anförde talar om *värderum*,
hon skiljer på *vad som namnges*. En annan axel, inte en överprövning; min precedens är oskadd. Jag hade
ingen öppen fordran där: mitt fynd var uttryckligen skopat till `ApplicationSummary`.

En sak i fix-deltat stänger något jag **hade** varit tvungen att ta upp om det stått kvar: docblocket i
`criteria-summary.tsx:39-45` påstod tidigare ett skäl lånat från `CompanySummary` som inte gäller en
komponent med ett anropsställe som alltid skickar rubriken. Nu omskrivet till att skälet uttryckligen
**inte** överförs. Falskt påstående borttaget genom omskrivning, inte genom tillagd prosa.

### Kontrollerat i fix-deltat, klarar sig

- `style={{ margin: 0 }}` borttagna utan ersättningsregel — `code-reviewer`s no-op-mätning håller.
- CSS-hunken flyttad ur radklustret till blockklustret — ren flytt, texten byte-identisk.
- Namnskuggningen `heading`/`rowName` upplöst, med en kommentar som skiljer blocknamn från radnamn.
- Anropsställena skickar inte längre någon rubrikprop till `ApplicationSummary`.

### Inte ett fynd, men värt en rad

`application-summary.test.tsx:143` bär en whitespace-only rad där `heading={null}` togs bort.
Kosmetiskt. §9.6 undantar uttryckligen phrasing-fynd från en skopad omkontroll, så jag graderar det
inte och det stänger ingenting.
