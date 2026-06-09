# HANDOVER-v4.md — Page-hero på inre sidor + brand-empty-states

> Handoff från designprototyp **JobbPilot v4.html** till Claude Code, för
> implementation i `web/jobbpilot-web` (Next.js 16, Tailwind v4, civic-utility
> tokens enligt `DESIGN.md`).

---

## 0. Problem som löses

Inloggade sidor är inkonsekventa idag:

| Route          | Idag                                                  | Problem |
|----------------|-------------------------------------------------------|---------|
| `/`            | Navy hero-band (LandingHeroSection)                   | OK |
| `/jobb`        | Navy hero-band (`.jp-hero`)                           | OK |
| `/oversikt`    | Helt vit, `.jp-page__title-block` med ljus titel      | Tappar varumärkesförankring |
| `/ansokningar` | Helt vit, samma mönster                               | Tom pipeline ser ostylad ut |
| `/cv`          | Helt vit, samma mönster                               | Tom CV-lista ser ostylad ut |

**Mål:** ge varje inre sida samma DNA som `/jobb` utan att överanvända mörkblått, och göra tomma listor till en avsiktlig välkomst-zon i stället för en streckad ruta.

**Designsamtal (sammanfattning, godkänt av Klas):**
- En signaturfärg genom hela appen (myndighetsblå). Inga gröna/lila per-sidors-färger — färg används för status, inte för navigation.
- Bandet ska vara *kompakt* — inte ett fullskaligt hero.
- Tomma sidor (CV, Mina ansökningar) får ett mörkblått välkomst-kort som ersätts av riktiga rader när data finns.

Allt enligt `DESIGN.md` §1 (civic-utility, GOV.UK/Digg/Stripe), §3 (myndighetsblå primärfärg, slate-neutralskala), §6 (Card/Button utan glow/skugga, radius 4px).

---

## 1. Två nya patterns att införa

### 1.1 `.jp-pagehero` — kompakt navy-band för inre sidor

Samma färgton som `.jp-hero` på `/jobb`, men **mindre höjd och titel** så det kan användas överallt utan att ta över sidan.

```
┌────────────────────────────────────────────────────────────┐
│  NAVY-BAND  (samma --jp-hero-bg som /jobb-hero)            │
│                                                            │
│  KICKER (mono caps, valfri)                                │
│  H1 sidans titel                                           │
│  Lede (max 60ch, vit-mjuk text)              [chip] [CTA]  │
└────────────────────────────────────────────────────────────┘
```

**Anatomi (BEM):**
- `.jp-pagehero` — yttersta `<section>`, full-bredd, navy bg
- `.jp-pagehero__inner` — `max-width: 1200px`, padding `32px 32px 36px`, flex row
- `.jp-pagehero__main` — vänster sida (kicker + titel + lede)
- `.jp-pagehero__kicker` — mono caps, 11.5px, `color: var(--jp-hero-ink-soft)`
- `.jp-pagehero__title` — H1, 34px / 700 / `-0.022em` tracking
- `.jp-pagehero__lede` — 16px, soft, `max-width: 60ch`
- `.jp-pagehero__aside` — höger sida (chips + primär-CTA)
- `.jp-pagehero-chip` — mini-stat (label + mono-värde), genomskinlig vit på navy
- Inom `.jp-pagehero` får `.jp-btn--primary` vit bakgrund + navy text så CTAn syns mot navy. `.jp-btn--ghost` får genomskinlig ljus border.

**Civic-utility-regler den uppfyller:**
- Ingen skugga, ingen gradient, ingen rundning över 4px
- Samma `--jp-hero-bg` som befintlig `.jp-hero` — ingen ny färg
- Mono-skala för chip-labels (DESIGN.md §4: 11.5px mono caps, letter-spacing 0.08em)
- Aldrig informationsbärande text < 14px

**Vilka sidor får den (bekräftat scope):**
- `/oversikt` — kicker "Inloggad som {namn}" + H1 "Översikt" + lede, aside = befintliga `TodayCard`-widgeten (vit kort med dagens datum + händelser) inuti navy bandet
- `/ansokningar` — H1 + lede, aside = `+ Ny ansökan`-knapp
- `/cv` — H1 + lede, aside = `+ Nytt CV`-knapp

**Vilka sidor får den INTE:**
- `/` (Landing) — har redan full hero
- `/jobb` — har redan full `.jp-hero` med sökruta
- Detaljsidor (`/ansokningar/[id]`, `/cv/[id]`) — använder breadcrumb + `.jp-page__title-block` som idag (titel är en ansökans-titel, inte en sektion)
- `/installningar`, `/sokningar`, `/sparade` — behåller dagens layout tills vidare (kan läggas till senare)

