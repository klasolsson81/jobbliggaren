# JobbPilot — Locale Formatting

Swedish date, time, currency and number **conventions**, and where the code that
implements them actually lives.

This file used to be an implementation guide for a `@/lib/format` module built on
`date-fns`. Neither exists — see the note at the bottom. The conventions below were
correct then and are correct now; only the code was fiction.

**next-intl is the formatting authority for NEW code.** It resolves the configured
locale and timezone deterministically across SSR and client, which is what keeps a
rendered date from drifting between server and browser. Do not reach past it to
`toLocaleString` or a fresh `Intl.DateTimeFormat` / `Intl.NumberFormat`, and do not
write the timezone yourself (see §Timezone).

**The exception is a criterion, not a list.** Reaching past next-intl is legitimate
where the value is **not a localized presentation** — a form-ready or operator value
that must read identically whatever the UI language is — and such code names the
zone explicitly, whether because there is no next-intl configuration to inherit or
because it is deliberately not routing through the one there is.
Worked example: `audit-log-table.tsx` reaches past next-intl for a ledger that needs
a seconds column the shared `formatDateTime` does not produce, names the zone, and
carries its own spec. It passes the test, and it is not a licence — it is what
passing looks like.

**Apply the test; do not look for a list.** Code failing it is drift rather than an
exception: a counter, a result total, or any ordinary presented number built from a
fresh `Intl` instance should call `formatNumber`. Convert such sites when you are in
the file anyway — `match-setup-rail-modal.tsx`'s counter was one, and #1155 converted
it, which is why it is named here as a worked case rather than as an outstanding one.
**No lint rule guards any of this**, unlike the zone literal, so this section states a
criterion and deliberately does not carry a count of the sites meeting it — such a
count would be stale the next time someone follows the instruction above.

---

## Where the code lives

The **shared** helpers live under `web/jobbliggaren-web/src/lib/i18n/`, and new code
should use them.

**A date shape can come from anywhere, though, so search rather than assume:** from a
shared helper, from a module with its own month arrays, or from a call site that
picked `dateTime` options inline. `lib/oversikt/aggregations.ts` is the one module of
the second kind today — hand-rolled Swedish formatters outside next-intl, including
`formatSwedishShortDateWithYear`, which produces the same shape as the Short row below
and returns an en-dash rather than `null` for bad input. `use-urgency-label.ts` is one
of the third kind: it decides a long-month form inline. `lib/time/swedish-calendar.ts`
(calendar facts) and `lib/company-criteria/format-magnitude.ts` (counts, not currency)
are further homes.

**And one is a name collision, which is the shape the tombstone below calls the
worst of all:** `admin/granskning/audit-log-table.tsx` declares a local
`formatDateTime(iso: string)` that shadows the shared `formatDateTime(format, iso)`
— different first argument, `toLocaleString` instead of next-intl, and seconds where
the ledger row has none. It is legitimate (see the criterion above); it is also
exactly the trap a reader hits when the two names mean different things.

**Consolidating any of this is not this file's business, but pretending it does not
exist would repeat, inverted, the defect this file was rewritten for.**

Signatures are given below so a caller knows what to pass; bodies are not
reproduced, because an example that points at a module ages visibly when the module
moves, while one that copies it ages silently.

### `format.ts` — locale presentation

Every function takes the formatter as its first argument. `JpFormatter` is the
`Pick<>` of next-intl's `useFormatter()` result these need — interface segregation,
so the real formatter is assignable as-is:

```ts
import { useFormatter } from "next-intl";          // client, and SYNC Server Components
import { getFormatter } from "next-intl/server";   // ASYNC Server Components
import { formatDate, formatDateTime, formatNumber, formatTime } from "@/lib/i18n/format";

const format = useFormatter();         // sync
const format = await getFormatter();   // async — it returns a Promise; without the
                                       // await, format.dateTime is undefined at runtime

formatDate(format, application.appliedAt);        // "18 maj 2026" · ISO in, null on missing
formatDateTime(format, entry.occurredAt);         // "2026-05-11 10:32" · ledger, 24h
formatTime(format, new Date(event.at));           // "14:32" · takes a DATE, not an ISO string
formatNumber(format, 1234);                       // "1 234" in sv (NBSP), "1,234" in en
```

