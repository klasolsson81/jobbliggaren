# Security-auditor — F-Pre Punkt 5b "Gäst-mode utbyggnad" (commits 1–3)

**Datum:** 2026-05-24
**Agent:** security-auditor
**HEAD vid rond:** `a9b4941` (3 pending push: `65b6111`, `08ff285`, `a9b4941`)
**Föregående rond:** `docs/reviews/2026-05-24-fpre-punkt5-security-auditor.md` (APPROVED)
**CTO-dom-referens:** `docs/reviews/2026-05-24-fpre-punkt5b-cto.md`
**Auktoritet:** GDPR Art. 5, 6, 25, 32 + CLAUDE.md §2, §5, §9.6 + ADR 0017 + ADR 0053

---

## TL;DR (för Klas)

**Status: ✓ APPROVED — inga critical, inga blockers, inga high. 2 Minor + 5 Praise.**

Utbyggnaden av gäst-tree:t (parallel-routes `@modal` + intercepting routes
`(.)ansokningar/[id]` + `(.)jobb/[id]` + fullsidor + mock-adapters) håller
samma cross-pollination-disciplin som Punkt 5. Alla sju verifieringspunkter
i Klas-uppdraget är gröna:

1. **Cross-pollination `(app)/*` ↔ `(guest)/*`:** noll `getServerSession()`,
   noll `fetch(`, noll `process.env`, noll Server Actions i hela
   `(guest)/*`-tree:t eller `lib/guest/`-modulen (utöver pre-godkänd
   `guest-mode-actions.ts` för welcome-cookien från Punkt 5).
2. **`<JobAdDetail>` återanvändning:** intercepting-route passar EJ
   `initialSaved`/`initialApplied` → `userActions`-narrowing i
   `job-ad-detail.tsx:64-67` ger `null` → `SaveJobAdToggle` +
   `HarAnsoktButton` renderas **inte** (verifierat mot rad 132–137).
   Det `userActions?.applied`-villkorade meddelandet (rad 149–168) som länkar
   till `/ansokningar` renderas heller inte (också gatat på `userActions`).
3. **`<GuestApplicationDetail>` mutationsfrihet:** noll `<form>`, noll
   `<button onClick>`, noll Server Action-anrop, noll fetch. Ren
   presentational med `<dl>`-metarow + CTA-paragraph mot `/vantelista`.
4. **`(guest)/gast/jobb/page.tsx` sök-attrapp:** `<input disabled>` +
   `<button disabled aria-disabled="true">` utan `<form>`-wrapper, utan
   `action=`, utan `onSubmit`, utan `onChange`. Genuint inert. Inga BE-anrop.
5. **`toJobAdDto`:** pure function utan side effects, ingen env-läsning,
   ingen fetch. Stabil referensdatum-konstant (`"2026-05-24T20:00:00Z"`)
   i `isWithinDays` förebygger snapshot-drift.
6. **PII i mockdata:** `GUEST_JOB_ADS` innehåller endast publika
   företagsnamn (Klarna, Folksam IT, Bonnier News, Skatteverket, ICA
   Gruppen, Region Stockholm, Trafikverket, Spotify) + generiska
   rolltitlar + Platsbankens publika URL-mönster. Noll personnamn,
   noll email, noll telefon, noll organisationsnummer, noll personnummer.
7. **`@modal`-slot:** `modal: React.ReactNode` är opaque ReactNode — kan
   per definition inte bära session-data eller PII från layout-handlern;
   den fylls av Next runtime från intercepting-route-renderingen som själv
   är auth-fri.

**Inga ADR-amendments krävs. Inga DPIA-implikationer. Inga nya sub-processors.
Inga in-block-fix-krav. Inga TDs ska lyftas.**

---

## Granskningsdetalj per fokuspunkt

### Fokus 1 — Cross-pollination (utbyggnad)

**Granskat:**

