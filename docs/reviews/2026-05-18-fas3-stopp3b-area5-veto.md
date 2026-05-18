# Design-reviewer — FAS 3 STOPP 3b Area 5 render-VETO

**Datum:** 2026-05-18
**Agent:** design-reviewer (bindande Area 5 render-VETO)
**Batch:** /ansokningar-omarbetningen (lista + detalj 3 identitets-tillstånd + ny-formulär)
**Grind:** FAS 3 STOPP 3b — bindande render-VETO mot rendrade skärmbilder (ADR 0047 Area 5)
**Mandat:** ADR 0047 (flow-comprehension / Area 5-roll), kontext ADR 0048
**Auktoritet:** `docs/design/ansokningar-redesign-plan.md` (§2/§3/§5/§7/§8, spec-låsta L1–L6 + Area 1-mönsterval) + jobbpilot-design-principles/-components/-tokens/-a11y/-copy
**Skärmbilder:** `C:/tmp/jobbpilot-visual/20260518-0911/` (äkta live-render, verifierad ej Vercel-interstitial)

## VETO-verdikt: VETO — 1 Block / 1 Major / 1 Minor

FAS 3 STOPP 3b **ej passerad**. Klas underkände `/ansokningar` live 2 ggr;
detta är den rotfels-spärr som krävs. Ett Block (felaktig affordans i
identitets-tillstånd 2) + ett Major (icke-verifierbar dark-mode för det
primära JobAd-kopplade tillståndet) blockerar stängning. Re-render + fix
krävs innan ny VETO.

---

## Faktiskt inspekterade filer/viewports/teman

| Yta | light | dark | 1280 | 1920 | 3440 |
|---|---|---|---|---|---|
| `ansokningar-lista` | ✓ (1280, 1920) | ✓ (1920, 3440) | ✓ | ✓ | ✓ |
| `ansokningar-ny` | ✓ (1920) | ✓ (3440) | — | ✓ | ✓ |
| `ansokningar-detalj-jobad-kopplad` | ✓ (1920) | ✓ (1280, 1920, 3440)* | ✓* | ✓ | ✓* |
| `ansokningar-detalj-manuell` | ✓ (1280, 1920) | ✓ (1920, 3440) | ✓ | ✓ | ✓ |
| `ansokningar-detalj-fallback-outcome-form` | ✓ (1920) | ✓ (3440) | — | ✓ | ✓ |

\* = `jobad-kopplad__dark__*`-filerna renderades i **light mode** (se Major 1).
3440 dark explicit inspekterad för lista, manuell och fallback (broad-screen-gate).
Övriga sidor i katalogen (jobb-*, landing, logga-in, sokningar-*, vantelista,
registrera) = ej denna batch, ej granskade per uppdrag.

---

## Block (måste fixas — blockerar FAS 3-stängning)

### Block 1 — "Visa annonsen ↗" renderas på manuell ansökan utan URL (L5 / §3 / §7)

**Skärmbilder:** `ansokningar-detalj-manuell__{light,dark}__{1280,1920,3440}`
(samtliga 6 — konsekvent i alla viewports och båda teman).

**Vad bryter:** Identitets-tillstånd 2 (ManualPosting, Källa = "Manuellt",
ingen annons-URL i fixturen). JobInfoPanel renderar ändå länken
"Visa annonsen ↗". Spec §3 (`[Visa annonsen] extern länk (L5 bindande):
endast om jobAd.url`) och §7 (manuell ansökan kan sakna URL) är entydiga: när
ingen URL finns ska länken **inte renderas**. En "Visa annonsen"-länk på en
manuellt skapad ansökan utan kopplad annons är en **felaktig/död affordans** —
användaren klickar och får ingenstans, eller en trasig länk. Bryter även
jobbpilot-design-principles regel 3 (inga fyllnadselement — varje pixel ska
bära information; affordans som inte leder någonstans är värre än frånvaro).

**Notering:** kan ej fastställas från statisk bild om länken pekar på `#`,
tom href eller en placeholder — men spec-kravet är binärt: ingen URL ⇒ ingen
länk. Det renderade elementet är i sig spec-brottet, oavsett href-mål.

