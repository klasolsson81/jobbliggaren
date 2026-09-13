# ADR 0140 — Översikt som bento-dashboard: scoped undantag från ledger-formen

**Datum:** 2026-09-13
**Status:** Accepted 2026-09-13 (Klas-beslut via Claude Design-handoff `docs/design_handoff_oversikt_bento/`, README + artboard `3a` + `screenshots/3a-oversikt-bento.png`; chat-direktiv samma dag: *"översikten måste vara visuellt tydlig och inte rörig med en massa rader text som idag"*)
**Beslutsfattare:** Klas Olsson (produktägare)
**Amends:** DESIGN.md §1.2 ("Tabeller och listor" / "Kort-layouter överallt") och §6 ("Inga stats-kort runt enstaka värden") — scoped undantag, ENBART `/oversikt`. `jobbpilot-design-tokens` (sju nya tokens).
**Relaterad:** ADR 0068 (hero-gradienten — samma slag av scoped undantag, förebilden för den här ADR:ens form), ADR 0038 (en primärknapp per skärm — avgränsas här per KORT), ADR 0116 (bevakad-axeln, vars fyllning får en oskiftad token), ADR 0117 + CTO #1681 del 3 D1 (branschbevakningar summeras aldrig — orört), ADR 0076 (setup-kort ↔ matchnotis ömsesidigt uteslutande — orört, flyttat in i Matchning-kortet). Issue #1723.

> **Livscykel-/proveniens-not:** Skriven av Claude Code på Klas-direktiv (autonomt spec-edit-flöde, CLAUDE.md §9.2/§13). Besluts-substansen är Klas egen (handoffens README bär beslutslogg och regler); mekanikdomarna nedan (knapphöjd, fyllningstokens, kugghjulet) är CC:s och granskas av `design-reviewer` + `code-reviewer` + `dotnet-architect` i PR:en.

---

## Kontext

`/oversikt` har sedan #726 varit ett notiscenter: tre `NoticeSection`-liggare per källa (Mina ansökningar, Jobbannonser, Företagsbevakning) med hairlines, monoetiketter i vänsterkolumnen och stående sammanfattningar (`ApplicationSummary`, `CompanySummary`, `CriteriaSummary`) i samma vänsterkant. Efter #1703 och #1717 läste Klas sidan och fann att den *"läser som en liggare, inte som en översikt"*: tre radgrammatiker staplade på samma kant, ett kugghjul per sektion, och siffrorna — de tal en användare öppnar sidan för — inbäddade i meningar.

Handoffen (Claude Design, 2026-09-13) föreslår ett 12-kolumns rutnät av sex kort där **storlek och färg speglar vikt**: Kräver dig (span 8, åtgärdsnotiser), Mina ansökningar (span 4, stort tal + stapellista), Matchning / Bevakade företag / Branschbevakning (span 4 var, tintade per axel med ett stort tal och en solid CTA), Senaste händelser (span 12, info-notiser). Datakällor och länkbyggare är oförändrade.

Det är ett medvetet avsteg från två skrivna regler: DESIGN.md §1.2 föredrar tabeller och listor framför kort-layouter, och §6 förbjuder stats-kort runt enstaka värden. Reglerna finns för att kort-grider på VARJE sida gör en civic-produkt till en SaaS-dashboard och gömmer siffror i chrome. `/oversikt` är den ena sidan vars enda uppgift är att sätta fem tal bredvid varandra och skicka vidare — där är regeln fel, och ett undantag som är scopat till den sidan är billigare än att låta regeln gälla en sida den inte skrevs för. Samma resonemang gav hero-plattan sin gradient (ADR 0068 Beslut 2).

## Beslut

### Beslut 1 — Kort-rutnät på `/oversikt`, och ingen annanstans

`/oversikt` renderar ett `repeat(12, minmax(0,1fr))`-rutnät, gap 20px, med sex kort (`.jp-ov-card`) i ordningen ovan. Under 1024px blir varje kort `span 12` i samma ordning.

**Dokumenterade undantag (scoped, ENBART `/oversikt`):**
1. **Kort-layout** — DESIGN.md §1.2 "Kort-layouter överallt" får ett scoped undantag för den här sidan. Gäller ALDRIG `/jobb`, `/ansokningar`, `/foretag/*`, `/cv/*` eller inställningar; de behåller ledger-formen.
2. **Stats-kort runt ett tal** — DESIGN.md §6 "Inga stats-kort runt enstaka värden" får samma scoped undantag: fyra av korten bär ett stort tal (`--jp-fs-oversikt-num` 40px/700, `letter-spacing: -0.03em`, `tabular-nums`) med enheten baseline-alignad efter talet.

Kortformen är husets: `--jp-surface` eller axelns `-bg`, `1px solid` kant, `--jp-r-lg` (8px — modalradien; radier över 8px är fortsatt förbjudna), padding 22px 24px, **ingen skugga** (§3 "Skuggor" är orört — djup bärs av kant, aldrig av skugga). Rubriken är en `h2` per kort med en 40×40 ikonruta; kortet är en `<section aria-labelledby>`.

