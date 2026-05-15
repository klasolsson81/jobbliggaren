# JobbPilot — v2 Token + @theme Structure (globals.css)

This is the **implemented** v2 structure in
`web/jobbpilot-web/src/app/globals.css` — it is canonical. The pattern is:

1. `--jp-*` canonical palette defined once in `:root {}` (light).
2. Same `--jp-*` names overridden in `[data-theme="dark"] {}` (dark).
3. `@custom-variant dark` so Tailwind `dark:` targets `data-theme="dark"`.
4. `@theme inline {}` bridges `--jp-*` → semantic Tailwind utilities
   (`bg-surface-primary` etc.) using `var()` so light/dark follow at runtime.
5. A second `@theme {}` carries static scales (shadows, type sizes).
6. A shadcn bridge maps `--background`/`--primary`/… to `--jp-*`.

Do not duplicate the palette into a separate Tailwind config. Override token
names, not class sets.

---

## Skeleton (structure, abridged values — full hex in `tokens-full.md`)

```css
@import "tailwindcss";
@import "tw-animate-css";
@import "shadcn/tailwind.css";

/* Attribute-based dark mode (data-theme="dark" on <html>) — NOT .dark class.
   Tailwind v4 syntax. Matches ThemeProvider + ThemeScript. */
@custom-variant dark (&:where([data-theme="dark"], [data-theme="dark"] *));

/* ── Canonical palette (defined once) ───────────────────────── */
:root {
  /* Surfaces */
  --jp-surface-primary:   #FFFFFF;
  --jp-surface-secondary: #F8FAFC;
  --jp-surface-tertiary:  #F1F5F9;
  --jp-surface-sunken:    #F1F5F9;
  --jp-surface-inverse:   #0F172A;
  /* Text */
  --jp-text-primary:   #0F172A;
  --jp-text-secondary: #475569;
  --jp-text-tertiary:  #94A3B8;
  --jp-text-inverse:   #FFFFFF;
  /* Brand */
  --jp-brand-50:#EAF2FB; --jp-brand-100:#C8DDF1; --jp-brand-300:#6BA1DC;
  --jp-brand-500:#1F6EB8; --jp-brand-600:#0B5CAD; --jp-brand-700:#094B8C;
  --jp-brand-900:#062F57;
  /* Status (50/500/600/700 per family) */
  --jp-success-50:#ECFDF5; --jp-success-600:#059669; --jp-success-700:#047857;
  --jp-warning-50:#FFFBEB; --jp-warning-600:#D97706; --jp-warning-700:#B45309;
  --jp-danger-50:#FEF2F2;  --jp-danger-600:#DC2626;  --jp-danger-700:#B91C1C;
  --jp-info-50:#F1F5F9;    --jp-info-600:#475569;    --jp-info-700:#334155;
  /* Borders */
  --jp-border:#E2E8F0; --jp-border-strong:#CBD5E1;
  --jp-border-soft:#F1F5F9; --jp-border-hairline:#E2E8F0;
  /* Focus */
  --jp-focus:#0B5CAD;
  /* Radius */
  --jp-r-sm:2px; --jp-r-md:4px; --jp-r-lg:6px; --jp-r-pill:9999px;
  /* Typography — families from next/font (--font-sans/--font-mono) */
  --jp-font-sans: var(--font-sans), -apple-system, BlinkMacSystemFont,
                  "Segoe UI", system-ui, sans-serif;
  --jp-font-mono: var(--font-mono), "SF Mono", Menlo, Consolas, monospace;
  /* Density multiplier — set via [data-density] on <html> */
  --jp-density:1;
  --jp-row-h:     calc(36px * var(--jp-density));
  --jp-section-y: calc(28px * var(--jp-density));
  --jp-pad-x:     calc(28px * var(--jp-density));
  /* Shadows — only two */
  --jp-shadow-sm: 0 1px 2px rgba(0,0,0,0.04);
  --jp-shadow-md: 0 2px 4px rgba(0,0,0,0.06);
}

[data-density="compact"]  { --jp-density:0.85; }
[data-density="standard"] { --jp-density:1;    }
[data-density="luftig"]   { --jp-density:1.18; }

/* ── Dark mode (civic slate scale, no decorative hue) ───────── */
[data-theme="dark"] {
  --jp-surface-primary:#020617; --jp-surface-secondary:#0F172A;
  --jp-surface-tertiary:#1E293B; --jp-surface-sunken:#000000;
  --jp-surface-inverse:#F8FAFC;
  --jp-text-primary:#F8FAFC; --jp-text-secondary:#94A3B8;
  --jp-text-tertiary:#64748B; --jp-text-inverse:#0F172A;
  --jp-brand-50:#1E3A5F; --jp-brand-100:#1E40AF; --jp-brand-300:#60A5FA;
  --jp-brand-500:#3B82F6; --jp-brand-600:#60A5FA; --jp-brand-700:#BFDBFE;
  --jp-brand-900:#062F57;
  --jp-success-50:#052E1A; --jp-success-600:#4ADE80; --jp-success-700:#86EFAC;
  --jp-warning-50:#2A1D05; --jp-warning-600:#FBBF24; --jp-warning-700:#FDE68A;
  --jp-danger-50:#2E1014;  --jp-danger-600:#F87171;  --jp-danger-700:#FECACA;
  --jp-info-50:#1E293B;    --jp-info-600:#94A3B8;    --jp-info-700:#CBD5E1;
  --jp-border:#1E293B; --jp-border-strong:#334155;
  --jp-border-soft:#1E293B; --jp-border-hairline:#1E293B;
  --jp-focus:#60A5FA;
  --jp-shadow-sm: 0 1px 2px rgba(0,0,0,0.6);
  --jp-shadow-md: 0 2px 4px rgba(0,0,0,0.7);
}

/* ── Tailwind @theme inline — semantic utilities via var() ──── */
/* inline = utility resolves var() at runtime → light/dark follow. */
@theme inline {
  --color-surface-primary:   var(--jp-surface-primary);
  --color-surface-secondary: var(--jp-surface-secondary);
  --color-surface-tertiary:  var(--jp-surface-tertiary);
  --color-surface-sunken:    var(--jp-surface-sunken);
  --color-surface-inverse:   var(--jp-surface-inverse);
  --color-text-primary:   var(--jp-text-primary);
  --color-text-secondary: var(--jp-text-secondary);
  --color-text-tertiary:  var(--jp-text-tertiary);
  --color-text-inverse:   var(--jp-text-inverse);
  --color-brand-50:  var(--jp-brand-50);   /* …100/300/500/600/700/900 */
  --color-success-50:  var(--jp-success-50);  /* …600/700; warning/danger/info alike */
  --color-border-default: var(--jp-border);
  --color-border-strong:  var(--jp-border-strong);
  --color-border-brand:   var(--jp-brand-600);
  --color-focus-ring:        var(--jp-focus);
  --color-focus-ring-offset: var(--jp-surface-primary);
  --font-sans: var(--font-sans), -apple-system, BlinkMacSystemFont,
               "Segoe UI", system-ui, sans-serif;
  --font-mono: var(--font-mono), "SF Mono", Menlo, Consolas, monospace;
}

@theme {
  --shadow-sm: 0 1px 2px rgba(0,0,0,0.04);
  --shadow-md: 0 2px 4px rgba(0,0,0,0.06);
  --text-display:56px; --text-h1:28px; --text-h2:20px; --text-h3:18px;
  --text-h4:16px; --text-body-lg:16px; --text-body:14px; --text-body-sm:13px;
  --text-caption:12px; --text-label:13px; --text-mono:13px;
}

body {
  background-color: var(--jp-surface-primary);
  color: var(--jp-text-primary);
  font-family: var(--jp-font-sans);
  font-size:14px; line-height:1.55; letter-spacing:-0.005em;
  -webkit-font-smoothing:antialiased; -moz-osx-font-smoothing:grayscale;
}

*:focus-visible {
  outline: 2px solid var(--jp-focus);
  outline-offset: 2px;
  border-radius: var(--jp-r-sm);
}

@media (prefers-reduced-motion: reduce) {
  *, *::before, *::after {
    animation-duration: 0.01ms !important;
    animation-iteration-count: 1 !important;
    transition-duration: 0.01ms !important;
  }
}
```