### 1.2 `.jp-empty--brand` — navy-tonad empty-state

Modifier på den befintliga `.jp-empty`. Används när hela sidan annars vore vit (CV-listan tom, pipeline tom).

```
┌────────────────────────────────────────────────────────────┐
│  NAVY-PANEL                                                │
│                                                            │
│         KICKER (valfri)                                    │
│         Rubrik i vitt (20px / 700)                         │
│         Beskrivande text i mjuk-vit (max 52ch)             │
│                                                            │
│         [ Primary CTA ]   [ Ghost CTA ]                    │
└────────────────────────────────────────────────────────────┘
```

**Anatomi:**
- `.jp-empty` + `.jp-empty--brand` på samma element
- Inuti: `.jp-empty__kicker` (valfri), `.jp-empty__title`, `.jp-empty__body`, `.jp-empty__actions`
- Bakgrund: flat `var(--jp-hero-bg)`, **ingen gradient, ingen skugga**, 1px border samma navy (för enhetlig kant)
- Primary-knapp blir vit på navy; ghost blir genomskinlig med 30% vit border

**Visas när:**
- `/cv` när `items.length === 0`
- `/ansokningar` när hela pipelinen är 0 (alla statusar tomma, inte bara en flik)
- INTE för "ingen träff i denna flik"-tillstånd — då räcker den befintliga ljusa `.jp-empty`

### 1.3 Vad som INTE ändras

- `.jp-hero` (jobb-sidan) — orörd
- `.jp-page__title-block`, `.jp-page__title`, `.jp-page__lede` — orörda, används fortfarande på detaljsidor och övriga inre sidor
- `.jp-empty` (ljus variant) — orörd
- Mörkt tema: navy-bandet behåller samma navy i dark mode (det är redan så `--jp-hero-bg` är definierad — fast färg oavsett tema). Brand empty-state likadant.

---

## 2. Filer som ändras

Endast 4 filer behöver röras. Ingen ny dependency.

### 2.1 `web/jobbpilot-web/src/app/globals.css`

**Lägg till två nya block.** Placera dem i samma sektion som befintliga `.jp-hero`-regler (sök efter `/* ── Hero (navy zon på /jobb) ───` i globals.css). Använd befintliga tokens (`--jp-hero-bg`, `--jp-hero-ink`, `--jp-hero-ink-soft`, `--jp-r-md`, `--jp-font-mono`).

Källan ligger i prototypen — kopiera 1:1 från:
- `jobbpilot-v4.css` rader **194–290** (`.jp-pagehero` + chips + knapp-overrides)
- `jobbpilot-v4.css` rader **1567–1634** (`.jp-empty--brand` + inre element + knapp-overrides)

Block 1 (page-hero):

```css
/* ── v4: kompakt navy-band för inre sidor ───────── */
.jp-pagehero { background: var(--jp-hero-bg); color: var(--jp-hero-ink); position: relative; }
.jp-pagehero__inner {
  max-width: 1200px; margin-inline: auto;
  padding: 32px 32px 36px;
  display: flex; align-items: flex-start; justify-content: space-between;
  gap: 32px; flex-wrap: wrap;
}
@media (max-width: 720px) {
  .jp-pagehero__inner { padding: 24px 20px 28px; }
}
.jp-pagehero__main { flex: 1 1 320px; min-width: 0; }
.jp-pagehero__kicker {
  font-family: var(--jp-font-mono);
  font-size: 11.5px; font-weight: 600;
  letter-spacing: 0.10em; text-transform: uppercase;
  color: var(--jp-hero-ink-soft); margin: 0 0 8px;
}
.jp-pagehero__title {
  font-size: 34px; font-weight: 700;
  letter-spacing: -0.022em; line-height: 1.1;
  margin: 0; color: var(--jp-hero-ink);
}
.jp-pagehero__lede {
  margin: 8px 0 0; font-size: 16px;
  color: var(--jp-hero-ink-soft); max-width: 60ch;
}
.jp-pagehero__aside {
  flex: 0 0 auto; display: flex; align-items: center;
  gap: 12px; flex-wrap: wrap;
}
.jp-pagehero-chip {
  display: inline-flex; align-items: baseline; gap: 8px;
  padding: 10px 14px;
  border-radius: var(--jp-r-md);
  background: rgba(255,255,255,0.06);
  border: 1px solid rgba(255,255,255,0.14);
  color: var(--jp-hero-ink);
}
.jp-pagehero-chip__value {
  font-family: var(--jp-font-mono); font-weight: 700;
  font-size: 18px; letter-spacing: -0.01em;
  color: var(--jp-hero-ink);
}
.jp-pagehero-chip__label {
  font-family: var(--jp-font-mono); font-size: 11px;
  letter-spacing: 0.08em; text-transform: uppercase;
  color: var(--jp-hero-ink-soft);
}
.jp-pagehero .jp-btn {
  white-space: nowrap;
}
.jp-pagehero .jp-btn--primary {
  background: #fff; color: var(--jp-hero-bg); border-color: #fff;
}
.jp-pagehero .jp-btn--primary:hover {
  background: #EAF1FA; color: #08213F; border-color: #EAF1FA;
}
.jp-pagehero .jp-btn--ghost {
  color: var(--jp-hero-ink); background: transparent;
  border-color: rgba(255,255,255,0.24);
}
.jp-pagehero .jp-btn--ghost:hover {
  background: rgba(255,255,255,0.08); color: #fff;
}
```

