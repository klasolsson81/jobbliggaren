---
name: jobbpilot-design-components
description: >
  Canonical reference for JobbPilot's UI components and their civic-utility
  adaptations of shadcn primitives. Use when building or modifying React
  components, implementing forms, choosing between Card/Table/Alert patterns,
  or composing new interactive UI. Triggers on: component, button, card, input,
  form, dialog, modal, toast, table, breadcrumb, badge, alert, skeleton, select,
  shadcn, tsx, jsx, React component, UI element.
---

# JobbPilot Design Components

> Canonical component patterns for JobbPilot's shadcn-based UI.
> - Design tokens used inside components → `jobbpilot-design-tokens`
> - Civic-utility philosophy behind component choices → `jobbpilot-design-principles`
> - Accessibility requirements per component → `jobbpilot-design-a11y`

---

## Component library scope

JobbPilot uses shadcn/ui as the component primitive layer. Components are
copied into `web/jobbpilot-web/components/ui/` — they are owned by the
project, not imported from npm. Install via `pnpm dlx shadcn@latest add <component>`.

**Never replace shadcn with:** Material UI, Chakra, Mantine, Headless UI.
shadcn is the single source of UI primitives.

---

## Civic-utility patterns (v2, `.jp-*` system)

v2 ships a `.jp-*` utility system in `globals.css` (verbatim from
`JobbPilotNEWDESIGN/jobbpilot.css`, Variant B). These are the canonical
civic-utility primitives — prefer them over re-styled shadcn for ledger/feed
layouts. Light/dark follow the `--jp-*` tokens automatically.

| Pattern | What it is | When |
|---|---|---|
| `.jp-table--flat` | Print-ledger table. **NO zebra**, **NO celled borders**, hairlines between rows, thicker (2px) top/bottom rule, mono uppercase header. | Default for data lists. |
| `.jp-attention` | Row-based feed: 8px dot + text (`max-width: 68ch`) + hover dismiss. Hairlines top/between, **no box**. | Översikt / "Aktuellt" feed. |
| `.jp-pipeline` | Kanban as ledger rows. Columns separated by `--jp-border-strong` (stronger than row hairlines), **NO floating cards** (`.jp-appCard` is `display:none`). Rows are `.jp-appRow`. | Application pipeline. |
| `.jp-statusDot` | Dot + text, **no background**. `--brand`/`--info`/`--success`/`--warning`/`--danger`/`--neutral`. | **First choice in tables** for status. |
| `.jp-pill` | Pill: colored `*-50` bg + 6px dot + `*-700` text, `rounded-pill`. | Status **at an entity** (accent moment), not in dense table columns. |
| `.jp-match` | 6px progress bar: brand fill ≥75, `--mid` (info) 50–74, `--low` (warning) <50. | Match-score. |
| `.jp-filterBar` | Flat grid between two hairlines, fields at natural width, **no chrome box**. | Filter rows. |
| `.jp-banner` | Brand-50 bg + 3px `brand-600` left border. Use sparingly. | Non-blocking info notice. |

**`.jp-statusDot` vs `.jp-pill`:** the dot is the default in tables (lowest
visual weight, no fill — rule 2/3). Use the pill only when status is the
entity's headline at a single point (e.g. a detail header), where a small
colored accent is warranted. Never both for the same datum.

---

## Core components (v1, shadcn-based)

### Button

Spec (matches `.jp-btn`, recalibrated per ADR 0038): height **40px**, sm
**36px**; `border-radius: 4px` (`var(--jp-r-md)`); `transition: 80ms linear`;
font 16px/500 sans; `letter-spacing: -0.005em`. Toolbar-knappar kvarstår som
dokumenterat undantag (28px).

Variants:
- `primary` — `bg-brand-600` (= accent-800 grön per ADR 0068, EJ dark-skiftad) + vit text i BÅDA teman (aldrig ljus knapp/mörk text), hover
  `bg-[var(--jp-accent-800-hover)]` (`#1E6B4C`, EJ dark-skiftad — `brand-700`/accent-700 dark-flippar till `#6EE7A8` som ALDRIG får vara fill, ADR 0068). Default CTA (Spara, Skicka, Ansök).
- `secondary` — `bg-surface-primary` + `border-border-default`, hover bg
  `surface-secondary` + `border-border-strong`. Sekundära actions.
- `ghost` — transparent, `text-text-secondary`, hover bg `surface-tertiary`.
  Minor actions (Avbryt, Stäng).
- `destructive` — `bg-danger-600`, destructive CTA (Radera, Avsluta).
- `link` — text-only, inline text links (Läs mer).

Rules:
- One primary button per form — never two side-by-side
- Destructive actions require a confirmation dialog before executing
- Icon-only buttons require `aria-label`
- Loading state: replace label with "Sparar…" and set `disabled`; keep width

### Card

Two flavors:
- `default` — `p-4 border border-border-default rounded-md` — distinct entities
- `compact` — `p-3` with smaller internal gap — tighter lists

**Use Card when:**
- Listing distinct entities (ansökningar, jobb-annonser per entry)
- Grouping related form fields into a named section
- Dashboard summary widgets