**Omätta tal renderas som en en-dash, aldrig som en siffra** — samma doktrin som `HeaderStats` (CTO-bind 2026-07-13 A′): `matchCount === null`, en degraderad `pipeline`/`companyWatches`/`criteria` ger `–` utan enhet, kortets `unavailable`-text och **ingen CTA**. Aldrig en blank cell — då tappar rutnätet sin form.

### Beslut 2 — Sju nya tokens; fyllning och hover följer knapp-kontraktet

| Token | Light | Dark | Roll |
|---|---|---|---|
| `--jp-follow-fill` | `#3E6C74` | **skiftas EJ** | Solid CTA + ikonruta i Bevakade företag-kortet; vit text 5,83:1 |
| `--jp-follow-hover` | `#2F5860` | **skiftas EJ** | Hover på den knappen; vit text 7,82:1 |
| `--jp-follow-border` | `#C5DDE1` | `#245059` | Kortkant mot `--jp-follow-bg` (dekorativ hairline: 1,20:1 light, 1,51:1 dark) |
| `--jp-info-fill` | `#1B5396` | **skiftas EJ** | Solid CTA + ikonruta i Branschbevakning-kortet; vit text 7,71:1 |
| `--jp-info-hover` | `#164478` | **skiftas EJ** | Hover; vit text 9,85:1 |
| `--jp-info-border` | `#C5D8F0` | `#2E4F7E` | Kortkant mot `--jp-info-bg` (1,18:1 light, 1,53:1 dark) |
| `--jp-fs-oversikt-num` | `40px` | — | Kortens stora tal (`.jp-ov-num`) |

Varför egna fyllningstokens och inte `--jp-follow`/`--jp-info`: de två är TEXT/KANT-tokens och skiftar i dark till `#7FC4CE`/`#8FBEEF` (ADR 0116, ADR 0037) — vit text på dem ger 1,6:1. Fyllningen behöver därför en token som inte skiftar, exakt som `--jp-accent-800` står bredvid `--jp-accent-700`. Matchning-kortet återanvänder `--jp-accent-800`/`-800-hover` och inför inget.

Kontrast, mätt 2026-09-13 (WCAG-formeln över värdena i `globals.css`):

| Par | Light | Dark |
|---|---|---|
| ink-2 på accent-50 / follow-bg / info-bg | 6,86 / 6,61 / 6,38 | 9,73 / 8,52 / 8,02 |
| ink-1 på samma tinter | 15,29 / 14,73 / 14,23 | 14,29 / 12,52 / 11,77 |
| ink-3 (tid) på info-bg | 5,45 | 4,75 |
| accent-700 (countlink) på follow-bg / info-bg | 6,38 / 6,16 | 8,74 / 8,22 |
| Statusetiketter 11px versal-mono på `--jp-surface-2` (warning / success / info / accent-700) | 5,48 / 5,06 / 7,12 / 6,99 | 10,01 / 9,01 / 8,29 / — |
| `--jp-warning`-vänsterkant (6px) mot vitt kort | 5,93 | 8,77 |
| `--jp-border-strong`-vänsterkant (tomläget) mot kort | 3,50 | 3,81 |

Alla textpar ≥ 4,5:1, alla UI-komponentpar ≥ 3:1, i båda teman. Kant-tokens är dekorativa hairlines och mäts inte mot 3:1 (samma klass som `--jp-border`).

### Beslut 3 — En solid knapp per kort, aldrig fler; höjden är husets

