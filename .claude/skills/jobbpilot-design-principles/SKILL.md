---
name: jobbpilot-design-principles
description: >
  Loads JobbPilot's civic-utility design philosophy and do/don't rules. Use
  this skill whenever design direction, UI tone, visual treatment, aesthetic
  fit, or comparison to other products is discussed. Triggers on: design, tone,
  civic, utility, aesthetic, look, feel, brand, philosophy, direction, visual,
  GOV.UK, 1177, Digg, Stripe Dashboard, Linear, Vercel, Notion, glasmorfism,
  gradient, glow, AI-aesthetics.
---

# JobbPilot Design Principles

> **Canonical reference** for JobbPilot's civic-utility design philosophy.
> Deeper specs live in companion skills:
> - Design tokens (colors, typography, spacing) → `jobbpilot-design-tokens`
> - Component behavior → `jobbpilot-design-components`
> - Swedish copy patterns → `jobbpilot-design-copy`
> - Accessibility (WCAG, keyboard, focus) → `jobbpilot-design-a11y`
>
> This skill is the **why**. The others are the **how**.

---

## Core principle

JobbPilot is a tool for stressed job-seekers. The UI signals **trust and
reliability** — it does not impress or entertain. The target user is a
55-year-old process operator in Alingsås looking for her next job. She should
feel the app is built to function, not to sell.

Every design decision answers one question: **does this help a stressed user,
or does it add cognitive load?**

---

## Reference aesthetics — what JobbPilot should feel like

| Product | What to borrow |
|---|---|
| **GOV.UK Design System** | Typographic hierarchy, content-first, minimal decoration |
| **Digg / Sveriges designsystem** | Swedish civic precedent, institutional credibility |
| **1177 Vårdguiden** | Safe, legible, accessible — never intimidating |
| **Stripe Dashboard** | Information density without visual chaos |
| **Mercury Bank** | Utility over branding — the product IS the interface |

---

## Anti-references — what JobbPilot must NOT feel like

| Product | Why it is wrong for JobbPilot |
|---|---|
| **Vercel / Linear / Arc** | Too trendy, too "vibe" — signals startup coolness, not public utility |
| **Notion** | Too playful — wrong emotional register for job stress |
| **Default shadcn/ui out-of-box** | Standard AI-app look — civic-utility requires deliberate override |

---

## Do / don't quick reference

| ✅ Ja | ❌ Nej |
|-------|--------|
| Light background default | Dark mode default |
| Myndighetsblå primary color | Neon, purple, cyan accents |
| Direct Swedish copy | Emojis, exclamation marks, "Let's go!" |
| Tables and lists | Card layouts everywhere |
| `border-radius: 4px` | 16px+ rounded corners |
| Muted status colors | Glow, drop shadow, glassmorphism |
| Breadcrumbs + hierarchy | Flat pages without context |
| Systemfont / Hanken Grotesk | Display fonts, scripts |
| Content-first pages | Hero sections, vibey microcopy |
| Quantified information | Vague "positive" feedback |
| Solid backgrounds via tokens | Gradient backgrounds |
| `shadow-sm` maximum | `shadow-2xl`, `shadow-3xl` |

---

## Decision framework

When a design decision is unclear, apply in order:

1. **Trust vs trend?** Choose trust.
2. **Will this look dated in 5 years, or will it feel like a public utility?**
   Choose utility.
3. **Does this help a stressed user, or does it add cognitive load?**
   Choose helping.
4. **If Digg or 1177 wouldn't do it, we don't either** — unless there is a
   documented exception via ADR.

---

## Tone identity (one sentence)

JobbPilot is Sweden's 1177 for job applications: authoritative, calm,
accessible, and built to be trusted — not admired.

---

## When this skill is not enough

Load the companion skill for the specific question:

- Exact token values (colors, type scale, spacing grid) → `jobbpilot-design-tokens`
- How a specific component (Button, Card, Table, Input) should behave →
  `jobbpilot-design-components`
- Swedish copy patterns, empty states, error messages → `jobbpilot-design-copy`
- WCAG requirements, keyboard navigation, focus management → `jobbpilot-design-a11y`
