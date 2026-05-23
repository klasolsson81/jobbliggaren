# ADR 0056 — Landing v3-shell + live-stats placeholder

**Datum:** 2026-05-20
**Status:** Accepted
**Kontext:** F6 Prompt 1 (Landing-refactor till v3-design). HANDOVER-v3.md §0 punkt 6+7, §6.4, §7.1 är veto-status över alla tidigare ADRs.
**Beslutsfattare:** Klas Olsson (produktägare); CC implementation
**Relaterad:** ADR 0052 (designsystem v3 — header/landing-tokens), ADR 0054 (header-meny ersätter sidebar — app-shell-kontext, ortogonal mot landing), ADR 0017 (frontend auth-pattern), ADR 0018 (cookie-/CSRF-strategi), HANDOVER-v3.md §0/§6.4/§7.1/§2.4, målbild `01-landing-light.png` + `02-landing-dark.png`

---

## Kontext

Landing-routen (`/`) i v2-implementationen hade fyra avvikelser från HANDOVER-v3:

1. Header innehöll både inloggningsknappar ("Logga in" / "Skapa konto"), `ThemeToggle` och en SV/EN-pill — HANDOVER §0 punkt 6+7 + §6.4 förbjuder allt detta i header.
2. En "Version 2 · Maj 2026"-mono-kicker fanns över h1 — HANDOVER §7.1 "Bort:" listar version-kicker som icke-leverans.
3. Footer hade en "Drift"-indikator (grön prick + statustext) — HANDOVER §7.1 "Bort:".
4. Ingen live-stats-block i header ("45 580 aktiva annonser · 312 nya idag") — HANDOVER §6.4 specificerar dessa som enda innehåll utöver brand.

Refaktorn omfattar dessutom att brand-länken till `/` ska vara ren markup (J-monogram + ordmärke) utan v2-tag-pill, och att auth-formuläret renderas i ett separat `AuthCard`-höger-kort i navy-hero (HANDOVER §7.1 punkt 2).

## Beslut

### Beslut 1 — Landing får egen `LandingTopbar`-shell, inte app-headern

Routen `/` (under `(marketing)`-route-grupp) renderar en egen RSC `<LandingTopbar />` som innehåller **endast** brand vänster + live-stats höger. Inga inloggningsknappar, ingen theme-toggle, ingen lang-toggle.

`(marketing)`-route-gruppen har ingen egen `layout.tsx` (verifierat 2026-05-20 — ingen fil finns), så landing ärver bara root `app/layout.tsx`. Ingen V3_NATIVE_ROUTES-opt-out-mekanik behövs eftersom app-headern bara aktiveras i `(app)`-gruppens layout.

### Beslut 2 — Theme/lang-togglarna placeras i landing-footer

`<LandingFooter />` (RSC-komposit) renderar länkrad + delade `<ThemeToggle />` (befintlig client-komponent) + ny `<LandingLangToggle />` (SV/EN, EN disabled tills `next-intl` messages/en.json finns).

ThemeToggle är samma komponent som app-skalet använder — ingen duplikat. LangToggle är ny eftersom v2 inte hade någon (header-versionen var hårdkodad SV-only).

### Beslut 3 — AuthCard på landing renderar delade `<LoginForm />` / `<RegisterForm />`

`<AuthCard />` (client-komponent) håller mode-state (`login`|`register`) via `useState`. Den importerar de existerande `<LoginForm />` och `<RegisterForm />`-komponenterna utan duplicering — samma validering, samma server-actions (`loginAction` / `registerAction`).

Mode-state lyfts till `<LandingHeroSection />` så hero-CTA "Skapa konto" kan flippa AuthCard till register-tab (matchar prototyp `setMode("register") + scrollTo(top)`). Inget URL-state — Klas pre-F6 Prompt 1 verbatim.

### Beslut 4 — Live-stats hårdkodas via `getLandingStats()`-helper tills datakontrakt klart

