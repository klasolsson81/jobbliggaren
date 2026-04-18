# JobbPilot — Screen Reader Testing Playbook

Practical guide for developers testing with NVDA (Windows) and VoiceOver (Mac).
Not a full screen reader tutorial — focused on what you need to verify JobbPilot
components and critical user flows.

---

## Setup

### NVDA (Windows) — recommended for CI/dev cycle

Free, open source. Download: nvaccess.org/download

```
Install → enable "Start NVDA after login" → set speech rate to comfortable
Browser: Firefox (best NVDA compatibility), Chrome (good), Edge (good)
```

Quick on/off: `Ctrl+Alt+N` to launch, `NVDA+Q` to quit.

### VoiceOver (Mac)

Built in. No install needed.

```
On:   Cmd+F5
Off:  Cmd+F5 again (or hold Cmd+F5 for options)
Browser: Safari (native) — Chrome works but less consistent
```

---

## NVDA keyboard shortcuts

| Action | Keys |
|---|---|
| Start/stop reading | `NVDA+Down Arrow` / any key |
| Next element | `Tab` |
| Previous element | `Shift+Tab` |
| Read current line | `NVDA+Up Arrow` |
| Read all from here | `NVDA+Down Arrow` |
| Next heading | `H` (browse mode) |
| Next form field | `F` (browse mode) |
| Next button | `B` (browse mode) |
| Next landmark | `D` (browse mode) |
| Next link | `K` (browse mode) |
| Next list | `L` (browse mode) |
| Enter forms mode | `Enter` on a form field |
| Exit forms mode | `Escape` |
| Open Elements List | `NVDA+F7` (headings, links, landmarks) |

Browse mode = reading; Forms mode = typing into inputs. NVDA switches automatically.

---

## VoiceOver keyboard shortcuts

| Action | Keys |
|---|---|
| VO modifier key | `Ctrl+Option` (abbreviated VO) |
| Next element | `VO+Right Arrow` |
| Previous element | `VO+Left Arrow` |
| Start interaction | `VO+Shift+Down Arrow` |
| Stop interaction | `VO+Shift+Up Arrow` |
| Next heading | `VO+Cmd+H` |
| Next form control | `VO+Cmd+J` |
| Next link | `VO+Cmd+L` |
| Open Rotor | `VO+U` (navigate by type) |
| Click element | `VO+Space` |
| Read from cursor | `VO+A` |

---

## Common fallgropar

| Issue | Symptom | Fix |
|---|---|---|
| Missing label | Screen reader says "button" with no context | Add `aria-label` or visible `<label>` |
| Icon SVG read aloud | Announcer reads SVG path data or title | Add `aria-hidden="true"` to icon |
| Dialog not trapped | Tab escapes the modal | Ensure `Dialog` uses shadcn (Radix handles this) |
| Live region not announced | Filter results update silently | Wrap count in `role="status" aria-live="polite"` |
| Error not announced | User submits form, no error feedback | Add `role="alert"` to error message |
| Focus not returned | Close modal, focus disappears | shadcn Dialog returns focus to trigger — verify trigger is not unmounted |
| Heading level skipped | Screen reader navigation jumps oddly | Audit h1→h2→h3 order with axe |
| `aria-describedby` ID mismatch | Description not linked | Verify IDs match between input and description element |

---

## Test scripts for critical flows

### Login

1. Navigate to `/logga-in` with keyboard only
2. Tab to E-post field — screen reader announces "E-post, redigeringsfält"
3. Tab to Lösenord — announces "Lösenord, lösenordsfält"
4. Submit with empty fields — both errors announced via `role="alert"`
5. Fill in wrong credentials — server error announced
6. Fill in correct credentials — successful navigation (no announcement required for redirect)

### CV-uppladdning

1. Navigate to CV-section
2. Tab to "Ladda upp CV" button — announces "Ladda upp CV, knapp"
3. Activate file picker (Enter/Space)
4. Select a PDF — loading state "Laddar upp…" announced via live region
5. Success toast: "CV uppladdat, {filnamn} är nu din aktiva profil" announced

### Ansökan-submission

1. Navigate to job detail
2. Confirm breadcrumb is readable: `nav` with "Ansökningar / Klarna Backend Engineer"
3. Activate "Skicka ansökan" — confirmation dialog opens
4. Screen reader announces dialog title and description
5. Tab within dialog — only Avbryt + Skicka ansökan reachable
6. Escape closes dialog — focus returns to trigger button
7. Confirm submission — success message with timestamp announced

### Gmail-koppling (OAuth dialog)

1. Navigate to Inställningar → Integrationer
2. Tab to "Koppla Gmail" — announces "Koppla Gmail, knapp"
3. Activate — dialog opens with title "Koppla ditt Gmail-konto"
4. VoiceOver/NVDA reads full dialog description (permissions, privacy link)
5. "Fortsätt till Google" navigates to OAuth (outside JobbPilot scope)

---

## Pass criteria

A flow passes screen reader testing when:

- Every form label is announced when its field receives focus
- Every error is announced without requiring visual inspection
- Every dialog title is announced on open
- Every button has a meaningful name (no bare "knapp" or "länk")
- Live regions announce state changes (loading, result counts, toasts)
- Focus never disappears (no focus to `<body>` or unannounced location)
- No raw icon content, IDs, or internal strings announced to user
