# Design-review: TD-43 — komponent-tests för forms

**Status:** Approved med rekommendationer (ingen Blocker)
**Granskat:** 2026-05-10
**Auktoritet:** DESIGN.md §9 (a11y), CLAUDE.md §10.3 (svensk copy), TD-15-läxan (form-a11y baseline)
**Scope:** test-filer låser fast UI-beteende — inte UI-kod i sig

Inga Blockers. Ingen civic-utility-regression. TD-15-pattern-skyddet finns på plats men har **observerbara luckor** som gör regression-fångsten svagare än den behöver vara. Inga av dessa är blockerande för merge — de är förbättringsrekommendationer.

---

## Kritiska (block)

Inga.

---

## Större (bör adresseras nu eller som omedelbar följ-task)

### S1. `me-profile-form.test.tsx` saknar verifiering av error-element `id`

**Fil:** `web/jobbpilot-web/src/components/me/me-profile-form.test.tsx:100`
**Nuvarande:**
```ts
expect(name).toHaveAttribute("aria-describedby", "me-profile-form-error");
```
**Problem:** `resume-content-form.test.tsx:125` är striktare:
```ts
expect(alert).toHaveAttribute("id", "content-form-error");
```
MeProfileForm-testet hoppar över att verifiera att error-`<p>` faktiskt har `id="me-profile-form-error"`. Konsekvens: en utvecklare kan rename `ERROR_ID`-konstanten i komponenten utan att uppdatera elementets `id`-attribut, och testet skulle fortfarande passera så länge `aria-describedby` matchar register-värdet — men screen reader får då en *dangling reference* (aria-describedby pekar på id som inte finns).

**Rekommendation:** Lägg till en assertion som speglar resume-content-testet:
```ts
const alert = await screen.findByRole("alert");
expect(alert).toHaveAttribute("id", "me-profile-form-error");
```
Detta är TD-15-pattern-skyddets **kärnpunkt**: aria-pekare måste peka på något som finns. Att resume-testet har det och me-profile-testet inte, gör coverage inkonsekvent över TD-15-formulären.

### S2. Path→element-routing testas bara för förstafältet

**Filer:** båda TD-15-testerna
**Problem:** Båda testerna verifierar focus-flytt **endast** för `displayName` / `personalInfo.fullName`. Komponenterna har dock omfattande `pathToElementId`-funktioner som mappar 4 olika paths (me-profile) respektive 8+ paths inkl. fält-arrays (resume-content). Om någon ändrar `pathToElementId`-mappingen — t.ex. `"emailNotifications"` → `"me-email-notifs"` — fångas det inte. Det är en **tyst regression i a11y-baseline** — focus-flytt fungerar inte för det fältet, men inget test failar.

**Rekommendation:** Lägg till åtminstone **en till** path-routing-test per komponent som täcker en icke-trivial path:
- MeProfileForm: ett checkbox-path (t.ex. `weeklySummary` → `me-weeklySummary`)
- ResumeContentForm: ett array-path (t.ex. `experiences.0.company` → `exp-0-company` efter `Lägg till erfarenhet`)

Detta är inte completeness — det är **mönsterskydd**. Två datapunkter räcker för att låsa konventionen; en gör det inte.

---

## Mindre (nice-to-have, inte blocker)

### M1. LoginForm-divergens från TD-15 — flagga för senare uppgradering

**Fil:** `web/jobbpilot-web/src/components/forms/LoginForm.tsx`
LoginForm använder `useActionState` (server action) istället för RHF + client-side `safeParse`. Det gör att TD-15-pattern (focus-flytt, `aria-invalid`-toggling) inte är direkt applicerbart — server returnerar en generisk `state.error` utan field-path. Civic-utility-perspektivet säger:

- **OK för nu:** Login-felet är medvetet *generiskt* ("Inloggningen misslyckades. Kontrollera e-post och lösenord.") — det får inte avslöja vilket fält som var fel (säkerhetspraxis). Path-baserad focus-flytt skulle vara emot säkerhetsmodellen här.
- **Men:** Vid `state.error` borde fokus åtminstone flyttas till **error-elementet** eller **email-fältet**, så screen reader hör felet utan att användaren behöver Tab. Idag annonseras `role="alert"` automatiskt, men keyboard-users förlorar visuell kontext om felet renderas långt ner.
- **Inte i scope för TD-43.** Flaggas som potentiell uppgradering. Inget krav att ta nu.

