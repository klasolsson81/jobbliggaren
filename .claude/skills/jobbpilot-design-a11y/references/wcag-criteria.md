# JobbPilot — WCAG 2.1 AA Success Criteria Reference

Criteria relevant for web applications. Not an exhaustive WCAG copy —
focused on what applies to JobbPilot's UI patterns. Grouped by principle.

Conformance level: AA. AAA criteria noted where JobbPilot exceeds minimum.

---

## Principle 1: Perceivable

### 1.1 Text Alternatives

**1.1.1 Non-text Content (A)**
All non-text content has a text alternative.
- Images: `alt` attribute (empty `alt=""` for decorative)
- Icons conveying meaning: `aria-label` on parent button, `aria-hidden="true"` on icon
- Form inputs: `<label>`, or `aria-label` / `aria-labelledby`
- CAPTCHA: not used in JobbPilot

### 1.3 Adaptable

**1.3.1 Info and Relationships (A)**
Structure conveyed via presentation is also conveyed programmatically.
- Use `<table>` for tabular data, not CSS grid
- Use `<ul>/<ol>` for lists, not `<div>` with bullet characters
- Form field labels associated via `htmlFor`/`id`, not visual proximity

**1.3.2 Meaningful Sequence (A)**
Reading order matches DOM order — not just visual layout.
- Flex/Grid reordering (`order`, `flex-direction: row-reverse`) must not break
  the logical reading sequence for screen readers

**1.3.3 Sensory Characteristics (A)**
Instructions don't rely solely on shape, color, size, position.
- Never: "Click the red button" or "See the list on the right"
- Always: "Click 'Skicka ansökan'" (button label) or "Under Inställningar"

**1.3.4 Orientation (AA)**
Content not restricted to one orientation.
- Job search and application flows must work portrait and landscape

**1.3.5 Identify Input Purpose (AA)**
Input purpose can be determined programmatically.
- Use `autocomplete` attributes on identity fields:
  ```html
  <input type="email" autocomplete="email" />
  <input type="password" autocomplete="current-password" />
  <input name="firstName" autocomplete="given-name" />
  ```

### 1.4 Distinguishable

**1.4.1 Use of Color (A)**
Color is not the only means of conveying information.
- Error states: red border + error text (not border alone)
- Status badges: color + text label (not color alone)
- Links in body text: underline in addition to color

**1.4.3 Contrast (Minimum) (AA)**
- Normal text: 4.5:1
- Large text (≥ 18pt / ≥ 14pt bold): 3:1
- See verified pairs in SKILL.md §4 and `jobbpilot-design-tokens/references/contrast-table.md`

**1.4.4 Resize Text (AA)**
Text can be resized to 200% without loss of content or functionality.
- No text in images
- No `overflow: hidden` on containers that clip resized text
- Tested at 200% browser zoom

**1.4.10 Reflow (AA)**
Content reflows at 320 CSS pixels width (equivalent to 400% zoom on 1280px).
- No horizontal scrolling at 320px for main content
- Exception: data tables, code blocks (2D scrolling content)

