# dotnet-architect — PR #1498 (`fix/cv-slash-date-1195`, closes #1195)

> **Report-only** (CLAUDE.md §6/§9.6). No production code or test was edited.
> **Worktree** `C:/DOTNET-UTB/JobbPilot/.claude/worktrees/fix+cv-slash-date-1195` ·
> **head** `280b04ea` · **range** `git diff origin/main...HEAD` (three dots) ·
> **Datum:** 2026-08-24 · **Skala:** Kritiskt / Viktigt / Nice-to-have (§9.6 — severity
> belongs to the reporting agent; nothing here is graded against another agent's table).

Läst i sin helhet: `gh pr view 1498`, ADR 0136, `docs/reviews/2026-08-24-1195-cto-bind.md`,
`DatePatterns.cs` (HEAD + `origin/main`), `CvMonthNames.cs`,
`HeadingDrivenResumeSegmenter.ExtractPeriod`, `PeriodParser.cs`s ändrade block,
`ReviewText.cs`s ändrade block, `DateRangeYearFirstCharacterisationTests` i sin helhet.

---

## Arkitektur-analys

### Sammanfattning

Behöver åtgärdas — **0 kritiska, 1 viktigt, 1 nice-to-have.** SRP-uppdelningen är genuin
och inte en fork; ISP-sömmen och vetots placering håller; `row.language ⊇ range.language`
är härledd **och mätt i .NET-motorn** (114 400 rader, 0 brott); prefix-ordningen håller i
alla fyra listorna; VÄRDE-grammatiken är **byte-identisk** med `origin/main`. Det viktiga
fyndet är att synkmekanismen bara når **punktlistorna** — de två regexens *skelett* är
duplicerat literalt och pinnas av ingenting.

---

### Bekräftat (mätt, inte antaget)

**§2.1 — inget lager rörde sig.** `git diff origin/main...HEAD --name-only`: fyra filer
under `src/Jobbliggaren.Infrastructure/Resumes/`, sex testfiler, tre docs. **Noll**
`*.csproj`/`*.props`/`*.sln`/`Directory.Packages.props`, **noll** filer under
`src/Jobbliggaren.Domain/` eller `src/Jobbliggaren.Application/`. Ingen EF Core-, provider-,
relational- eller ASP.NET-referens tillkommer. `ParsedExperience` bor i Domain
(`src/Jobbliggaren.Domain/Resumes/Parsing/ParsedExperience.cs`) och är orörd.

**VÄRDE-grammatiken är byte-identisk med `origin/main`.** Fragment-refaktoreringen
(`PointOpen`/`LooseHyphenPoint`/`ExactHyphenPoint`/`SharedPointTailHead`/`Foot`) är
värdebevarande — jag löste upp konstanterna i båda revisionerna och jämförde:

```
StartPoint IDENTICAL  (?:<MONTHS><AFTER>\d{4}|\d{4}-\d{2}|\d{2}/\d{4}|\d{4})
EndPoint   IDENTICAL  (?:<MONTHS><AFTER>\d{4}|\d{4}-(?:0[1-9]|1[0-2])|\d{2}/\d{4}|\d{4})
```

Frågan *"kan .NET:s ordnade alternation ge en KORTARE totalmatchning än förut i
VÄRDE-grammatiken"* besvarar sig därmed själv: mönstersträngen ändrades inte alls, så
ingen matchning kan flytta. Det är ett starkare svar än en grenordningsanalys.

**Prefix-ordningen håller i alla fyra listorna.** Uttömmande över alternativens språk (par
för par, `i` före `j`, proper prefix): 0 inversioner i `StartPoint`, `EndPoint`,
`LineStartPoint`, `LineEndPoint`. **Positiv kontroll:** samma instrument med bare `\d{4}`
placerad FÖRE `\d{4}/\d{2}` upptäcker inversionen (`1029/02`, `2111/09`, `0210/10`), så en
nolla här är en mätning och inte ett blint instrument. Filens egen läsning — *"order only
bites where a short branch can SUCCEED"* — stämmer mot den nya listan: `SlashPoint` är
längre än den bare `\d{4}` den föregår, och där den lyckas men helheten faller backtrackar
motorn ner i `\d{4}` precis som förut.

**`row.language ⊇ range.language` — HÅLLER.** Härledning: en alternation-union är additiv,
ingen gren tas bort, och en gren som lyckas men får helheten att falla backtrackas ur. Alltså
`DateRange().IsMatch(s) ⟹ DateRowRange().IsMatch(s)`, med matchning på samma eller tidigare
index. Mätt i **.NET:s egen motor** (`dotnet fsi`, samma mönsterkonstruktion, 114 400
konstruerade rader över {22 punktformer} × {5 separatorer} × {26 slutformer} × {5 prefix} ×
{8 svansar}): **0** rader där VÄRDE matchar och RAD inte gör det, **0** där RAD-matchningen
börjar senare, **0** där den är kortare vid samma index; 33 600 rader är den avsedda
utvidgningen. Ej en `dotnet test`-körning — sviten är PR-kroppens instrument — utan en
direkt mätning av just den egenskap designen vilar på.

*Notera en precisering av frågan:* vetots tredje konjunkt **är** `!DateRange().IsMatch(line)`,
så vetot kan per konstruktion aldrig fyra på en rad VÄRDE-grammatiken läser — oavsett
supersets. Det superset-egenskapen skyddar är den andra riktningen: att LINJE-undertryckningens
täckning aldrig krymper, dvs. att β-3 förblir stängd.

**SRP-uppdelningen är genuin.** Fyra oberoende belägg, starkast först:
1. Efter uppdelningen har `DateRange()` **en** konsument utanför typen —
   `HeadingDrivenResumeSegmenter.DateRangeRegex()` → `ExtractPeriod` — och det är den som
   lagrar. (Plus en användning inne i typen: vetots tredje konjunkt, som ställer en
   VÄRDE-fråga och därför korrekt läser VÄRDE-grammatiken.) En grammatik med en konsument och
   en nedströmsförpliktelse är en modul med ett change-reason.
2. De två har olika **nedströmskontrakt**: `DateRange`s matchvärde måste överleva
   `PeriodParser` (hela skälet till att `CvMonthNames` existerar); `DateRowRange`s matchvärde
   konsumeras av ingen. Det är en skillnad i förpliktelse, inte i bekvämlighet.
3. Historiken avgör: rond 5:s Blocker var en ändring som var **korrekt på den ena axeln och
   en defekt på den andra**. Två axlar som mätbart rört sig åt olika håll är två
   change-reasons.
4. Deltat uttrycks som **komposition över delade fragment**, inte som en kopia — den enda
   literal som förekommer en gång är `SlashPoint`. Det är skillnaden mellan en SRP-uppdelning
   och en fork.

**CTO:ns DRY-läsning är sund.** *"Hur ser ett CV-datum ut"* är inte ett kunskapsstycke här
utan två — *hur ser en datumRAD ut* och *hur ser ett datumVÄRDE vi får lagra ut* — och filen
hade redan delat dem två gånger före denna PR: per POSITION (lös vs exakt månadsklass) och
per MEKANISM (`IsIgnorableTail` bär kvalificeraren och det nyckelordslösa slutet **utanför**
`DateRange`, uttryckligen för att matchvärdet rider in i det promotade CV:t). Den tredje
instansen namnger sig själv; att avvisa den vore att avvisa filens levererade design.

**ISP-sömmen är rätt.** `DateRowRange()` är `private`; konsumentkartan över `src/` visar att
ingen konsument når grammatiken. Segmenteraren behöver tre saker och får alla tre
(`StripTrailingDate`, `IsDateOnlyLine`, `IsUnreadableDateRow`); review-motorn behöver två och
får båda (`IsDateOnlyLine`, `StripDates`). Ingenting den behöver är nu ofrågbart.

**Vetots placering håller, och ingen enklare regel fanns att välja.** `ExtractPeriod` är enda
producenten av `ParsedExperience.Period` (två anropsställen, experience + education). Regeln
är ett påstående om **den här parserns notationskompetens**, inte en domäninvariant — Domain
kan inte bära den utan att lära sig om regexgrammatiker, så Infrastructure är rätt hem under
§2.1. Den uppenbara starkare regeln — *"lagra bara ett `Period` som `PeriodParser` kan läsa"*
— är **inte tillgänglig**: den skulle radera det medvetet källtrogna men oläsbara
bindestrecks-läsåret (`2019-20 – 2021` lagras och parsas inte, pinnat). Notation-skopat veto
är alltså rätt skop, inte en svagare version av en renare regel. Placeringen **före** båda
fallbackarna är nödvändig — det är fallbackarna som producerar de två fel svaren — och
`entry.Lines` är rätt skop för ett radpredikat.

**R3 (`StripDates` → RAD-grammatiken) är arkitektoniskt rätt** — maskning har inget lagrat
värde och hör till LINJE/MASK-halvan av partitionen. Kostnadsriktningen är mätt och bunden:
maskningen växer strikt, så en prosapunkt vars enda siffror är ett
`\d{4}/\d{2} – \d{4}/\d{2}`-par läses nu som okvantifierad. `Year()` maskerade redan
fyrsiffringarna före denna PR, så deltat är de två efterföljande tvåsiffringarna; den
levererade negativa kontrollen täcker den realistiska formen. **Inget fynd.**

---

### Fynd

**[Viktigt]** `src/Jobbliggaren.Infrastructure/Resumes/Parsing/DatePatterns.cs:115–118` och `:190–193`
**Vad:** De två grammatikerna delar sina punktlistor **per konstruktion** men duplicerar sitt
**range-skelett literalt**. Rad 116 och rad 191 är byte-identiska så när som på de två
listidentifierarna, och `RegexOptions.CultureInvariant | RegexOptions.IgnoreCase` står två
gånger. Kontraktstestet
`TheFourPointLists_AreATwoByTwoOverTwoOneTokenDeltas` läser bara de fyra
`*PointForTests`-konstanterna — det ser inte skelettet alls, i någon riktning.
**Varför:** Designens lastbärande egenskap är `L(DateRowRange) ⊇ L(DateRange)`, och den håller
i dag bara därför att **båda** halvorna av mönstret sammanfaller: punktlistorna (pinnade) och
skelettet (opinnat). En utvidgning av `DateRange`s skelett ensamt bryter supersetet **tyst** —
separatorklassen är den levande kandidaten, eftersom filens eget docblock redan noterar att
`PeriodParser` är bredare på `"till"`/`"to"`. Följden är inte ett vetofel utan att β-3 öppnas
igen: en datumrad VÄRDE-grammatiken läser slutar vara date-only, `SplitTitleOrganization`
lämnar den till Organization-slotten och `ReviewText.DescriptionLines` släpper in den i
bullet-scoringen. 2×2-testet står grönt genom alltihop. Detta är bindens **egen R1-regel**
(*"noll duplicerad literal … en ny punktform läggs till på ETT ställe och landar i alla fyra"*,
OCP) tillämpad på det fragment R1:s uppräkning inte namngav — kör regeln, inte uppräkningen.
Det är också varför ADR 0136:s *"Built from shared fragments with no duplicated literal …
unsynced divergence … is closed"* är sann om **listorna** och för stark om **grammatikerna**.
**Föreslagen åtgärd:** avsluta extraktionen R1 påbörjade. Skelettet är ett kunskapsstycke som
båda genuint delar (*hur ser en datum-RANGE ut strukturellt*), till skillnad från punktlistorna
som legitimt skiljer sig:

    private const string RangeOpen = @"\b(";
    private const string RangeMiddle = @")\s*[-–—]\s*(";
    private const string RangeClose = "|" + CvMonthNames.PresentKeywords + @")\b";

    [GeneratedRegex(RangeOpen + StartPoint + RangeMiddle + EndPoint + RangeClose,
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    public static partial Regex DateRange();

    [GeneratedRegex(RangeOpen + LineStartPoint + RangeMiddle + LineEndPoint + RangeClose,
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex DateRowRange();

Efter det kan **bara** de två punktlistorna skilja grammatikerna åt, och dem pinnar 2×2:n redan
— synkpåståendet blir sant som skrivet och ingen ny assertion är skyldig. (Options-paret kan
gå samma väg via `private const RegexOptions RangeOptions = …`; det är valfritt och en mycket
mindre yta än mönstret.) Resultatet är byte-identiskt med dagens mönsterströngar, vilket
sessionen kan mäta på samma sätt som ovan. **Vill man i stället bevara möjligheten till
divergerande skelett** är alternativet en property-pin genom den publika sömmen — *varje rad
`DateRange` matchar till radslut är `IsDateOnlyLine`* — men den upptäcker felet i stället för
att ta bort det, och är därför andrahandsvalet.
**Omkontroll:** fixen lägger till rader (stängs inte mekaniskt), så den går tillbaka till mig,
rapport-läge, skopad till fix-deltat (§9.6). Den är billig: lös upp de två mönsterströngarna
och visa byte-identitet mot förfix-värdena.

**[Nice-to-have]** `src/Jobbliggaren.Infrastructure/Resumes/Parsing/DatePatterns.cs:118`
**Vad:** `DateRange()` är fortfarande `public` och returnerar ett rått `Regex`, medan
RAD-grammatiken är `private` bakom namngivna frågor.
**Varför:** ISP-argumentet PR:en gör för `DateRowRange` (*"the segmenter needs the question,
not the spelling"*) gäller ordagrant för VÄRDE-grammatiken, som nu har **en** extern konsument.
Att lämna den publik håller sömmen öppen för exakt det misstag PR:en reparerar: en framtida
LINJE-konsument som griper efter det råa värde-regexet, som `StripDates` gjorde.
**Föreslagen åtgärd:** **inte** in-block. `private` + `TryMatchStorablePeriod(string text, out
string period)` flyttar `ExtractPeriod`s form och kräver egna pinnar — ett eget change-reason.
Disposition enligt §9.6: **namngiven skip i PR-kroppen, inte en issue** — fyndet ligger i en
Infrastructure-fil ingen annan lane rör, så ingen peer-lane behöver se det, och sessionens
netto-tak rörs inte.

---

### Referenser

- CLAUDE.md §9.6 (severity tillhör rapportören; Blocker/Major in-block eller följd-PR; namngiven
  skip för Minor; omkontroll rapport-läge till samma agent) · §6 (`agents-done`, innehållspush
  river grinden) · §5 `Comments:` (en faktiskt felaktig kommentar är en defekt) · §13.
- AGENTS.md §2.1 (Clean Architecture, EF Core-beroenderegeln) · §2.2 (DDD) · §5 (anti-patterns)
  · §8 punkt 9 (ADR för arkitekturbeslut).
- ADR 0136 — `docs/decisions/0136-the-year-first-slash-date-is-a-row-recognised-and-dated-by-nobody.md`
  (Decision 1–5, Consequences → Negative, Implementation status → Drift control).
- `docs/reviews/2026-08-24-1195-cto-bind.md` — R1 (noll duplicerad literal, OCP-utvidgningspunkt),
  R2 (radfråga + `private` grammatik, ISP), R3 (`StripDates` → RAD-grammatiken).
- ADR 0071 — determinism / honest-absent, principen vetot tillämpar.
- Egna mätningar 2026-08-24 (worktree `fix+cv-slash-date-1195`, head `280b04ea`):
  konstantupplösning `origin/main` vs `HEAD`; prefix-ordning över alla fyra listor med positiv
  kontroll; superset-egenskapen i .NET-motorn över 114 400 konstruerade rader.
