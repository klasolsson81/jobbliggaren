# JobbPilot — v3/G1 Token + @theme Structure (globals.css)

> **Synkad mot `globals.css` 2026-06-10 (G1, ADR 0068).**

This is the **implemented** structure in
`web/jobbpilot-web/src/app/globals.css` — that file is canonical. The pattern:

1. `--jp-*` v3-kanon (ink/surface/canvas/accent/status/hero) defined once in
   `:root {}` (light).
2. Same `--jp-*` names overridden in `[data-theme="dark"] {}` (dark).
   **Accent-800/800-hover/900 och hero-tokens omdefinieras INTE** (tema-
   stabila per knapp-kontraktet/ADR 0068).
3. `@custom-variant dark` so Tailwind `dark:` targets `data-theme="dark"`.
4. **v2-alias-bryggan**: `--jp-surface-primary`/`--jp-text-primary`/
   `--jp-brand-*` är tunna alias → v3-kanon (branch-by-abstraction). G1
   alias-flip: `--jp-brand-600` → `--jp-accent-800`, `--jp-brand-700` →
   `--jp-accent-700` osv. — Tailwind-@theme-bryggan + shadcn följer gratis.
5. `@theme inline {}` bridges aliasen → semantic Tailwind utilities
   (`bg-surface-primary` etc.) using `var()` so light/dark follow at runtime.
6. A second `@theme {}` carries static scales (shadows, type sizes).
7. A shadcn bridge maps `--background`/`--primary`/… to `--jp-*`.

Do not duplicate the palette into a separate Tailwind config. Override token
names, not class sets.

---

## Skeleton (structure, abridged — full hex in `tokens-full.md`)