> **⚠ AMENDAD 2026-05-23 — se [Amendment 2026-05-23 — Live-stats Beslut 4 lyft: implementation byts, utbytespunkt bevarad](#amendment-2026-05-23--live-stats-beslut-4-lyft-implementation-byts-utbytespunkt-bevarad) nedan. Originaltexten nedan bevaras oförändrad; amendment-lagret gäller implementations-status (FAS-DEFERRAL upphävd, async-fetch via `GET /api/v1/landing/stats` realiserad i F6 P5 Punkt 3 per ADR 0064).**

`src/components/landing/landing-stats.ts` exporterar `getLandingStats(): LandingStats` som returnerar `{ activeCount: 45_580, newToday: 312 }` — konstanter från HANDOVER-målbild 01.

**Datakontrakt-stub (FAS-DEFERRAL):** när backend exponerar en endpoint för aggregerade Platsbanken-stats (förslagsvis `GET /api/v1/job-ads/landing-stats` med daglig snapshot eller real-time count) byter helpern till `fetch`-anrop i RSC-context. Inget API-arbete i F6 — Landing-prompten är ren frontend.

## Amendment 2026-05-23 — Live-stats Beslut 4 lyft: implementation byts, utbytespunkt bevarad

> **Amendment 2026-05-23 (Klas-godkänd, CTO-triagead — senior-cto-advisor agentId `a1da26dc2029a5def` multi-approach-triage 2026-05-23, Variant B vald över A/C/D):** ADR 0056 Beslut 4:s FAS-DEFERRAL för live-stats är **lyft**. F6 P5 Punkt 3 (HEAD `e6b08fa` 2026-05-23) realiserar backend-endpointen — den arkitektur-substansen är dokumenterad separat i [ADR 0064 — Publik anonym aggregat-read via Worker-precomputed Redis-cache](./0064-public-aggregate-read-via-worker-precomputed-redis-cache.md), som etablerar mönstret för publik anonym aggregat-read som arkitekturprecedens (återanvänds av F6 P5 Punkt 4 `/oversikt`).
>
> **Utbytespunkt-design (Beslut 4) bevarades — endast implementationen byttes:**
> - `getLandingStats()`-helpern i `src/components/landing/landing-stats.ts` behåller **samma komponent-API-yta** mot konsumenten (`<LandingTopbar />`): prop-driven konsumtion av `{ activeCount, newToday, isStale }`.
> - **Signatur-byte:** sync hårdkodad konstant → async `fetch('/api/v1/landing/stats')` i RSC-context. Detta är frontend-isolerad förändring; ingen konsumerande komponent ändrar `props`-shape utöver tillägget av `isStale`-flagga från ADR 0064:s Floor-fallback-semantik.
> - **Endpoint-namn:** ADR 0056 Beslut 4 föreslog `GET /api/v1/job-ads/landing-stats`. Faktisk implementation valde `GET /api/v1/landing/stats` (landing-namespace istället för job-ads-suffix) — kosmetisk path-justering, ingen semantisk skillnad. Förslagsnamnet i ADR 0056 var inte bindande.
>
> **Vad amendment INTE rör:** Beslut 1 (LandingTopbar-shell), Beslut 2 (theme/lang i footer), Beslut 3 (AuthCard delar `<LoginForm/>`/`<RegisterForm/>`), Beslut 5 (OAuth-knappar stubs) — alla bestå oförändrade. Endast Beslut 4:s FAS-DEFERRAL-status byts.
>
> **Amendment-proveniens:** Klas-godkänd amendment-prosa 2026-05-23 (memory `feedback_klas_can_override_adr_verbatim_source` — explicit Klas-override av §9.4 webb-Claude-verbatim-konvention för F6 P5 Punkt 3 ADR-leverans). Grundad i senior-cto-advisor multi-approach-dom 2026-05-23 (agentId `a1da26dc2029a5def`, Variant B vald över Variant A cache-aside / Variant C PG materialiserad vy / Variant D Next.js fetch revalidate) + verifierad mot redan committed implementation (HEAD `e6b08fa`). ADR 0056 förblir **Accepted** — additivt tilläggslager, EJ supersession; originalt Beslut 4 + utbytespunkt-design bevaras, endast FAS-DEFERRAL-status flips.

### Beslut 5 — OAuth-knappar är stubs som länkar till `/logga-in?provider=<id>`

`<AuthCard />` renderar Google/LinkedIn/Microsoft-knappar (med SVG-monogram från `src-v3/landing.jsx`) som `<a>`-tags pekande mot `/logga-in?provider=<id>`. Inget fullt OAuth-flöde — Klas pre-F6 Prompt 1 FAS-DEFERRAL.

Befintlig `/logga-in`-route ignorerar `?provider`-querysträngen idag (no-op-redirect). När OAuth-flödet wires:as i framtida fas blir det en ny ADR (likt ADR 0017/0018-stilen för cookie-/CSRF-strategi).

---

## Konsekvenser

### Positiva
- HANDOVER §0/§6.4/§7.1 är veto-uppfyllt utan att tidigare ADRs supersedas — landing-shell är en route-specifik komposition, inte en global header-omdesign.
- Auth-form delas mellan `/`, `/logga-in`, `/registrera` — single source of truth för validering.
- `getLandingStats()` är en isolerad utbytespunkt — stats-data-källan kan flyttas till en endpoint utan att komponent-API:t ändras.
- Civic-utility-disciplin: ingen Sparkles, ingen "Drift"-indikator, ingen "Så funkar det"-numrerad cirkel, ingen trust-pill, ingen Version-kicker.

### Negativa
- Två client-islands i landing (`<LandingHeroSection />` + `<LandingFooter />`-toggles) — minimalt overhead, men inte ren RSC-baseline.
- Live-stats-värden är hårdkodade — användare ser samma tal varje besök tills backend-endpoint finns. Acceptabelt under MVP (placeholder-disclaimer i `landing-stats.ts`-jsdoc).
- LangToggle är en visuell stub — `aria-disabled` på EN-knappen, inget `next-intl`-arbete i denna fas.

### Mitigering
- ADR-not till framtida session: när Platsbanken-stats-endpoint byggs, byt `getLandingStats()`-implementationen + lägg till en server-side cache-strategi (förslagsvis daglig snapshot om real-time count är för dyrt).
- När `next-intl` aktiveras, byt `<LandingLangToggle />` till en wired toggle med locale-cookie-write + revalidatePath.

---

## Alternativ övervägda

### Alternativ A — Lyft hela `<LoginForm />` + `<RegisterForm />`-paret till en delad "auth-form-host" som både landing och `/logga-in` använder
Avvisat. Befintliga `/logga-in` och `/registrera` är egna routes med egna page-shells. AuthCard på landing är en TREDJE renderingskontext för samma form-komponenter — direkt-import räcker. Att lyfta hela host-komponenten är YAGNI.

### Alternativ B — Behåll v2-headerns inloggningsknappar och bara lägg till stats-block höger
Avvisat. HANDOVER §0 punkt 6 är icke-förhandlingsbart veto: inga inloggningsknappar i header. v3-spec överrider explicit. ADR-disciplin skulle vara att supersede HANDOVER-spec, vilket Klas inte mandaterat.

### Alternativ C — Bygg en RSC-only landing utan client-islands genom att hantera mode-state via URL-fragment (`#register`)
Avvisat. Klas pre-F6 Prompt 1 verbatim: "Enklast: lyft state till LandingPage och passera ner som prop till både hero och AuthCard. Ingen URL-state behövs för expanderingen." — direktiv klart.

### Alternativ D — Hämta live-stats direkt från Platsbanken-källan i RSC-context (utan helper-utbytespunkt)
Avvisat. Ingen backend-endpoint finns idag, och att fetch:a Platsbanken JobTech-API direkt från Next.js skulle bryta arkitekturen (frontend-till-backend-API-konvention per ADR 0030). Helper-stub är rätt nivå av indirektion.

---

## Implementation

- `src/app/(marketing)/page.tsx` — RSC-shell, renderar 4 sektioner
- `src/components/landing/landing-topbar.tsx` — RSC, brand + stats
- `src/components/landing/landing-hero-section.tsx` — client-island, mode-state + AuthCard
- `src/components/landing/auth-card.tsx` — client, tabs + form + OAuth-stubs
- `src/components/landing/landing-features.tsx` — RSC, 4 mono-key/text-rader
- `src/components/landing/landing-footer.tsx` — RSC-komposit, länkar + ThemeToggle + LangToggle
- `src/components/landing/lang-toggle.tsx` — client, SV aktiv / EN disabled-stub
- `src/components/landing/landing-stats.ts` — helper, `getLandingStats()` + `formatLandingNumber()`
- `src/components/landing/oauth-mark.tsx` — SVG-monogram (Google/LinkedIn/Microsoft)

CSS `.jp-land-*` redan ingjuten i `globals.css` (rad 2022–2354 från tidigare F6-förberedelse — ADR 0052). Ingen ny CSS denna fas.

---

## Acceptanskriterier

- [x] Landing-headern innehåller endast brand + stats (inga login/theme/lang)
- [x] Theme + lang placeras i footer
- [x] Auth-form i höger-kort (AuthCard), delar `<LoginForm />` / `<RegisterForm />`
- [x] Hero-CTA "Skapa konto" flippar AuthCard till register-tab
- [x] Hero-CTA "Utforska som gäst" navigerar till `/jobb`
- [x] Funktioner-sektionen renderar 4 mono-key/text-rader (matchar `src-v3/landing.jsx` FEATURES verbatim)
- [x] Footer-länkar pekar på `/` (placeholders tills riktiga om/villkor-routes finns)
- [x] Live-stats: 45 580 + 312 hårdkodade via `getLandingStats()`
- [x] Vitest-tester: topbar (5), auth-card (7), landing-page-smoke (5) — totalt +17 nya
- [x] design-reviewer rendered-veto = GODKÄND
