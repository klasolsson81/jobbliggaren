# ADR 0057 — Inställningar v3: route-rename `/mig` → `/installningar`, direct-apply-preferenser

**Datum:** 2026-05-20
**Status:** Accepted
**Kontext:** F6 Prompt 2 (Inställningar-refactor till v3-design). HANDOVER-v3.md §7.6 + §0 punkt 6+7 är veto-status över alla tidigare ADRs.
**Beslutsfattare:** Klas Olsson; CC implementation; senior-cto-advisor 2026-05-20 (4 multi-approach-val A/B/C — Klas accepterade CTO-rek A/B/B/B).
**Relaterad:** ADR 0017 (frontend auth-pattern — middleware-protected prefixes), ADR 0020 (frontend DTO-validering), ADR 0030 (api result kind-union), ADR 0052 (designsystem v3), ADR 0054 (header-meny ersätter sidebar — user-menu placering), ADR 0056 (Landing v3-shell — föregångare i F6-batchen), HANDOVER-v3 §7.6, målbild `10-installningar-dark.png`

---

## Kontext

HANDOVER-v3 §7.6 specificerar Inställningar-routen som `/installningar` (medvetet byte från `/konto` i prototypen). Discovery 2026-05-20:

1. **Ingen `/konto`-route** finns i kodbasen — Klas-promptens referens till `/konto` matchar v3-prototypen, inte live-koden.
2. **`/mig`-route** är dagens funktionella motsvarighet: Kontoinformation (e-post + roller, read-only) + Profil-form (`MeProfileForm`) + `<DeleteAccountSection />`. UserMenu i app-shell pekar mot `/mig` med rubriken "Inställningar".
3. **`MeProfileForm`** wires:ar `JobSeekerProfileDto` (`displayName`, `language`, `emailNotifications`, `weeklySummary`) via `updateMyProfileAction`. Komponenten har integrerad zod-validering, path-routed-felhantering och `useTransition`-baserad submit.
4. **Aviserings-DTO** saknar fält för "Nya matchningar", "Påminnelser", "Statusändringar". Klas-promptens 4-toggle-lista täcker bara 2 fält som finns idag (`emailNotifications` + `weeklySummary`).
5. **Phone**-fält saknas helt i `JobSeekerProfileDto`.
6. **Theme-hook** (`useTheme()` i `@/components/theme-provider`) hanterar bara `"light" | "dark"` — ingen `"system"`-läge att överväga.

## Beslut

### Beslut 1 — `/mig` byter namn till `/installningar` med 308-redirect (CTO Val 1A)

`/mig`-routen migreras helt till `/installningar`. `next.config.ts` får en `redirects()`-konfiguration som returnerar `permanent: true` (HTTP 308 method-preserving) för `/mig` och `/mig/:path*`. Den gamla `(app)/mig/page.tsx`-filen raderas.

Effekter:
- UserMenu (`app-shell.tsx`) länkar mot `/installningar`
- Drawer-länk i mobil-shell mot `/installningar`
- `middleware.ts` `PROTECTED_PREFIXES` utökas med `"/installningar"` (behåller `/mig` för redirect-skydd under övergångsperiod)
- `revalidatePath("/mig")` i `updateMyProfileAction` byter till `revalidatePath("/installningar")`
- Befintliga app-shell-tester uppdateras till `/installningar`

Avvisat alternativ: behålla `/mig` som "profil-overview" parallellt med `/installningar` som "preferenser" — bryter CCP (Martin 2017, kap. 13): två moduler med samma change-reason (användarens preferenser/profil-data).

### Beslut 2 — En `SettingsForm` orchestrerar alla kort, en `updateMyProfileAction` per ändring (CTO Val 2B + Klas-direktiv direct-apply)

Klas-direktiv 2026-05-20: "Visning/Aviseringar är direct-apply, Sekretess/Logga ut är action-knappar, en form, kort = visuella grupperingar."