**Don't use Card when:**
- Data is tabular — use Table instead
- Card is a one-off page wrapper — use `<main>` + `<section>`
- Content is purely decorative — use a plain div with border

### Table

Default pattern for data lists (preferred over card grids for app data).
Use `.jp-table--flat` (the base `.jp-table` already is the ledger style):
**no zebra-stripes, no celled/inramade borders**, hairlines between rows,
2px `border-strong` top and bottom rule, mono uppercase header in
`text-text-secondary`.

Features in v1:
- Sortable columns (click header, arrow indicator)
- Pagination above and below
- Row click opens detail view
- Skeleton loading state (full row skeletons, not spinner)
- Empty state inline when no rows
- Status column uses `.jp-statusDot` (dot + text, no bg) — not a filled pill

Not in v1: column resize, column reorder, inline editing.

### Input / Textarea / Select

Spec (matches `.jp-input` / `.jp-select`, recalibrated per ADR 0038):
- Height: **44px**, sm **40px**
- Border: `border-border-default` (slate-200), `border-radius: 4px`
  (`var(--jp-r-md)`)
- Background: `bg-surface-primary` (white in light — distinct from the
  slate-50 chrome)
- Focus: `border-brand-600` + `box-shadow: 0 0 0 3px var(--jp-brand-50)`
  (3px brand-50 ring)
- Font: 16px
- Error state: `border-danger-600`, error message below in `text-danger-700`

Always pair with a `<label>` — never placeholder-only inputs. No floating
labels (label sits above the field).

#### Rena input-fält — ingen placeholder-exempeltext (Platsbanken-regel)

**Klas hård designregel 2026-05-17 (förstärker ADR 0038):** Inga input-fält i
JobbPilot får ha exempel-/instruktions-text i `placeholder`. Inputs ska alltid
vara **rena à la Platsbanken** — tomt fält, ingen grå exempeltext i rutan.
Gäller hela appen (auth, sök/filter, dialoger, CV-/ansöknings-/admin-formulär).

- **Default = ingen `placeholder` alls.** Ta bort den.
- Behöver fältet ett exempel eller formathjälp (e-postsyntax, concept-id-format,
  vad ett fritextfält ska innehålla): lägg det som **hjälptext (hint) under
  input-rutan** — `text-body-sm text-text-secondary`, kopplad via
  `aria-describedby` (a11y-skillens form-mönster). Label kvarstår alltid ovanför.
- **Hint-placering:** default är **under** fältet (codebase-konventionen:
  label → input → hint/fel i samma scan-rytm). **Ovanför** är tillåtet endast
  när fältet saknar synlig label (t.ex. `sr-only`-label) — då ger en synlig
  rad ovanför fältet sighted-användare fältets syfte där labeln annars suttit.
  Blanda inte placeringarna i samma vy utan skäl.
- **Rationale:** placeholder försvinner vid inmatning, har svag kontrast
  (kan inte bära information — WCAG), och kollideras med ifyllt värde. 1177,
  Digg och Platsbanken använar rena fält + synlig hint. Civic-utility:
  information är synlig och beständig, inte tillfällig dekoration i rutan.
- **Undantag (ej `placeholder`-exempeltext, behålls):** shadcn `SelectValue`
  `placeholder` (t.ex. "Välj kanal") — det är combobox-ens *unselected display
  value*, funktionellt nödvändig för att signalera "inget valt", inte
  exempeltext i ett textfält. Icke-textuella fält utan synlig label (global
  chrome-sök) bär scope via `aria-label`, inte placeholder.
- **a11y:** hint kopplas med `aria-describedby`; vid fel tar `role="alert"`-
  felmeddelandet beskrivnings-prioritet (se `jobbpilot-design-a11y` §5).

ADR 0038:s tidigare formulering om kvarhållna auth-format-placeholders
(`din.email@exempel.se`) är **upphävd** av denna regel — e-postsyntax flyttad
till hint under fältet ("Formatet är namn@domän.se").

### Form (shadcn Form wrapper)

Always use the shadcn Form component for structured forms:

```tsx
<Form {...form}>
  <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-4">
    <FormField
      control={form.control}
      name="email"
      render={({ field }) => (
        <FormItem>
          <FormLabel>E-post</FormLabel>
          <FormControl><Input {...field} /></FormControl>
          <FormMessage />
        </FormItem>
      )}
    />
    <Button type="submit" disabled={form.formState.isSubmitting}>
      {form.formState.isSubmitting ? "Sparar…" : "Spara"}
    </Button>
  </form>
</Form>
```

- Zod schemas for type-safe validation
- react-hook-form for form state
- Server Action for submission
- `FormMessage` wires `aria-describedby` automatically

### Dialog / Modal

**Use for:**
- Confirmation before destructive action
- Brief focused tasks (edit a single field, rename an item)

**Don't use for:**
- Multi-step workflows — use a dedicated page
- Showing read-only content — use inline or Toast

Every Dialog must:
- Have an explicit close button (ghost variant, top-right)
- Close on Escape key
- Trap focus inside while open
- Return focus to the trigger element on close

