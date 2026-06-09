# JobbPilot v3 — Översikt (handover till Claude Code)

> **Status:** Godkänt av produktägaren (Klas Olsson, 23 maj 2026).
> **Källfiler:** `JobbPilot v3.html` + `jobbpilot-v3.css` + `src-v3/oversikt.jsx` (klickbar prototyp i Claude Design).
> **Målbilder:** `handover/oversikt/01-…05-*.png` (refereras inline nedan).
> **Uppdrag:** Bygg en ny route `/oversikt` (alt. `/`) i `web/jobbpilot-web/` enligt denna spec.
> **Förhållande till HANDOVER-v3.md:** Detta dokument är en **tilläggsspec** — alla veto-regler, designtokens och §1–§5 i HANDOVER-v3.md gäller fortfarande.

---

## 0. Beslut: mockdata är OK tills BE är på plats

Klas har explicit godkänt att Översiktssidan får använda **mock-data** för värden där backend inte ännu exponerar endpoint. Sidan ska **inte** blockeras på saknad backend.

**Regel:** För varje datapunkt — välj alltid riktigt BE först. Använd mock bara för de specifika fält som inte finns ännu. Markera dem tydligt i koden (`// MOCK: ersätt med … när /api/<endpoint> finns`).

Se §3 nedan för exakt mappning vad som är riktigt vs mock.

---

## 1. Målbilder

| # | Vy | Tema | Fil |
|---|----|------|-----|
| 01 | Översikt — topp (titel, "I dag"-kort, notiser-start) | Light | `handover/oversikt/01-oversikt-top.png` |
| 02 | Översikt — Information-grupp (matchning, intervju, sparad sökning) | Light | `handover/oversikt/02-oversikt-notiser-information.png` |
| 03 | Översikt — Sammanfattning (tre kolumner) | Light | `handover/oversikt/03-oversikt-sammanfattning.png` |
| 04 | Översikt — topp | Dark | `handover/oversikt/04-oversikt-dark-top.png` |
| 05 | Översikt — Sammanfattning | Dark | `handover/oversikt/05-oversikt-dark-sammanfattning.png` |

![Översikt topp light](handover/oversikt/01-oversikt-top.png)
![Notiser — Information](handover/oversikt/02-oversikt-notiser-information.png)
![Sammanfattning](handover/oversikt/03-oversikt-sammanfattning.png)
![Översikt topp dark](handover/oversikt/04-oversikt-dark-top.png)
![Sammanfattning dark](handover/oversikt/05-oversikt-dark-sammanfattning.png)

---

## 2. Sidans uppbyggnad — designintention

Översikten är **inte** en AI-/SaaS-dashboard med KPI-kort. Tonen är **civic utility** — en daglig sammanställning från en myndighetsutility. Tänk personlig ärendeöversikt på Mina Sidor, inte Notion-startsida.

### 2.1 Hög nivå: tre sektioner ovanför varandra

