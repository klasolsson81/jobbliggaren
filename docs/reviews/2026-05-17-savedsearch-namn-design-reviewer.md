# Design-review: saved-search-namn-frontend (ADR 0043 Approach A, commit 04b679e)

**Status:** APPROVED — 0 blocker / 0 major / 0 minor
**Granskat:** 2026-05-17 · design-reviewer (agentId `a3118277d607ecf84`)
**Auktoritet:** DESIGN.md §1.2/§3/§4/§6/§8/§9 + jobbpilot-design-copy/-a11y/-principles

## Kärnfråga: är concept-id-jargongen HELT borta från /sokningar?

**Ja, fullständigt — sista läckan ADR 0043 adresserade är stängd.** `criteriaSummary()` renderar `ssykLabels[].label`/`regionLabels[].label` (svenska namn); gamla `s.ssyk.join(", ")` (rå `MVqp_eS8_kDZ`) borttagen. git grep: noll concept-id i renderad produktionskod (alla träffar i .test.tsx-fixturer/kommentar). Fallback `: s.ssyk` endast om backend-labels saknas helt (graceful, bevakat av `not.toHaveTextContent("MVqp_eS8_kDZ")`).

## Per yta
- **Civic regel 3/7:** `<ul>/<li>` hairline-separerat, inga cards. Namn separerade med ` · `, läsbar svensk fras för §1.1-målanvändaren. Ingen gradient/glow/emoji/AI-klyscha.
- **Tokens/typografi:** font-mono-borttagningen KORREKT mot DESIGN.md §4 (mono = IDs/koder, aldrig brödtext; svenska namn = `text-body-sm`). Regressionsbevakad (`not.toHaveClass("font-mono")`). Inga hex/Tailwind-defaults. Empty state = konstatering + nästa steg (copy §1).
- **A11y:** `<ul aria-label="Sparade sökningar">`, semantiska `<li>`, label som ren JSX-text (ingen dangerouslySetInnerHTML — ADR 0043 FE-säkerhetsregel hålls). Inga kontrast-/fokus-/tangentbordsregressioner. E2E/visual-verify migrerade till roll/aria-label.
- **Copy:** "SSYK-kod"→"yrke", "region"→"län" — ren svenska, du-tilltal, ingen jargong. "län" mer precist för målanvändaren (JobTech `region` ≈ 21 län).
- **DTO/Zod:** additivt `.default([])`, `TaxonomyLabel.label` = z.string() konsumeras som text, ingen designyta.

## Verdict
Kod-review-grinden **APPROVED 0/0/0**. Mergeklar. Post-deploy skärmbilds-granskning av /sokningar (light+dark) görs när visual-verify körts (Batch 6-mönster).
