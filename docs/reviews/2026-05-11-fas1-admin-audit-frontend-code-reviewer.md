# Code-review: Fas 1-stängning admin-granskning (frontend)

**Status:** Approved med Minor-noteringar
**Granskat:** 2026-05-11
**Auktoritet:** CLAUDE.md §1 (civic-utility), §4 (TS/Next.js), §5.2 (FE anti-patterns), §10 (svenska)
**Scope:** FE — `(admin)`-route-grupp, admin-API-modul, typer, `(app)/layout.tsx`-diff

**TL;DR:** Inga Blockers. Inga Major. 5 Minors + 2 Nits. Koden är på Mastercard-nivå. Server Components-disciplin är exemplarisk. Zero `"use client"`, zero `any`, zero `useEffect`. Diskriminerat union för `AuditLogResponse` är **en förbättring** över befintliga `T | null`-pattern och bör adopteras vidare — se Minor 1.

---

## Blockers

Inga.

## Major

Inga.

## Minor

### Minor 1 — Inkonsistens i API-resultatformat (kind-union vs `T | null`)

**Filer:**
- `web/jobbpilot-web/src/lib/api/admin.ts` (ny: kind-union)
- `web/jobbpilot-web/src/lib/api/applications.ts` (befintlig: `T | null`)
- `web/jobbpilot-web/src/lib/api/me.ts` (befintlig: `T | null`)

Din nya `AuditLogResponse` med `kind: "ok" | "forbidden" | "unauthorized" | "error"` är **konceptuellt bättre** än det befintliga `T | null`-mönstret eftersom det låter UI:t skilja mellan 401/403/network-error/empty. `(app)/mig/page.tsx` rad 56–62 visar exakt det problem du undvikit: `getMyProfile()` returnerar `null` både för "auth-fel" och "backend nere", och UI:t kan bara säga "Kunde inte hämta din profil. Försök ladda om sidan" — vilket är vilseledande om problemet är auth-läckage.

Detta är inte ett brott mot CLAUDE.md (§5.2 talar inte om resultat-discriminanter), men det är en **inkonsistens som bör lösas medvetet**. Tre vägar:

1. **Lyft mönstret till ny ADR + TD**: dokumentera kind-union som JobbPilot-konvention och lägg TD för att refactor `applications.ts` + `me.ts` (faller utanför 4h-regeln §9.6 — TD är rätt).
2. **Anpassa admin.ts** till `T | null` för konsistens — sämre design, gör inte detta.
3. **Lämna som-är** och acceptera att admin är en pionjär. Också OK, men då bör en kort kommentar i `admin.ts` förklara varför just denna avviker.

**Rekommendation:** Väg 1. Diskriminerat union är ett kvalitetslyft för civic-utility-felmeddelanden ("Saknar behörighet" vs "Kunde inte ladda" är inte samma sak för en användare). Invokera `senior-cto-advisor` för formalisering eller skapa TD direkt.

**Auktoritet:** CLAUDE.md §1 (civic-utility-ton kräver precisa felmeddelanden), §9.6 (TD-policy).

---

### Minor 2 — Magic string "Admin" på två ställen

**Filer:**
- `web/jobbpilot-web/src/app/(admin)/layout.tsx:19` — `user.roles.includes("Admin")`
- `web/jobbpilot-web/src/app/(app)/layout.tsx:40` — `user.roles.includes("Admin")`

Exportera en const från `@/lib/auth/session.ts`:

```ts
export const ROLES = {
  Admin: "Admin",
} as const;

export type Role = (typeof ROLES)[keyof typeof ROLES];
```

Sedan: `user.roles.includes(ROLES.Admin)`.

**Motivering:** CLAUDE.md §5.1 förbjuder magic strings i backend. §1 säger Mastercard-nivå. Två handkopierade `"Admin"`-strängar för säkerhetskritisk åtkomstkontroll är inte Mastercard.

**Trivial fix.** 4h-regeln §9.6 säger fix in-block.

---

### Minor 3 — `AuditLogFilter`-komponenten saknar test

**Fil saknas:** `web/jobbpilot-web/src/app/(admin)/admin/granskning/audit-log-filter.test.tsx`

Tabell + paginering testas. Filter-komponenten testas inte, trots icke-trivial logik:
- `toLocalInput()` trunkering av ISO till `YYYY-MM-DDTHH:mm`
- `defaultValue ?? ""`-pattern på 5 fält
- `method="get"` + `action="/admin/granskning"` (kritiskt — fel action → filter går till fel route)
- aria-label `"Filtrera granskningsloggen"` (a11y-regression-skydd)

**Föreslagna tester:** 4-5 tester (rendering, defaultValue, toLocalInput, form-attribut, rensa-länk).

---

### Minor 4 — Tidszons-osäkerhet i `formatDateTime` (audit-log-table.tsx:90–97)

Använder `d.getHours()` / `d.getMinutes()` — lokala tidszon-getters. Skört, kräver att server kör Europe/Stockholm.

**Rekommendation:** Använd `toLocaleString("sv-SE", { timeZone: "Europe/Stockholm", ... })`.

Existerande test (rad 52–57) använder regex `/^2026-05-1\d \d{2}:\d{2}:\d{2}$/` som accepterar dag 10–19 — testet maskerar buggen.

**Auktoritet:** CLAUDE.md §10.2 (svensk locale), §5.1 (inga magic constants).

---

### Minor 5 — `pageSize`-validering saknar upper-bound

**Fil:** `web/jobbpilot-web/src/app/(admin)/admin/granskning/page.tsx:26`

`parsePositiveInt` accepterar valfri positiv int. Användare kan sätta `?pageSize=10000`.

**Föreslå:** klamp till `[1, 200]`:

```ts
const pageSize = Math.min(parsePositiveInt(params.pageSize, DEFAULT_PAGE_SIZE), 200);
```

Backend validerar redan via FluentValidation (max 200), så fix är defense-in-depth.

---

## Nits

### Nit 1 — `forbidden` i `ErrorBlock`-prop kan aldrig hända i praktiken
AdminLayout redirectar bort non-Admin innan page.tsx kör. Defensiv kod är OK.

### Nit 2 — Interpunktion-inkonsistens
`audit-log-table.tsx:20` "Inga poster matchar filtret" saknar punkt; övriga meddelanden har punkt.

---

## Verdict

**Approved.** Inga blockers, inga major. Mergeklar.

## Filer granskade
(Lista — se task-output i agent-transkript.)