### M2. "Sparat"-status-meddelandet testar inte tidsstämpeln

**Filer:** båda success-testerna
**Nuvarande:**
```ts
expect(await screen.findByRole("status")).toHaveTextContent(/Sparat/);
```
Komponenterna renderar "Sparat 14:32." (sv-SE locale, 24h). Testet låser bara ordet "Sparat", inte locale-formatet. Civic-utility-copy-regeln är tydlig: 24h-format alltid, decimaler i sv-SE. Om någon byter till `toLocaleTimeString()` utan locale-arg eller till AM/PM, fångas det inte.

**Rekommendation (low-prio):** Komplettera med locale-pattern:
```ts
expect(status).toHaveTextContent(/Sparat \d{2}:\d{2}/);
```
Inte blocker — det är en sekundär guardrail bakom existerande locale-testning.

### M3. ResumeContentForm — empty-states copy är rik men bara *en* testas

**Fil:** `resume-content-form.test.tsx:43`
Komponenten har tre empty-states:
- "Ingen erfarenhet tillagd." ✓ testat
- "Ingen utbildning tillagd." ✗ inte testat
- "Inga färdigheter tillagda." ✗ inte testat

Per CLAUDE.md §10.3 och DESIGN.md §8.4 är empty-state-copy låst svensk. Risken är att de två otestade kan drifta (t.ex. utvecklare normaliserar till "Tom lista" eller liknande). Inte high-stakes — men civic-utility-perspektivet säger att alla user-facing svenska strängar förtjänar ett test som låser dem när de redan finns i scope.

**Rekommendation:** Lägg till två rader:
```ts
expect(screen.getByText("Ingen utbildning tillagd.")).toBeInTheDocument();
expect(screen.getByText("Inga färdigheter tillagda.")).toBeInTheDocument();
```

---

## OK-lista (vad som är solitt)

- **Svensk copy låst på rätt platser:** "Logga in", "Spara profil", "Spara CV", "Sparat", "Visningsnamn", "Fullständigt namn", "Ingen erfarenhet tillagd.", "E-postadress", "Lösenord", error-strängarna ("Inloggningen misslyckades. Kontrollera e-post och lösenord.", "Kunde inte uppdatera profilen.", "Kunde inte spara CV."). Alla utan "Du" med stort D, utan utropstecken, utan emoji, utan "Hoppsan!"-mönster.
- **TD-15 a11y-trio testat:** `aria-invalid="true"`, `aria-describedby="<error-id>"`, `toHaveFocus()` — kärnan är på plats för båda RHF-formulären.
- **`role="alert"` används korrekt** för error-fall, `role="status"` för success-fall — semantiskt korrekt distinction (assertive vs polite live region).
- **HTML-required-konstrant testad separat** (`toBeRequired()` i LoginForm) — bra distinction mellan browser-native a11y och schema-level a11y.
- **"Bypass HTML required-constraint" via `removeAttribute('required')`** — pragmatiskt och välkommenterat. Detta är ett legitimt sätt att testa schema-level validation utan att kämpa mot jsdom:s HTML5-form-handling.
- **Action-mock-pattern konsekvent** över alla tre filer — gör det enkelt att lägga till nya tester med samma mönster.
- **LoginForm-error-test verifierar exakt error-strängen** — civic-utility "Inloggningen misslyckades. Kontrollera e-post och lösenord." är låst, vilket skyddar mot att någon byter till "Whoops, fel användarnamn!" eller liknande AI-slop.

---

## Sammanfattning

TD-43 etablerar en **gedigen a11y-baseline för komponent-testning** som matchar JobbPilots civic-utility-krav. Pattern-skyddet finns; det är bara inte konsekvent strikt över de två RHF-formulären. **S1 + S2 rekommenderas** att lyftas som omedelbar följ-task (5–10 min arbete) eftersom de stärker TD-15-läxans hållbarhet utan scope-creep. M1–M3 är opportunistiska förbättringar.

**Civic-utility-perspektivet är tillgodosett.** Inget i denna PR uppmuntrar AI-aesthetics, ingen tyst-felning är möjlig, status-feedback är klar och deterministisk. Approved.
