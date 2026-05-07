# Tech Debt — JobbPilot

Items deferrade från reviews och arkitektur-flaggor. Adressering
planerad till Fas 1 om inte annat anges. Källa och severity
dokumenteras för spårning.

ID-konvention: TD-{nummer}. Items hänvisas till via ID i framtida
ADR:er, PR-beskrivningar och commits.

---

## TD-1: Skip-link saknas i (app)-layout
**Kategori:** Accessibility (WCAG 2.4.1 Bypass Blocks)
**Severity:** Minor
**Källa:** design-reviewer, 2026-05-07 (Turn 2)

`src/app/(app)/layout.tsx` har `<header>` och `<main>` men ingen
"Skip to main content"-länk. Tangentbordsanvändare måste tabba
igenom hela headern på varje sida.

**Föreslagen åtgärd:** Lägg till `<a href="#main">Hoppa till
huvudinnehåll</a>` som första element i layout-body, dolt visuellt
men synligt vid tangentbordsfokus (`sr-only focus:not-sr-only`).
Tagga `<main id="main">`.

---

## TD-2: CardTitle renderas utan heading-tag
**Kategori:** Accessibility (heading-hierarki)
**Severity:** Minor
**Källa:** design-reviewer, 2026-05-07 (Turn 2)

Shadcn `CardTitle` renderar default som `<div>`. Heading-trädet på
/mig blir därmed `<h1>` ("Min profil") följt av `<div>`
("Kontoinformation") — bryter h1→h2-hierarki som skärmläsare
förlitar sig på.

**Föreslagen åtgärd:** Passa `as="h2"`-prop om shadcn-versionen
stödjer det, annars wrap manuellt med `<h2>` eller customize
CardTitle-komponenten centralt.

---

## TD-3: Tom-state-copy "Inga roller tilldelade" saknar next-action
**Kategori:** UX
**Severity:** Minor
**Källa:** design-reviewer, 2026-05-07 (Turn 2)

På /mig visas "Inga roller tilldelade" om `user.roles` är tom array.
Användaren får ingen vägledning om vad det betyder eller om de
behöver agera.

**Föreslagen åtgärd:** Antingen (a) gör tom-state stum — visa inte
fältet alls om listan är tom — eller (b) ge context: "Inga roller
tilldelade än — kontakta support om du förväntade dig roller här."
Beslut hör hemma i Fas 1 UX-pass när roles-konceptet konkretiseras
produktmässigt.

---

## TD-4: userId visas i UI utan tydligt användarbehov
**Kategori:** UX / Privacy hygiene
**Severity:** Minor
**Källa:** security-auditor, 2026-05-07 (Turn 2)

mig/page.tsx visar `user.userId` (Guid) som första fält. Slutanvändare
har inget direkt behov av Guid. Möjligt support-värde — men då bör
syftet kommuniceras tydligt ("Support-id för felanmälningar").

**Föreslagen åtgärd:** Antingen ta bort fältet ur UI eller omformulera
label så syftet är klart. Beslut i Fas 1 UX-pass.

---

## TD-5: Redundant getServerSession-anrop på /mig
**Kategori:** Code hygiene
**Severity:** Minor
**Källa:** security-auditor, 2026-05-07 (Turn 2)

Både (app)/layout.tsx och mig/page.tsx anropar getServerSession().
Funktionellt OK — funktionen är `React.cache()`-ad så andra anropet
träffar cache. Men kodflödet är otydligare än nödvändigt.

**Föreslagen åtgärd:** Förmodligen acceptera duplikationen som
dokumenterad pattern (cache är billig, läsbarhet vinner) snarare
än att fixa. Alternativ: refaktorera så att layout passerar `user`
via context eller layout-prop. Inte trivialt i Server Components
— pragmatiskt rätt är troligen "no-op + dokumentera pattern".

---

## TD-6: Logout-backend-call utan fel-loggning
**Kategori:** Observability
**Severity:** Minor
**Källa:** security-auditor, 2026-05-07 (Turn 2)

`logoutAction` anropar backend `/auth/logout`. Om anropet misslyckas
(network, 500) raderas cookien lokalt och användaren redirectas —
men backend-session blir kvar i Redis tills TTL.

**Föreslagen åtgärd:** Lägg till strukturerad loggning vid logout-fel.
Övervägning för Fas 1: ska klienten retry:a, eller är "best-effort
logout"-semantik acceptabel? Beslut beror på threat-model.

---

## TD-7: Zod runtime-validering för DTOs från backend
**Kategori:** Type safety / Architecture
**Severity:** Major (latent)
**Källa:** security-auditor, 2026-05-07 (Turn 2) — extension

Frontend tar emot DTOs från backend och deklarerar matchande
TypeScript-typer manuellt. `tsc` verifierar inte att backend
faktiskt returnerar det typen säger — mismatch sker tyst (det var
Major 1 i Turn 2: `roles?: string[]` vs `IReadOnlyList<string>`).

**Föreslagen åtgärd:** Introducera Zod-schema per DTO i frontend
(t.ex. `lib/dto/current-user.ts`). `getServerSession()` och andra
backend-konsumtioner validerar via schema vid `res.json()`. Vid
mismatch: throw + log. Schema bör genereras från backend OpenAPI-
spec eller en delad source of truth om sådan etableras.

Egen ADR krävs (placeholder: **ADR 0020**) eftersom valet påverkar
samtliga frontend-DTO-konsumtioner.

---

## Adresseringsstrategi

- Items i kategorierna a11y, UX och observability adresseras
  gruppvis i Fas 1 i dedikerade passes (ett a11y-pass, ett
  UX-pass, ett observability-pass).
- TD-7 (Zod) får egen ADR och egen implementations-fas — den
  arkitekturella payoffen är hög och förändringen rör många filer.
- TD-5 utvärderas vid första touch — kan landas som "no-op,
  dokumentera" i layout-kommentar utan separat fas.
- Vid touch på berörda filer i andra ärenden: addressera relevanta
  TD-items opportunistiskt om scope tillåter.