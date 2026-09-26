# 2026-09-08 — #1706 + #1707 (hela PR:en) — dotnet-architect

> ⚠ **Transkriberad av sessionen ur agentens svar** (charter: read-only).

**Status (första ronden):** Behöver åtgärdas — **0 kritiska, 5 viktiga, 2 nice-to-have.**
Clean Architecture och dependency-regeln är rena (inga projekt- eller paketändringar; C#-deltat är
rent kommentarer). Fynden sitter i **kostnadshalvan** av samma docblock som PR:en städade
produkthalvan i, och i den daterade rapport som produktionskod nu citerar.

## Direkta svar på de sex frågorna

1. **Ja, båda Majors stängs — och sessionens läsning av carve-outen är rätt.** Stängningsdiffen för
   handlern är **−6/+4**, alltså inte "adds zero lines" — **§9.6:s mekaniska carve-out är inte
   tillgänglig och en scopad omkontroll är skyldig till `security-auditor`.**
2. **Ja, triggermeningen är sund.** Inget tal, ingen mätning, inget andra hem; `#1706` daterar den.
3. **Nej — inte helt.** Produkthalvan är korrekt städad, men kostnadshalvan står kvar som etablerad
   medan denna PR:s **egen** ADR-amendment drar undan dess instrument (F1).
4. **`SweepBatchSize` — ja, se F4.** `CommandTimeoutSeconds`: intakt. Testpinnen: intakt, korrekt
   oberörd (CTO P5 binder att den flyttas i samma commit som konstanten).
5. **Ja, tre saker — F2, F3, F4.**
6. **Rent.** Domain = endast `Ardalis.SmartEnum`; Application = Domain + `Microsoft.EntityFrameworkCore`
   + `Mediator.Abstractions` + FluentValidation + DI.Abstractions — ingen provider, inget `.Relational`,
   inget `Npgsql`. `CriterionAdLines`-splitten är arkitektoniskt sund.

## Fynd

**[Viktigt] F1** — `CompanyWatchCriterionMember.cs:65-73`: ankare 1 står kvar påstått som etablerat
medan denna PR:s ADR-amendment skriver att det inte går att ta om rent. Dikotomin "vid taket ja / vid
5x nej" håller på **inget** av de två instrumenten. **Det är exakt det svep-fel repot redan bär: PR:en
svepte det citerbara hemmet (ADR) men inte det exekverande (docblocket).**

**[Viktigt] F2** — mätrapporten `:124`: *"Result 5 reproduces."* Rapporten skriver `Index Scan` där
2026-09-06 skriver `Bitmap Index Scan`. **Det är en annan nod, och "reproduces" påstås ovanpå
skillnaden utan att den nämns.** Planidentitet är precis den egenskap båda ankarna vilar på.
Godtagbara upplösningar: (a) rätta nodnamnet, eller (b) stryk *"Result 5 reproduces"* och skriv vad
som faktiskt observerades plus vad nodskillnaden gör med §3b:s giltighet.

**[Viktigt] F3** — `:26` mot `:113`: 41 597 mot 41 148 för samma kvantitet samma dygn, med fixturen
märkt "exact". Minst en är fel, och "exact" är då bevisligen inte exakt.

**[Viktigt] F4** — §5:s slutstycke: ~0,26 s finns inte i tabellen (det är 207,15 + 51,50), och
**jämförelsen korsar instrument** — 22 % taget på steady state, ~5 % från 2026-09-06:s pristine.
Detta är den enda nya data som bär på `SweepBatchSize`, vars docblock säger *"COMPUTED from the
measurement"*. **En jämförelse som byter instrument mellan täljare och nämnare är inte en mätning.**

**[Viktigt] F5** — kopplingen *"ONE decision, not two"* är onavigerbar **från båda hållen**: handlern
nämner efter Major 1-fixen ingen hink alls, och hinkens recompute-trigger nämner inte
`MaxPerCriterion` — samtidigt som ADR-amendmenten påstår att ett takbyte fyrar den. **Samma
defektklass som PR:en existerar för att laga.**

**[Nice-to-have] N1** — re-derive-stycket räknar upp tre termer och lägger till en fjärde utlösare som
inte är en term i funktionen.

**[Nice-to-have] N2** — typen tillåter `variant="detail"` + `adviceStatedByCaller={true}`. Förslag:
diskriminerad union.

---

## SKOPAD OMKONTROLL (rapport-läge) — fix-deltat `878e1fed..5264752e`

**Alla 5 Viktigt är stängda och N1 är stängd. N2 håller inte — jag drar tillbaka den.** Ett nytt
Nice-to-have. HEAD orörd på `5264752e`; inga redigeringar gjorda.

**F1 STÄNGD.** Tvåposts-`<list>` borta. ADR 0139 Amendment 2026-09-08 **finns** (rad 527,
retirement-texten rad 549) — pekaren är levande. Den gitignorerade-ADR-hasarden är inte ny i deltat
(54 tracked filer citerar redan ADR 0139), och docblocket citerar dessutom den **tracked** rapporten
bredvid. Inget fynd.

**F2 STÄNGD.** `"Result 5 reproduces"` grepar till **0**. *"Din upplösning (b) är rätt vald och
mätningen är starkare än en omformulering."*

**F3 STÄNGD.** 41 597 − 41 148 = 449 ✓. Rapporten är nu internt koherent.

**F4 STÄNGD som instrumentkorsning.** Alla fyra numeratorer verifierade i rapportens egna tabeller,
båda selektionstermerna i §4. Aritmetiken stämmer i alla fyra cellerna (11,4 / 21,6 / 3,7 / 45,8 %).
*"Att du behöll pristine-kolumnens motsägelse mot 'roughly 1,6x' i klartext är rätt disposal."*

**F5 STÄNGD, och kopplingen är nu ömsesidig.** **Uttryckligen prövat och friat:** att ett
Application-docblock citerar en Api-typ är ingen layer-violation — csproj:en mätt och oförändrad,
citatet är dokumentation utan compile-time-koppling, och samma docblock citerade redan en
Infrastructure-intern före deltat.

**N1 STÄNGD.**

### N2 — fyndet håller inte, och jag drar tillbaka det
*"Din vägran är korrekt, och skäl 3 är det bärande."* Mätt: **alla fyra kombinationerna är semantiskt
lagliga**, så det finns **ingen olaglig kombination att utesluta**. En trearmad union skulle *påstå*
att `detail`+`true` är omöjlig — och därmed återinföra på typnivå precis den härledning av det ena
flaggvärdet ur det andra som orsakade #1707. **§9.6:s utfall *"the finding does not hold"*.**

### Nytt fynd
**[Nice-to-have]** mätrapporten `:256-265`: F4-tabellens additioner lägger en **p95** till en **p50**,
och additionen är dessutom kors-host. **Slutsatsen överlever varje läsning** — med §4:s max-kolumn
blir cellerna 11 / 25 / 4 / 49 %, ingen som spränger ticken. Disposal: en bisats som namnger
provenienserna, eller en named skip. *(Sessionens disposition: bisatsen, tillagd.)*

### Utanför deltat — uttryckligen INTE ett fynd
`CompanyWatchMaterialisationOptions.cs:115` och `CompanyWatchCriterionMaterialisationWorker.cs:69` bär
*"50 × 60 ms = 3,0 s, 5 % av intervallet"*, som nu bara håller på pristine-instrumentet. Deltat rör
inte de raderna; **ingen fix är skyldig i denna PR.**
