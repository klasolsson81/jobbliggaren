# JobbPilot — Full Component Variant Specs

All states per component variant. Reference when building a new variant or
verifying that a custom override matches the design system.

Token names reference `jobbpilot-design-tokens`. Hex values in `tokens-full.md`.

---

## Button

### Sizes

| Size | Height | Padding | Font |
|---|---|---|---|
| `sm` | 32px (`h-8`) | `px-3 py-1.5` | `text-body-sm` (13px) |
| `md` | 36px (`h-9`) | `px-4 py-2` | `text-body` (14px) — **default** |
| `lg` | 44px (`h-11`) | `px-5 py-2.5` | `text-body-lg` (16px) |

### Variant states

**primary**
| State | Classes |
|---|---|
| Default | `bg-brand-600 text-white border border-brand-600` |
| Hover | `bg-brand-700 border-brand-700` |
| Active | `bg-brand-900` |
| Disabled | `opacity-50 cursor-not-allowed` |
| Focus | `ring-2 ring-brand-600 ring-offset-2` |
| Loading | `disabled` + spinner, preserve width |

**secondary**
| State | Classes |
|---|---|
| Default | `bg-surface-secondary text-text-primary border border-border-default` |
| Hover | `bg-surface-tertiary border-border-strong` |
| Disabled | `opacity-50 cursor-not-allowed` |
| Focus | `ring-2 ring-brand-600 ring-offset-2` |

**ghost**
| State | Classes |
|---|---|
| Default | `bg-transparent text-text-primary` |
| Hover | `bg-surface-secondary` |
| Disabled | `opacity-50 cursor-not-allowed` |

**destructive**
| State | Classes |
|---|---|
| Default | `bg-danger-600 text-white border border-danger-600` |
| Hover | `bg-danger-700 border-danger-700` |
| Disabled | `opacity-50 cursor-not-allowed` |
| Focus | `ring-2 ring-danger-600 ring-offset-2` |

**link**
| State | Classes |
|---|---|
| Default | `text-brand-600 underline-offset-4` |
| Hover | `underline` |
| Visited | `text-brand-700` |

---

## Input / Textarea / Select

### States

| State | Border | Other |
|---|---|---|
| Default | `border-border-default` | — |
| Hover | `border-border-strong` | — |
| Focus | `border-brand-600 ring-2 ring-brand-100` | — |
| Error | `border-danger-600` | Error message below in `text-danger-700 text-body-sm` |
| Disabled | `opacity-50 cursor-not-allowed bg-surface-tertiary` | — |
| Read-only | `bg-surface-secondary border-border-default` | — |

### Label

```
font-size: text-label (13px, weight 500)
margin-bottom: mb-1.5 (6px)
color: text-text-primary
```

Required indicator: asterisk `*` after label text in `text-danger-600`.

### Help text

```
font-size: text-body-sm (13px)
margin-top: mt-1 (4px)
color: text-text-secondary
```

---

## Card

### Default card

```
bg-surface-primary
border border-border-default
rounded-md (4px)
p-4
```

Card header (when used): `text-h3 mb-3`
No shadow — depth from border only.

### Compact card

```
bg-surface-primary
border border-border-default
rounded-md
p-3
```

For entity lists where vertical density matters.

### Interactive card (clickable row)

```
cursor-pointer
hover:bg-surface-secondary
transition-colors duration-150
```

Always has a visible keyboard focus state (focus-visible ring).

---

## Table

### Structure classes

```
Head row:    bg-surface-secondary text-text-secondary text-label h-9 px-3
Body row:    h-11 px-3 border-b border-border-default hover:bg-surface-secondary
Selected:    bg-brand-50 border-l-2 border-brand-600
Sorted col:  text-text-primary (vs unsorted: text-text-secondary)
Sort arrow:  text-text-tertiary (inactive) / text-brand-600 (active direction)
```

No alternating zebra rows. No uppercase headers.

### Pagination

```
text-body-sm text-text-secondary
"Visar 21–40 av 156"
Previous/Next buttons: Button ghost sm
Page numbers (if shown): max 7 visible, ellipsis for truncation
```

---

## Badge

### Structure

```
rounded-pill px-2 py-0.5 text-xs font-medium (500)
```

### All variants

| Variant | Background | Text |
|---|---|---|
| Success | `bg-success-50` | `text-success-700` |
| Warning | `bg-warning-50` | `text-warning-700` |
| Danger | `bg-danger-50` | `text-danger-700` |
| Info | `bg-info-50` | `text-info-700` |
| Brand | `bg-brand-50` | `text-brand-700` |
| Neutral | `bg-surface-tertiary` | `text-text-secondary` |

Application status → Badge variant mapping:

| Status | Variant |
|---|---|
| Draft | Info |
| Submitted / Active | Brand |
| Acknowledged | Success |
| InterviewScheduled / Interviewing | Warning |
| OfferReceived / Accepted | Success |
| Rejected / Ghosted | Danger |
| Withdrawn | Neutral |

---

## Dialog

### Sizes

| Size | max-width | Use |
|---|---|---|
| Confirmation | `max-w-sm` (400px) | "Are you sure?" dialogs |
| Default | `max-w-lg` (560px) | Single-field edits |
| Large | `max-w-2xl` (720px) | Multi-field forms in modal |

### Structure classes

```
Overlay:  bg-black/45
Panel:    bg-surface-primary rounded-lg (6px) border border-border-default
Header:   px-6 pt-6 pb-0
Body:     px-6 py-4
Footer:   px-6 pb-6 flex justify-end gap-2
Close:    absolute top-4 right-4, Button ghost size-sm
```

---

## Toast

### Structure

```
max-w-sm
border border-border-default
rounded-md p-3
text-body-sm
```

Per variant: same 50/700 color pattern as Badge.
Position: `top-right` (desktop) / `bottom-center` (mobile).
Animation: fade + slide-in 150ms — no bounce.

---

## Skeleton

```
bg-surface-tertiary rounded-md animate-none
```

No shimmer. Use to approximate the shape of loading content:
- Text line: `h-4 w-48`
- Table row: `h-11 w-full`
- Card: match card dimensions

---

## Alert

### Variants

| Variant | Border | Icon | Text |
|---|---|---|---|
| Default / Info | `border-border-default` | — | `text-text-primary` |
| Success | `border-success-600` left accent | CheckCircle | `text-success-700` |
| Warning | `border-warning-600` left accent | AlertTriangle | `text-warning-700` |
| Danger | `border-danger-600` left accent | XCircle | `text-danger-700` |

Structure: `rounded-md p-4 border`. Left-accent variant: add `border-l-4`.