- `(guest)/gast/@modal/(.)ansokningar/[id]/page.tsx` — endast
  `findGuestApplication(id)` från lokal `mock-data.ts`. Noll
  `getServerSession()`, noll BE-anrop.
- `(guest)/gast/@modal/(.)jobb/[id]/page.tsx` — endast `findGuestJobAd(id)` +
  `toJobAdDto(mock)`. Noll BE-anrop.
- `(guest)/gast/@modal/[...catchAll]/page.tsx` — returnerar `null`. Inert.
- `(guest)/gast/@modal/default.tsx` — returnerar `null`. Inert.
- `(guest)/gast/ansokningar/[id]/page.tsx` (fullsida) — endast
  `findGuestApplication(id)`. Hard-nav-paritet utan auth-läckage.
- `(guest)/gast/jobb/[id]/page.tsx` (fullsida) — endast `findGuestJobAd(id)`
  + `toJobAdDto(mock)`.

**Grep-evidens** (`src/app/(guest)` + `src/lib/guest`):
- `getServerSession` → 2 träffar, båda i kommentarer som dokumenterar
  frånvaron (`guest-shell.tsx:10`, `layout.tsx:7`, `oversikt/page.tsx:6`).
- `fetch(` → 0 träffar.
- `process.env` → 0 träffar.
- `"use server"` → 1 träff, isolerad till `lib/guest/guest-mode-actions.ts`
  (welcome-cookien, godkänd i Punkt 5-ronden, oförändrad här).

**Middleware-verifiering:** `middleware.ts:6-15` `PROTECTED_PREFIXES` listar
inte `/gast` — gäst-tree exkluderas medvetet från cookie-presence-check.
Dock: utebliven cookie för `/gast/*` är **rätt** beslut (anonym yta), men
det innebär också att INGEN session-cookie sätts vid besök i `/gast/*`.
Kontroll bekräftad: `lib/guest/guest-mode-actions.ts` sätter endast
`__Host-jobbpilot_guest_welcomed` (välkomst-flagg, ej session).

**Verdikt:** ✓ Cross-pollination-skydd bevarat i utbyggnaden.

### Fokus 2 — `<JobAdDetail>` återanvändning utan muterande knappar

**Granskat:** `components/job-ads/job-ad-detail.tsx:55-170` mot
intercepting-route-anropet `<JobAdDetail jobAd={jobAd} headless />`
(rad 31 i `(.)jobb/[id]/page.tsx`).

**Narrowing-mekanism (rad 64–67):**

```typescript
const userActions =
  initialSaved !== undefined && initialApplied !== undefined
    ? { saved: initialSaved, applied: initialApplied }
    : null;
```

**Renderingsgrindar:**

- Rad 132: `{userActions && (<><SaveJobAdToggle.../><HarAnsoktButton.../></>)}`
  — båda muterande klient-islands skippas när `userActions === null`.
- Rad 149: `{userActions?.applied && (...)}` — `/ansokningar`-länk
  ("Mina ansökningar") skippas också. Detta är viktigt eftersom länken
  annars skulle leda gäst till en auth-gated route (middleware-block →
  `/logga-in`-redirect — UX-brott men inte säkerhetsbrott).
- Rad 138–147: Extern `jobAd.url`-länk (Platsbanken) **renderas** för gäst.
  Verifierat säker: `target="_blank" rel="noopener noreferrer"` —
  ingen tabnabbing-vektor, ingen window.opener-läckage.

**Fullsidan** (`(guest)/gast/jobb/[id]/page.tsx:45`) anropar
`<JobAdDetail jobAd={jobAd} />` utan `headless` — samma userActions=null
narrowing → samma skydd. Header med titel/företag renderas istället av
`<JobAdDetail>`s egen `!headless`-gren (rad 73–84), inte av `jp-pagehero`
(fullsidan har båda — medvetet design-val, ingen säkerhetsrisk).

