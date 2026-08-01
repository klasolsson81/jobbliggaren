# JobbPilot — Locale Formatting: Code Examples

Deploy-ready utility functions for Swedish date, time, currency, and number
formatting. All functions use `date-fns` with `sv` locale and `Intl` APIs
configured for `sv-SE`. Import from `@/lib/format`.

---

## Setup

```ts
// lib/format.ts
import { format, formatDistanceToNow, isToday, isYesterday, isThisWeek } from "date-fns"
import { sv } from "date-fns/locale"
```

Install:
```bash
pnpm add date-fns
```

No additional locale packages needed — `date-fns` ships `sv` locale.

---

## Date formatting

### Short date — "14 apr 2026"

```ts
export function formatDateShort(date: Date | string): string {
  const d = typeof date === "string" ? new Date(date) : date
  return format(d, "d MMM yyyy", { locale: sv })
}

// formatDateShort(new Date("2026-04-14")) → "14 apr 2026"
```

### Long date — "14 april 2026"

```ts
export function formatDateLong(date: Date | string): string {
  const d = typeof date === "string" ? new Date(date) : date
  return format(d, "d MMMM yyyy", { locale: sv })
}

// formatDateLong(new Date("2026-04-14")) → "14 april 2026"
```

### ISO date — "2026-04-14"

```ts
export function formatDateISO(date: Date | string): string {
  const d = typeof date === "string" ? new Date(date) : date
  return format(d, "yyyy-MM-dd")
}
```

---

## Time formatting

### Time — "14:32"

```ts
export function formatTime(date: Date | string): string {
  const d = typeof date === "string" ? new Date(date) : date
  return format(d, "HH:mm")
}

// Never: "2:32 PM", "14.32"
```

### Date + time — "14 apr 2026 kl 14:32"

```ts
export function formatDateTime(date: Date | string): string {
  const d = typeof date === "string" ? new Date(date) : date
  return `${formatDateShort(d)} kl ${formatTime(d)}`
}
```

### Time + date for confirmations — "14:32 den 18 apr"

Used in success copy: "Ansökan skickad 14:32 den 18 apr."

```ts
export function formatSubmittedAt(date: Date | string): string {
  const d = typeof date === "string" ? new Date(date) : date
  return `${formatTime(d)} den ${format(d, "d MMM", { locale: sv })}`
}
```

---

## Relative time

### Relative distance — "3 dagar sen"

```ts
export function formatRelative(date: Date | string): string {
  const d = typeof date === "string" ? new Date(date) : date
  return formatDistanceToNow(d, { locale: sv, addSuffix: true })
}

// formatRelative(new Date("2026-04-15")) → "3 dagar sedan"
// Never: "3 days ago", "for 3 days"
```

### Smart label for lists (today/yesterday/weekday/date)

```ts
export function formatSmartDate(date: Date | string): string {
  const d = typeof date === "string" ? new Date(date) : date

  if (isToday(d)) return `idag kl ${formatTime(d)}`
  if (isYesterday(d)) return `igår kl ${formatTime(d)}`
  if (isThisWeek(d, { locale: sv })) return format(d, "EEEE", { locale: sv }) // "måndag"

  const now = new Date()
  if (d.getFullYear() === now.getFullYear()) {
    return format(d, "d MMM", { locale: sv }) // "3 apr"
  }
  return format(d, "d MMM yyyy", { locale: sv }) // "3 apr 2025"
}
```

---

## Currency

### SEK — "33 456 kr"

```ts
const krFormatter = new Intl.NumberFormat("sv-SE", {
  style: "currency",
  currency: "SEK",
  minimumFractionDigits: 0,
  maximumFractionDigits: 0,
})

export function formatSEK(amount: number): string {
  return krFormatter.format(amount)
}

// formatSEK(33456) → "33 456 kr"
// Never: "33,456 SEK", "33456 kr"
```

---

## Numbers

### Decimal — "4,5"

```ts
const decimalFormatter = new Intl.NumberFormat("sv-SE", {
  minimumFractionDigits: 1,
  maximumFractionDigits: 1,
})

export function formatDecimal(n: number): string {
  return decimalFormatter.format(n)
}

// formatDecimal(4.5) → "4,5"
// Never: "4.5"
```

### Thousands — "12 345"

```ts
const intFormatter = new Intl.NumberFormat("sv-SE")

export function formatInt(n: number): string {
  return intFormatter.format(n)
}

// formatInt(12345) → "12 345"
// Never: "12,345", "12.345"
```

### Procent — används inte för matchning

Svensk konvention är mellanslag före `%` (`89 %`, aldrig `89%`). Men **ingen
matchnings- eller CV-yta får visa ett procenttal** — se `SKILL.md` §5 (ADR 0076
Decision 4, ADR 0053 Amendment 2026-06-19, CLAUDE.md §5). Det fanns en
`formatPercent`-hjälpare dokumenterad här med exakt `"89 %"` som exempel; den har
**noll anropsställen** och finns inte i koden, och `messages/sv/` innehåller noll
strängar med `" %"`. Behöver en framtida icke-matchningsyta procent, skriv
formateraren då och bind den till den ytan.

---

## Timezone

All backend timestamps are stored and returned as UTC ISO 8601.
Frontend converts to Europe/Stockholm for display.

**Do not write the zone yourself.** Which module you need depends on the question
you are asking:

- **Presentation** — `src/lib/i18n/format.ts`. next-intl is the timezone
  authority for anything a reader sees; it resolves the configured zone itself.
- **The global pin** — `src/i18n/request.ts`. Declared once, for every
  `useFormatter()` call, so SSR and client agree.
- **Calendar facts** — `SWEDISH_TIME_ZONE` in `src/lib/time/swedish-calendar.ts`.
  For questions like "which Swedish civil month is this instant in", which stay
  Swedish even when the user's locale is `en`.

`no-restricted-syntax` fails the literal written as a value under `src/`, in
pre-commit and in CI, so a new site does not merge (#1148). The two DECLARING
modules are exempt — `src/i18n/request.ts` and `src/lib/time/swedish-calendar.ts`
— as is test code. `format.ts` is not exempt and does not need to be: it never
writes the zone, because next-intl resolves the pin for it. The authoritative
list, including the test-code globs, lives in `eslint.config.mjs`.

> **Removed 2026-08-01:** this section used to document `toStockholm` /
> `formatDateTimeStockholm` built on **`date-fns-tz`** — that package, not the
> `date-fns` entry in BUILD.md §3.1 — with an `Install:` block. `date-fns-tz` is
> not in `package.json`, has no usage in `src/`, and is not in BUILD.md §3.1, so
> following the section produced a §12 change; its local `const STOCKHOLM` is the
> duplication the guard above now fails. Recorded rather than deleted silently, so
> it is not reintroduced. (Same treatment as the `formatPercent` note earlier in
> this file.)

Never store local time in DB. Never assume client timezone == Stockholm.

---

## Usage in components

```tsx
import { formatDateShort, formatRelative, formatSEK, formatSmartDate } from "@/lib/format"

// Table cell — last updated
<TableCell className="text-text-secondary">
  {formatSmartDate(app.updatedAt)}
</TableCell>

// Reminder text
<p>Du har inte följt upp med Ericsson sedan {formatDateShort(app.lastContactAt)}.</p>

// Success toast
toast({ description: `Ansökan skickad ${formatSubmittedAt(new Date())}.` })
```
