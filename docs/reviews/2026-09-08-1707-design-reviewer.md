# 2026-09-08 — #1707 (the N=1 form on /oversikt) — design-reviewer

> ⚠ **Transkriberad av sessionen ur agentens svar.** Hon har inget `Write`-verktyg i den här
> konfigurationen och returnerade rapporten för transkribering. Innehållet är hennes, ordagrant.

**Status:** ⚠ Changes requested — formen bunden nedan
**Auktoritet:** DESIGN.md §5 (4px-rutnät) · §6 (hårlinjeliggare, inga kort runt enstaka värden) ·
AGENTS.md §10 (svensk ton) · §5 `Frontend:` · ADR 0047 (task-completion) · CLAUDE.md §9.6
**Egen mätning 2026-09-08**, stub `127.0.0.1:59995` + `localhost:3022`, 1280 px, probe
`C:/tmp/jbl-1707-dr-probe.mjs`, skärmbilder `C:/tmp/jbl-1707-dr-shots/`. Defekt (a) reproducerad
oberoende: `tooBroad` N=1 → "för bred" **2 ggr**; N=3 → 4 (3 rader + 1 råd); N=20 → 21. Dubbleringen
är strikt ett N=1-fenomen. `notAssessedTooBroad` N=1 → också **2 ggr**.
**Dark mode:** `theme-provider.tsx:37` `DARK_MODE_ENABLED = false` — `[data-theme="dark"]` är
onåbart i appen, så ingen mörk avläsning tas. Det är ett mätt skäl, inte en skippad kontroll.

---

## Blockers

**B1. `inList` ska SPLITTAS, inte flippas — en ren flipp återinför min part-3 Major 3**
Fil: `web/jobbliggaren-web/src/components/company-criteria/criterion-ad-lines.tsx:85`