ADR 0038 säger max en `--primary` per skärm. Den här sidan bär tre solida knappar (Matchning accent-800, Bevakade `--jp-follow-fill`, Bransch `--jp-info-fill`) — men i **tre olika axelfärger**, en per kort, och ingen av dem är den gröna primären utom Matchningens. Regeln avgränsas här per KORT: **en solid knapp per kort, aldrig fler**, och kortets solida knapp leder alltid till annonserna kortets tal räknar. Ansökningskortet (leder till en lista, inte till annonser) och alla tomlägen använder outline/`--emphasis`-nivån. Kräver dig-radernas CTA är `.jp-btn--sm .jp-btn--emphasis` — betonad, aldrig solid (DESIGN.md §6, CTO-bind 2026-07-12 #788: N rader = N knappar).

**Knapphöjden avviker medvetet från mocken.** Handoffen ritar 40px; huset ratificerar två system — `.jp-btn` 44px (`--sm` 36) och shadcn `Button` 40px (DESIGN.md §6) — och 40 på `.jp-btn`-chassit vore ett tredje. Kort-CTA:erna är `.jp-btn` (44px) med en tint-modifier som bara sätter bakgrund, kant och hover; rad-CTA:erna `.jp-btn--sm` (36px); dismiss-knappen är den befintliga `.jp-notice__dismiss` (32px, 44px under 768). Mockens 40 är en ritning, inte ett beslut.

### Beslut 4 — Kugghjulet flyttar till toolbaren; funktionen tas inte bort

Handoffen tar bort per-sektionskugghjulen och lämnar öppet om notisinställningarna flyttar till `/installningar` eller behålls som ett enda kugghjul i toolbaren. CTO-domen 2026-08-29 binder: en yta får inte filtreras av en preferens den inte kan visa — så "bort" utan `InertNoticePrefsProvider` är förbjudet, och med provider är det att radera funktionen. Beslutet är **ett** kugghjul i toolbar-raden (höger), samma popover-markup som förut, med alla nio typerna grupperade per källa. `localStorage`-nyckeln `jp-oversikt-notice-prefs` och dess `"<källa>:<typ>"`-format är oförändrade (#1580).

### Beslut 5 — Vad som INTE ändras

- Notisbyggarna i `oversikt-page.tsx` (vilka notiser som finns, deras text, länkar och tider) — oförändrade. Kräver dig = `warning`/`success`/`brand`; Senaste händelser = `info`. Intervju (`brand`) blir därmed åtgärd — det är handoffens flytt, och den enda semantiska ändringen i notisdatat.
- `NoticeToolbar`, `MarkAllReadRow`, `useDismissedNotices`, `useNoticePrefs` — oförändrade i beteende. Läst-foten (`.jp-notice-foot`) behålls i botten av Kräver dig respektive Senaste händelser, så `MarkAllReadRow`:s fokusmål består.
- **Branschbevakningar summeras aldrig** (CTO #1681 del 3 D1). Vid en bevakning bär kortet dess tal; vid två eller fler blir kortet `span 12` med en rad per bevakning, och Matchning + Bevakade företag blir `span 6` så raden förblir jämn. Ett summerat tal hade brutit D1 och dubblat en duplicerad bevakning exakt.
- Setup-läget (`!hasStatedDesiredOccupation`) ersätter Matchning-kortets innehåll i samma cell — ömsesidigt uteslutande mot matchtalet som förut (ADR 0076). Det är den enda inställningslänken på sidan, och bara i det läget.
- Gästsidan `/gast/oversikt` renderar de gamla komponenterna byte-identiskt (gästläget är ute ur MVP 2026-08-30; migrering → #1585).
- Ingen ny backend: inga per-användar-deltan ("+N sedan igår"), inga nya tidsstämplar. Matchnings- och bevakningsnotisernas `timeToday` är fortsatt MOCK-märkt i koden; en riktig händelselogg (statusändringar, företagshändelser, riktiga stämplar) är följd-PR:er under epik #1662/#1666.

## Konsekvenser

**Positiva:** siffrorna står först och störst; en CTA per kort leder till exakt det talet räknar (samma H2-invariant som förut: `buildJobbHref` bär profilens facetter och inga `matchGrades`); färgaxlarna (grön = matchning, slate-teal = bevakad, blå = bransch) återanvänder ADR 0116:s semantik i stället för att uppfinna nya; sidan tappar två sektionsrubriker, två kugghjul och tre stående sammanfattningar i samma vänsterkant.

**Negativa/risker:** (1) två scoped regelundantag till att bevaka — `design-reviewer` ska avvisa varje kort-grid utanför `/oversikt` med hänvisning hit; (2) tre solida knappar på en skärm är mer än ADR 0038:s ordagranna "en" — avgränsningen per kort är den här ADR:ens och ingen annans; (3) `CriteriaSummary` och `SetupCallout` raderas (deras invarianter flyttar till korten och deras tester); (4) kortformen är `span 4` vid en branschbevakning och `span 12` vid fler — två former för ett block, valt för att D1 lämnar ingen summa att bära i ett litet kort.

**Spec-ändringar i samma PR:** DESIGN.md §1.2, §3 (tokens), §6 (stats-kort-regeln); `jobbpilot-design-tokens` (SKILL.md, `references/tokens-full.md`, `references/theme-block.md`).

## Alternativ som övervägdes

- **Behålla liggaren och bara harmonisera etiketterna** (#1717:s väg) — avvisat av Klas efter renderad läsning: rätt regel, fel form för sidan.
- **Kort på alla sidor** — avvisat: det är exakt vad §1.2 finns för att hindra. Undantaget är scopat och skrivet så att det inte sprider sig.
- **Summera branschbevakningar till ett tal vid N ≥ 2** — avvisat: CTO #1681 del 3 D1 är bunden på mätning (predikat utan unikhetsvillkor).
- **Ta bort notisinställningarna helt** — avvisat: CTO 2026-08-29 gör det till antingen en dold filtrering eller en raderad funktion; toolbar-kugghjulet kostar 32px.
- **40px knappar enligt mocken** — avvisat: ett tredje höjdsystem (DESIGN.md §6, Klas-beslut 2026-07-28).

## Referenser

- `docs/design_handoff_oversikt_bento/README.md` — handoffens regler, tillståndstabell och datakällor
- ADR 0068 (scoped-undantagets form), ADR 0038, ADR 0116, ADR 0117, ADR 0076
- `docs/reviews/2026-09-07-1681-part3-form-cto.md` (D1)
- Issue #1723