Implementation:
- `<SettingsForm />` (client) håller all preferens-state lokalt + orkestrerar `updateMyProfileAction`-anrop
- `displayName` är form-kontrollerad text-input + explicit "Spara ändringar"-knapp (Personuppgifter-kort)
- `language` (Segment) + `emailNotifications` + `weeklySummary` (ToggleRow) appliceras DIREKT vid förändring via optimistic state + sekventiell `updateMyProfileAction`-call (via `useTransition`)
- `theme` byts via `useTheme().setTheme()` — ingen backend-anrop, lokal persistens via befintlig theme-provider
- Vid action-fail revertas optimistic state till föregående värde + felmeddelande visas

Race-condition: sekventiella anrop genom `useTransition` (en åt gången). Användare som klickar flera toggles snabbt får sista anrop som vinner (last-write-wins). Cross-aggregate-locking är out-of-scope — `JobSeekerProfile` är single-user-aggregate med naturlig last-write-wins-semantik (Evans 2003, "Aggregates").

Avvisade alternativ:
- 3 separata submit-knappar per kort: fragmenterad UX
- 3 parallella `PUT /me` utan koordinering: aggregate-race-condition

### Beslut 3 — Reducera aviseringar till 2 wirade toggles, no-mock-doktrin (CTO Val 3B + Klas-godkänt)

Klas-promptens 4-toggle-lista ("Nya matchningar", "Påminnelser", "Statusändringar", "Veckosammanfattning") reduceras till 2 toggles som faktiskt persisteras:

- "E-postnotifikationer" → `emailNotifications`
- "Veckosammanfattning via e-post" → `weeklySummary`

De 3 omappade toggles RENDERAS INTE. Memory `feedback_design_reviewer_deferral_manifest` + civic-utility-doktrin (CLAUDE.md §1, §5.2): app:en visar bara det som fungerar. Stubbade toggles är "AI-lure"-anti-pattern (1177/Digg/GOV.UK visar inte "kommer snart"-element).

DTO-utvidgning för de 3 saknade fälten är out-of-scope för F6 (frontend-only) — egen STEG vid behov.

### Beslut 4 — Telefon-fält skippas helt, no-mock-doktrin (CTO Val 4B + Klas-godkänt)

Klas-promptens "Namn, E-post, Telefon" i Personuppgifter-kortet reduceras till **Namn (write via `displayName`) + E-postadress (read-only från `getServerSession()`)**.

`phone`-fält saknas i `JobSeekerProfileDto`. Inkludera stub-input = bryter no-mock-doktrin. DTO-utvidgning är out-of-scope för F6 frontend-only — egen STEG vid behov.

### Beslut 5 — `<Segment />` + `<ToggleRow />` skapas som shared UI-primitiver

`src/components/ui/segment.tsx` — `role="radiogroup"`, tangentbord (Vänster/Höger pilar växlar value, disabled options hoppas över), aktiv option = `navy-800` + vit text i båda lägena (matchar primary-knappens "aldrig inverterad"-regel, §5.1).

`src/components/ui/toggle-row.tsx` — label vänster, `<button role="switch">` höger, klick togglar, aria-labelledby kopplar label → switch.

`shadcn/ui` har inte motsvarande primitiver i live-koden idag — anti-bloat-disciplin: inga externa deps lagts till.

### Beslut 6 — FAS-DEFERRAL för export-flöde och engelsk lokalisering

- **"Exportera mina data"-knapp** renderas med `aria-disabled` och no-op `onClick` + TODO-kommentar. GDPR-rätten kommuniceras synligt, flödet wired:as i framtida fas.
- **"English"-option** i språk-segmentet är `disabled: true` med hint "Engelska är ännu inte tillgängligt". `next-intl` är ej aktiverat — backend `JobSeekerProfileDto.language` accepterar `"sv" | "en"` men UI tillåter inte byte tills översättningar finns.
- **"Radera konto"** använder befintlig `<DeleteAccountSection />` (TD-28-flöde, ej regression i denna prompt).

---

## Konsekvenser

### Positiva
- En route, en mental modell — användarens preferenser samlade på `/installningar`
- En source-of-truth för profil-data — `updateMyProfileAction` är enda mutationsväg
- `<Segment />` + `<ToggleRow />` är shared primitiver för framtida v3-ytor (sökningar/CV-faserna)
- Civic-utility-disciplin uppfylld — inga stubbade fält som lurar användaren
- 308-redirect skyddar bokmärken mot gamla `/mig`-länken

