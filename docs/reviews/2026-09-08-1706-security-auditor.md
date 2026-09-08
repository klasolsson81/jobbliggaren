# 2026-09-08 — #1706 (breddgrindens VÄRDE) + namnbytet — security-auditor

> ⚠ **Transkriberad av sessionen ur agentens svar** (charter: hon rapporterar, reparerar aldrig
> repot). Innehållet är hennes.

**Status (första ronden):** ⛔ BLOCKED — **0 Blocker, 2 Major, 1 Minor.**
**Auktoritet:** GDPR Art. 5(1)(c), 5(1)(d), 5(1)(e), 17, 24(1), 30(1)(c) · ADR 0139 · ADR 0125 ·
ADR 0120 · AGENTS.md §5 `Comments:`/`Security:` · CLAUDE.md §9.6
**Mätt 2026-09-08 i `c:/tmp/jbl-1706` @ `f05944ba`.**

**Jag väljer inget tal, och jag lämnar inget indirekt tal genom att avvisa alla utom ett.**

## Svar på de sex frågorna

### 1. Begränsar Art. 5(1)(c) VÄRDET? — Sessionens läsning BEKRÄFTAS, med en skärpning

Testet är **syftesrelativt** — inte "så litet som möjligt", och inte enbart "bundet". Tre led:

1. **Predikat-härledning.** Varje lagrad rad är ett bolag som faktiskt uppfyller hennes eget predikat.
   **Detta håller vid varje bound**, och det är därför obundenhet faller *per se*.
2. **Ändamålsuppfyllelse.** Bounden får inte sättas där den lagrade mängden slutar tjäna sitt syfte.
   **En trunkerad mängd faller här.** Att vägran är **strukturell** (`LIMIT maxMembers + 1`) är därför
   inte en produktdetalj utan den konstruktion som gör att minimeringen håller *oavsett* N.
   ⚠ **Skulle någon föreslå partiell materialisering med ett mättat tal ändras mitt svar.**
3. **Ingen ändamålslös rest.** FK `ON DELETE CASCADE`, ovillkorlig DELETE på varje väg, kontokaskadens
   beteendeorakel.

**Slutsats.** Art. 5(1)(c) begränsar värdet **endast genom led 2**, och led 2 är redan strukturellt
uppfyllt vid varje bound. **Bestämmelsen skiljer alltså inte 1 000 från 2 500.**

⛔ **Därmed: Art. 5(1)(c) är INTE min grund för att vägra ett högre tak, och jag erbjuder den inte som
en sådan.** De verkliga begränsningarna är läskostnaden (CTO:ns) och rate-limit-hinken (min, fråga 4).
Att låta en GDPR-artikel bära ett tal den inte bär är hur en veto-rätt förlorar sin trovärdighet.

### 2. Finns ett värde där minimeringsargumentet slutar hålla?

**Inte på minimeringsgrund, någonstans i det mätta bandet 1 000–5 000.** Två ställen där något ändå
går sönder, och inget av dem är ett radantal: **vid ett tak som ingenting når** (vid 10 000 vägras
0 celler — då är grinden en formalitet), och **vid varje trunkerande form**.

### 3. Gavs Klas-beviljande 3 vid en STORLEK? — Nej, som text. Men posten saknar magnituden.

Mätt i alla fyra hem (ADR 0139, `MappedPlaintextExposureRegistry`, Art. 30-posten, ADR 0125):
**beviljandet är kategoriskt, inte kvantifierat.** Ett takbyte återöppnar det alltså inte som en fråga
om ordalydelsen.
⚠ **Men grunden är Art. 24(1), som skalar mot *scope* — och scope är precis vad ett takbyte ändrar.**
**Villkor:** vilket tak som än binds ska **acceptansens magnitud skrivas in i samma ADR-/CLAUDE.md-post
som taket**, aldrig i en PR-kropp.

### 4. ⛔ Premissen i frågan är FALSK på detta träd — rutten ligger inte på `MeListRead`

Mätt (`CompanyWatchCriteriaEndpoints.cs:52`, `RateLimitingExtensions.cs:37,402-414`): rutten ligger på
**`CompanyWatchCriteriaList`** (5 burst / 3 per minut). **Min Major 1 från del 2 är alltså urladdad —
genom exakt den åtgärd den krävde.**