**1.4.11 Non-text Contrast (AA)**
UI components and graphical objects: 3:1 against adjacent color.
- Input border (`--color-border-default` #D8D6D0 on white) = 1.4:1 — marginal.
  Compensated by focus ring (6.1:1) and error state (danger border 5.8:1).
- Use `border-border-strong` (#B8B6B0) in contexts where input border must
  meet 3:1 independently.

**1.4.12 Text Spacing (AA)**
Content/functionality retained when line height ≥ 1.5×, letter spacing ≥ 0.12em,
word spacing ≥ 0.16em, paragraph spacing ≥ 2×.
- Default `line-height: 1.55` in globals.css already satisfies this.
- Never clamp container height to `line-height` (text gets clipped).

**1.4.13 Content on Hover or Focus (AA)**
Tooltips and popovers: dismissible without moving pointer, persistent (not
auto-dismissed while hovering), hoverable (pointer can move over them).
- Tooltip component must implement this pattern.

---

## Principle 2: Operable

### 2.1 Keyboard Accessible

**2.1.1 Keyboard (A)**
All functionality available via keyboard. No keyboard trap (except modals with Escape exit).

**2.1.2 No Keyboard Trap (A)**
Focus can always move away via keyboard alone.
- Modal exception: Escape key must always break out.

**2.1.4 Character Key Shortcuts (A)**
Single-character shortcuts (if any) must be remappable or only active on focus.
- JobbPilot has no single-key shortcuts in v1 — not applicable currently.

### 2.4 Navigable

**2.4.1 Bypass Blocks (A)**
Skip link to main content required on pages with navigation.
- Implementation: see SKILL.md §6 (skip link code example)

**2.4.2 Page Titled (A)**
Each page has a descriptive `<title>`.
```tsx
// Use Next.js metadata API
export const metadata = { title: "Ansökningar | JobbPilot" }
// Format: "PageName | JobbPilot"
```

**2.4.3 Focus Order (A)**
Focus order preserves meaning and operability. See SKILL.md §2.

**2.4.4 Link Purpose (A)**
Link purpose clear from link text or context.
- Never: `<a href="/jobb/123">Läs mer</a>` in a list of multiple jobs
- Always: `<a href="/jobb/123">Visa Klarna Backend Engineer</a>` or
  use `aria-label` to augment generic text

**2.4.6 Headings and Labels (AA)**
Headings and labels describe topic or purpose. No decorative headings.

**2.4.7 Focus Visible (AA)**
Keyboard focus indicator visible. See SKILL.md §3. Never `outline: none`.

**2.4.11 Focus Appearance (AA — WCAG 2.2)**
Focus indicator: area ≥ CSS perimeter × 2px, contrast ≥ 3:1.
- JobbPilot focus ring: 2px outline, brand-600 on white = 6.1:1. Passes.

### 2.5 Input Modalities

**2.5.3 Label in Name (A)**
Accessible name of interactive elements contains visible label text.
- Button "Spara CV" → `aria-label` (if used) must include "Spara CV"
- Never: visible label "Spara CV", aria-label "confirm-save"

**2.5.5 Target Size (AAA — applied as standard)**
Touch target ≥ 44×44 CSS pixels. Treated as standard in JobbPilot. See SKILL.md §9.

---

## Principle 3: Understandable

### 3.1 Readable

**3.1.1 Language of Page (A)**
```html
<html lang="sv">
```

**3.1.2 Language of Parts (AA)**
If English text appears inline, mark it: `<span lang="en">API key</span>`.
Proper nouns (brand names) are exempt.

### 3.2 Predictable

**3.2.1 On Focus (A)**
Focus alone does not trigger a context change.
- Never submit a form when a field receives focus
- Never navigate away when an option is highlighted (but not selected)

**3.2.2 On Input (A)**
Changing a control's value does not automatically cause a context change
unless the user has been warned.
- Select dropdowns: changing value updates filter results (live region
  announces result count — not a full page navigation)

**3.2.3 Consistent Navigation (AA)**
Navigation landmarks consistent across pages.

**3.2.4 Consistent Identification (AA)**
Components with same function have same name. "Spara" always means save,
not sometimes "Bekräfta" or "OK".

### 3.3 Input Assistance

**3.3.1 Error Identification (A)**
Error described in text. Error item identified. See SKILL.md §10.

**3.3.2 Labels or Instructions (A)**
Labels or instructions provided for user input requiring a specific format.
- Password field: hint text "Minst 12 tecken" before error state

**3.3.3 Error Suggestion (AA)**
If error detected and suggestion known, suggestion provided.
- "E-postadressen har fel format." — tells what is wrong
- "Lösenordet måste vara minst 12 tecken." — tells how to fix

**3.3.4 Error Prevention (AA)**
For legal commitments and financial transactions: reversible, checked, or confirmed.
- "Avsluta konto" requires explicit confirmation dialog (Dialog with destructive button)
- "Radera CV" gives 30-day grace period

---

## Principle 4: Robust

### 4.1 Compatible

**4.1.1 Parsing (A)**
Valid HTML. No duplicate IDs. Proper nesting.

**4.1.2 Name, Role, Value (A)**
All UI components have accessible name, role, and state.
- shadcn components handle this by default for standard patterns
- Custom components: verify with axe before merging

**4.1.3 Status Messages (AA)**
Status messages programmatically determined without focus.
- Toast success → `role="status"` (polite announcement)
- Toast error → `role="alert"` (assertive announcement)
- Loading state → `aria-live="polite"` region (see SKILL.md §6)