Block 2 (brand empty-state) — placeras direkt efter befintliga `.jp-empty`-regler:

```css
/* ── v4: brand-tonad empty-state (CV / pipeline tomma) */
.jp-empty--brand {
  background: var(--jp-hero-bg);
  border: 1px solid var(--jp-hero-bg);
  color: var(--jp-hero-ink-soft);
  padding: 56px 32px;
  display: flex; flex-direction: column; align-items: center;
  gap: 14px; text-align: center;
}
.jp-empty--brand .jp-empty__kicker {
  font-family: var(--jp-font-mono);
  font-size: 11.5px; font-weight: 600;
  letter-spacing: 0.10em; text-transform: uppercase;
  color: var(--jp-hero-ink-soft); margin: 0;
}
.jp-empty--brand .jp-empty__title {
  color: var(--jp-hero-ink);
  font-size: 20px; font-weight: 700;
  letter-spacing: -0.015em; margin: 0;
  text-wrap: balance;
}
.jp-empty--brand .jp-empty__body {
  color: var(--jp-hero-ink-soft);
  max-width: 52ch; margin: 0;
  font-size: 15px; line-height: 1.5;
}
.jp-empty--brand .jp-empty__actions {
  display: flex; gap: 10px; margin-top: 6px;
  flex-wrap: wrap; justify-content: center;
}
.jp-empty--brand .jp-btn--primary {
  background: #fff; color: var(--jp-hero-bg); border-color: #fff;
}
.jp-empty--brand .jp-btn--primary:hover {
  background: #EAF1FA; color: #08213F; border-color: #EAF1FA;
}
.jp-empty--brand .jp-btn--ghost {
  color: var(--jp-hero-ink); background: transparent;
  border-color: rgba(255,255,255,0.30);
}
.jp-empty--brand .jp-btn--ghost:hover {
  background: rgba(255,255,255,0.08); color: #fff;
  border-color: rgba(255,255,255,0.45);
}
```

### 2.2 `web/jobbpilot-web/src/components/oversikt/oversikt-page.tsx`

**Före:** title + lede + TodayCard sitter i `.jp-page__title-block` med `.jp-oversikt__head` som flex row.

**Efter:** title + lede flyttar upp i ett `.jp-pagehero`-band (edge-to-edge, utanför `.jp-container`). TodayCard flyttar ner som ett innehållskort över Notiser-sektionen.

Strukturen i komponenten blir:

```tsx
return (
  <div>
    <section className="jp-pagehero">
      <div className="jp-pagehero__inner">
        <div className="jp-pagehero__main">
          <div className="jp-pagehero__kicker">Inloggad som {kickerName}</div>
          <h1 className="jp-pagehero__title">Översikt</h1>
          <p className="jp-pagehero__lede">
            Senaste händelser och status för dina ansökningar.
          </p>
        </div>
        <div className="jp-pagehero__aside">
          {/* TodayCard (vit kort) ligger INNE i det navy bandet */}
          <TodayCard
            today={today}
            events={OVERSIKT_MOCK.todaysEvents}
            googleSynced={OVERSIKT_MOCK.googleSynced}
          />
        </div>
      </div>
    </section>

    <div className="jp-container jp-page">
      <NoticeList ... />
      <section className="jp-section">
        <Summary ... />
      </section>
    </div>
  </div>
);
```

