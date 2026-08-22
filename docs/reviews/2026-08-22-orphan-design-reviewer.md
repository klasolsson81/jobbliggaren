# design-reviewer — PR #1438 (#1349)

- **Agent:** `design-reviewer` (§9.2, FE-copy i två locales + e-postcopy)
- **Datum:** 2026-08-22 · **Worktree:** `c:/tmp/jbl-orphan`
- **Runda 1 skope:** `git diff origin/main...HEAD`, HEAD `17ba6a4c`, bas `076851fd`
- **Omkontroll skope:** fix-delta `d8179433`, rapport-only
- **Status runda 1:** ⚠ Changes requested — **0 Blocker, 1 Major, 0 Minor**
- **Status omkontroll:** ✓ stängd, 0 new-in-delta
- **Auktoritet:** DESIGN.md · `jobbpilot-design-copy` · `jobbpilot-design-principles` · ADR 0047 · CTO-bindet

Ingen FAS-DEFERRAL-MANIFEST behövs: fyndet är läsbart ur JSON + komponenten. Diffen rör noll CSS och noll
`className`, så det finns inget att validera i dark mode.

## Major

**1. `intro` säger ingenting — den upprepar H1 och knappen ordagrant, och lämnar flödets enda öppna fråga
obesvarad** — `messages/{sv,en}/pages.json:501`. Renderat läser användaren tre satser: H1 "Bekräfta din
e-postadress" → P "Klicka på knappen för att bekräfta din e-postadress." → knapp "Bekräfta e-postadressen".
Stycket är helt härledbart ur raden ovanför och raden under. Före ändringen bar `intro` flödets konsekvens;
strykningen tog konsekvensen och lämnade instruktionen, som redan fanns två gånger. Skarpare: användaren har
**just klickat på en knapp i mejlet som heter "Bekräfta din e-postadress"**. Dubbelklicket är avsiktligt och
bärande (mejlskannrar GET:ar länken), men `intro` är den enda ytan som kan förklara varför det finns ett
andra steg. Det är ADR 0047.

**Krävs (exakt sträng per locale):**
`sv:501` → `"Adressen bekräftas först när du klickar på knappen nedan."`
`en:501` → `"The address is not confirmed until you click the button below."`
Den håller egenskapen **hårdare** än nuvarande text: grammatiskt ett *nödvändighets*-påstående ("först när"),
inte ett tillräcklighetspåstående.

## Domar på de fyra frågorna
1. **Prosan:** rätt utom `intro`. Mätt i båda locales: `…` är U+2026, inga em-dash, inga utropstecken, ingen
   emoji, du-tilltal, inget versalt "Du". Eget svep: noll överlevande aktiveringsvokabulär i flödet.
2. **`pending` håller — och koherensgrunden är för svag.** "Aktiverar…" fäller egenskapen **på egen hand**:
   "aktivera" är kontoaktiveringens verb, så en kontroll som heter "Bekräfta e-postadressen" i vila och
   "Aktiverar…" i flykten låter systemet självt beskriva klicket som en kontoaktivering. Ett hem CTO:ns
   uppräkning missade, precis som HEM B.
3. **`successBody` ska stanna.** (i) Matchar skeppad success-form i båda syskonflödena. (ii) Att hedga vore
   sämre: det **lägger till** en påståendemening, är falskt när `RegistrationsOpen=false`, och tillverkar
   tvivel hos ~alla vid den enda punkt där systemet har något sant och gott att säga. (iii) R1:s botemedel
   hör hemma där felet inträffar.
4. **Engelskan håller.** `successTitle` är bättre än en rak översättning: tillståndsform, exakt det 204:an
   intygar. `Log in`/`Sign in`-driften mellan auth-grupperna är pre-existerande med eget change-reason.

## Observation, routad ingenstans
E-postmejlet namnger nu samma handling på två sätt (öppningen "registrerat dig", avslutningen "skapat något
konto"). **Ändra den ändå inte** — avslutningen är Klas-krav, riktar sig till en icke-aktör, och är en
negerad villkorssats som påstår ingenting.

## Bra gjort
- Egenskapen applicerad som **strykning** i alla tre e-postrenderingarna samtidigt.
- H1 identisk med mejlets subject — källa-till-mål-kontinuitet.
- `successTitle` ankrar till nuvarande tillstånd, inte till en kontolivscykel (ADR 0047).

## Omkontroll (delta `d8179433`)
**M-1 STÄNGD.** Den renderade idle-sekvensen bär nu tre distinkta propositioner i stället för två plus ett eko:
H1 namnger uppgiften, P:t bär **systemstatusen** (adressen är ännu inte bekräftad — precis det tillstånd sidan
är byggd för, eftersom POST:en avsiktligt inte fyras på mount), knappen bär handlingen. "nedan" är sant
(DOM-ordning = visuell ordning). **0 new-in-delta.**

## Eskaleringar
Inga. Påminnelse (ej fynd): CTO:ns R1-disposal kräver en kommentar på #1349 **före** stängning.
