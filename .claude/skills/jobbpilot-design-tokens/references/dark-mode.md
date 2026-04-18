# JobbPilot — Dark Mode Tokens (v2 roadmap)

> **Status: NOT ACTIVE in v1.** Dark mode is deferred to v2+.
>
> In v1, JobbPilot always renders in light mode. We explicitly refuse
> `prefers-color-scheme: dark` auto-theming and inform users that dark mode
> is on the roadmap.

---

## v1 stance

```css
/* globals.css — v1 explicit light lock */
@media (prefers-color-scheme: dark) {
  /* intentionally empty — dark mode not implemented in v1 */
}
```

Users who prefer dark mode see a light UI. This is a deliberate product
decision, not an oversight. Inform users via settings when the feature ships.

---

## Planned v2 dark mode tokens (from DESIGN.md §2.4)

These are design intent, not shipped values. Verify and adjust when dark mode
implementation begins.

### Surfaces (dark)

| Token | Planned hex | Note |
|---|---|---|
| `--color-surface-primary` | `#0F1014` | Main background |
| `--color-surface-secondary` | `#181920` | Secondary panels |
| `--color-surface-tertiary` | `#21222B` | Hover states |
| `--color-surface-inverse` | `#F5F5F2` | Inverted (light elements on dark) |

### Text (dark)

| Token | Planned hex | Note |
|---|---|---|
| `--color-text-primary` | `#F5F5F2` | Body text |
| `--color-text-secondary` | `#B8B6B0` | Secondary |
| `--color-text-tertiary` | `#7A7874` | Tertiary |
| `--color-text-inverse` | `#1A1A1A` | |

### Brand (dark)

Brand-600 (`#0B5CAD`) is used sparingly on dark backgrounds. Verify contrast
against planned dark surfaces before implementation — the light-mode value may
need adjustment.

### Requirements when implementing

1. All existing contrast ratios must be re-verified for dark surfaces
2. `prefers-reduced-motion` must still be respected
3. System preference should be the default; user override stored in DB
   (not `localStorage` — XSS risk per security-auditor rules)
4. `design-reviewer` must approve the full dark theme before merge
5. Accessibility: WCAG AA remains the floor — dark mode does not lower the bar

---

## Implementation notes for v2

- Use CSS custom property override in `[data-theme="dark"]` selector
- Do not use separate Tailwind config — override the same token names
- shadcn/ui supports CSS variable theming; the same component tree works for
  both themes if tokens are properly mapped
- Test with NVDA + Windows high-contrast mode as well as standard dark mode