**Notera:**
- Den befintliga `.jp-page__title-block` försvinner helt från Översikt.
- `TodayCard` flyttar från title-block-flex till `.jp-pagehero__aside` — samma komponent, ny container. Korten behåller sin vita styling och syns som ett vitt kort mot navy.
- TodayCard's befintliga bredd (~320px i `.jp-oversikt__today`) funkar som den är. Inga ytterligare inline-styles nödvändiga.
- Aria/screen reader: H1 sitter visuellt i en `<section className="jp-pagehero">` men page-titeln är fortfarande den första H1 → ingen a11y-regression.

### 2.3 `web/jobbpilot-web/src/app/(app)/ansokningar/page.tsx`

**Före:** `.jp-page__title-block` med inline-flex för titel + lede vänster och "Ny ansökan" till höger.

**Efter:** flytta hela title-blocket till ett `.jp-pagehero`-band ovanför `.jp-container`. Behåll all befintlig logik (loader, error states, rate limit). Brand-empty-state visas **bara när hela pipelinen är 0** (`total === 0` — befintlig variabel).

```tsx
return (
  <>
    <section className="jp-pagehero">
      <div className="jp-pagehero__inner">
        <div className="jp-pagehero__main">
          <h1 className="jp-pagehero__title">Mina ansökningar</h1>
          <p className="jp-pagehero__lede">
            Pipeline över alla ansökningar. Klicka på en rad för detaljer.
          </p>
        </div>
        <div className="jp-pagehero__aside">
          <Link href="/ansokningar/ny" className="jp-btn jp-btn--primary">
            <Plus size={16} aria-hidden="true" /> Ny ansökan
          </Link>
        </div>
      </div>
    </section>

    <div className="jp-container jp-page">
      {total === 0 ? (
        <div className="jp-empty jp-empty--brand">
          <div className="jp-empty__kicker">Pipeline</div>
          <div className="jp-empty__title">Inga ansökningar ännu</div>
          <p className="jp-empty__body">
            Så fort du registrerar din första ansökan hamnar den här.
            Spåra status från utkast till svar utan att tappa en enda råd.
          </p>
          <div className="jp-empty__actions">
            <Link href="/ansokningar/ny" className="jp-btn jp-btn--primary">
              <Plus size={14} aria-hidden="true" /> Skapa första ansökan
            </Link>
            <Link href="/jobb" className="jp-btn jp-btn--ghost">
              <Search size={14} aria-hidden="true" /> Sök annonser först
            </Link>
          </div>
        </div>
      ) : (
        <ApplicationsPipeline groups={groups} rowSlots={rowSlots} />
      )}
    </div>
  </>
);
```

Felfall (rateLimited / error) behåller `.jp-page__title-block` — de visar ett fel, inte sidans titel-band. Lämna dem orörda.

### 2.4 `web/jobbpilot-web/src/app/(app)/cv/page.tsx`

Samma mönster. Title-blocket blir ett `.jp-pagehero`, tom-state får `.jp-empty--brand`.

```tsx
return (
  <>
    <section className="jp-pagehero">
      <div className="jp-pagehero__inner">
        <div className="jp-pagehero__main">
          <h1 className="jp-pagehero__title">CV</h1>
          <p className="jp-pagehero__lede">
            Hantera dina CV-varianter. AI-stöd hjälper dig anpassa innehållet
            per ansökan, men du behåller alltid kontrollen.
          </p>
        </div>
        <div className="jp-pagehero__aside">
          <Link href="/cv/ny" className="jp-btn jp-btn--primary">
            <Plus size={16} aria-hidden="true" /> Nytt CV
          </Link>
        </div>
      </div>
    </section>

    <div className="jp-container jp-page">
      {sorted.length === 0 ? (
        <div className="jp-empty jp-empty--brand">
          <div className="jp-empty__kicker">CV-varianter</div>
          <div className="jp-empty__title">Inga CV ännu</div>
          <p className="jp-empty__body">
            Skapa ditt första CV för att komma igång. Du kan ha flera varianter
            — t.ex. en för ledarskap och en för teknisk roll — och välja rätt
            CV per ansökan.
          </p>
          <div className="jp-empty__actions">
            <Link href="/cv/ny" className="jp-btn jp-btn--primary">
              <Plus size={14} aria-hidden="true" /> Skapa första CV
            </Link>
          </div>
        </div>
      ) : (
        <>
          <div className="jp-cvgrid">
            {sorted.map((resume) => (
              <ResumeCard key={resume.id} resume={resume} />
            ))}
          </div>
          <AnpassaCvBanner />
        </>
      )}
    </div>
  </>
);
```