```
┌─────────────────────────────────────────────────────────────┐
│ Title block            │   ╭─ I dag ─────────────────────╮  │
│ — kicker               │   │ 23 lördag maj 2026          │  │
│ — h1 "Översikt"        │   │ 10:30 — Telefonscreening    │  │
│ — lede                 │   │ 14:00 — Förbered intervju   │  │
│                        │   │ Google Calendar inte synkad │  │
│                        │   ╰─────────────────────────────╯  │
├─────────────────────────────────────────────────────────────┤
│ NOTISER                          [Markera alla som lästa]   │
│ Kräver åtgärd (3)                                           │
│   ▌ Erbjudande   Bonnier News …   Granska →   3 dgr  ✕      │
│   ▌ Uppföljning  Du har 2 ansökningar …                     │
│   ▌ Deadline     2 sparade annonser …                       │
│ Information (3)                                             │
│   ▌ Matchning    143 nya annonser …                         │
│   ▌ Intervju     Folksam bekräftat …                        │
│   ▌ Sparad sökn. Remote/Distans 4 nya träffar …             │
├─────────────────────────────────────────────────────────────┤
│ SAMMANFATTNING       registrerat per 2026-05-23             │
│ ┌──────────────┬─────────────┬────────────────────────────┐ │
│ │ ANSÖKNINGAR  │ BEVAKNING   │ UNDERLAG                   │ │
│ │ Aktiva … 5 › │ Sparade …   │ CV-varianter … 3 ›         │ │
│ │ Utkast … 1 › │ Sparade s. 3│ Personliga brev … 4        │ │
│ │ Intervjuer.1›│ Nya match.28│ Senast uppd. CV … 13 maj › │ │
│ │ Erbjudande.1│ Aktiva …    │ Sökstart · 46 dagar … 6 apr │ │
│ │ Avslag … 1   │ Senaste …   │                            │ │
│ │ Inget svar.0 │             │                            │ │
│ └──────────────┴─────────────┴────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

### 2.2 Title block

- **Kicker** (mono uppercase, 11.5px, letter-spacing 0.10em): `Inloggad som Klas Olsson` — texten kommer från `getMyProfile()` (`displayName` eller `email`-prefix). **Inget ärendenr** — vi har inget ärende att referera till.
- **H1**: `Översikt` (Hanken Grotesk 32px / 700)
- **Lede**: `Senaste händelser och status för dina ansökningar.` (17–18px)

### 2.3 "I dag"-kort (övre högra)

Bredd 320px (max-width 100% på mobil), border 1px `--jp-border`, radius `--jp-r-md` (6px), padding `14px 16px 12px`.

Innehåll:
1. **Head** — `I DAG` (mono kicker) + datum-display (`23` stor mono navy, `lördag maj 2026` ljus mono till höger)
2. **Lista** — varje rad: 44px tids-kolumn (mono fet) + titel + ev. "where". Vänster border 2px `--jp-navy-700`.
3. **Foot** — dashed top-border, kalender-ikon + `Google Calendar inte synkad — visar endast JobbPilot-händelser.`

**Tre tillstånd:**
- Inga händelser i dag → `Inget planerat i dag.` (italic ink-3)
- Bara JobbPilot-händelser → som ovan, fot säger "inte synkad"
- Google synkad (framtid) → google-händelser får border-color `--jp-info` (klass `--google`), fot ändras till "Synkad med Google Calendar · 2026-05-23 08:42"

### 2.4 Notiser-sektion

**Sektionsrubrik** (`<h2 class="jp-section__title">Notiser</h2>`) + meta-text till höger: `senast uppdaterad 2026-05-23 · 08:42` (mono). Knapp längst till höger: `Markera alla som lästa` (ghost, sm).

**Två grupper** med mono uppercase-rubrik och count `(N)`:
1. **KRÄVER ÅTGÄRD** — erbjudanden, uppföljning, deadlines (orange/röd-tonade)
2. **INFORMATION** — matchning, intervju-bekräftelse, sparad sökning (blå/grön-tonade)

Varje notis-rad:
- 4px **färgremsa** i vänsterkant (info-blå, warning-orange, brand-navy, success-grön)
- **Label** (mono uppercase, statusfärg): `MATCHNING`, `UPPFÖLJNING`, `ERBJUDANDE`, etc.
- **Text** (16px brödtext, `<b>` för nyckeltal som "143 nya annonser")
- **CTA**-knapp (navy-700, fet, ej understruken default; hover-understrykning) + chevron
- **Tid** (mono 12px ink-3)
- **Dismiss-X** (markera som läst — försvinner från översikten; ingen arkiv-vy)

CSS-grid kolumner: `4px 130px 1fr auto 120px 36px`. Vid <880px viewports radbryts till två-radig layout (se `.jp-notice` media query i `jobbpilot-v3.css`).

**Empty state**: när alla 6 dismissade → `<div class="jp-empty">` med titel `Inga olästa notiser` och text `Inkorgen är tom. Du får besked så snart det händer något i ditt ärende.`

### 2.5 Sammanfattning-sektion

**Sektionsrubrik** + meta: `registrerat per 2026-05-23` (mono).

**Tre kolumner** i grid (`repeat(3, 1fr)`), surround av kort med border 1px + radius 6px:

| Kolumn | Vad |
|---|---|
| **Ansökningar** | Aktiva, Utkast, Intervjuer bokade, Erbjudande, Avslag, Inget svar |
| **Bevakning** | Sparade annonser, Sparade sökningar, Nya matchningar i dag, Aktiva annonser totalt, Senaste sökning |
| **Underlag** | CV-varianter, Personliga brev, Senast uppdaterat CV, Sökstart |

Varje rad är en `SummaryRow`:
- Label (vänster)
- Dotted leader (CSS `border-bottom: 1px dotted`)
- Värde (höger, mono fet 19px, navy om highlight)
- Chevron 14px (alltid renderad — `visibility: hidden` när raden inte är klickbar — så värdena alignar)
- Hover på klickbar rad: label + leader skiftar till navy, hela raden navigerar

Vissa rader är klickbara (har `onClick` → navigerar till `/ansokningar`, `/jobb`, etc), andra är rena data-rader.

På narrow viewport (<880px) faller grid till en kolumn med horisontella separatorer.

---

## 3. Datakälla — riktig BE vs mock

CC ska **alltid välja riktig backend när den finns**. Läs `web/jobbpilot-web/src/lib/api/*` och servera Översikten via en RSC (Server Component) som heter `app/(app)/oversikt/page.tsx`. Använd `Promise.all` för parallella requests.

### 3.1 Titelblock

| Fält | Källa | Status |
|------|-------|--------|
| `displayName` / `email` | `getMyProfile()` från `@/lib/api/me` | ✅ Riktig BE |
| Datum (`new Date()`) | `Date.now()` server-side | ✅ Inbyggt |

### 3.2 "I dag"-kort

| Fält | Källa | Status |
|------|-------|--------|
| `todaysEvents` | **MOCK** array tills vidare | ⚠️ Mock — se 3.6 nedan |
| Google-flagga | Hårdkodad `googleSynced=false` | ⚠️ Mock — se 3.6 |

> **Framtid:** Google Calendar-integration ska gå via en server-action `getTodaysAgenda()` som returnerar `{ events: AgendaEvent[], googleSynced: boolean, syncedAt?: string }`. Ingen behov för CC att stub:a endpoints — bara hålla mock-data isolerat så det är enkelt att byta.

### 3.3 Notiser

| Notis | Källa | Status |
|---|---|---|
| Erbjudande | Filtrera `getPipeline()` på `status === "OfferReceived"`, ta nyaste | ✅ Riktig BE |
| Uppföljning | Filtrera `getPipeline()` på `status ∈ {Submitted, Acknowledged}` + `submittedAt < idag-14d` | ✅ Riktig BE |
| Deadline | **MOCK** tills "sparade annonser" har deadline-fält | ⚠️ Mock |
| Matchning ("143 nya annonser") | **MOCK** | ⚠️ Mock — kräver matchningstjänst |
| Intervju bekräftad | Filtrera `getPipeline()` på `status === "InterviewScheduled"` + `updatedAt > idag-1d` | ✅ Riktig BE |
| Sparad sökning ny träff | Anropa `getSavedSearches()` och jämför `runCount` / `lastRunAt` | 🟡 Delvis — eller mock om saknas |

### 3.4 Sammanfattning — Ansökningar

| Rad | Källa | Status |
|---|---|---|
| Aktiva | `getPipeline()` — count där status ∉ {Rejected, Withdrawn, Accepted} | ✅ Riktig BE |
| Utkast | `getPipeline()` — count `Draft` | ✅ Riktig BE |
| Intervjuer bokade | `getPipeline()` — count `InterviewScheduled` + `Interviewing` | ✅ Riktig BE |
| Erbjudande | `getPipeline()` — count `OfferReceived` | ✅ Riktig BE |
| Avslag | `getPipeline()` — count `Rejected` | ✅ Riktig BE |
| Inget svar (över 30 dagar) | `getPipeline()` — count `Ghosted` (eller status=Submitted+>30d) | ✅ Riktig BE |

### 3.5 Sammanfattning — Bevakning

| Rad | Källa | Status |
|---|---|---|
| Sparade annonser | Saknar endpoint nu. Använd `0` om saknas, mocka annars. | ⚠️ Mock tills `/api/saved-jobs` finns |
| Sparade sökningar | `getSavedSearches()` — `items.length` | ✅ Riktig BE |
| Nya matchningar i dag | **MOCK** (`28`) | ⚠️ Mock — kräver matchningstjänst |
| Aktiva annonser totalt | `getJobAds({ page: 1, pageSize: 1 })` — läs `total` från svaret | ✅ Riktig BE |
| Senaste sökning | `getSavedSearches()` — sortera på `lastRunAt`, ta `name` | ✅ Riktig BE (eller läs `recent-searches` cookie/lokal) |

### 3.6 Sammanfattning — Underlag

| Rad | Källa | Status |
|---|---|---|
| CV-varianter | `getResumes()` — `items.length` | ✅ Riktig BE |
| Personliga brev | **MOCK** (`4`) | ⚠️ Mock — ingen brev-tabell ännu |
| Senast uppdaterat CV | `getResumes()` — `items[0].updatedAt` (svensk kort form) | ✅ Riktig BE |
| Sökstart | `getMyProfile()` — `createdAt` (svensk kort form) + diff till idag i dagar | ✅ Riktig BE (om `createdAt` exponeras; annars mock) |

### 3.7 Mock-data — håll det centraliserat

Lägg all mock i **en** modul, t.ex. `web/jobbpilot-web/src/lib/oversikt/mock-data.ts`:

```ts
// MOCK: ersätt när /api/<endpoint> finns
export const OVERSIKT_MOCK = {
  todaysEvents: [...],
  googleSynced: false,
  matchCountToday: 28,
  matchCountThisWeek: 143,
  savedJobsCount: 2,
  savedJobsDeadlines: [{ date: "2026-05-25" }, { date: "2026-05-27" }],
  personalLettersCount: 4,
};
```

Varje konsument importerar därifrån — när BE finns, byter vi en plats.

---

## 4. Filer i prototypen

| Fil | Syfte |
|-----|-------|
| `src-v3/oversikt.jsx` | **Hela Översiktssidan** — komponenter `OversiktPage`, `NoticeRow`, `SummaryRow` + mock-data inline |
| `src-v3/app.jsx` | Visar var routen monteras (`route === "oversikt"` → `<OversiktPage />`) |
| `src-v3/shell.jsx` | Header-nav-item `oversikt` läggs först; brand-länken navigerar till `oversikt` |
| `src-v3/data.jsx` | Mock applications, savedSet, savedSearches, CVs — driver de "äkta" siffrorna i prototypen |
| `jobbpilot-v3.css` | All CSS för sidan finns under kommentaren `/* Översikt — civic dossier */` (sök `.jp-oversikt__`, `.jp-notice`, `.jp-summary`) |

CC har läsåtkomst till hela projektet.

---

## 5. CSS-tokens & klasser att klona

Alla tokens existerar redan i `jobbpilot-v3.css` (§2 i HANDOVER-v3.md). Kopiera till `globals.css` om inte redan klart.

Nya klasser CC behöver implementera (tailwind eller plain CSS — välj vad som matchar resten av repot):

| Klass | Användning |
|---|---|
| `.jp-oversikt__head` | Flex-container för title + I-dag-kort |
| `.jp-oversikt__kicker` | Mono uppercase ovanför h1 |
| `.jp-oversikt__today` | I-dag-kortet |
| `.jp-oversikt__today__head` / `__kicker` / `__date` / `__day` / `__rest` / `__weekday` / `__month` | Innan i I-dag-kortet |
| `.jp-oversikt__today__list` / `__event` / `__time` / `__title` / `__where` | Lista i I-dag-kortet |
| `.jp-oversikt__today__event--google` | Google-källa-variant (border-color info) |
| `.jp-oversikt__today__foot` | Sync-status fot |
| `.jp-notice-list` | UL container |
| `.jp-notice` | LI rad |
| `.jp-notice--info/--warning/--brand/--success` | Färgvarianter |
| `.jp-notice__strip` / `__label` / `__text` / `__cta` / `__time` / `__dismiss` | Inre delar |
| `.jp-notice-group` / `--info` | Grupprubrik (KRÄVER ÅTGÄRD / INFORMATION) |
| `.jp-notice-group__title` / `__count` | |
| `.jp-summary` | Grid container |
| `.jp-summary__group` / `__group__title` | Kolumner |
| `.jp-summary__row` / `--btn` / `--highlight` | Rader |
| `.jp-summary__row__label` / `__hint` / `__leader` / `__value` / `__chev` | Inre delar |

Exakta CSS-värden finns redan skrivna i `jobbpilot-v3.css` — kopiera dem rakt av eller mappa till Tailwind-utilities. Avvik inte från storlekarna utan att fråga.

---

## 6. Interaktioner

| Interaktion | Beteende |
|---|---|
| Klick på notis-CTA | Navigera (Next.js `Link`) till relevant route (`/jobb`, `/ansokningar`, `/sokningar`) |
| Klick på dismiss-X | Markera som läst — försvinner från sidan. **Server-action** rekommenderas (`markNotificationRead(id)`); om endpoint saknas, använd `useOptimistic` + localStorage som fallback. |
| Klick på "Markera alla som lästa" | Bulk-action — alla notiser går till läst |
| Klick på klickbar Summary-rad | Navigera till relevant route |
| Hover på notis-rad | Border-color skiftar till navy-700 (samma som job-row hover) |
| Empty state | När alla notiser dismissade visas `jp-empty`-block |

**Tillgänglighet:**
- Notice-rad är en `<li>` (inte knapp) — CTA-knappen och dismiss-knappen är fokuserbara
- Dismiss-knappen har `aria-label="Markera som läst"` + `title="Markera som läst"`
- Notice-grupp använder visuell `<div>` inte heading — den semantiska sektionen är "Notiser" som helhet
- SummaryRow renderas som `<button>` när klickbar, `<div>` annars

---

## 7. Route & navigation

- **URL:** `/oversikt` (svensk-route som resten av appen)
- **Default efter login:** redirect från `/login` och `/` till `/oversikt`
- **Header-nav:** Lägg `Översikt` som **första** nav-item, före Jobb
- **Brand-länk:** Klick på `JobbPilot`-logon → `/oversikt` (inte landing — landing är publik)
- **Skydd:** RSC kontrollerar `getServerSession()`; redirect till `/login` om obrukad. Samma mönster som övriga `(app)`-routes.

---

## 8. Vad CC SKA göra

1. **Läsa HANDOVER-v3.md** + detta dokument + öppna prototypen `JobbPilot v3.html` och navigera till Översikt
2. **Studera** `src-v3/oversikt.jsx` (komponentstruktur) och `jobbpilot-v3.css` (klasser under `/* Översikt — civic dossier */`)
3. **Skapa route** `web/jobbpilot-web/src/app/(app)/oversikt/page.tsx` som server-component:
   - Skydd via `getServerSession()`
   - Parallella requests via `Promise.all` mot riktig BE där den finns (§3)
   - Importera mock-data från `lib/oversikt/mock-data.ts` för resten
   - Beräkna `counts`-objektet från `getPipeline()` (samma logik som prototypen, men nu med riktig pipeline-data)
4. **Implementera komponenter** under `web/jobbpilot-web/src/components/oversikt/`:
   - `oversikt-page.tsx` (server) — orkestrerar layouten
   - `today-card.tsx` (server om inga interaktioner; annars client)
   - `notice-list.tsx` (client — dismiss behöver state)
   - `notice-row.tsx`
   - `summary.tsx` (server — ren render)
   - `summary-row.tsx`
5. **Implementera dismiss-knappar** via server-action när `markNotificationRead` finns; om inte — `useOptimistic` + localStorage med TODO-kommentar
6. **Uppdatera nav** i `app-shell.tsx` så `Översikt` är första nav-item; brand-länk pekar på `/oversikt`
7. **Lägg login-redirect** så `/oversikt` blir default efter inloggning
8. **Lägg in i DESIGN.md** — referera detta dokument + lägg en kort §X.Y "Översiktssidan" som speglar §2 ovan
9. **ADR** för: Översikt som default-route efter login (supersedes ev. tidigare beslut om `/jobb` som default)
10. **Bekräfta med Klas innan commit.** Plan-design först. STOPP-disciplin gäller.

---

## 9. Vad CC INTE ska göra

- **Inte** lägga in KPI-kort med stora siffror centrerade och ikoner i hörnen. Sammanfattning är en ledger, inte en dashboard.
- **Inte** introducera gradient-bg, drop-shadows på radkort, eller stora avrundade hörn
- **Inte** lägga in "snabb-CTA"-knappar längst ner (Sök jobb / Mina ansökningar / CV) — de finns redan i headern
- **Inte** lägga in dolda "AI-insikter" eller "Föreslagna åtgärder"-sektioner
- **Inte** byta ut den dotted leader-stilen i Sammanfattning mot border-bottom solid eller hairline
- **Inte** lägga svenska *och* engelska blandat — sidan är på svenska (i18n kommer senare)
- **Inte** stub:a en endpoint som inte finns. Använd centralized mock-modul och kommentera tydligt
- **Inte** lägga en "Snooze"-funktion på notiser. Klick på X = markera läst, klart.
- **Inte** lägga "Ärendenr" eller liknande dekorativ fake-civic-text. Vi har inget ärende att referera till.

---

## 10. Acceptanskriterier

Refaktorn är klar när:

- [ ] Route `/oversikt` finns och kräver auth
- [ ] Default-redirect efter login går till `/oversikt`
- [ ] Header har `Översikt` som första nav-item
- [ ] Title block visar inloggad användarens namn/e-post
- [ ] "I dag"-kortet renderar mock-events + Google-not-synked-fot
- [ ] Notiser-sektion delas i "Kräver åtgärd" / "Information" med count
- [ ] Notiser där det finns BE (Erbjudande/Uppföljning/Intervju) drivs av `getPipeline()`-data; övriga från mock
- [ ] Dismiss-X markerar som läst (server-action eller localStorage-fallback)
- [ ] "Markera alla som lästa" bulk-action fungerar
- [ ] Empty state syns när alla notiser är lästa
- [ ] Sammanfattning visar tre kolumner med dotted leaders och mono-siffror
- [ ] Klickbara summary-rader navigerar; ej klickbara är inerta
- [ ] Värdekolumnen alignar — chevron-slot reserveras alltid samma bredd
- [ ] Pixel-nära `01-…05-*.png` i både light och dark mode
- [ ] Mock-data ligger isolerat i `lib/oversikt/mock-data.ts` med TODO-kommentarer per fält
- [ ] DESIGN.md uppdaterad; ADR skapad för default-route-bytet

---

## 11. Mock-data i prototypen (referens)

Här är **exakta värden** som visas i prototypen — använd som standard tills riktig BE finns:

```ts
// I dag (lördag 23 maj 2026)
const todaysEvents = [
  { id: "ev-1", time: "10:30", title: "Telefonscreening — Klarna",
    where: "Rebecca Lind, rekryterare", source: "jobbpilot" },
  { id: "ev-2", time: "14:00", title: "Förbered intervju med Folksam IT",
    where: "30 min", source: "jobbpilot" },
];

