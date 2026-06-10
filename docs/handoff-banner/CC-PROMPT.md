# CC-prompt: Banner + grönt accentsystem (vald design "F4 Hybrid")

Kopiera allt under strecket rakt in till CC. Referensfiler ligger i `referens/`:
- `F4-banner-referens.html` — levande spec i ren HTML/CSS med alla tokens, light + dark (öppna i browser, toggla nere till höger)
- `F4-light.png` / `F4-dark.png` — målbilder

---

Vi återinför bannern på Jobb-sidan med egen JobbPilot-identitet — inte Platsbankens. Samtidigt byter appen accentfärg från blå till mörkgrön. Designbeslut är taget och utforskat i Claude Design (variant "F4 Hybrid" — inramad platta + asymmetrisk komposition). En komplett HTML/CSS-referens med exakta tokens bifogas (`F4-banner-referens.html`) — den är facit; titta i den innan du frågar.

## Beslut i korthet

1. **Bannern är tillbaka** — som en INRAMAD platta (inte full-bleed): marginal runt om, 6px rundning, mörkgrön diagonal gradient.
2. **Asymmetrisk komposition** — stor display-rubrik vänster, sök + åtgärder höger. Inte Platsbankens centrerade stapel.
3. **Accentbyte i HELA appen**: blå `#0B5CAD` → mörkgrön `#15603F` (light) / `#6EE7A8` (dark). En interaktionsfärg, konsekvent.
4. **Kontrollerna i bannern är neutrala** — vita knappar, ink-mörk Sök-knapp. Bannern bär färgen, kontrollerna gör det inte.
5. **Stats (aktiva annonser / nya idag) stannar i headern** — de är globala och ska INTE in i bannern.

## Tokens (lägg i token-systemet, hårdkoda inte)

```css
/* Hero-banner */
--jp-hero-from: #0B2A1E;
--jp-hero-mid:  #14503A;
--jp-hero-to:   #1E6B4C;
/* gradient: linear-gradient(118deg, from 0%, mid 60%, to 100%) */
--jp-hero-radius: 6px;

/* Accent (ersätter blå brand) */
--jp-accent:    #15603F;   /* light mode */
--jp-accent-50: #E9F2ED;   /* selektion/aktiv rad, ersätter brand-50 */
/* dark mode: --jp-accent: #6EE7A8; --jp-accent-50: #0E2A1E; */

/* Guld — signatur, sparsamt (logo-prick). Aldrig textfärg. */
--jp-gold: #E8C77B;
```

Status-färgerna (success `#059669`, warning `#D97706`, danger `#DC2626`) är OFÖRÄNDRADE — status är inte accent.

## Banner-blocket (Jobb-sidan)

- Wrapper: padding 24px topp, 32px sidor, mot vit canvas. Plattan: gradient ovan, radius 6px, padding 44px 48px 48px.
- Grid 2 kolumner (1fr 1fr, gap 48px, align-items end):
  - **Vänster:** H1 "Lediga jobb.<br>I lugn och ro." — vit, 44px, vikt 800, line-height 1.1, letter-spacing -0.025em. Lede under (14px margin): "Sök bland aktiva annonser från Platsbanken. Filtrera och jämför utan att tappa en enda annons." — `rgba(255,255,255,0.78)`, 16px, max-width 42ch.
  - **Höger (stack, gap 14px):** (1) "Senaste sökningar" + "Sparade annonser" högerställda — vita solida, border `#CBD5E1`, ink-text, 38px, radius 4px. (2) Sökrad: etikett `rgba(255,255,255,0.75)` 13px; vitt fält 52px; Sök-knapp `#0F172A` (ink — INTE grön), vit text + ikon. (3) Chips "Ort"/"Yrke": vita solida, 36px.
- Ingen logga/vattenmärke i bannern. Inga skuggor. Inga stats.
- Bygg som återanvändbar komponent (gradient + rubrik/lede som props) — samma system rullas ut på övriga sidor senare. Samma färg på alla sidor tills vidare.
- Mobil (≤768px): grid → en kolumn (rubrik först), padding ner till 28/24px, hit-targets 44px.

## Accentmigrering — inventera ALLT blått

Sök igenom kodbasen efter den gamla blå (`#0B5CAD`, `brand-600`, `brand-50`, `#60A5FA` m.fl.) och byt till accent-tokens. Checklista — inget får bli kvar i blått av misstag:

- [ ] Aktiv nav-flik (underline + textfärg) i header
- [ ] Länkar (jobbtitlar i träfflistan, inline-länkar, "se alla"-länkar)
- [ ] Primärknappar
- [ ] Selektion / aktiv rad (`brand-50`-bakgrund + vänsterkant)
- [ ] Fokus-ringar (se nedan)
- [ ] Match-bars ≥75 %
- [ ] Pills/badges med brand-färg
- [ ] Sidebar aktiv-markering (om sidebar-skalet används)
- [ ] Checkboxar, radios, toggles, progress
- [ ] Eventuella blå hover-states

**Undantag:** logotypens kompass-mark förblir blå med guldprick — den är varumärket och byter INTE färg.

## Fokus-regler

- **På ljus yta:** 2px grön ring (`--jp-accent`) + 2px offset.
- **Inne i bannern:** 2px VIT ring + 2px offset (grön syns inte mot grönt). Scopa t.ex. `.hero :focus-visible { outline-color: #fff; }`.
- **Dark mode:** ljusgrön ring (`#6EE7A8`) på mörk canvas; i bannern fortsatt vit.
- Orange/bärnsten används INTE för fokus (krockar med warning).

## Dark mode

- Banner-gradienten är OFÖRÄNDRAD i dark mode (den är redan mörk) — verifierad mot canvas `#020617`.
- Lägg 1px `--jp-border`-hairline runt/under plattan om avgränsningen mot canvas behövs.
- Accent i dark mode: `#6EE7A8` för länkar/aktiv nav (AA mot `#020617` och `#0F172A`).
- Se dark-värdena i referens-HTML:ens `html.dark`-block.

## Tillgänglighet

- Vit rubrik/lede mot gradienten klarar AA (mörkaste delen `#0B2A1E`, ljusaste `#1E6B4C`) — gör inte texten svagare än specat.
- Accentgrön `#15603F` på vitt: ~7:1, godkänd för text och UI.
- Fokus-ring alltid synlig (`:focus-visible`), hit-targets ≥36px desktop / 44px touch.

## Omfattning / icke-mål

- Detta steg: Jobb-sidans banner + den globala accentmigreringen (den kan inte delas upp — halvbytt accent ser trasig ut).
- Övriga sidors banners: senare, med samma komponent.
- Loggan: ses över separat — rör den inte nu.
- Notera: gradient + 6px-radien på plattan är MEDVETNA undantag från civic-utility-reglerna ("inga gradients", radieskala) — gäller ENBART hero-plattan. Dokumentera undantaget i designsystemet så design-reviewern inte revertar.

---

*Genererad från Claude Design — `Banner-utforskning v2.html`, artboard "F4 · HYBRID F1+F2". Referens: `referens/F4-banner-referens.html`.*