Loader/fel-cases (rateLimited, notFound, forbidden, error) behåller dagens markup med `.jp-h1` + `.jp-lede` (de är fallbacks utan layout).

---

## 3. App-shell hänsyn (`(app)/layout.tsx`)

Snabbkoll: prototypen visar `.jp-pagehero` som edge-to-edge. För att det ska funka måste sidans wrapper INTE redan ha `.jp-container`-padding runt det.

Sidorna ovan returnerar nu **två element** (band + container) i en `<>`-fragment / yttre `<div>`. Layout-filen `(app)/layout.tsx` ska:
- INTE wrappa `children` i en `.jp-container` (skulle skapa horisontellt padding kring bandet)
- Innehållet i bandet sköter sin egen `max-width: 1200px` via `.jp-pagehero__inner`
- Innehållet under bandet sköter sin egen `max-width` via `.jp-container`

Verifiera att `(app)/layout.tsx` returnerar children oförändrade i `<main className="jp-content">` — om så är fallet behövs ingen ändring där. Om den wrappar något extra, ta bort det wrappet specifikt för dessa tre routes.

---

## 4. Tester

### 4.1 Visuellt

Jämför med prototypen (`JobbPilot v4.html`) på dessa tre sidor:
- `/oversikt` — band ska matcha `/jobb`-hero i färg och höjd (~120-150px), chips synas till höger, TodayCard placerad ovanför Notiser
- `/ansokningar` — med data: band + statusbar + lista. Utan data (`total === 0`): band + brand-empty
- `/cv` — med data: band + grid + AnpassaCvBanner. Utan data: band + brand-empty

Dark mode: navy band ska fortsätta visa samma navy (det är `--jp-hero-bg` som är fast). Resten av sidan följer dark-tokens.

### 4.2 a11y (DESIGN.md §9 — golv WCAG 2.1 AA)

- H1 i `.jp-pagehero__title` är fortsatt sidans enda H1 — verifiera med axe DevTools
- Vit text på `--jp-hero-bg` (#0A2647) = ~14.5:1 kontrast — OK
- Vit-soft text (`--jp-hero-ink-soft` = #BBCCE5) på navy = ~8.4:1 — OK för body/lede
- Knapparna i bandet (vit bakgrund, navy text) = invertering av primärknappens kontrast — också OK
- Fokus-ringar måste fortfarande synas mot navy. Verifiera att befintlig `:focus-visible` outline (förmodligen amber/blå) syns mot mörk bakgrund. Annars: lägg `.jp-pagehero :focus-visible { outline-color: #fff }` (egen judgement).

### 4.3 Lighthouse

Lighthouse a11y-score ≥ 95 enligt DESIGN.md §9. Kör innan merge.

### 4.4 Playwright

Befintliga test för `/oversikt`, `/cv`, `/ansokningar` kommer förmodligen att leta efter `.jp-page__title`-selektor. Uppdatera tester till `.jp-pagehero__title` på dessa tre routes. Lämna tester för detaljsidor och `/installningar` orörda.

---

## 5. Out of scope (gör INTE i denna PR)

Hålls medvetet utanför för att hålla diffen tight:

- `/sokningar`, `/sparade`, `/installningar` — behåller dagens `.jp-page__title-block`. Kan migreras i en uppföljnings-PR om det visar sig vara värt det.
- Detaljsidor (`/ansokningar/[id]`, `/cv/[id]`) — behåller breadcrumb + `.jp-page__title-block`.
- Admin-sidor (`/admin/granskning`) — egen visuell logik, orörd.
- Footer / auth-sidor — orörda.
- Nya ikoner — vi använder bara `Plus` och `Search` från Lucide, båda finns redan.

---

## 6. Reference-filer i denna designprojekt

Allt CSS och alla JSX-snippets ligger live i prototypen:

- `JobbPilot v4.html` — kör prototypen i webbläsare
- `jobbpilot-v4.css` — CSS-källan (sök efter `v4 — kompakt navy-band` och `v4 — Brand-tonad empty-state`)
- `src-v4/oversikt.jsx` — Översikt-referens (line ~213+ i `OversiktPage`)
- `src-v4/pages.jsx` — AnsokningarPage (~line 36) och CvPage (~line 468)
- Tweaks-panelen i prototypen har en toggle "Visa tom CV-lista och tom pipeline" som tvingar empty-state — använd den för att jämföra båda lägen.

Frågor → ping Klas. Tack!