// Notiser — Kräver åtgärd
const noticesAction = [
  { label: "Erbjudande", kind: "success",
    text: "Bonnier News — verksamhetsutvecklare. Erbjudande väntar svar senast 27 maj.",
    cta: "Granska erbjudande", time: "3 dagar sedan" },
  { label: "Uppföljning", kind: "warning",
    text: "Du har 2 ansökningar som inte fått svar på över 14 dagar. Överväg att höra av dig.",
    cta: "Visa ansökningar", time: "i dag · 07:00" },
  { label: "Deadline", kind: "warning",
    text: "2 sparade annonser har sista ansökningsdag denna vecka (25 maj, 27 maj).",
    cta: "Visa sparade", time: "denna vecka" },
];

// Notiser — Information
const noticesInfo = [
  { label: "Matchning", kind: "info",
    text: "Det finns 143 nya annonser som matchar din profil sedan i tisdags — de flesta inom Mjukvaru- och systemutvecklare.",
    cta: "Visa annonser", time: "i dag · 08:12" },
  { label: "Intervju", kind: "brand",
    text: "Folksam IT har bekräftat intervjutid tisdag 26 maj 14:00, digitalt möte.",
    cta: "Öppna ärende", time: "i går" },
  { label: "Sparad sökning", kind: "info",
    text: "Remote / Distansjobb har 4 nya träffar sedan din senaste körning.",
    cta: "Kör sökning", time: "i går" },
];

// Sammanfattning — mock-värden där BE saknas
const summaryMock = {
  matchCountToday: 28,
  activeJobsTotal: 45580,
  lastSearchName: "Backend Sthlm",
  personalLettersCount: 4,
  searchStartDate: "2026-04-06", // 46 dagar sedan
};
```

---

**Klas Olsson · produktägare · 2026-05-23**
