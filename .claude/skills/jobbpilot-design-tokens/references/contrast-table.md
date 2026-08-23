# JobbPilot — Contrast Ratio Table (v3 + G1 grön accent)

> **Canonical against `globals.css` (G1, ADR 0068).** Deliberately undated — a
> sync date decays silently and cannot be told from one that is still true.
> Re-derive with **all four** checks below. Run them over `.claude/skills/`, not
> just this skill: a value transcribed into a consumer skill is the same defect.
> Check 4 is the one that measures that — checks 1–3 cannot.
>
> **1. Pair check — does each token still hold the value stated?**
>
> ```bash
> G=web/jobbliggaren-web/src/app/globals.css
> grep -rhoE -- '--jp-[a-z0-9-]+ *: *#[0-9A-Fa-f]{6}' .claude/skills/ \
>   | tr -d ' ' | sort -u \
>   | while IFS=: read -r tok val; do
>       grep -qiE -- "$tok *: *$val *;" "$G" || echo "stale or orphan: $tok = $val"
>     done
> ```
>
> Expect **no rows**. Blind to any line that does not write `--token: #hex` — a
> table cell such as `| Warning | #A34A06 / #FBC267 |` names no token and is
> invisible here.
>
> **2. Orphan check — does each hex exist in `globals.css` at all?**
>
> ```bash
> G=web/jobbliggaren-web/src/app/globals.css
> grep -rhoiE '#[0-9A-F]{6}' .claude/skills/ | tr 'a-f' 'A-F' | sort -u \
>   | while read -r h; do [ "$(grep -ic "$h" "$G")" = 0 ] && echo "not in globals.css: $h"; done
> ```
>
> Read its output, do not count it: `#B4540B` (provenance, `mörkad från …`) and
> `#020617` (a negative citation, `INTE #020617`) are the expected hits.
>
> **Blind to a stale value that still appears in `globals.css` for any other
> reason at all** — as a `guard-allow` literal (`#97A4B8`, `#2C8A3F`), or inside
> a comment while its token has zero declarations (`#FFCD00`, `globals.css:266`).
> Read those as instances, not as the set.
>
> **3. Size check — the same question on the px axis.**
>
> ```bash
> G=web/jobbliggaren-web/src/app/globals.css
> grep -rhoE -- '--[a-z0-9-]+ *: *[0-9]+px' .claude/skills/ | tr -d ' ' | sort -u \
>   | while IFS=: read -r tok val; do
>       real=$(grep -oE -- "$tok: *[0-9]+px" "$G" | grep -oE '[0-9]+px' | sort -u)
>       [ -n "$real" ] && ! printf '%s\n' $real | grep -qx "$val" \
>         && echo "stale: $tok = $val (globals.css: $real)"
>     done
> ```
>
> Expect **no rows**. Checks 1 and 2 match hex only, so a size token is invisible
> to both — `--text-h1` sat at `28px` here while `globals.css:434` had said
> `32px` since #549, and neither check could see it (PR #1447). Values that are
> neither hex nor px are still unmeasured; read those against `globals.css` by
> hand.
>
> **4. Consumer check — does a file outside `jobbpilot-design-tokens` carry a
> value at all?**
>
> ```bash
> grep -rniE '#[0-9A-F]{6}' .claude/agents/ .claude/skills/ \
>   | grep -v '/jobbpilot-design-tokens/'
> ```
>
> Name the two directories rather than `.claude/`. The main checkout carries a
> full worktree under `.claude/worktrees/`, so `.claude/` would traverse another
> worktree's HEAD and report it as this one — the command counting itself, not
> merely running slowly. Do not "simplify" the two roots back to one.
>
> Expect only **preterite provenance** — today, the `#7C8AA0` inside *"darkened
> from … in issue #296"*. A present-tense row is a finding. Same test that keeps
> `perf-test-writer.md`'s dated path note: imperfect is a record, present tense
> is a claim.
>
> **The rule it enforces: a consumer file names the token, never the value that
> token holds — neither its hex nor a ratio measured from it.** WCAG's own
> thresholds (4.5:1, 3:1) are not values of our tokens and stay wherever they
> are useful.
>
> Checks 1–3 cannot be widened into this one. Check 1 requires `--token: #hex`
> and check 3 `--token: Npx`, while a consumer file writes `` `--token`
> (`#hex`) `` — a colon-free form neither matches. Check 2 does see those hexes,
> but asks only whether the value exists in `globals.css` at all, never whether
> the token still carries it, so a value that moved between tokens passes it
> silently. That gap is binding-blindness, not existence-blindness. Chasing the
> syntax costs a new regex per phrasing; removing the values ends the class.
>
> Blind to every value that is not a six-digit hex, so every value that is not
> a six-digit hex is swept by hand and stays unmeasured — a px size, a ratio, an
> `rgba()`, but equally a three- or four-digit hex and any other function form
> (`hsl()`, `oklch()`, `color-mix()`). The three named are examples, not the set.
> In the other direction, a six-digit issue reference would report as a false row.
>
> **No check is sufficient alone, and that is measured, not theoretical.**
> In PR #1447 three tokens were stale and check 2 reported *the same two hits on
> the broken tree as on the fixed one*. Check 1, run against the same broken
> tree, reported four — the three plus one nobody had named. Check 2 earns its
> place only for the table cells check 1 cannot see.