### Negativa
- Klas-promptens "4 toggles + Telefon" reduceras till "2 toggles + Namn/E-post" — DTO-utvidgning krävs för fullt scope (framtida fas)
- Direct-apply ger ingen "Avbryt"-paus efter klick — användaren kan inte ångra en toggle utan att klicka tillbaka manuellt
- Race-condition vid extrem snabb-klick på flera toggles — last-write-wins (accepterad single-user-aggregate-semantik)

### Mitigering
- DTO-utvidgning (phone + 3 aviserings-fält) öppen för framtida STEG. Ingen ADR-blockerare.
- Klas kan override Beslut 3/4 till "stubbade fält + synlig FAS-DEFERRAL-not" om UI-spec-granskning före backend-bygg är prio. Override skulle behöva FAS-DEFERRAL-MANIFEST renderad i komponenten, inte bara i prompt.

---

## Alternativ övervägda

### Alternativ A — Skapa `/installningar` parallellt med `/mig` (behåll båda)
Avvisat (CTO + Klas). Två routes för samma change-reason = CCP-brott. Bokmärken mot `/mig` finns inte i externa system; rename + redirect är gratis.

### Alternativ B — En submit-knapp per kort (3 separata `updateMyProfileAction`-anrop)
Avvisat. Race-condition på `JobSeekerProfile`-aggregate. Aggregate-skydd (CLAUDE.md §2.2) kräver atomära transitions.

### Alternativ C — Visa alla 4 toggles + Telefon-fält som stubbar med synlig FAS-DEFERRAL-not
Avvisat (Klas accepterade no-mock-doktrin). Klassisk AI-lure-anti-pattern. Civic-utility (1177/Digg/GOV.UK) renderar inte "kommer snart"-element.

### Alternativ D — Utöka backend DTO i samma fas
Avvisat (Klas direktiv: F6 är frontend-only). Backend-utvidgning är egen STEG.

---

## Implementation

**Nya filer:**
- `src/app/(app)/installningar/page.tsx` (RSC-shell)
- `src/components/settings/settings-form.tsx` (client-orchestrator)
- `src/components/settings/personal-info-card.tsx`
- `src/components/settings/display-card.tsx`
- `src/components/settings/notifications-card.tsx`
- `src/components/settings/privacy-card.tsx`
- `src/components/settings/logout-card.tsx`
- `src/components/ui/segment.tsx` + `segment.test.tsx`
- `src/components/ui/toggle-row.tsx` + `toggle-row.test.tsx`
- `src/components/settings/settings-form.test.tsx`

**Modifierade filer:**
- `next.config.ts` (308-redirect `/mig` → `/installningar`)
- `src/middleware.ts` (lägg `/installningar` i `PROTECTED_PREFIXES`)
- `src/components/shell/app-shell.tsx` (UserMenu + drawer-länk → `/installningar`)
- `src/components/shell/app-shell.test.tsx` (assertions → `/installningar`)
- `src/lib/actions/me.ts` (`revalidatePath("/installningar")`)
- `src/app/globals.css` (`.jp-segment`, `.jp-togglerow`, `.jp-settings-grid`, `.jp-settings-field`)

**Raderade filer:**
- `src/app/(app)/mig/page.tsx`

Tester: +19 (totalt 563 gröna).

---

## Acceptanskriterier

- [x] `/installningar` renderar 5 kort i ordning: Personuppgifter, Visning, Aviseringar, Sekretess och data, Logga ut
- [x] `/mig` 308-redirectas till `/installningar` (querystring bevaras via `:path*`-pattern)
- [x] UserMenu + drawer pekar mot `/installningar`
- [x] Personuppgifter visar Namn (write) + E-post (read-only), inget Telefon-fält
- [x] Visning har Tema-segment (direct-apply via useTheme) + Språk-segment (English disabled)
- [x] Aviseringar har 2 toggles (E-postnotifikationer + Veckosammanfattning), inga stubbade
- [x] Sekretess har Exportera-stub + DeleteAccountSection (befintlig TD-28-flöde)
- [x] Logga ut är secondary-knapp som triggar `logoutAction`
- [x] design-reviewer rendered-veto = GODKÄND
