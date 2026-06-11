# Design-review: Fas E2f — Klas rendered-feedback-fixar (branch `fix/sok-paritet-e2f-rendered-feedback`, commit a116cce)

**Status:** ⚠ Changes requested (1 Major — spec-sync, ej riktningen) → **Major delvis åtgärdad in-block; spec-edit-delen Klas-gated (se Åtgärds-trail)**
**Granskat:** 2026-06-11
**Auktoritet:** DESIGN.md, jobbpilot-design-tokens/a11y/copy-skills, ADR 0068 Beslut 1, ADR 0047
**FAS-DEFERRAL-MANIFEST:** bekräftat bindande — rendered pending live-deploy; chip-komposit = E2d; TD-108 egen touch; "Hela {länsnamn}"/dialogLabel = E2d-Minors.

### Fråga 1 — Tom-kolumn: **Godkänd.** Copy uppfyller empty-state-regeln (imperativ, konkret nästa steg, inget utropstecken); tomstart = Platsbanken-paritet som MINSKAR gissning (auto-vald grupp kunde misstas för aktivt val); wayfinding via grön prick (närvaro/frånvaro-cue, 1.4.1 OK) + chevron; ink-2-tomtext klarar 4.5:1 båda teman. FYI: "till vänster" är direktionellt — ses över om popovern enkolumns-staplas på mobil.

### Fråga 2 — Markerade kommun-rader: **Godkänd, accessible state är ärlig.** Raderna ÄR effektivt valda via region-id:t — visuellt och accessible state sammanfaller (att rendera dem omarkerade vore oärligt). "Minus en" följer exakt WAI-ARIA select-all-mönstret; URL-växlingen region-id ↔ materialisering är osynlig för användaren — rätt ställe att gömma komplexiteten. Kollaps-tillbaka = samma mönster baklänges. Materialiserings-volym verifierad mot backend-cap (47 < 400). Minor 4: "Hela länet"-raden saknar `aria-checked="mixed"` vid partiellt val (tri-state) — pre-existing E2b-arv, mer synligt nu.

### Fråga 3 — De-gröningen: komplett mot direktivet; alla sex flippar dark-säkra (ink-1-token själv-skiftar, ~16:1 light / ~13:1 dark). **Behåll grönt (interaktion/semantik):** `.jp-avatar` (menytrigger), tags/pills `--brand` (kategori-badges), notice-systemet (symmetri), `.jp-notice__cta` (länk), summary-hover (feedback), `.jp-save[data-saved]` (kontroll-state), `aria-selected`-rader (selektion per ADR 0068), `.jp-cv-banner__icon`. **Rester = Klas-dom (Minors):** `.jp-land-top__link.is-active` (text-only grön aktiv nav — flipp kräver medföljande icke-färg-indikator), `.jp-land-feature__key` (eyebrow-labels = information), `.jp-summary__row--highlight`-värdet (samma mönster som träffräknaren — om semantiskt match-indikator: behåll + dokumentera).

### Fråga 4 — Nav-aktiv: **Bär indikationen, 1.4.1 OK.** ::after-baren är form/positions-cue (ej färg-mot-färg) och klarar non-text-kontrast (≥3:1 båda teman); + weight 600 + aria-current = tre oberoende cues. Drawer = 4px vänster-border + transparent bas (ingen layout-shift). **Starkare än gamla läget** där grön text var dominerande cue.

### Blockers
Inga.

### Major

1. **Spec/token-dokumentation beskriver fel kontrakt:** globals.css:42-kommentaren, DESIGN.md rad 66, tokens-skillen rad 75 och ADR 0068 Beslut 1-tabellen säger fortfarande "titlar/aktiv nav = accent-700-text". G2/G3-precedensen kräver ADR 0068-dokumentation; utan sync ljuger token-tabellen för nästa agent ("titlar = accent-700" → regression av exakt det Klas rättade). globals.css + ADR kräver ingen spec-spärr; **DESIGN.md + tokens-skillen kräver Klas `approve-spec-edit.sh`.**

### Minor
1. `.jp-land-top__link.is-active` grön text-only aktiv nav — Klas-dom (flipp kräver bar-indikator). 2. `.jp-land-feature__key` → ink-2-kandidat. 3. `.jp-summary__row--highlight`-värdet — match-indikator eller information? Klas-dom. 4. tri-state `aria-checked="mixed"` — E2d-touchen.

### Bra gjort
CheckRow-semantiken ärlig i alla fyra övergångar; drawer-aktiv enligt boken; varje CSS-flip princip-motiverad; tomtext via token; testtäckningen följde beteendet hela vägen.

### Sammanfattning
0 blockers, 1 major (spec-sync), 4 minors. Utförandet i kod genomgående korrekt. Re-review ej nödvändig efter sync (mekanisk).

---

## Åtgärds-trail (huvud-CC, 2026-06-11)

| # | Fynd | Åtgärd |
|---|---|---|
| M1a | globals.css:42-kommentaren | Uppdaterad till nya kontraktet (samma PR). |
| M1b | ADR 0068-dokumentation | Implementerings-notat E2f tillagt i ADR 0068 (accent-700-text-rollens precisering + spec-sync-status). Samma PR. |
| M1c | DESIGN.md rad 66 + tokens-skillen rad 75 | **Spec-edit → Klas `approve-spec-edit.sh`** — lyft i E2f-rapporten/pending-listan; kan inte tas autonomt. |
| m1–m3 | De-grönings-rester | Klas-dom — listade i pending/startprompt. |
| m4 | tri-state mixed | E2d-touchen (med "Hela {länsnamn}"/dialogLabel-Minors). |