Men svaret blir skarpare, inte mjukare: den policyns recompute-trigger innehåller *"a re-measurement
that moves the 381,5 ms figure"*, och **ett takbyte ÄR en sådan ommätning**. ⛔ **Hinken har ingen
nedåtmarginal kvar** — 3/min är golvet i dess nuvarande form, och höjd `PermitLimit` är uttryckligen
inte tillgänglig. **Verifieringen är min och den är BLOCKING. Jag förauktoriserar inte 5/3 vid ett
okänt tak.**

### 5. Vad ett takbyte INTE rör — fyra bekräftade, tre saknade

Bekräftat invarianta: M-D6:s ersättare · Major 4:s pnr-filter · Major 5a:s FK-kaskad · 5b/5c:s orakel.
⛔ **Tre kopplingar som INTE står på listan:** `SweepBatchSize` = 50 (beräknad ur ~60 ms mätt VID
nuvarande bound; *"This value and `SweepCron` are one decision"*) · literal-pinnen
`MaxPerCriterion.ShouldBe(1000)` · `CommandTimeoutSeconds`-docblockets *"a full 1 000-member replace
is 30,67 ms p95 … ~4 000x"*.

### 6. Art. 30 — bekräftat

Flyttas taket är meningen fel, och Minor 6-klassen gäller: uppdateras i **samma PR**.
⛔ **`docs/reviews/2026-09-06-1681-membership-measurement.md` ska INTE ändras.** Sessionens
*"Supersedes nothing"* är rätt hållning och jag skriver under den uttryckligen.

## Major

**1. Handlerns docblock namnger FEL rate-limit-policy** — `ListCompanyWatchCriteriaQueryHandler.cs:36-37,44`.
Krävs: båda `MeListRead`-satserna STRYKS; *"The bucket decision and … `MaxPerCriterion` are therefore
ONE decision, not two"* står kvar orört.
⚠ **Jag förutsäger INTE mekanisk stängning.** Jag gjorde den förutsägelsen i del 3 och hade fel.
Mät den stängande diffen. Delegera till `dotnet-architect`.

**2. `MaxPerCriterion`-docblocket påstår min Art. 5(1)(c)-grund STARKARE än jag gav den** —
`CompanyWatchCriterionMember.cs:82-87`. Krävs: satsen *"so the per-user storage ceiling this constant
sets IS that argument's operative value…"* STRYKS. **Att låta en GDPR-artikel bära ett tal den inte
bär kostar mer än det köper: nästa gång jag säger stopp ska det betyda stopp.**

## Minor

**3. `CompanyWatchCriteriaList`:s recompute-trigger saknar `MaxPerCriterion`** — bindande villkor på
den PR som flyttar taket.

## Omdöpningen och slug-flytten — mätt, inga fynd

Auth-grinden orörd (`/foretag` är toppsegment i `PROTECTED_PREFIXES`, segmentgränsmatchning) · robots
härleds ur samma lista · ingen path-kopplad kontroll finns att bryta · `revalidatePath`-målen
konsekvent flyttade · noll kvarvarande `smarta-bevakningar` utanför daterad provenance ·
integritetspolicyn faller inte · **att gamla bokmärken 404:ar är en produktkonsekvens Klas accepterat,
inte en säkerhetsfråga.** **Område 8:** ingen trigger; guarden kördes ändå — `no findings`, inte
`SKIPPED`.

---

## SKOPAD OMKONTROLL (rapport-läge) — fix-deltat `878e1fed..5264752e`

**Status:** ✓ **Approved — 0 Blockers, 0 Major, 0 nya Minor.**

**Major 1 STÄNGD.** `MeListRead` = **0** i handlern (två oberoende instrument). Och jag mätte att den
nya namngivningen är **sann**: `CompanyWatchCriteriaEndpoints.cs:52` → `RateLimitingExtensions.cs:37`
→ `:402` → `:411` → `RateLimitingOptions.cs:484`. Kedjan är sluten. Bevarandekravet håller ordagrant.

