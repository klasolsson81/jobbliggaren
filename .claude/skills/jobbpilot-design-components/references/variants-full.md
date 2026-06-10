# JobbPilot — Full Component Variant Specs

All states per component variant. Reference when building a new variant or
verifying that a custom override matches the design system.

Token names reference `jobbpilot-design-tokens`. Hex values in `tokens-full.md`.

---

## Button

Civic spec (matches `.jp-btn`): `border-radius: 4px` (`var(--jp-r-md)`),
`transition: background/border/color 80ms linear`, `letter-spacing: -0.005em`,
font weight 500 sans.

### Sizes

| Size | Height | Padding | Font |
|---|---|---|---|
| `sm` | 36px | `px-2.5` | 14px |
| `md` | **40px** (default) | `px-3` | 16px — **default** |

(Recalibrated per ADR 0038. Toolbar-knappar kvarstår som dokumenterat
undantag, 28px — touch bumps i toolbars hanteras via hit-area padding, se
a11y-skillen.)

### Variant states

**primary**
| State | Classes |
|---|---|
| Default | `bg-brand-600` (= accent-800 `#15603F`, EJ dark-skiftad per ADR 0068) + vit text i båda teman |
| Hover | `bg-[var(--jp-accent-800-hover)]` (`#1E6B4C`, EJ dark-skiftad — accent-700 är textfärg, aldrig fill i dark; ADR 0068) |
| Disabled | `opacity-50 cursor-not-allowed` |
| Focus | global `*:focus-visible` ring (2px `--jp-focus`, offset 2px) |
| Loading | `disabled` + label → "Sparar…", preserve width |

**secondary**
| State | Classes |
|---|---|
| Default | `bg-surface-primary text-text-primary border border-border-default` |
| Hover | `bg-surface-secondary border-border-strong` |
| Disabled | `opacity-50 cursor-not-allowed` |
| Focus | global focus-visible ring |

**ghost**
| State | Classes |
|---|---|
| Default | `bg-transparent text-text-secondary` |
| Hover | `bg-surface-tertiary text-text-primary` |

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

Civic spec (matches `.jp-input` / `.jp-select`, recalibrated per ADR 0038):
height **44px**, sm **40px**, `border-radius: 4px` (`var(--jp-r-md)`),
`bg-surface-primary` (white in light), font 16px. Beskrivande
placeholder-exempel i sök-/filterfält tas bort — label ovanför och hint
nedanför bär informationen. Auth-formulärens format-placeholders
(`din.email@exempel.se`) behålls (syntaxmönster med stark label-kontext).

### States

| State | Border | Other |
|---|---|---|
| Default | `border-border-default` (slate-200) | — |
| Focus | `border-brand-600` | `box-shadow: 0 0 0 3px var(--jp-brand-50)` (3px brand-50 ring) |
| Error | `border-danger-600` | Error message below in `text-danger-700` (14px) |
| Disabled | `opacity-50 cursor-not-allowed bg-surface-tertiary` | — |
| Read-only | `bg-surface-secondary border-border-default` | — |

### Label

```
font-size: text-label (14px, weight 500)
margin-bottom: mb-1.5 (6px)
color: text-text-primary
```

Required indicator: asterisk `*` after label text in `text-danger-600`.

### Help text

```
font-size: text-body-sm (14px)
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

---

## Civic-utility patterns (`.jp-*`)

Verbatim from `globals.css` / `JobbPilotNEWDESIGN/jobbpilot.css`. All colors via
`--jp-*`, so light/dark follow automatically.

### `.jp-table--flat` (print-ledger)

```
table:        width:100%; border-collapse:collapse; font-size:16px
thead th:     mono 11.5px, letter-spacing 0.14em, UPPERCASE,
              color text-secondary, weight 600, padding 12px 12px 10px,
              border-top 2px border-strong, border-bottom 1px border-strong
tbody td:     padding 14px 12px, border-bottom 1px border (hairline),
              color text-primary, vertical-align middle
tbody tr:hover td:   bg surface-tertiary
last row td:  border-bottom 2px border-strong (thicker bottom rule)
```

NO zebra. NO celled/per-cell borders. Hairlines between rows only; the frame is
the 2px top/bottom rule.

### `.jp-attention` (row feed, no box)

```
container:  margin-top 28px; border-top 1px border
row:        flex; gap 16px; padding 18px 4px; border-bottom 1px border
dot:        8px circle, margin-top 8px (status color)
text:       flex 1; 16px; line-height 1.55; max-width 68ch
link:       accent-700 underlined (dark: #6EE7A8 auto via token; hover accent-600 #A7F3D0 i dark), thicker underline on hover
dismiss:    22px ghost button, opacity 0 → 1 on row hover
```

No card, no shadow — hairlines only. Text capped at 68ch (newspaper column).

### `.jp-pipeline` / `.jp-col` / `.jp-appRow` (kanban as ledger)

```
pipeline:   grid repeat(4, minmax(0,1fr)); gap 0; border-top 1px border
col:        transparent; border-right 1px border-STRONG (column divider,
            stronger than row hairlines); min-height 360px
col:last-child:  no right border
col__head:  flex; padding 12px 14px 10px; border-bottom 1px border;
            title 14px/500, count mono 13px text-secondary (ADR 0038 — mono
            inline-data är aldrig tertiary)
appRow:     padding 12px 14px; border-bottom 1px hairline; transparent
appRow:hover:    bg surface-tertiary + inset 2px 0 border-strong
.jp-appCard:     display:none  (legacy — floating cards removed)
```

NO floating cards. Columns are visually separated by `--jp-border-strong`.

### `.jp-statusDot` (default in tables)

```
inline-flex; gap 8px; 16px; color text-primary; weight 400
dot: 6px circle, color per modifier
modifiers: --brand --info --success --warning --danger --neutral
```

No background, no border — lowest visual weight. First choice in table status
columns.

### `.jp-pill` (entity accent)

```
inline-flex; gap 6px; height 22px; padding 0 8px 0 7px; rounded-pill
font 11.5px/500; 6px dot
--info:    info-600 text on info-50 bg
--brand:   brand-700 on brand-50
--success: success-700 on success-50
--warning: warning-700 on warning-50
--danger:  danger-700 on danger-50
--neutral: text-secondary on surface-secondary, border-hairline
```

Use when status is an entity's headline at one point — not for dense table
columns (use `.jp-statusDot` there).

### `.jp-match` (score bar)

```
inline-flex; gap 8px; mono 13px; color text-secondary
bar:  72px × 6px; bg surface-tertiary; border-radius 2px
fill: brand-600   (default, score ≥ 75)
fill--mid:  info-600    (50–74)
fill--low:  warning-600 (< 50)
```

### `.jp-filterBar` (flat, no chrome box)

```
grid 1.4fr 1fr 1fr 1fr auto; gap 16px; align-items end; padding 18px 0
background transparent; border 0
border-top 1px border; border-bottom 1px border; border-radius 0
field: flex column gap 6px; label 14px/500 text-secondary;
       hint mono 13px text-secondary (ADR 0038 — informationsbärande
       hint är aldrig tertiary). Inga beskrivande placeholder-exempel
       i sök-/filterfält — label + hint bär informationen.
```

### `.jp-banner` (3px brand left border)

```
flex; gap 12px; padding 14px 16px
bg brand-50; border 1px brand-100; border-left 3px brand-600
border-radius var(--jp-r-md) (4px)
title 16px/500 text-primary; text 14px text-secondary
cta brand-700 underlined
```

Use sparingly — one non-blocking notice, not a decoration.
