---
name: jobbpilot-design-a11y
description: >
  Canonical WCAG 2.1 AA reference for JobbPilot. Use when building
  interactive components, forms, navigation, dialogs, or any UI
  with keyboard/screen reader interaction. Also use when auditing
  existing components for accessibility compliance. Triggers on:
  a11y, accessibility, WCAG, ARIA, aria-label, aria-describedby,
  focus, focus-ring, keyboard, tab, screen reader, contrast,
  tillgänglighet, skärmläsare, tangentbord, fokus, kontrast,
  axe, Lighthouse.
---

# JobbPilot Accessibility (WCAG 2.1 AA)

> Canonical WCAG 2.1 AA compliance reference for JobbPilot.
> - Design tokens (focus-ring color, contrast values) → `jobbpilot-design-tokens`
> - Component-specific a11y patterns → `jobbpilot-design-components`
> - Accessible copy (clear error messages, descriptive labels) → `jobbpilot-design-copy`
> - Civic-utility accessibility rationale → `jobbpilot-design-principles`

---

## Compliance floor

WCAG 2.1 AA is JobbPilot's non-negotiable baseline. Applies to all pages,
all components, all states. No exceptions for MVP status, internal tools,
or time pressure.

**Verification gates (all must pass before merge):**
- Lighthouse a11y score ≥ 95 per page
- axe DevTools: 0 violations per new page/component
- NVDA (Windows) + VoiceOver (Mac) manual test for critical flows
- Keyboard-only navigation test per view

Design-reviewer classifies any a11y failure as **Blocker**. No variant, no deferral.

---

## 1. Semantic HTML

Use the right element. Semantic HTML is free a11y — no ARIA needed when
the element already communicates its role.

| Element | Use for |
|---|---|
| `<main>` | Primary content — one per page |
| `<nav>` | Navigation landmarks — global nav + breadcrumb |
| `<header>` | Page/section header |
| `<footer>` | Page/section footer |
| `<article>` | Self-contained content (ansökan, jobbannons) |
| `<section>` | Thematic grouping, usually with heading |
| `<aside>` | Tangential content (filter-panel, sidebar) |
| `<button>` | Interactive action — never `<div onClick>` |
| `<a href>` | Navigation — never `<button>` for routing |
| `<label>` | Always paired with form inputs |

Never:
- `<div onClick>` where `<button>` is correct
- `<span role="button">` instead of `<button>`
- `<button>` for navigation (use `<a href>`)
- Heading levels out of order (`<h1>` → `<h3>` skipping `<h2>`)
- Multiple `<h1>` per page

---

## 2. Keyboard navigation

Every interactive element must be reachable and operable via keyboard only.

Requirements:
- Tab order follows visual reading order (left-to-right, top-to-bottom)
- Never `tabIndex > 0` — breaks natural tab order
- `tabIndex={-1}` OK for programmatic focus (modals, post-submit error focus)
- `tabIndex={0}` OK for custom interactive elements that are not natively focusable
- Escape closes modals, dialogs, dropdowns
- Enter/Space activates buttons
- Arrow keys navigate menus, tab panels, grids
- No keyboard trap — except inside open modals where Escape must break it

Test: unplug the mouse. Can you reach everything? Can you use everything?

---

## 3. Focus ring

Every focusable element must show a visible focus indicator when keyboard-focused.
Never `outline: none` without a replacement — that is a WCAG 2.4.7 violation.

```css
/* From globals.css — do not override without design-reviewer approval */
*:focus-visible {
  outline: 2px solid var(--color-focus-ring);  /* #0B5CAD */
  outline-offset: 2px;
  border-radius: var(--radius-sm);
}
```

Use `:focus-visible` not `:focus` — mouse clicks won't show ring, keyboard
navigation will. This is the correct modern pattern.