---

## shadcn/ui bridge

shadcn tokens map to `--jp-*`, so light/dark follow automatically. Radii are
clamped to the civic scale (6px max, pill excepted).

```css
@theme inline {
  --color-background: var(--background);
  --color-foreground: var(--foreground);
  --color-primary:    var(--primary);
  /* …card/popover/secondary/muted/accent/destructive/border/input/ring,
     chart-1..5, sidebar-* … all → var(--…) */
  --radius-sm:2px; --radius-md:4px; --radius-lg:6px; --radius-xl:6px;
  --radius-pill:9999px;
}

:root {
  --background: var(--jp-surface-primary);
  --foreground: var(--jp-text-primary);
  --primary:    var(--jp-brand-600);
  --primary-foreground: #FFFFFF;
  --border:     var(--jp-border);
  --ring:       var(--jp-brand-600);
  /* …rest map to --jp-* … */
}

/* Dark inherits via --jp-* shift; only the primary foreground is set
   explicitly (dark text on light-blue primary in dark). */
[data-theme="dark"] {
  --primary-foreground:         #0F172A;
  --sidebar-primary-foreground: #0F172A;
}

@layer base {
  * { @apply border-border outline-ring/50; }
  body { @apply bg-background text-foreground; }
  html { @apply font-sans; }
}
```

The full verbatim block (including every status/brand step and the complete
shadcn map) is in `globals.css` — that file is the source of truth. This
reference documents the structure; copy the actual values from `globals.css` or
`tokens-full.md`.