**Verdikt:** ✓ Återanvändningsmönstret är säkert. `userActions`-narrowing
fungerar som ADR 0053-amendment 2026-05-19 föreskriver ("frånvaro, inte
disabled-knapp-teater").

### Fokus 3 — `<GuestApplicationDetail>` mutationsfrihet

**Granskat:** `components/guest/guest-application-detail.tsx` (62 rader,
hela filen).

- 0 `<form>`-element
- 0 `<button>`-element
- 0 `onClick` / `onSubmit` / `onChange` handlers
- 0 Server Action-importer (`"use server"` / action-prop)
- 0 fetch-anrop
- Endast: `<span>` pill + `<dl>`/`<dt>`/`<dd>` metarow + `<p>` CTA-text

**Klas-direktiv §F-efterlevnad:** ingen "Ny ansökan"-knapp, ingen
status-edit, inga anteckningar, inga uppföljningar — alla muterande
elements från live `<ApplicationDetail>` är frånvarande, inte disabled.

**Verdikt:** ✓ Mutationsfri presentational. Klas-direktiv §F uppfyllt.

### Fokus 4 — Sök-attrapp disabled-disciplin

**Granskat:** `components/guest/guest-jobb-page.tsx:22-55`.

- `<input id="guest-jobb-q" type="search" disabled ... />` — `disabled`
  HTML-attribut, inte bara CSS. Submit blockeras av webbläsare.
- `<button type="button" disabled aria-disabled="true" ...>` — `type="button"`
  (inte `submit`), `disabled` HTML-attribut. Dubbelt skydd.
- **INGEN `<form>`-wrapper** runt input + button. Det innebär att även
  Enter-tangenten i fältet inte triggar någon form-submit — det finns
  ingen form att submitta. Detta är medveten arkitektur.
- 0 `onChange` / `onSubmit` / `action=` handlers. Helt inert.

**Verdikt:** ✓ Genuin attrapp utan dolda submit-vektorer. Inga BE-anrop
möjliga.

### Fokus 5 — `mock-adapters.ts` purity

**Granskat:** `lib/guest/mock-adapters.ts` (36 rader, hela filen).

- `toJobAdDto(mock)`: deterministisk map från `GuestMockJobAd` → `JobAdDto`,
  hårdkodad `status: "Active"`. Inga side effects.
- `isWithinDays(iso, days)`: använder `Date.parse()` (pure) och
  hårdkodad referens-stämpel `"2026-05-24T20:00:00Z"` (inte `Date.now()`)
  — undviker både timing-attack-yta (irrelevant här) och snapshot-drift.
- 0 fetch, 0 env-läsning, 0 side effects.

**Notering (icke-fynd):** den hårdkodade referens-stämpeln "binder" datum
till 2026-05-24. Efter denna dag kommer alla mockannonser klassas som
`isNew: false`. Det är ett UX-skav, **inte ett säkerhetsfynd** — ingen
PII påverkas. Lyfts inte ens som Minor här (faller utanför scope).

**Verdikt:** ✓ Pure adapter. Säker.

### Fokus 6 — PII i mockdata

**Granskat:** `lib/guest/mock-data.ts:198-303` (`GUEST_JOB_ADS`) +
`lib/guest/mock-data.ts:74-138` (`APPLICATIONS`).

**Företagsnamn:** alla är publika juridiska personer från reella
arbetsgivare i Sverige (Klarna, Folksam IT, Skatteverket, Bonnier News,
ICA Gruppen, Region Stockholm, Trafikverket, Spotify). Företagsnamn är
inte PII per GDPR (avser fysiska personer).

**Rolltitlar:** generiska (Backend-utvecklare, Systemutvecklare .NET,
Lösningsarkitekt, Verksamhetsutvecklare, Fullstack-utvecklare). Ingen
identifierar någon individ.

**URL:er:** `https://arbetsformedlingen.se/platsbanken/annonser/exempel-gj-N`
— publika placeholder-paths som inte resolvar till riktiga annonser
(prefix `exempel-`). Ingen rekryteringskontakt-PII läcker.

**Beskrivningstext:** generisk text om teknik, krav och förmåner. Inga
personnamn, inga email-adresser, inga telefonnummer, inga
organisationsnummer (org.nr för svenska företag är publik info men ej
inkluderat ändå), inga personnummer.

**`APPLICATIONS`:** inga personnamn (kandidaten), inga email-adresser,
inga referenser. Endast (företag, roll, status, källa, "updatedAtLabel").

**Verdikt:** ✓ Noll PII i mockdata. GDPR Art. 4(1)-compliant (ingenting
relaterar till identifierad eller identifierbar fysisk person).

### Fokus 7 — Layout `modal`-slot

**Granskat:** `(guest)/gast/layout.tsx:14-41`.

- `modal: React.ReactNode` är opaque-typad prop. Next.js parallel-routes
  fyller den från `@modal/.../page.tsx`-rendering, inte från layoutens
  egen data-fetch.
- Layouten har två data-läsningar: `hasSeenGuestWelcome()` (cookie-read,
  inte PII) och `getServerSession()` — **noll** av dessa, layouten är
  auth-fri. `hasSeenGuestWelcome()` används bara för `showWelcome`-prop
  på `<GuestWelcomeModal>` (server-bestämd så hydration-flash undviks).
- `modal`-slot:ens innehåll genereras av intercepting-route-handlern
  som själv är auth-fri (verifierat under Fokus 1).

**Verdikt:** ✓ Slot-prop kan inte bära PII genom layout-passagen.

---

## Fynd

### Minor

**m-1: Fullsidan `(guest)/gast/jobb/[id]/page.tsx` har dubbel
titel-rendering vid hard-nav.**

Fullsidan renderar både `<section class="jp-pagehero">` med
`<h1>{jobAd.title}</h1>` (rad 30) OCH `<JobAdDetail jobAd={jobAd} />`
utan `headless`-flagga (rad 45) — `JobAdDetail`s `!headless`-gren
(rad 73–84) renderar då en till `<h1 class="jp-modal__title">`-likvärdig
header. Inte säkerhetsfynd (ingen PII-läckage, ingen mutationsvektor),
endast a11y-/SEO-osammanhängande heading-struktur. Faller utanför
security-scope men noteras för design-reviewer-överlämning.

**Åtgärd:** lämnas till design-reviewer för STEG-stängningsrunda. Inte
in-block-fix-krav.

**m-2: Disabled-input `aria-disabled` saknas på `<input>`.**

`guest-jobb-page.tsx:30-36` har `<input disabled>` men inte
`aria-disabled="true"`. HTML `disabled` räcker för moderna skärmläsare
(NVDA/JAWS/VoiceOver respekterar attributet), men explicit `aria-disabled`
är belt-and-suspenders-pattern som JobbPilot använder på knappen (rad 41)
och borde användas konsekvent. Defense-in-depth-polish, inte WCAG-brott
(disabled HTML-attribut ÄR semantiskt korrekt).

**Åtgärd:** lämnas till design-reviewer. Inte säkerhetskritiskt.

### Praise

- **P-1:** `userActions`-narrowing i `<JobAdDetail>` återanvänds i två
  nya kontexter (gäst-modal + gäst-fullsida) utan att ändra `<JobAdDetail>`
  alls. ADR 0053-amendment-disciplinen ("frånvaro, inte disabled-knapp-
  teater") håller i utbyggnaden. CTO Beslut 6-implementation är
  säkerhetsmässigt vattentät.
- **P-2:** Mockdata är fortsatt noll-PII. Nya `GUEST_JOB_ADS` (8 annonser
  med beskrivningar) håller samma disciplin som ursprungliga
  `APPLICATIONS`/`RESUMES`. Företagsnamn är publika juridiska personer;
  ingen kontakt-PII införs.
- **P-3:** Sök-attrappen utan `<form>`-wrapper är arkitekturellt bättre
  än "form med disabled-button" — det finns ingen submit-vektor att
  obfuskera. Genuin inertness.
- **P-4:** `toJobAdDto`-adapter använder referens-stämpel istället för
  `Date.now()` — eliminerar både snapshot-drift och eventuell
  timing-uppmätbar kod-path-skillnad (irrelevant attack här men gott
  hantverk).
- **P-5:** `<GuestApplicationDetail>` som egen presentational-komponent
  (CTO Beslut 6) istället för att tvinga in adapter på live
  `<ApplicationDetail>` (som har mutationsformulär utan
  "passa undefined"-mönster) är rätt arkitekturval. Säkerhets-vinst:
  ingen risk att en framtida live-refaktor av `<ApplicationDetail>`
  råkar exponera en muterande button till gäst-vy.

---

## GDPR-bedömning

| Krav | Status | Kommentar |
|---|---|---|
| Lawful basis | N/A | Ingen PII behandlas i gäst-tree |
| Data minimization | ✓ | Mockdata = nödvändigt minimum för demo |
| Storage region | N/A | Ingen lagring; mockdata är compile-time-konstant |
| Encryption at rest | N/A | Ingen lagring |
| Encryption in transit | ✓ | TLS via Vercel-deploy |
| Soft delete | N/A | Ingen entitet |
| Audit log | N/A | Ingen PII-touch att logga |
| Retention | N/A | Inget att retain:a |
| Right to access | N/A | Ingen användardata |
| Right to deletion | N/A | Ingen användardata |
| Sub-processors | ✓ | Inga nya |
| DPIA-värdighet | ✗ Nej | Ingen high-risk processing — gäst är anonym |
| Konsent-UI | N/A | Ingen processing → ingen consent behövs |

**Verdikt:** GDPR-compliant. Privacy-by-design uppnådd genom
arkitektur-isolation (anonym tree, inga BE-anrop, inga cookies utöver
funktional welcome-flagg).

---

## Cross-pollination-summering

| Yta | Auth-gated? | Mekanism |
|---|---|---|
| `/oversikt`, `/ansokningar`, `/jobb`, `/cv`, `/installningar`, `/mig`, `/sokningar`, `/sparade` | ✓ Ja | middleware.ts:6-15 PROTECTED_PREFIXES + RSC `getServerSession()` |
| `/gast/oversikt`, `/gast/ansokningar`, `/gast/jobb`, `/gast/cv` | ✗ Nej (medvetet) | Inte i PROTECTED_PREFIXES; ingen `getServerSession()` i layout |
| `/gast/ansokningar/[id]` (full + modal) | ✗ Nej | Endast `findGuestApplication()` mot mockdata |
| `/gast/jobb/[id]` (full + modal) | ✗ Nej | Endast `findGuestJobAd()` + `toJobAdDto()` |

**Ingen route i `(guest)/*` läcker till `(app)/*`-resurser. Ingen route
i `(app)/*` läcker mockdata till auth-gated kontext.**

---

## Re-review-krav

Ingen re-review krävs. APPROVED som-är för push.

**Vid framtida utbyggnad av gäst-tree** (Punkt 5c+, eller om
`/gast/cv`-detaljvyer läggs till):

- Samma cross-pollination-checklista (Fokus 1–7) gäller
- Nya komponenter som återanvänder live-presentational måste verifieras
  mot `userActions`-narrowing-pattern (eller motsvarande "passa
  undefined → muterande element döljs")
- Ny mockdata: noll PII (inga personnamn, email, telefon, org.nr,
  personnummer)
- Inga `fetch(` eller `"use server"` i `(guest)/*` utöver
  `guest-mode-actions.ts` (welcome-cookie)

---

**Slut på rapport.** Klas: säker att pusha 3 commits (`65b6111`,
`08ff285`, `a9b4941`) ur säkerhets-/GDPR-perspektiv. m-1 och m-2 är
design-reviewer-scope, inte security-blockers.