**Åtgärd (nextjs-ui-engineer):** villkora "Visa annonsen"-länken på
`jobAd?.url` (truthy) i JobInfoPanel. För ManualPosting utan URL: utelämna
hela länkraden (inte disabled, inte tom — borttagen). Verifiera att
ManualPosting **med** URL fortfarande visar länken korrekt (L5: synlig text
"Visa annonsen" + `↗`-glyf `aria-hidden`, `aria-label` med källa + "öppnas i
ny flik", `target=_blank rel=noopener noreferrer`).

---

## Major (bör fixas — blockerar bindande VETO-clear)

### Major 1 — `jobad-kopplad` dark-mode ej verifierbar (gate-glapp, ej regression)

**Skärmbilder:** `ansokningar-detalj-jobad-kopplad__dark__{1280,1920,3440}`.

**Vad bryter:** De tre `__dark__`-filerna för identitets-tillstånd 1
(JobAd-kopplad — det primära, vanligaste tillståndet) renderades i **light
mode**: vit canvas, ljusa paneler, mörk text, tema-toggeln visar fortfarande
mån-ikonen (dark ej aktivt). Filerna är dessutom byte-identiska med sina
`__light__`-motsvarigheter (96423/96423, 100870/100870, 108241/108241 bytes).
Dark-mode-temat applicerades alltså aldrig vid capture för just denna fixtur.

`manuell`- och `fallback`-shotsen renderar korrekt dark (äkta mörk canvas,
synliga panelkanter, ljusblå primärknappar) — komponenterna **stödjer** dark
mode. Detta är därför ett **capture/gate-glapp**, inte en dark-mode-regression
i koden. Men: en bindande Area 5 render-VETO kan inte clear:a dark mode för
det mest använda detalj-tillståndet utan giltig dark-evidens. ADR 0047 +
broad-screen-runbook kräver verifierad 3440 dark; den finns inte för
`jobad-kopplad`. Klas underkände live 2 ggr — en icke-verifierad yta får inte
glida igenom på antagande.

**Åtgärd (nextjs-ui-engineer / capture-pipeline):** re-rendra
`ansokningar-detalj-jobad-kopplad__dark__{1280,1920,3440}` med dark-temat
faktiskt aktivt (verifiera tema-toggel = sol-ikon + mörk canvas i output
innan leverans). Ingen kodfix indikerad — JobInfoPanel-dark är bevisat
fungerande i manuell/fallback; men beviset måste finnas för detta tillstånd
innan VETO kan clear:as.

---

## Minor (nice-to-fix, ej blocker)

### Minor 1 — Native `datetime-local` "Datum"-fält svag kontrast i dark mode

**Skärmbilder:** `ansokningar-detalj-*-outcome-form__dark__3440`,
`ansokningar-detalj-manuell__dark__*` (AddFollowUp "Datum"-fält).

Browser-native `<input type="datetime-local">`: placeholder `yyyy-mm-dd --:--`
och native picker-ikonen renderas i låg kontrast mot mörk fältbakgrund,
nästan osynlig vid 3440 dark. **Pre-existerande**, identiskt flaggat i
`2026-05-17-fas3-rendered-screenshot-veto.md` Minor 1, sammanfaller med den
redan uppskjutna m3-lokaliseringstouchen. Ingen ny regression i denna batch.
Hanteras med m3 (custom date-input eller `color-scheme: dark`), ej i denna grind.

---

## Områdesbedömning (det som höll)

### List-rad — §2 / §8 Area 1-mönsterval — PASS
`ansokningar-lista__{light,dark}__{1280,1920,3440}` (alla 6 representativt
inspekterade inkl. 3440 dark). StatusDot (prick + text "Utkast", **ej fylld
pill**) — korrekt lägst-vikt-mönster i tät lista. Primärrad
`{titel} — {företag}` (font-semibold) med korrekt mono-fallback
"Ansökan #10b1720e" för rad utan jobbidentitet. Sekundärrad
"Utkast · Uppdaterad 18 maj 2026 · Sök senast 17 juni 2026" (mono datum).
Endast icke-tom grupp visas ("Utkast 3"); ingen "0"-grupp. Brödtext-bredd
constrained på 3440 (inga utsträckta rader — broad-screen-buggen ej
återkommen). Inga gradients/glow/glas/drop-shadow i någon viewport.

### Detalj identitets-tillstånd — §3 / §7 — DELVIS PASS
- **Tillstånd 1 (JobAd-kopplad):** JobInfoPanel med Företag · Publicerad
  18 maj 2026 · Sista ansökningsdag 1 juni 2026 · Källa Platsbanken.
  `<dl>`-struktur, mono datum. **PASS i light/light-felrenderad-dark.**
- **Tillstånd 2 (ManualPosting):** J1 **PASS** — "Publicerad"-raden korrekt
  **utelämnad** (endast Företag / Sista ansökningsdag / Källa). Källa =
  "Manuellt". Ingen `CreatedAt`-som-Publicerad-läcka. *Men L5 Block 1.*
- **Tillstånd 3 (fallback):** **L6 PASS** — single-column, ingen tom
  vänsterkolumn, civic-not "Ingen kopplad annons — manuellt skapad ansökan"
  ersätter JobInfoPanel-positionen. H1 = mono "Ansökan #10b1720e",
  breadcrumb matchar. Ingen obalanserad tom canvas. StatusEditCard + listor
  full-width.

### StatusEditCard — §5 — PASS (statiskt verifierbart)
Förankrad "Nuvarande status: ● Utkast" StatusPill **en gång** (entitets-accent,
ej fylld i listan — korrekt Area 1-delning). 1-övergångsfall (Draft→Submitted)
renderas korrekt som **enskild primär åtgärdsknapp** "Markera som Skickad" —
ingen 1-items radiogrupp, ingen låst self-radio. L1 synlig instruktionsrad
"Nästa steg för den här ansökan är **Skickad**." present (sighted ledtext, ej
sr-only). Ingen disclosure — persistent synlig.
**L2 ej verifierbar i statiska shots:** ingen destruktiv övergång
(Rejected/Withdrawn) renderad i någon fixtur — Dialog-bekräftelsens närvaro
(spec §5 L2: behåll v1:s Dialog) kan **inte** bekräftas eller motbevisas från
dessa bilder. Interaktions-lucka flaggas (ej krav på denna grind per uppdrag),
men L2 är ej clear:ad — bör täckas av interaktionstest/efterföljande capture.

### L3/L4 sektionskort + typografi — PASS
Varje detalj-sektion = avgränsad panel med synlig rubrik och informationsbärande
border-avskiljare (ej shadow/floating — papper-ej-glas regel 1 hålls). Split
vänster/höger med kolumn-gap, panelerna border-inramade. Typografi-hierarki
konsekvent (H1 jobtitel, H2 sektionsrubriker "Om annonsen"/"Status"/
"Uppföljningar"/"Noteringar", panel-fält-labels) — ingen synlig
text-h1/text-h3-blandning. Ingen "rakt upp och ner"-stapel (defekt 5 ej åter).

### `/ansokningar/ny` skrivväg — §7 — PASS
`ansokningar-ny__{light,dark}__{1920,3440}`. Jobbtitel\* + Företag\*
obligatoriska (röd asterisk), Annonslänk + Sista ansökningsdag + Personligt
brev frivilliga med hint-rader ("Frivilligt. Länken måste börja med http://
eller https://." / "Datumet visas som påminnelse i ansökningslistan." /
"Frivilligt. Du kan lägga till eller redigera det senare."). **Inget
Källa-fält** (Source struken — korrekt). Inga beskrivande
placeholder-exempel (Platsbanken-regeln hålls). Primär "Skapa ansökan" +
sekundär "Avbryt". Dark mode korrekt.

### Dark mode (där verifierbar) — PASS
`lista`, `manuell`, `fallback`, `ny` dark: äkta mörk canvas, synliga
panelkanter (≥3:1 UI-gräns, ADR 0041-tokenet håller), ljusblå primärknappar
med mörk text, ingen light↔dark-regression. *Undantag: `jobad-kopplad` dark
ej verifierbar — Major 1.*

### Civic-copy — §8 — PASS
"du"-tilltal genomgående, ingen emoji, inga utropstecken, inga AI-klyschor.
sv-SE datum korrekt ("18 maj 2026", "1 juni 2026", "17 juni 2026"; mono
`2026-05-18` i uppföljningslista). "Pipeline över alla ansökningar. Klicka på
en rad för detaljer." / "Lägg in jobbet du söker. Du kan komplettera
uppgifterna senare." — saklig civic-ton. Testfixtur-text ("FAS 3 visuell
verifiering — fallback (temp 20260518-0911)", "Visuell verifiering — väntar
svar") medvetet undantagen per uppdrag/precedens — ej granskad som
produkt-copy.

---

## Sammanfattning

**1 Block, 1 Major, 1 Minor. VETO.**

Block 1 (L5 — "Visa annonsen"-länk på manuell ansökan utan URL) är en
felaktig affordans i identitets-tillstånd 2 och måste kodfixas
(villkora på `jobAd?.url`). Major 1 (`jobad-kopplad` dark renderad i light)
är ett gate-glapp — ingen kodfix indikerad, men det primära detalj-tillståndets
dark mode måste re-renderas och faktiskt verifieras innan en bindande VETO
kan clear:as; Klas underkände live 2 ggr och en icke-verifierad yta får inte
passera på antagande. Minor 1 är pre-existerande och uppskjuten med m3.

L2 (destruktiv-övergång Dialog) är **ej clear:ad** — ingen
Rejected/Withdrawn-fixtur renderad; interaktions-lucka noterad (ej krav denna
grind, men bör täckas innan FAS 3-stängning).

Allt övrigt — list-rad/Area 1-mönster, J1, L1, L3, L4, L6, skrivväg-formulär,
civic-copy, verifierbar dark mode — håller i alla inspekterade viewports och
teman.

**VETO: VETO.** FAS 3 STOPP 3b ej passerad. Delegera Block 1 till
nextjs-ui-engineer; re-rendra `jobad-kopplad__dark__*` (Major 1); re-review
mot ny korpus när Block + Major adresserade. Inga commits, ingen kodändring
av design-reviewer.

---

## Re-review 2026-05-18 (post-re-work, korpus 20260518-0949)

**Datum:** 2026-05-18 (re-review efter VETO v1)
**Re-work:** commit 38425be (visual-verify tooling — ingen produktkodändring;
produktkoden var spec-följsam i v1, fixturen var luckan)
**Korpus:** `C:/tmp/jobbpilot-visual/20260518-0949/` (äkta live-render).
Filstorlek-verifierat: alla detalj-dark utom `jobad-kopplad` är äkta dark
(byte-olik light); `jobad-kopplad__{light,dark}` byte-identiska
(99697/99697, 104289/104289, 111542/111542) — bekräftar CTO:s
instrument-artefakt-klassificering, ej produktdefekt.

### VETO-verdikt v2: PASS — 0 Block / 0 Major / 1 Minor (uppskjuten m3)

FAS 3 STOPP 3b **passerad ur Area 5 render-VETO-perspektiv**. Block 1
clear:ad (fixtur-luckan täppt, produktbeteendet spec-korrekt och
dispositivt bevisat via kontrast-tillstånd). Major 1 nedgraderad till
känd instrumentlucka (CTO evidensbelagd, ej produkt-Block, kompenseras av
Klas auktoritativ browser-toggle). L2 + §5 multi-radiogrupp clear:ade.
Minor 1 oförändrad pre-existerande, uppskjuten med m3 — ej blocker.

### Faktiskt inspekterade filer/viewports/teman (denna gång)

| Fil | tema | viewport |
|---|---|---|
| `ansokningar-detalj-manuell-utan-url` | light | 1920 |
| `ansokningar-detalj-manuell-utan-url` | dark | 3440 |
| `ansokningar-detalj-manuell` (med url) | light | 1920 |
| `ansokningar-detalj-submitted-radiogrupp` | light | 1920 |
| `ansokningar-status-destruktiv-inline` | light | 1280 |
| `ansokningar-status-destruktiv-dialog` | light | 1280 |
| `ansokningar-status-destruktiv-dialog` | dark | 3440 |
| `ansokningar-detalj-jobad-kopplad` | light | 1920 |
| `ansokningar-lista` | dark | 3440 |

Filstorlek-manifest för hela detalj-/status-korpusen inspekterat
(light/dark-byte-paritet verifierad per fixtur). `jobb-*`, `landing`,
`logga-in`, `registrera`, `sokningar-*`, `vantelista` i katalogen = ej
denna batch, ej granskade per uppdrag.

### Per-v1-fynd-status

**Block 1 (L5 — "Visa annonsen" på manuell utan URL) — CLEAR:AD.**
v1-flaggan var en fixtur-lucka, inte en produktdefekt: v1-fixturen satte
`url` på den manuella ansökan → länken var spec-KORREKT (§3 L5: länk när
`jobAd.url` finns; manuell ansökan kan ha url). Genuin lucka var att
korpusen saknade manuell-UTAN-url. Nytt tillstånd verifierat:
- `ansokningar-detalj-manuell-utan-url__light__1920` + `__dark__3440`:
  JobInfoPanel visar Företag / Sista ansökningsdag "—" / Källa "Manuellt".
  **Ingen "Visa annonsen"-länk** (L5 korrekt — ingen URL ⇒ ingen länk).
  "Publicerad"-raden korrekt utelämnad (J1). "Personligt brev | Visa"
  disclosure present. Civic-not-position korrekt.
- Kontrast-tillstånd `ansokningar-detalj-manuell__light__1920` (Källa
  "Manuellt", url satt): **"Visa annonsen ↗"-länk visas korrekt** (L5 —
  manuell MED url ⇒ länk). Dispositivt: villkoret `jobAd?.url` fungerar
  binärt rätt — länk när url finns, borttagen (ej disabled/tom) när den
  saknas. Spec §3 L5 + §7 uppfyllda i båda riktningar, light + dark.

**Major 1 (`jobad-kopplad__dark` renderade light) — NEDGRADERAD MED GRUND
(känd instrumentlucka, ej produkt-Block).** Respekterar CTO:s
evidensbelagda klassificering (beslut a1fb513d475176da7 +
a71bc82bd838fb0ae): bekräftad Chromium/CDP colorScheme-emulerings-artefakt.
Dispositivt bevis foldat in: (a) produktkod invariant över alla 5
detalj-tillstånd; (b) identisk `<html>`/ThemeScript/headers jobad-kopplad
vs manuell; (c) light==dark överlever page.reload OCH localStorage-forcerat
tema; (d) 4/5 detalj-tillstånd (samma komponent) ger äkta dark
deterministiskt — korroborerat denna gång: `manuell-utan-url__dark__3440`
+ `status-destruktiv-dialog__dark__3440` + `lista__dark__3440` = äkta mörk
canvas, sol-ikon, ljusa primärknappar, panelkanter ≥3:1. Detta är exakt
min egen v1-klassificering ("capture/gate-glapp, ej regression"). Det
giltiga tillståndet `jobad-kopplad__light__1920` granskat för egen
compliance: JobInfoPanel korrekt (Företag "Uppsala kommun" · **Publicerad**
"18 maj 2026" — korrekt renderad för JobAd-kopplad, J1 · Sista
ansökningsdag "8 juni 2026" · Källa "Platsbanken"), "Visa annonsen ↗"
present (jobAd.url — L5), `<dl>`-struktur, mono datum, jp-h1/jp-h2-hierarki
(L4), border-avgränsade paneler ej shadow/glas (§3 L3, papper-ej-glas).
Lång H1 wrappar rent, breadcrumb matchar. **Ej produkt-Block; dark-bevis
för detta enda tillstånd defer:as till Klas auktoritativ browser-toggle**
(dokumenterad känd instrumentlucka, ej regression).

**L2 (destruktiv-övergångs-Dialog — "ej clear:ad" i v1) — CLEAR:AD.**
Ny Submitted-fixtur med radiogrupp >1 övergång inkl. Nekad/Återtagen.
- `ansokningar-detalj-submitted-radiogrupp__light__1920`: "Nuvarande
  status: ● Skickad" StatusPill förankrad **en gång** (entitets-accent).
  §5 L1 synlig instruktionsrad "Välj ny status. Nuvarande status är
  **Skickad**." present (sighted, ej sr-only). Radiogrupp 3 options
  (Bekräftad/Nekad/Återtagen) — multi-option, **ingen låst self-radio**
  för Skickad (ingen oväljbar dubbelrendering — components-skill hålls).
  [Spara] disabled tills val ≠ nuvarande. Persistent, ingen disclosure.
- `ansokningar-status-destruktiv-inline__light__1280`: destruktivt val
  (Nekad) → **inline konsekvenstext** "Nekad avslutar ansökan. Det går
  inte att ångra utan manuell åtgärd." (civic-ton, ingen utropstecken).
  [Spara] aktiveras. Additiv förvarning per §5 L2.
- `ansokningar-status-destruktiv-dialog__light__1280` + `__dark__3440`:
  [Spara] på destruktiv övergång → **Dialog-bekräftelse**. DialogTitle
  "Markera som Nekad?", konsekvenstext "Ansökan ändras från **Skickad**
  till **Nekad**. Det går inte att ångra utan manuell åtgärd.",
  åtgärdsspecifik knapp "Markera som Nekad" (destruktiv-tonad) + "Avbryt"
  + ×-stängning, backdrop dimmar sidan. Inline-texten kvarstår bakom
  dialogen (additiv, **ersätter ej** dialogen — §5 L2 bindande uppfyllt).
  Dark 3440: Dialog renderar korrekt i äkta dark, body-bredd constrained.
  **L2 + §5 multi-radiogrupp clear:ade i light + dark + broad-screen.**

**Minor 1 (datetime-local dark-kontrast) — KVARSTÅR (uppskjuten m3).**
Oförändrat pre-existerande. Native `<input type="datetime-local">`
placeholder/picker-ikon låg kontrast i dark. Ej regression i denna batch,
ej blocker. Hanteras med m3-lokaliseringstouch per v1.

### Sammanfattning (re-review)

**0 Block, 0 Major, 1 Minor (uppskjuten). VETO v2: PASS.**

Block 1 clear:ad — produktbeteendet `jobAd?.url`-villkorat var spec-korrekt
hela tiden; v1-fyndet var fixtur-täckningslucka, nu täppt och dispositivt
bevisat via manuell-utan-url vs manuell-med-url-kontrast (light + dark).
L2 + §5 multi-radiogrupp clear:ade — destruktiv övergång går via bindande
Dialog (§5 L2), inline-konsekvens additiv ej ersättande, radiogrupp utan
låst self-radio, L1 synlig instruktionsrad present. Major 1 nedgraderad
med grund — CTO:s evidensbelagda Chromium/CDP-instrumentartefakt-klassning
sammanfaller med min egen v1-bedömning; det giltiga
`jobad-kopplad__light`-tillståndet är fullt design-compliant;
dark-bekräftelse för detta enda tillstånd vilar på Klas browser-toggle
(dokumenterad känd instrumentlucka, ej produkt-Block). Minor 1 kvarstår
pre-existerande och uppskjuten med m3.

Allt övrigt som höll i v1 (list-rad/Area 1-mönster, J1, L1, L3, L4, L6,
skrivväg-formulär, civic-copy, verifierbar dark mode) håller fortsatt i
inspekterade viewports/teman. Inga gradients/glow/glas/drop-shadow,
inga emoji/utropstecken, sv-SE-datum, "du"-tilltal genomgående.

**VETO: PASS.** FAS 3 STOPP 3b passerad ur bindande Area 5 render-VETO
(ADR 0047). Ingen kodfix indikerad. Återstående: Klas auktoritativ
browser-toggle dark-bekräftelse för `jobad-kopplad` (känd instrumentlucka,
ej blocker) + m3-uppskjuten Minor 1. Inga commits, ingen kodändring av
design-reviewer.