**Major 2 STÄNGD, och stängningen förbättrade separationen.** *"**As a COST term** the bound is a
function of…"* — kvalifikationen är hela poängen: den gör de icke-kostnadsburna lasterna nåbara i
stället för överskuggade.

**Minor 3 urladdad i denna PR, och i rätt form** — klausulen ligger inne i utlösarlistan, upprepar
inget mätt tal, och pekar tillbaka.

**Art. 5(1)(c)-stycket: ja — det säger min position exakt, och inte mer.** Det tillskriver mig ett
påstående om **obegränsad** härledd lagring och påstår **inte** att 1 000 är minimeringshärlett.
**Hinkkopplingen: ja, ärligt navigerbar** — mätt i båda riktningarna, ingen ände påstår ett tal.

**Ingen gradering av min dras tillbaka av deltat:** `RateLimitingOptions.cs` har en enda hunk (+3);
*"hers to ratchet (BLOCKING)"* och *"Raising `PermitLimit` is expressly NOT an available remedy"* står
utanför den. **PII i spårad fil:** mätrapporten skannad — 0 pnr/org.nr, 0 e-post, 0 GUID.

### Praise
- De tre rättelserna till mätrapporten flyttar alla förtroendet **nedåt** — och gränsen står kvar på
  1 000. En försvagad mätning som inte licensierar en större mängd är rätt riktning för Art. 5(1)(c).
- §3b-caveaten namnger att en **annan exekveringsstrategi** mättes i stället för att jämna ut det.

---

## Addendum — registrerad notering, OGRADERAD, utanför skopet

`docs/runbooks/gdpr-processing-register.md:1508` bar:
> `no members at all and renders "för bred" (Art. 5(1)(c) — the gate IS the minimisation argument).`

**Två fel av olika sort.** (a) Den citerade literalen är nu falsk — `grep "för bred"` över `messages/`
ger **0**. (b) *"the gate IS the minimisation argument"* är **exakt den överdrift Major 2 strök**, i
ett andra hem.

**Varför hon inte graderar det:** inte nytt i deltat, inte på rader deltat rörde, inte Blocker-klass —
Art. 30:s innehållsplikt (ändamål/kategorier/mottagare/retention) är intakt. *"Att gradera det Blocker
för att komma förbi §9.6:s gag vore bekvämlighetsgradering, och det gör jag inte."*
**Varför det ändå måste skrivas ner:** både registret och ADR 0139 är gitignorerade, alltså osynliga
för varje svep som greppar spårade filer — *"precis den fällan som gör att en spec-fix lagar det
citerbara hemmet och lämnar det exekverande."*

**Sessionens disposition:** halva (a) orsakades av denna PR:s copy-ändring, så den fixades in-block;
(b) fixades i samma mening eftersom det är samma överdrift Major 2 strök. Rättelsen ligger i
**huvudkopian** (filen är gitignorerad).

## Eskalering till Klas — ordagrant

> Klas måste avgöra vem som sveper `docs/runbooks/gdpr-processing-register.md:1508`, där
> behandlingsregistret (i) citerar en vägran-sträng som ingen yta renderar längre (mätt: 0 träffar på
> "för bred" i `messages/`, copyn heter nu "matchar fler företag än vi kan räkna annonser för") och
> (ii) bär meningen "the gate IS the minimisation argument", vilket är samma överdrift som min Major 2
> strök ur `CompanyWatchCriterionMember.cs` — min position är att minimeringen kräver att gränsen
> finns, inte att grinden är argumentet, och 1 000 är kostnadshärlett, inte minimeringshärlett. Båda
> filerna är gitignorerade (`.gitignore:170`, `.gitignore:162`), alltså utan publik läsare men också
> osynliga för varje agentsvep som greppar spårade filer, så fyndet existerar bara som denna
> nedskrivna rad. Jag graderar det inte som ett fynd i denna omkontroll. Jag promotar aldrig
> behandlingsregistret själv, och jag väljer inte routingen: den är sessionens och
> senior-cto-advisors.

> **Stående notering, ingen eskalering:** `MaxPerCriterion` är minimeringsargumentets operativa term,
> så **en PR som HÖJER den är en ny granskning för mig och min att signera — den ärver ingenting av
> det här godkännandet.** Denna PR höjer den inte.