WCAG 2.1 AA requirements:
- Body text (< 18.66px bold, < 24px regular): **4.5:1 minimum**
- Large text (≥ 18.66px bold or ≥ 24px regular): **3:1 minimum**
- UI components, icons, and information-bearing dividers: **3:1 minimum**

Verify new combinations at https://webaim.org/resources/contrastchecker

> Light and dark are validated **separately**. A pair that passes in light is
> not assumed to pass in dark — recompute per theme.

---

## Light mode — verified pairs

| Text token | Background token | Ratio | WCAG | Notes |
|---|---|---|---|---|
| `ink-1` (#0C1A2E) | `surface` (#FFFFFF) | ~17.5:1 | AAA ✓ | Body text, rubriker |
| `ink-2` (#455366) | `surface` (#FFFFFF) | ~7.8:1 | AAA ✓ | Lede, metadata, mono-labels |
| `ink-3` / `--jp-text-tertiary` (#4F5D72) | `surface` (#FFFFFF) | ~6.7:1 | AA ✓ | Demoterad metadata-tier (mörkad från #7C8AA0/3.5:1, issue #296; min 5.45:1 över surfaces/info-bg) — placeholder = `--jp-placeholder` |
| `accent-700` (#15603F) | `surface` (#FFFFFF) | 7.56:1 | AAA ✓ | Länkar, aktiv nav, titlar, fokusring |
| `accent-700` (#15603F) | `canvas` (#F4F6FA) | ~7.0:1 | AAA ✓ | Länk på canvas |
| vit (#FFFFFF) | `accent-800` (#15603F) | 7.56:1 | AAA ✓ | Vit text på primärknapp (fill-kontraktet) |
| vit (#FFFFFF) | `accent-800-hover` (#1E6B4C) | ~6.4:1 | AA ✓ | Vit text på primärknapp-hover |
| `placeholder` (#626B78) | `surface` (#FFFFFF) | 5.39:1 | AA ✓ | Placeholder light-fält |
| `placeholder` (#626B78) | dark-input-fält (#F0F4FB) | 4.89:1 | AA ✓ | Placeholder i dark-temats ljusa fält (tema-oberoende token) |

## Light mode — borders / dividers

| Token | Against | Ratio | Notes |
|---|---|---|---|
| `border` (#C9D2E0) | `surface` (#FFFFFF) | ~1.5:1 | Synlig avgränsare men ej informationsbärande ensam |
| `border-input` (#7C8AA0) | `surface` (#FFFFFF) | ~3.5:1 | Input-vila — klarar 3:1 UI-golvet |
| `border-strong` (#7C8AA0) | `surface` (#FFFFFF) | ~3.5:1 | AA ✓ UI — klarar 3:1-golvet (höjt från #97A4B8/2.5:1, issue #193); delar nu värde med border-input |

## Light mode — status pairs

| Text token | Background token | Ratio | WCAG | Notes |
|---|---|---|---|---|
| `success` (#16793B) | `success-bg` (#DFF3E5) | ~4.7:1 | AA ✓ | Pill-text |
| `success` (#16793B) | `surface` (#FFFFFF) | ~5.5:1 | AA ✓ | Statusikon/text |
| `leaf-600` (#1C7530) | `leaf-50` (#DFF3E5) | ~5.0:1 | AA ✓ | "Ny"-tag / .jp-job__newflag (mörkad från #2C8A3F/3.76:1, issue #193) |
| `warning` (#A34A06) | `warning-bg` (#FCE9D1) | ~5.0:1 | AA ✓ | Pill-text (mörkad från #B4540B/4.2:1, issue #193) |
| `warning` (#A34A06) | `surface` (#FFFFFF) | ~5.9:1 | AA ✓ | Felfri som text på vit |
| `danger` (#BE1B1B) | `danger-bg` (#FBE0E0) | ~5.0:1 | AA ✓ | Pill-text |
| `danger` (#BE1B1B) | `surface` (#FFFFFF) | ~6.2:1 | AA ✓ | Felmeddelande-text |
| `info` (#1B5396) | `info-bg` (#DEE9F8) | ~6.3:1 | AA ✓ | Pill-text |
| `info` (#1B5396) | `surface` (#FFFFFF) | ~7.7:1 | AAA ✓ | Info-text |

---

## Dark mode — verified pairs

| Text token | Background token | Ratio | WCAG | Notes |
|---|---|---|---|---|
| `ink-1` (#F4F7FC) | `canvas` (#0B1525) | ~17.0:1 | AAA ✓ | Body text, rubriker |
| `ink-1` (#F4F7FC) | `surface` (#1B2B47) | ~13.2:1 | AAA ✓ | Text på kort |
| `ink-2` (#C2CFE2) | `canvas` (#0B1525) | ~11.6:1 | AAA ✓ | Sekundärtext |
| `accent-700` (#6EE7A8) | `canvas` (#0B1525) | 11.9:1 | AAA ✓ | Länkar, aktiv nav, fokus — **ENDAST text/länk/fokus/border, aldrig fill** |
| `accent-700` (#6EE7A8) | `surface` (#1B2B47) | ~9.2:1 | AAA ✓ | Länk på kort |
| `accent-600` (#A7F3D0) | `canvas` (#0B1525) | ~14.3:1 | AAA ✓ | Länk-hover |
| vit (#FFFFFF) | `accent-800` (#15603F) | 7.56:1 | AAA ✓ | Primärknapp i dark — accent-800 skiftas EJ |
| mörk text (#0C1A2E) | dark-input-fält (#F0F4FB) | ~15.8:1 | AAA ✓ | Ljusa input-fält i dark (user-krav) |

## Dark mode — status pairs

| Text token | Background token | Ratio | WCAG | Notes |
|---|---|---|---|---|
| `success` (#5DD894) | `success-bg` (#143E29) | ~6.7:1 | AA ✓ | Pill-text |
| `warning` (#FBC267) | `warning-bg` (#3F2A0B) | ~8.4:1 | AAA ✓ | Pill-text |
| `danger` (#FB8989) | `danger-bg` (#3F1419) | ~6.8:1 | AA ✓ | Pill-text |
| `info` (#8FBEEF) | `info-bg` (#1B3358) | ~6.6:1 | AA ✓ | Pill-text |

---

## Gradient-ytor (hero-plattan, pagehero, land-hero)

Gradienten är tema-stabil (samma i light + dark). Fokusringen i
gradient-scope är **VIT** (`--jp-focus: #FFFFFF`) — grön ring syns inte
mot grönt. **Undantag: ytor inne i plattan som inte själva är gradient** vänder
tillbaka till en ring, eftersom vitt är osynligt mot dem.

| Text | Background | Ratio | WCAG | Notes |
|---|---|---|---|---|
| vit (#FFFFFF) | `hero-from` (#0B2A1E) | ~15.4:1 | AAA ✓ | Gradient-start |
| vit (#FFFFFF) | `hero-mid` / `hero-bg` (#14503A) | ~9.4:1 | AAA ✓ | Gradient-mitt = solid ankare |
| vit (#FFFFFF) | `hero-to` (#1E6B4C) | ~6.4:1 | AA ✓ | Gradient-slut — sämsta stoppet, fortfarande AA |
| `hero-pill-ink` (#0C1A2E) | `hero-pill-bg` (#FFFFFF) | ~17.5:1 | AAA ✓ | Tema-stabila vita kontroller i plattan |
| vit (#FFFFFF) | `hero-sok-bg` (#0C1A2E) | ~17.5:1 | AAA ✓ | Sök-knapp (ink, INTE grön) |
| `--jp-focus` = `accent-800` (#15603F) | `accent-50` (#E9F2ED) | ~6.6:1 | AA ✓ | **Femte fokus-scopet** (`.jp-pagehero__helpedctl`). Pillen hålls ljus i BÅDA teman av `.jp-pagehero__inner`s tema-pin — utan den är `accent-50` i dark `#0E2A1E` och ringen faller till ~2.0:1, så pinnen är bärande för den här raden. Plattans VITA ring är osynlig mot en ljus pill. `accent-800` dark-skiftas aldrig ⇒ håller i båda teman. `accent-700` får **inte** användas här: dess dark-värde `#6EE7A8` mot samma pill = ~1.35:1, WCAG 2.4.7-fail |

---

## Pairs that FAIL — do not use

| Text | Background | Issue |
|---|---|---|
| vit text | `accent-700` dark (#6EE7A8) som fill | ~1.5:1 — kontraktsbrott. #6EE7A8 är ENDAST text/länk/fokus/border; fill = `accent-800` (skiftas EJ). |
| Ljus knapp + mörk text som "primary" | — | Bryter knapp-kontraktet (ADR 0068): primärknapp är alltid mörkgrön accent-800 + vit text, båda teman. |
| Grön fokusring | gradient-ytor | Syns inte mot grönt — gradient-scope sätter `--jp-focus: #FFFFFF`. |
| `border` (hairline-bruk) | som ensam informationsbärande avgränsare | ~1.5:1 light — komplettera med text/ikon eller starkare separation. |

---

## Adding new color combinations

Before shipping any new text/background pair not in this table:
1. Check ratio at https://webaim.org/resources/contrastchecker
2. Verify the threshold for its use case (body = 4.5:1, large/UI = 3:1)
3. Verify **in both light and dark** — they are validated separately
4. Add to this table
5. Flag to `design-reviewer` for confirmation

Ratios are computed values; treat any pair within ~0.2 of a threshold as
borderline and re-check with the live checker before shipping.