```tsx
<Dialog>
  <DialogTrigger asChild>
    <Button variant="destructive">Radera CV</Button>
  </DialogTrigger>
  <DialogContent>
    <DialogTitle>Radera CV-v3?</DialogTitle>
    <DialogDescription>
      Detta kan inte ångras efter 30 dagar.
    </DialogDescription>
    <DialogFooter>
      <Button variant="ghost">Avbryt</Button>
      <Button variant="destructive">Radera CV</Button>
    </DialogFooter>
  </DialogContent>
</Dialog>
```

Button text is always specific: "Radera CV", never "Bekräfta" or "OK".

### Toast

Timing:
- Success: 3 seconds, auto-dismiss
- Info: 5 seconds, auto-dismiss
- Error: persists until user dismisses — never auto-dismissed

Stack limit: never more than 3 toasts visible simultaneously.

Content: specific (not "Något gick fel"), Swedish copy, no emoji.

### Badge

For status indicators, counts, and categorization.

| Variant | Classes |
|---|---|
| Success | `bg-success-50 text-success-700` |
| Warning | `bg-warning-50 text-warning-700` |
| Danger | `bg-danger-50 text-danger-700` |
| Info / Neutral | `bg-info-50 text-info-700` |
| Brand | `bg-brand-50 text-brand-700` |

Always `rounded-pill` — explicit exception to the 6px radius rule.

### Alert

Inline feedback blocks for non-transient messages.

**Use for:**
- Empty states with a concrete next step
- Non-blocking warnings (outdated data, missing profile section)
- Informational notices (feature preview, beta notice)

**Don't use for:**
- Transient feedback — use Toast
- Critical blocking errors — use Dialog or page-level banner

### Breadcrumb

Always visible on pages deeper than the first navigation level.

```
Ansökningar / Klarna Backend Engineer / Intervjuförberedelse
```

- `text-body-sm`, `text-text-secondary`
- Separator: `/` in `text-text-tertiary`
- Current page: `text-text-primary`, no underline
- Parent links: `text-brand-600`, underline on hover

### Skeleton

Loading state for predictable content shapes (lists, detail views, cards).

- Use flat neutral gray: `bg-surface-tertiary` — no shimmer animation
- Match the approximate shape of what will load (row height, column widths)
- Skeleton is the **default** for content loading — it shows the shape that fills in

### Spinner (BrandSpinner) — spinner-vs-skeleton doctrine

`BrandSpinner` ("Sigillet i rörelse", ADR 0070 Fas 2) is the civic loading mark:
the register rows pulse while a gold arc rotates the inner ring. Motion here
carries information — a real wait — never decoration.

- **Use BrandSpinner only for known-slow, formless waits (> ~1–2 s)** where there
  is no predictable shape to skeleton. Open the surface **instantly**, then show
  the spinner + a Swedish status line inside it (e.g. "Jobbannonsen läses in…").
- First consumers: the **job-ad modal** and **saved-application modal** loading
  states — `ModalLoadingShell`, rendered as the `loading.tsx` Suspense fallback
  while the intercepted modal's server detail streams in.
- **Never put a spinner where it is already fast** — page/route changes, tab
  switches, light nav. A spinner on a fast action reads as jank and erodes the
  serious/trustworthy feel.
- **Skeleton stays the default (~90 %); the spinner is the narrow exception.**
  When a shape is known (a list, a card, a detail layout), skeleton it. Reach for
  the spinner only when the wait is genuinely formless and known-slow.
- `prefers-reduced-motion` → BrandSpinner falls back to a static seal (no
  rotation/pulse). It carries `role="status"` + `aria-live="polite"` + an sr-only
  label so the wait is announced to screen readers.

---

## Composition patterns

### Empty state

```tsx
<Alert>
  <AlertTitle>Inga ansökningar</AlertTitle>
  <AlertDescription>
    Du har inga aktiva ansökningar. Hitta jobb som passar din profil under Jobb.
  </AlertDescription>
  <Button asChild variant="primary" className="mt-3">
    <Link href="/jobb">Visa jobb</Link>
  </Button>
</Alert>
```

Always: brief title + explanation + concrete next action. Never just "Tomt här."

### Confirmation dialog

See Dialog section above. Button text is always action-specific.

---

## Icons

- Library: `lucide-react`
- Default size: `size-4` (16px) inline with text; `size-5` (20px) standalone
- Color: inherits `currentColor` — never hardcode icon color
- Style: stroke/outline only — no filled icon variants

---

## When this skill is not enough

- Specific hex values and Tailwind token classes → `jobbpilot-design-tokens`
- WCAG / a11y requirements per component → `jobbpilot-design-a11y`
- Swedish copy inside components (labels, errors, empty states) → `jobbpilot-design-copy`
- Design philosophy behind component choices → `jobbpilot-design-principles`
- Full shadcn component API → https://ui.shadcn.com/docs/components
- All states (hover, active, disabled, focus) per variant → `references/variants-full.md`
- Full JSX examples for Fas 1 flows → `references/composition-examples.md`