**The two contracts differ, and the difference type-errors.** `formatDate` and
`formatDateTime` take `string | null | undefined` and return `null` for missing or
unparseable input, so a caller can omit the row rather than render "Invalid Date".
`formatTime` takes a known-good `Date` and is non-nullable. Call sites either hold a
`Date` in state already or wrap an ISO string once; the helper does neither for you,
and the null guard is yours.

`formatDateTime` is deliberately **locale-stable**: `YYYY-MM-DD HH:mm` is a fixed
operator convention for admin tables where rows must align column-wise, not a
localized presentation, so it reads identically in sv and en.

### `relative-time.ts` — "hur längesen"

```ts
import { daysSince, formatDaysAgo } from "@/lib/i18n/relative-time";

daysSince(isoString, now?): number;
formatDaysAgo(t: RelativeTimeTranslator, isoString: string, now?: Date): string;
```

The translator comes **first**, so the wording lives in `messages/sv/` rather than
in the helper. The clock is injectable — pass `now` rather than letting the helper
read it, so callers stay testable.

**Its missing-value contract is not `format.ts`'s.** `daysSince` returns `0` for
unparseable input, so `formatDaysAgo` renders "idag" for garbage rather than
returning null. Do not generalise the null contract across the module boundary.

---

## The conventions themselves

These are CLAUDE.md §10, and they hold regardless of which module implements them.

### Dates

**Normative for new code, not an inventory of the tree.** Reach for the nearest row;
do not read the absence of a shape as a ban on one that already ships.

| Shape | Example | Where |
|---|---|---|
| Short | `18 maj 2026` | anything a job seeker reads |
| Short, no year | `13 apr` | same-season contexts where the year adds nothing |
| Long month, no year | `18 april` | urgency and deadline copy |
| Weekday | `lördag` | "today" surfaces |
| ISO date | `2026-05-11` | form-ready values, exports, copyable fields |
| Ledger | `2026-05-11 10:32` | admin tables whose rows must align column-wise |
| Month label | `maj 2026` | month pickers, period headings |

Never `05/18/2026`, never `May 18, 2026` in Swedish copy. The ISO date and the
ledger shape are different rows on purpose: a form-ready value carries no time.