```css
@import "tailwindcss";
@import "tw-animate-css";
@import "shadcn/tailwind.css";

/* Attribute-based dark mode (data-theme="dark" on <html>) — NOT .dark class. */
@custom-variant dark (&:where([data-theme="dark"], [data-theme="dark"] *));

/* ── v3-kanon (ADR 0052) + G1 grön accent (ADR 0068) ───────── */
:root {
  /* Navy — utan konsument sedan ADR 0070 (kompassen pensionerad); städas F-städ */
  --jp-navy-900:#08213F; --jp-navy-800:#0A2647; --jp-navy-700:#133F73;
  --jp-navy-600:#1B5396; --jp-navy-500:#2E6CC2; --jp-navy-300:#7FA9DF;
  --jp-navy-100:#D6E3F4; --jp-navy-50:#EAF1FA;

  /* Accent — mörkgrön (G1). 800/800-hover/900 dark-skiftas ALDRIG. */
  --jp-accent-900:#0B2A1E;
  --jp-accent-800:#15603F;        /* FILL: primärknapp, checked */
  --jp-accent-800-hover:#1E6B4C;  /* fill-hover båda teman */
  --jp-accent-700:#15603F;        /* TEXT/BORDER: länkar, aktiv nav, fokus */
  --jp-accent-600:#1E6B4C; --jp-accent-500:#2E8B63; --jp-accent-300:#74C29A;
  --jp-accent-100:#D3E7DC; --jp-accent-50:#E9F2ED;
  --jp-gold:#E8C77B;              /* signatur — sigillets guldrad (--jp-mark-accent, ADR 0070) */

  /* Surfaces + canvas */
  --jp-surface:#FFFFFF; --jp-surface-2:#F4F6FA; --jp-surface-3:#E8EDF4;
  --jp-canvas:#F4F6FA;

  /* Text */
  --jp-ink-1:#0C1A2E; --jp-ink-2:#455366; --jp-ink-3:#4F5D72;  /* ink-3 mörkad i #296 (var #7C8AA0) */
  --jp-ink-inverse:#FFFFFF;

  /* Borders (synliga, inte hairlines) */
  --jp-border:#C9D2E0; --jp-border-soft:#E3E8F0; --jp-border-strong:#97A4B8;
  --jp-border-input:#7C8AA0;

  /* Status */
  --jp-success:#16793B; --jp-success-bg:#DFF3E5;
  --jp-warning:#B4540B; --jp-warning-bg:#FCE9D1;
  --jp-danger:#BE1B1B;  --jp-danger-bg:#FBE0E0;
  --jp-info:#1B5396;    --jp-info-bg:#DEE9F8;

  /* Dekorativa accenter */
  --jp-leaf-600:#2C8A3F; --jp-leaf-50:#DFF3E5;
  --jp-coral-600:#DA2A47; --jp-coral-50:#FCE4E9;
  --jp-amber-500:#E89A1A; --jp-amber-50:#FBEBC8;

  /* Hero (G1 "F4 Hybrid" — gradient ENBART hero/pagehero/empty-brand/land-hero) */
  --jp-hero-from:#0B2A1E; --jp-hero-mid:#14503A; --jp-hero-to:#1E6B4C;
  --jp-hero-gradient: linear-gradient(118deg, var(--jp-hero-from) 0%,
                      var(--jp-hero-mid) 60%, var(--jp-hero-to) 100%);
  --jp-hero-bg:#14503A;            /* SOLID ankare */
  --jp-hero-ink:#FFFFFF; --jp-hero-ink-soft:rgba(255,255,255,0.78);
  --jp-hero-pill-bg:#FFFFFF; --jp-hero-pill-ink:#0C1A2E;
  --jp-hero-pill-border:#CBD5E1; --jp-hero-sok-bg:#0C1A2E;

  /* Placeholder — WCAG AA mot både #FFFFFF och #F0F4FB, tema-oberoende */
  --jp-placeholder:#626B78;

  /* Focus — följer accent-700 (#15603F light → #6EE7A8 dark automatiskt) */
  --jp-focus: var(--jp-accent-700);

  /* Radius (ADR 0052: 6 rad/kort, 4 inputs, 8 modal, 12 ENDAST hero) */
  --jp-r-sm:4px; --jp-r-md:6px; --jp-r-lg:8px; --jp-r-xl:12px;
  --jp-r-pill:9999px;

  /* Typography — families from next/font (--font-sans/--font-mono) */
  --jp-font-sans: var(--font-sans), -apple-system, BlinkMacSystemFont,
                  "Segoe UI", system-ui, sans-serif;
  --jp-font-mono: var(--font-mono), "SF Mono", Menlo, Consolas, monospace;

  /* Density multiplier — set via [data-density] on <html> */
  --jp-density:1;
  --jp-row-h:     calc(36px * var(--jp-density));
  --jp-section-y: calc(28px * var(--jp-density));
  --jp-pad-x:     calc(28px * var(--jp-density));

  /* Shadows (v3 — undantag: popover/modal får skugga) */
  --jp-shadow-card:  0 1px 2px rgba(15,27,45,0.05), 0 1px 0 rgba(15,27,45,0.04);
  --jp-shadow-pop:   0 10px 30px rgba(8,23,48,0.16), 0 2px 6px rgba(8,23,48,0.08);
  --jp-shadow-modal: 0 30px 80px rgba(8,23,48,0.35);
  --jp-shadow-sm: 0 1px 2px rgba(0,0,0,0.04);   /* v2-alias-nivå */
  --jp-shadow-md: 0 2px 4px rgba(0,0,0,0.06);   /* v2-alias-nivå */

  /* ── v2-kompat-alias → v3-kanon (städas efter nollkonsumtion) ── */
  --jp-surface-primary:var(--jp-surface); --jp-surface-secondary:var(--jp-surface-2);
  --jp-surface-tertiary:var(--jp-surface-3); --jp-surface-sunken:var(--jp-surface-2);
  --jp-surface-inverse:var(--jp-ink-1);
  --jp-text-primary:var(--jp-ink-1); --jp-text-secondary:var(--jp-ink-2);
  --jp-text-tertiary:var(--jp-ink-3); --jp-text-inverse:var(--jp-ink-inverse);
  /* G1 alias-flip: brand → accent (Tailwind + shadcn följer gratis) */
  --jp-brand-50:var(--jp-accent-50);   --jp-brand-100:var(--jp-accent-100);
  --jp-brand-300:var(--jp-accent-300); --jp-brand-500:var(--jp-accent-500);
  --jp-brand-600:var(--jp-accent-800); /* primary = fill-kontraktet, EJ dark-skiftad */
  --jp-brand-700:var(--jp-accent-700); /* länk/hover */
  --jp-brand-900:var(--jp-accent-900);
  --jp-brand-accent:#FFCD00;           /* kompass-prick — UTGÅR (ADR 0070) */
  /* status-alias: *-50 → *-bg; *-500/600/700 → bastoken (alla tre samma) */
  --jp-success-50:var(--jp-success-bg); --jp-success-600:var(--jp-success); /* … */
  --jp-border-hairline:var(--jp-border-soft);
  --jp-border-modal:var(--jp-border); --jp-border-structural:var(--jp-border);
}

[data-density="compact"]  { --jp-density:0.85; }
[data-density="standard"] { --jp-density:1;    }
[data-density="luftig"]   { --jp-density:1.18; }

/* ── Dark (mörk navy-grå canvas, ljusa input-fält) ──────────── */
[data-theme="dark"] {
  --jp-surface:#1B2B47; --jp-surface-2:#142136; --jp-surface-3:#283C5E;
  --jp-canvas:#0B1525;             /* mörk navy-grå, INTE svart */
  --jp-ink-1:#F4F7FC; --jp-ink-2:#C2CFE2; --jp-ink-3:#8DA0BD;
  --jp-ink-inverse:#0C1A2E;
  --jp-border:#44598A; --jp-border-soft:#2C3F65; --jp-border-strong:#6F86A8;
  --jp-border-input:#6F86A8;
  /* Navy-ramp ljusare i dark (LOGO-ONLY; 800/900 ej skiftade) */
  --jp-navy-700:#4F8AD0; --jp-navy-600:#6FA4E3; --jp-navy-500:#3D75B8;
  --jp-navy-300:#2C5894; --jp-navy-100:#1F3866; --jp-navy-50:#1F3866;
  /* Accent i dark: #6EE7A8 ENDAST text/länk/fokus/border — ALDRIG fill.
     800/800-hover/900 skiftas EJ (knapp-kontraktet). */
  --jp-accent-700:#6EE7A8; --jp-accent-600:#A7F3D0; --jp-accent-500:#3E8E68;
  --jp-accent-300:#2E5C46; --jp-accent-100:#0E2A1E; --jp-accent-50:#0E2A1E;
  /* Status — ljusare ramp mot mörk canvas */
  --jp-success:#5DD894; --jp-success-bg:#143E29;
  --jp-warning:#FBC267; --jp-warning-bg:#3F2A0B;
  --jp-danger:#FB8989;  --jp-danger-bg:#3F1419;
  --jp-info:#8FBEEF;    --jp-info-bg:#1B3358;
  --jp-leaf-600:#5BCB7B; --jp-leaf-50:#143E29;
  --jp-coral-600:#F47185; --jp-coral-50:#3A1722;
  /* --jp-focus omdefinieras EJ: var(--jp-accent-700) resolvar själv
     till #6EE7A8 via accent-skiftet. Hero-tokens omdefinieras EJ
     (gradienten är tema-stabil). */
  --jp-shadow-card:  0 1px 2px rgba(0,0,0,0.5), 0 1px 0 rgba(0,0,0,0.4);
  --jp-shadow-pop:   0 10px 30px rgba(0,0,0,0.55), 0 2px 6px rgba(0,0,0,0.4);
  --jp-shadow-modal: 0 30px 80px rgba(0,0,0,0.7);
  --jp-shadow-sm: 0 1px 2px rgba(0,0,0,0.6);
  --jp-shadow-md: 0 2px 4px rgba(0,0,0,0.7);
}

/* ── Tailwind @theme inline — semantic utilities via var() ──── */
@theme inline {
  --color-surface-primary:   var(--jp-surface-primary);
  /* …secondary/tertiary/sunken/inverse… */
  --color-text-primary:   var(--jp-text-primary);
  /* …secondary/tertiary/inverse… */
  --color-brand-50:  var(--jp-brand-50);   /* …100/300/500/600/700/900 */
  --color-success-50: var(--jp-success-50); /* …600/700; warning/danger/info alike */
  --color-border-default: var(--jp-border);
  --color-border-strong:  var(--jp-border-strong);
  --color-border-modal:   var(--jp-border-modal);
  --color-border-structural: var(--jp-border-structural);
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
  --text-h4:16px; --text-body-lg:17px; --text-body:16px; --text-body-sm:14px;
  --text-caption:13px; --text-label:14px; --text-mono:13px;
}

body {
  background-color: var(--jp-canvas);   /* baslager = canvas, EJ surface */
  color: var(--jp-text-primary);
  font-family: var(--jp-font-sans);
  font-size:16px; line-height:1.55; letter-spacing:-0.005em;
  -webkit-font-smoothing:antialiased; -moz-osx-font-smoothing:grayscale;
}

*:focus-visible {
  outline: 2px solid var(--jp-focus);
  outline-offset: 2px;
  border-radius: var(--jp-r-sm);
}

a { color: var(--jp-accent-700); }
a:hover { color: var(--jp-accent-600); }

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

shadcn tokens map to `--jp-*` (via v2-aliasen), so light/dark follow
automatically. Radii are clamped to the civic scale — `--radius-xl` cappas
till `--jp-r-lg` (8px); 12px är ENDAST hero (ej shadcn-primitiv).

```css
@theme inline {
  --color-background: var(--background);
  --color-foreground: var(--foreground);
  --color-primary:    var(--primary);
  /* …card/popover/secondary/muted/accent/destructive/border/input/ring,
     chart-1..5, sidebar-* … all → var(--…) */
  --radius-sm: var(--jp-r-sm);   /* 4px */
  --radius-md: var(--jp-r-md);   /* 6px */
  --radius-lg: var(--jp-r-lg);   /* 8px */
  --radius-xl: var(--jp-r-lg);   /* cappad — 12px är ENDAST hero */
  --radius-pill: var(--jp-r-pill);
}

