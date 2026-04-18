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

### Percentage — "89 %"

```ts
export function formatPercent(n: number): string {
  return `${Math.round(n)} %`
}

// formatPercent(89.4) → "89 %"
// Space before % is Swedish convention
```

---

## Timezone

All backend timestamps are stored and returned as UTC ISO 8601.
Frontend converts to Europe/Stockholm for display.

```ts
import { toZonedTime, format as formatTz } from "date-fns-tz"

const STOCKHOLM = "Europe/Stockholm"

export function toStockholm(utcDate: Date | string): Date {
  const d = typeof utcDate === "string" ? new Date(utcDate) : utcDate
  return toZonedTime(d, STOCKHOLM)
}

export function formatDateTimeStockholm(utcDate: Date | string): string {
  const local = toStockholm(utcDate)
  return formatDateTime(local)
}
```

Install:
```bash
pnpm add date-fns-tz
```

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
