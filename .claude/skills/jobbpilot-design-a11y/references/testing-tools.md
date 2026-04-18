# JobbPilot — Accessibility Testing Tools Setup

Deploy-ready configuration for the a11y toolchain. All tools are already
part of the dev workflow — this file is setup reference, not aspirational.

---

## 1. axe DevTools (browser extension)

Automated detection of ~57% of WCAG issues. Always the first scan.

**Install:**
- Chrome/Edge: Chrome Web Store → "axe DevTools"
- Firefox: Firefox Add-ons → "axe DevTools"

**Usage:**
1. Open page in browser
2. Open DevTools (F12) → "axe DevTools" tab
3. Click "Scan ALL of my page"
4. Fix every violation before merge — no exceptions

**axe severity levels and JobbPilot response:**

| axe level | Meaning | JobbPilot action |
|---|---|---|
| Critical | WCAG A violation | Fix immediately, blocks merge |
| Serious | WCAG AA violation | Fix immediately, blocks merge |
| Moderate | Best practice / minor barrier | Fix if < 30 min, document if larger |
| Minor | Enhancement | Optional, log as issue |

**Common false negatives** (axe can't catch — must test manually):
- Keyboard navigation order
- Focus ring visibility
- Color contrast on custom gradients
- Live region announcement timing
- Screen reader label quality

---

## 2. Lighthouse (built into Chrome DevTools)

Measures overall a11y score. Run per page. Target: ≥ 95.

**Usage:**
1. Open page in Chrome (incognito to avoid extension interference)
2. DevTools → Lighthouse tab
3. Select "Accessibility" category only (faster)
4. "Analyze page load"
5. Expand "Accessibility" section in report

**Run via CLI (for CI integration):**

```bash
pnpm add -D lighthouse

# Single page
npx lighthouse http://localhost:3000/logga-in \
  --only-categories=accessibility \
  --output=json \
  --output-path=./lighthouse-report.json

# Parse score
node -e "
  const report = require('./lighthouse-report.json')
  const score = report.categories.accessibility.score * 100
  console.log('Score:', score)
  if (score < 95) process.exit(1)
"
```

Add to CI pipeline (GitHub Actions step):
```yaml
- name: Lighthouse a11y audit
  run: |
    npx lighthouse ${{ env.BASE_URL }}/logga-in \
      --only-categories=accessibility \
      --output=json \
      --output-path=./lh-report.json
    node -e "const s=require('./lh-report.json').categories.accessibility.score*100; if(s<95){console.error('Score:',s,'< 95');process.exit(1)}"
```

---

## 3. eslint-plugin-jsx-a11y

Static analysis — catches a11y issues at write-time, before the browser.

**Install:**
```bash
pnpm add -D eslint-plugin-jsx-a11y
```

**Config (`.eslintrc.json` or `eslint.config.mjs`):**

```json
{
  "plugins": ["jsx-a11y"],
  "extends": ["plugin:jsx-a11y/recommended"],
  "rules": {
    "jsx-a11y/anchor-is-valid": ["error", {
      "components": ["Link"],
      "specialLink": ["hrefLeft", "hrefRight"],
      "aspects": ["invalidHref", "preferButton"]
    }],
    "jsx-a11y/no-autofocus": "warn",
    "jsx-a11y/interactive-supports-focus": "error",
    "jsx-a11y/click-events-have-key-events": "error",
    "jsx-a11y/no-static-element-interactions": "error"
  }
}
```

This runs in the pre-commit hook (Husky + lint-staged) — see CLAUDE.md §11.1.

**Rules that catch the most bugs:**
- `label-has-associated-control` — missing label association
- `interactive-supports-focus` — non-focusable interactive elements
- `click-events-have-key-events` — `onClick` without keyboard equivalent
- `alt-text` — images without alt
- `aria-props` — invalid aria- attributes

---

## 4. WAVE (Web Accessibility Evaluation Tool)

Browser extension that overlays visual accessibility markers on the page.
Useful for spotting structural issues and contrast at a glance.

**Install:** wave.webaim.org/extension

**Use for:**
- Quick visual audit of label associations (shows where labels connect to inputs)
- Spotting missing alt text on images
- Structural errors (heading order, landmark missing)

WAVE complements axe — they catch partially overlapping sets of issues.
Run both on new pages.

---

## 5. Colour Contrast Analyser

Desktop tool for spot-checking custom color pairs not covered by axe.

**Install:** paciellogroup.com/colour-contrast-analyser (Windows/Mac)

**When to use:**
- Before adding any new color combination not in the JobbPilot token set
- When a designer proposes a custom color outside DESIGN.md
- Verify `text-tertiary` use case is non-essential before applying

---

## Summary: which tool for which job

| Question | Tool |
|---|---|
| "Does this page have WCAG violations?" | axe DevTools |
| "What's the page's overall a11y score?" | Lighthouse |
| "Did I forget a label in my JSX?" | eslint-plugin-jsx-a11y (in editor) |
| "Is this color pair safe?" | Colour Contrast Analyser |
| "What does a screen reader actually say?" | NVDA / VoiceOver |
| "Can I navigate this with keyboard only?" | Manual test (unplug mouse) |
| "Are labels visually connected to their inputs?" | WAVE |