:root {
  --background: var(--jp-surface-primary);
  --foreground: var(--jp-text-primary);
  --primary:    var(--jp-brand-600);   /* = accent-800, fill-kontraktet */
  --primary-foreground: #FFFFFF;
  --border:     var(--jp-border);
  --input:      var(--jp-border);
  --ring:       var(--jp-focus);       /* G1 WCAG-fix — skiftar i dark */
  --sidebar-ring: var(--jp-focus);
  /* …rest map to --jp-* … */
}

/* Dark ärvs via --jp-*-skiftet. --primary = accent-800 (EJ dark-skiftad)
   → vit foreground i BÅDA teman — aldrig "ljus knapp med mörk text". */
[data-theme="dark"] {
  --primary-foreground:         #FFFFFF;
  --sidebar-primary-foreground: #FFFFFF;
}

@layer base {
  * { @apply border-border outline-ring/50; }
  body { @apply bg-background text-foreground; }
  html { @apply font-sans; }
}
```

---

## Scoped overrides (längre ner i globals.css — medvetna undantag)

- **Gradient-fokus-scope:** `.jp-hero__plate, .jp-pagehero, .jp-empty--brand,
  .jp-land-hero { --jp-focus: #FFFFFF; }` — vit ring på gradient (grön syns
  inte mot grönt). `.jp-popover` återställer `var(--jp-accent-700)`.
- **Tema-stabilitets-pins:** `.jp-pagehero, .jp-empty--brand` pinnar
  `--jp-accent-50/-100` till light-värdena (vit-knapp-hover på gradienten).
- **Vit header i dark:** `[data-theme="dark"] .jp-header` (och `.jp-land-top`)
  pinnar om hela light-paletten scoped — inkl. light-accent `#15603F` och
  re-deklarerad `--jp-focus` (ärvs annars som färdigberäknat dark-värde).
- **Ljusa input-fält i dark:** `#F0F4FB` bg + `#0C1A2E` text — `.jp-input`-
  familjen + shadcn `data-slot`-fält (med `!important` mot Tailwind-kaskaden).

The full verbatim block (every token, alias and the complete shadcn map) is in
`globals.css` — that file is the source of truth. This reference documents the
structure; copy the actual values from `globals.css` or `tokens-full.md`.
