# JobbPilot — Locale Formatting

Swedish date, time, currency and number **conventions**, and where the code that
implements them actually lives.

This file used to be an implementation guide for a `@/lib/format` module built on
`date-fns`. Neither exists — see the note at the bottom. The conventions below were
correct then and are correct now; only the code was fiction.

**next-intl is the formatting authority.** It resolves the configured locale and
timezone deterministically across SSR and client, which is what keeps a rendered
date from drifting between server and browser. Do not reach past it to
`toLocaleString`, and do not write the timezone yourself (see §Timezone).

---

## Where the code lives

Two modules, both under `src/lib/i18n/`. Their signatures are given so a caller
knows what to pass; their bodies are not reproduced here, because an example that
points at a module ages visibly when the module moves, while one that copies it
ages silently.

### `format.ts` — locale presentation

Every function takes the formatter as its first argument. `JpFormatter` is the
`Pick<>` of next-intl's `useFormatter()` result these need — interface segregation,
so the real formatter is assignable as-is:

```ts
import { useFormatter } from "next-intl";
import { formatDate, formatDateTime, formatNumber, formatTime } from "@/lib/i18n/format";

const format = useFormatter();          // or getFormatter() in a Server Component
formatDate(format, application.appliedAt);   // "18 maj 2026" · null on missing input
formatDateTime(format, entry.occurredAt);    // "2026-05-11 10:32" · ledger shape, 24h
formatTime(format, event.at);                // "14:32"
formatNumber(format, 1234);                  // "1 234" in sv, "1,234" in en
```

`formatDate` and `formatDateTime` return `null` for missing or unparseable input,
so a caller can omit the row rather than render "Invalid Date".

`formatDateTime` is deliberately **locale-stable**: `YYYY-MM-DD HH:mm` is a fixed
operator convention for admin tables where rows must align column-wise, not a
localized presentation, so it reads identically in sv and en.

### `relative-time.ts` — "hur längesen"

```ts
import { daysSince, formatDaysAgo } from "@/lib/i18n/relative-time";
```

`daysSince(isoString, now?)` is the pure day count; `formatDaysAgo` takes a
translator so the wording lives in `messages/sv/`, not in the helper. The clock is
injectable — pass `now` rather than letting the helper read it, so callers stay
testable.

---

## The conventions themselves

These are CLAUDE.md §10, and they hold regardless of which module implements them.

### Dates

| Shape | Example | Where |
|---|---|---|
| Short | `18 maj 2026` | anything a job seeker reads |
| ISO / ledger | `2026-05-11 10:32` | admin tables, exports, form-ready values |

Never `05/18/2026`, never `May 18, 2026` in Swedish copy.

### Time

24-hour, colon-separated: `14:32`. Never `2:32 PM`, never `14.32`.

### Currency

`1 234 kr` — grouped with a **non-breaking space** (U+00A0), amount before the
unit, `kr` lowercase. Never `1,234 SEK`, never `1234 kr`, never `kr 1 234`.

No product surface formats currency today, so there is no helper. If one is needed,
write it against `formatNumber`'s grouping rather than a second `Intl` instance,
and bind it to the surface that needs it.

### Numbers

Decimal **comma** in UI, decimal point in code: `4,5` on screen, `4.5` in a literal.
Grouping is a non-breaking space: `12 345`, never `12,345` and never `12.345`.

### Percent

Swedish convention is a space before `%` (`89 %`, never `89%`). But **no matching or
CV surface may show a percentage** — `SKILL.md` §5 (ADR 0076 Decision 4, ADR 0053
Amendment 2026-06-19, CLAUDE.md §5). A `formatPercent` helper was once documented
here and never existed in the code. If a future non-matching surface needs percent,
write the formatter then and bind it to that surface.

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

Never store local time in DB. Never assume client timezone == Stockholm.

---

## What was removed, and why it is recorded

> **Removed 2026-08-01 (#1148, and the follow-up to it).** This file documented a
> `@/lib/format` module built on **`date-fns`** — that package, not any other — with
> `pnpm add date-fns` and `pnpm add date-fns-tz` install blocks, and helpers
> `formatDateShort`, `formatDateLong`, `formatDateISO`, `formatTime`,
> `formatDateTime`, `formatSubmittedAt`, `formatRelative`, `formatSmartDate`,
> `formatSEK`, `formatDecimal`, `formatInt`, `toStockholm` and
> `formatDateTimeStockholm`.
>
> Measured against `src/`: the module path does not exist, neither `date-fns` nor
> `date-fns-tz` is in `package.json`, and neither is in BUILD.md §3.1 (which lists
> `date-fns`, which is not what was installed either).
>
> Of the thirteen names, **eleven appear nowhere in `src/`** — and the other two,
> `formatTime` and `formatDateTime`, are the worst shape of all: they collide with
> real exports of `src/lib/i18n/format.ts` that take a **different first argument**.
> A reader who trusted the old page wrote a call that type-errors; one who trusted
> it harder installed a library the repo does not use.
>
> Following this file therefore produced a §12 change. It is recorded rather than
> deleted silently so it is not reintroduced — the same treatment the
> `formatPercent` note above already had, which is where the pattern comes from.
>
> **Kept, because it was never the fiction:** every convention in this file. What
> "14 apr 2026", `14:32`, `1 234 kr` and `4,5` should look like was right; only the
> code that claimed to produce them was invented.