Minimum focus indicator contrast: 3:1 against adjacent colors (WCAG 2.4.11).
`--color-focus-ring` (#0B5CAD) on white = 6.1:1 — verified.

---

## 4. Color contrast

Minimum ratios (WCAG 1.4.3 + 1.4.11):

| Content type | Min ratio | Note |
|---|---|---|
| Body text (< 18pt regular, < 14pt bold) | 4.5:1 | Most UI text |
| Large text (≥ 18pt regular, ≥ 14pt bold) | 3:1 | Headings |
| UI components (button borders, input borders, icons) | 3:1 | — |
| Focus indicator | 3:1 | Against adjacent colors |
| Placeholder text | 4.5:1 | Same as body text |
| Disabled elements | Exempt | But don't rely on color alone |

**JobbPilot-verified pairs:**

| Pair | Ratio | Level |
|---|---|---|
| text-primary (#1A1A1A) on surface-primary (#FFFFFF) | 17:1 | AAA |
| text-secondary (#5A5A5A) on surface-primary | 7.4:1 | AA |
| brand-600 (#0B5CAD) on surface-primary | 6.1:1 | AA |
| danger-600 (#B42121) on surface-primary | 5.8:1 | AA |
| success-700 (#0B5E24) on success-50 (#E8F3EC) | 5.1:1 | AA |

Full table → `references/contrast-table.md` in `jobbpilot-design-tokens`.

Never create a new color combination without verifying at
webaim.org/resources/contrastchecker. text-tertiary (#8A8A85) fails for
body text on surface-secondary — only use it for decorative/non-essential text.

---

## 5. Form accessibility

Every form input must have all of the following:

```tsx
<FormItem>
  <FormLabel htmlFor="email">
    E-post <span aria-hidden="true" className="text-danger-600">*</span>
  </FormLabel>
  <Input
    id="email"
    type="email"
    required
    aria-required="true"
    aria-invalid={!!errors.email}
    aria-describedby="email-help email-error"
  />
  <p id="email-help" className="text-body-sm text-text-secondary">
    Vi använder aldrig din e-post för reklam.
  </p>
  {errors.email && (
    <p id="email-error" role="alert" className="text-body-sm text-danger-700">
      {errors.email.message}
    </p>
  )}
</FormItem>
```

Requirements:
- `<label>` associated via `htmlFor` + `id`, or `aria-label` if label is not visible
- `aria-required="true"` on required fields (in addition to `required`)
- `aria-invalid` set dynamically based on error state
- `aria-describedby` links both help text and error message (space-separated IDs)
- Error message appears as text — never only as a red border
- Error message uses `role="alert"` so screen readers announce it immediately
- On submit failure: focus moves to first error field

shadcn Form component wires `aria-describedby` automatically via `FormMessage`.
Still verify `aria-invalid` is set on the underlying input.

---

## 6. Screen reader support

### Icon-only buttons
```tsx
<button aria-label="Stäng dialog">
  <XIcon aria-hidden="true" />
</button>
```

### Decorative icons
```tsx
<StarIcon aria-hidden="true" />
<span>Betyg: 4,5 av 5</span>
```

### Status/count updates (live regions)
```tsx
<div role="status" aria-live="polite" aria-atomic="true">
  {isLoading ? "Hämtar jobbannonser\u2026" : `${jobs.length} träffar`}
</div>
```

Use `aria-live="polite"` (waits for user pause). Use `aria-live="assertive"`
only for critical errors that interrupt — never for routine updates.

### Dialogs
Every `<Dialog>` must have:
- `role="dialog"` (shadcn Dialog adds automatically)
- `aria-labelledby` pointing to `<DialogTitle>` id
- `aria-describedby` pointing to `<DialogDescription>` id
- Focus trapped inside while open (shadcn handles)
- Focus returns to trigger element on close (shadcn handles)
- Escape key closes (shadcn handles)

### Skip link
Required on every page with a navigation landmark:
```tsx
<a
  href="#main-content"
  className="sr-only focus:not-sr-only focus:absolute focus:top-4 focus:left-4 focus:z-50 focus:px-4 focus:py-2 focus:bg-surface-primary focus:border focus:border-brand-600"
>
  Hoppa till innehåll
</a>
```

---

## 7. Motion

```css
/* Already in globals.css — do not remove */
@media (prefers-reduced-motion: reduce) {
  *, *::before, *::after {
    animation-duration: 0.01ms !important;
    animation-iteration-count: 1 !important;
    transition-duration: 0.01ms !important;
  }
}
```

Allowed animations per DESIGN.md §10:
- Fade 150ms (toast, dropdown appear/disappear)
- Slide 200ms (side panel)
- Opacity 150ms (hover states)

Never:
- Bounce, spring, scale-on-hover
- Parallax scrolling
- Auto-playing video or audio
- Flashing content > 3 times/second (photosensitive epilepsy risk, WCAG 2.3.1)

---

## 8. Text sizing and zoom

- Minimum body font size: 14px (`text-body` token)
- Line-height: 1.55 for body text (set on `body` in globals.css)
- Page must be usable at 200% browser zoom without horizontal scrolling
- Use `rem` for font sizes in custom components (not `px`) so user font size
  preferences are respected
- Never suppress `font-size` scaling via `max-width` in `em` that breaks at zoom

---

## 9. Touch targets

Minimum interactive hit area: **44×44 CSS pixels** (WCAG 2.5.5).

Applies to: buttons, links, form controls, table row click areas, icon buttons.

Use `padding` to expand hit-area without changing visual size:
```tsx
/* Icon-only button — visual size 16px, hit area 44px */
<button className="p-3">   {/* 16px icon + 12px padding each side = 40px */}
  <XIcon className="size-4" aria-label="Lukk" />
</button>
```

Button component size `md` (default, `h-9` = 36px) — verify hit area is
sufficient in dense contexts. Use `lg` (`h-11` = 44px) for critical CTAs.

---

## 10. Error handling accessibility

Every form submission error must:
1. Be announced to screen readers immediately (via `role="alert"`)
2. Appear as text — not only as a red border or color change
3. Be linked to its field via `aria-describedby`
4. Move focus to the first error field (or error summary at top)

Error summary pattern for multi-field forms:
```tsx
{hasErrors && (
  <div role="alert" aria-labelledby="error-summary-title">
    <h2 id="error-summary-title">Kontrollera följande fält:</h2>
    <ul>
      {errors.map((err) => (
        <li key={err.field}>
          <a href={`#${err.field}`}>{err.message}</a>
        </li>
      ))}
    </ul>
  </div>
)}
```

---

## Testing checklist (per new component/page)

Design-reviewer uses this checklist as her audit source. All must pass.

- [ ] Lighthouse a11y score ≥ 95
- [ ] axe DevTools: 0 violations
- [ ] Keyboard-only: reach all interactive elements via Tab
- [ ] Keyboard-only: activate all buttons/links via Enter/Space
- [ ] Keyboard-only: Escape closes all modals/dropdowns
- [ ] Focus ring visible on every focusable element
- [ ] Focus order follows visual reading order
- [ ] Screen reader (NVDA/VoiceOver) announces each element meaningfully
- [ ] All color pairs verified ≥ 4.5:1 (body) / 3:1 (UI components)
- [ ] All form inputs have associated `<label>`
- [ ] Form errors appear as text, not only color
- [ ] `aria-invalid` set on inputs in error state
- [ ] Icon-only buttons have `aria-label`
- [ ] Decorative icons have `aria-hidden="true"`
- [ ] Skip link present on pages with navigation
- [ ] Dialogs trap focus + return focus on close
- [ ] `prefers-reduced-motion` respected (no animations at 0.01ms)
- [ ] Page usable at 200% zoom without horizontal scroll
- [ ] Touch targets ≥ 44×44px

---

## When this skill is not enough

- Full WCAG 2.1 AA success criteria per principle → `references/wcag-criteria.md`
- NVDA + VoiceOver keyboard shortcuts and test scripts → `references/screen-reader-testing.md`
- axe, Lighthouse, eslint-plugin-jsx-a11y setup → `references/testing-tools.md`
- Token values for focus-ring, contrast pairs → `jobbpilot-design-tokens`
- Component-specific ARIA patterns (Dialog, Table, Badge) → `jobbpilot-design-components`
- Accessible Swedish copy (error messages, labels) → `jobbpilot-design-copy`
- Why civic-utility demands high a11y → `jobbpilot-design-principles`