Nuvarande: `const inList = variant === "summary"` grindar **fem** ställen. CTO:s [ADVISORY] ("byt namn
till one-of-many") läst bokstavligt = `inList = items.length > 1`, och då renderar rad 149
`ads.linkLabel` = *"42 aktiva annonser från dessa företag"* och rad 156 `ads.none` = *"Inga aktiva
annonser från dessa företag just nu."* vid N=1 — på ett block där **noll företag renderas**. Min
part-3 M3 mätte "dessa företag" till 0 förekomster; flippen sätter tillbaka den.

Krävs: flaggan bär **två olika fakta** och måste delas.

- `variant` fortsätter ensam avgöra **antecedenten** (`linkLabelStandalone` / `noneStandalone`,
  rad 148-157) — det är en egenskap hos *ytan*, aldrig hos N, och gäller oförändrat vid N=1, N=3 och
  N=20.
- En **ny** diskriminator avgör enbart vägran-copyn (rad 116, 131, 167): sann omm
  `variant === "summary" && items.length > 1`.
- `CriteriaSummary` renderar `.jp-appsummary__advice` (rad 161) på **samma** beräknade värde,
  nedskickat — aldrig två oberoende `items.length > 1`-uttryck, då driver de isär.
- Namnet ska påstå det den växlar på, t.ex. `adviceStatedByCaller`. Förbjudet: ett namn som säger
  vilken yta man är på.

Motivering: DRY/SRP — en flagga, ett faktum. §9.6: ett stängt fynd får inte öppnas av nästa fix.

**B2. Den korta armen får inte lämna N=1 utan väg framåt**
Fil: `criterion-ad-lines.tsx:129-132`

Nuvarande: grenen `ads.tooBroad` ensam renderar `adsTooBroad` **utan** CTA-länk, till skillnad från
`sharedRefusal`-grenen (rad 120-124) och matchningsgrenen (rad 171-175). Mätt: vid B1 hamnar
`notAssessedTooBroad` N=1 i just den grenen, och eftersom blockrådet då är tyst **förlorar användaren
"Ändra bevakningen" helt** — en vägrad bevakning utan någon affordans alls.

Krävs: den långa armen i den grenen bär samma `ads.matchingTooBroadCta`-länk som sina två syskon.
Efter fixen: vid varje vägran, oavsett tillstånd och N, förekommer "Ändra bevakningen" **exakt en
gång** i blocket.

Motivering: ADR 0047 — statusen är synlig men uppgiften har ingen väg framåt; ett blindskär är inte
ett tillstånd.

---

## Major

**B3. Ankartalet AVSLÅS — D2:s villkor (iv) faller på mätning**
Fil: `criteria-summary.tsx:112-123`

CTO:s D2 medger talet men lapsar det om ankarpositionen inte bär **alla** tillstånd. Mätt vid N=1,
åtta avläsningar: `counted` (2 tal), `saturated` (1), `nonCoincident` (1), `notAssessed` (1) — men
`tooBroad` (0 tal, h=173 px), `countedZero` (0), `notMaterialised` (0, h=142), och `error`/`empty`
har **inget ankare alls**. **Fyra av åtta N=1-avläsningar har inget tal att flytta upp**, och två av
dem är enligt CTO:s egen fördelning vanliga i dag och en (räknad nolla, 0,93 % av registret) förblir
vanlig efter omhärledningen. En ankarform som bara fungerar i talfallet ger blocket två strukturer
och lägger **Klas eget tillstånd utanför mönstret** — precis (iv).

Även avslaget: **etiketten i ankaret** ("Utveckling i Göteborg | Visa branschbevakningar"). Den
formen tappar blockets substantiv på vänsterkanten, och två sammanfattningar i samma sektion som
delar klass, typografi och vänsterkant skiljs *just* på det substantivet — min egen part-3 M4 vilar
på det. Vid 3440 ligger länken som ensam identifierare ~3 200 px bort.

Krävs: **ankaret ändras inte.** `.jp-appsummary__totals` bär `t("anchor", {count})` vid varje N.

Motivering: defekt (b):s premiss — *"ankarraden bär bara antalet, talen ligger en nivå ned"* —
**håller inte som formfel**. Regeln som styr båda blocken är att ett tal står på den nivå där det är
exakt: syskonet har ett talpar för hela mängden, det här blocket ett per bevakning. Den paritet Klas
faktiskt namngav (*"samma siffror, samma länkar, ett klick"*) levereras av raden och blockeras i dag
enbart av breddgrinden. Renderat `counted` N=1 visar redan "42 aktiva annonser" och "7 matchande
annonser just nu", båda länkade.

⚠ Omprövas om en avläsning **efter** #1706 visar att modaltillståndet fortfarande läser asymmetriskt.
Då är det ett nytt fynd, inte det här.

**B4. Vägran-copyn (CTO fix 7) — imperativen stryks, klausulen finns redan i repot**

Fem strängar bär *"Prova att välja färre branscher eller kommuner"* (`messages/sv/oversikt.json:39`,
`messages/sv/pages.json:460,465,470,474`). Klas kriterium är `{62100} × {1480}` — en SNI-kod i en
kommun; det finns inget att välja bort. Fallet **överlever** takhöjningen (största cellen 9 958
bolag).

Kanonisk klausul: **"Bevakningen matchar fler företag än vi kan räkna annonser för"** — det är
`ads.tooBroadNoList`:s befintliga ordalydelse, så ingen ny vokabulär myntas, och "matchar din
bevakning" är redan husets term för företagsrelationen (sex strängar).
Mekanismmening, **bara** i långformerna: **"Färre branscher eller kommuner ger färre företag."** —
konstaterande, inte uppmaning; sann även där användaren inte kan avgränsa.

| Nyckel | Krävs |
|---|---|
| `pages.foretag.criteria.ads.tooBroadShort` | "Bevakningen matchar fler företag än vi kan räkna annonser för." |
| `…ads.adsTooBroad` | "Bevakningen matchar fler företag än vi kan räkna annonser för. Därför visas inte antalet annonser. Färre branscher eller kommuner ger färre företag." |
| `…ads.adsAndMatchingTooBroad` | "Bevakningen matchar fler företag än vi kan räkna annonser för. Därför visas varken antalet annonser eller matchningen mot din profil. Färre branscher eller kommuner ger färre företag." |
| `…ads.matchingTooBroad` | "Bevakningen matchar fler företag än vi kan räkna annonser för. Därför kan vi inte räkna hur många annonser som matchar dig. Färre branscher eller kommuner ger färre företag." |
| `…ads.matchingTooBroadOnList` | "Bevakningen matchar fler företag än vi kan räkna annonser för, så alla aktiva annonser visas här i stället för de matchande." |
| `…ads.adsTooBroadHeadline` | "Antalet annonser kan inte räknas" |
| `oversikt.criteriaSummary.tooBroadAdvice` | "En bevakning som matchar fler företag än vi kan räkna annonser för får inga tal här." |
| `…ads.tooBroadNoList` | **oförändrad** — den var redan korrekt |

Bindande begränsning utöver strängarna: **ingen vägran-sträng får bära en imperativ som förutsätter
att bevakningen går att avgränsa.** `messages/en/` bär samma åtta nycklar och flyttas i samma commit
under samma begränsning. `ads.emptyBody` ("Prova att **bredda**…") rörs inte — den gäller räknad
nolla och är rätt där.

**B5. `deriveDisplayLabel` namnger avdelningen även för ett enda löv**
Fil: `web/jobbliggaren-web/src/lib/company-criteria/display-label.ts:43-57, 75-79`

`divisionNamesFor` namnger en avdelning så snart **något** löv är valt, och `formatNames` sätter
"m.fl." bara när flera **avdelningar** täcks — aldrig när ett av flera löv inom en avdelning är valt.
`{62100}` och hela avdelning 62 renderar därför **identisk** sträng. Docblocket rad 11 säger *"a
whole-division pick reads as exactly its name"* och nämner inte att ett enstaka löv gör det också.

Mätt konsekvens: det är detta som fick #1706:s ursprungliga premiss att se sann ut för två läsare. På
`/oversikt` kan användaren inte avgöra om den egna bevakningen är smal eller bred, och "färre
branscher" läser absurt under en etikett som redan ser ut som en hel bransch.

Krävs: `/oversikt`-raden måste bära samma disambiguering som katalograden redan gör —
`criterion-row.tsx:58` renderar `row.branschCount · row.kommunCount` ("1 bransch · 1 kommun") under
rubriken. Billigaste vägen är att återanvända det paret; formen är fixens.

Disposition: **följd-PR, inte in-block och inte en issue** (§9.6 — egen ändringsorsak:
etikettshärledningen, inte sammanfattningsformen). Den får **inte** vidga #1706/#1707. Blockerar
därmed inte den här PR:en.

---

## Minor

**m6. Blandad interpunktion i samma rad** — `ads.noneStandalone` slutar med punkt,
`jobads.companyWatches.matchingAds` gör det inte. Mätt vid `countedZero` N=1: "Inga aktiva annonser
just nu.Inga matchande annonser just nu". `matchingAds` delas med en annan yta, så fixen har
räckvidd. Rekommenderad disposition: **namngiven skip i PR-kroppen**, en rad (§9.6) — ingen annan
lane behöver se den.

**m7. Räknad nolla ger inget nästa steg** — vid `countedZero` N=1 läser blocket två negationer utan
väg vidare, och CTO:s fördelning gör tillståndet strukturellt vanligt (0,93 %). Jag **binder inget**
här: syskonet beter sig likadant, en nolla är inget fel, och ett breddningsråd vid varje tyst dag blir
brus vid N=20. Noterad så att en senare läsare inte öppnar den som ny.

---

## Vad som ska renderas efteråt — mekaniskt kontrollerbart

Kör alla 13 tillstånd vid **N=1, N=3, N=20** (`mixed`/`allCounted`/`atCap` bär fasta radmängder och
körs en gång). Varje avläsning bär kontroller — sektionen finns, blocket renderade, `role="alert"` =
0 — annars mäter en grön filnamn ingenting.

1. Kanonisk klausul (`/matchar fler företag än vi kan räkna annonser för/`) i blocket: **exakt 1** vid
   N=1; `rader_med_vägran + 1` vid N≥2. `tooBroad`: 1 / 4 / 21.
2. `/för bred/` i hela blocket: **0** i samtliga 13 × 3 avläsningar.
3. `/Prova att välja färre/`: **0** i DOM **och** `grep -r` mot `messages/` (sv+en) till 0. Vet att
   den var 5 före.
4. `.jp-appsummary__advice`: **0** noder vid N=1; **1** vid N≥2 när någon rad vägras.
5. "Ändra bevakningen": **exakt 1** i blocket när någon vägran är på skärmen — inklusive
   `notAssessedTooBroad` N=1 (B2:s pin).
6. `/dessa företag/` i blocket: **0** vid N=1, N=3, N=20 (B1:s regressionspin, part-3 M3).
7. `.jp-appsummary__totals` textContent = `"N branschbevakning(ar)"` vid varje N (B3 — beviset att
   ankaret inte rörts).
8. `allCounted`: strängarna "60" och "9 matchande" förekommer **inte** (summan, mätt en gång till i
   render utöver `criteria-summary.test.tsx:118`, som **inte redigeras**).
9. Höjd `tooBroad` N=1 @1280 mot dagens **173 px** — får inte överstiga med mer än en radhöjd
   (~25 px). Rapportera talet, påstå det inte.
10. A11y vid N=1 och N=20: `:focus-visible` ger outline ≠ none på radens nya CTA; `tabindex > 0` = 0;
    noll inversioner DOM-ordning mot visuell ordning i absoluta dokumentkoordinater;
    `prefers-reduced-motion` respekterad; kontrast oförändrad mot part-3:s 16,14:1 / 6,99:1.
11. Bredder **1280 / 1920 / 3440**, ingen horisontell overflow, radens `.jp-matchline` renderad bredd
    ≤ 68ch.
12. Nya enhetstester bredvid det orörda: ett N=1-test, och en pin att klausulen förekommer **exakt en
    gång** (CTO D2 (vi)).

Ingen ny CSS krävs: `jp-nudgelink` inuti `.jp-appsummary__watch .jp-matchline` är redan renderat i
produktion (`notAssessed` N=1, mätt), och `max-width: 68ch` täcker redan radens långform
(`globals.css:5166-5169`).

---

## Bra gjort

- `tooBroadNoList` bar redan den icke-anklagande formuleringen — B4 är en anpassning till repots egen
  bästa sträng, inte ny copy.
- Rådet står **en** gång vid N=3 och N=20 (mätt 4 resp. 21 klausuler mot 3 resp. 20 rader): part-3:s
  Major 2 håller där den var argumenterad.

---

## Sammanfattning

2 Blockers, 3 Major, 2 Minor. **N=1-formen bunden till den minimala vägen med en korrigering av CTO:s
[ADVISORY]: flaggan splittas, den byter inte betydelse** (B1), och den korta armen får sin CTA (B2).
**Ankartalet avslås** på D2 (iv), mätt: fyra av åtta N=1-avläsningar har inget tal att bära (B3) —
defekt (b) håller inte som formfel och löses av #1706 plus B1. Vägran-copyn omskriven, åtta nycklar ×
två locales (B4). `deriveDisplayLabel` graderad **Major → följd-PR**, blockerar inte den här PR:en och
får inte vidga #1706/#1707 (B5). Ingen FAS-DEFERRAL — allt ligger i innevarande fas.

Delegera fixarna till `nextjs-ui-engineer`. Re-review efter fix: samma agent, report-only, skopad till
fix-deltat (CLAUDE.md §9.6) — det blir hennes enda skopade omkontroll.