Shipped variants sit outside these rows on purpose — `audit-log-table`'s ledger
carries **seconds**, and `aggregations.ts`'s notices stamp renders `2026-05-11 ·
10:32` in **UTC** with a middle dot, which its own doc comment argues for. A shape
outside the rows is not automatically wrong; it does have to answer what the row
could not express.

### Time

24-hour, colon-separated: `14:32`. Never `2:32 PM`, never `14.32`.

**Bound to a date with `kl.`** — with the full stop, and a comma before it:
`18 maj 2026, kl. 14:32`. That connector is `messages/sv/jobads.json`
(`ui.card.published*`). Never `kl` bare, never `klockan`, which appears nowhere in
`messages/sv/`.

**`idag` / `igår`, closed up.** Klas-direktiv 2026-08-26 (#1168), which settled a
13:4 split the other way than the count alone would have. `messages/sv/` carries no
spaced form, and `relative-time.ts` resolves the word through the catalogue rather
than spelling it. The connector row above therefore reads
`idag, kl. 14:32`, which is what `jobads.json` sends.

### Currency

`1 234 kr` — grouped with a **non-breaking space** (U+00A0), amount before the
unit, `kr` lowercase. Never `1,234 SEK`, never `1234 kr`, never `kr 1 234`.

No product surface formats currency today, so there is no helper. If one is needed,
write it against `formatNumber`'s grouping rather than a second `Intl` instance,
and bind it to the surface that needs it.

### Numbers

Decimal **comma** in UI, decimal point in code: `4,5` on screen, `4.5` in a literal.
Grouping is a non-breaking space: `12 345`, never `12,345` and never `12.345`.

The grouping space in every example on this page is written with an ordinary space,
because a literal U+00A0 is invisible in a diff and in review. The character
`formatNumber` actually emits is U+00A0 — its sv grouping test asserts the output
does **not** contain an ASCII space, and binds the real character to a named `NBSP`
constant rather than scattering it through the assertions. Do the same if you need
it: name it once.

### Percent

Swedish convention is a space before `%` (`89 %`, never `89%`). But **no matching or
CV surface may show a percentage** — `SKILL.md` §5 (ADR 0076 Decision 4, ADR 0053
Amendment 2026-06-19, CLAUDE.md §5). A `formatPercent` helper was once documented
here with exactly `"89 %"` as its example; measured, it has **zero call sites**, does
not exist in the code, and `messages/sv/` contains **zero** strings with `" %"`. If a
future non-matching surface needs percent, write the formatter then and bind it to
that surface.

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

> **Removed 2026-08-01 (#1148 for §Timezone, #1150 for the rest).** This file documented a
> `@/lib/format` module built on **`date-fns`** — that package, not any other — with
> `pnpm add date-fns` and `pnpm add date-fns-tz` install blocks, and helpers
> `formatDateShort`, `formatDateLong`, `formatDateISO`, `formatTime`,
> `formatDateTime`, `formatSubmittedAt`, `formatRelative`, `formatSmartDate`,
> `formatSEK`, `formatDecimal`, `formatInt`, `toStockholm` and
> `formatDateTimeStockholm`.
>
> Measured against `web/jobbliggaren-web/`: the module path does not exist, and
> **neither package is in `package.json`**. The two differed where it mattered *at
> the time of that removal*: `date-fns` was then listed in BUILD.md §3.1
> (`| Datum | date-fns | 4.x |`), so installing it would have been an undiscussed
> dependency add (§9.2) against a fictional module path — while `date-fns-tz` was
> in neither, so the §Timezone half specifically is what produced a §12
> *non-BUILD.md library* change.
>
> **That §3.1 row was the root cause, and the follow-up removed it.** §3.1 now
> records the delivered mechanism (`@/lib/i18n/format` + `@/lib/i18n/relative-time`)
> with a self-asserting absence claim in place of the package name. `date-fns` is
> therefore no longer a listed decision, and `date-fns-tz` never was — so the
> §9.2-vs-§12 asymmetry above no longer holds for anything written after it
> (truth-sync #1154).
>
> The names above are the thirteen this section carried; `formatPercent` made
> **fourteen** in the same guide, and has its own note above.
>
> Of the thirteen, **eleven appear nowhere in `web/jobbliggaren-web/src/`** — and the
> other two, `formatTime` and `formatDateTime`, are the worst shape of all: they
> collide with real exports of `web/jobbliggaren-web/src/lib/i18n/format.ts` that take
> a **different first argument**.
> A reader who trusted the old page wrote a call that type-errors; one who trusted
> it harder installed a library the repo does not use.
>
> Following this file therefore produced, at best, an undiscussed dependency add
> against a module that does not exist — and in the §Timezone half, a §12 change
> outright. It is recorded rather than
> deleted silently so it is not reintroduced — the same treatment the
> `formatPercent` note above already had, which is where the pattern comes from.
>
> **Kept, because the conventions were not where the fiction lived** — though review
> found three this rewrite had dropped (the `kl.` connector, the date-only ISO form,
> the long-month shape) and one the old file had wrong (`kl` without the stop).
> What `14 apr 2026`, `14:32`, `1 234 kr` and `4,5` should look
> like was broadly right; it was the code claiming to produce them that was invented.
