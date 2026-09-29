# Form round brief — #1918, Matchning edited one part at a time (report-only)

**Mode:** REPORT-ONLY — no edits, commits or pushes; leave the tree as found. Worktree (tracked files here, never
C:/DOTNET-UTB/JobbPilot itself, which is stale): `C:/DOTNET-UTB/JobbPilot/.claude/worktrees/oauth-google-1744-4b3a94`
at `cb9f00bb` (= main). A production build of main runs at http://localhost:3916 over a stub at
http://127.0.0.1:59916 (session cookie `__Host-jobbliggaren_session=x`, domain localhost; `GET /__mode?prefs=
filled|empty&put=ok|error|conflict|rateLimited`, `GET /__last` shows the last write body — restore
`profile=ok&prefs=filled&taxonomy=ok&put=ok` if you change it). Note: the taxonomy read is revalidate-cached, so
`taxonomy=error` does not reach a running server (the degraded state needs a fresh server).

## The directive (Klas, 2026-09-28)
"Matchning ska gå att ändra en del i taget: bara yrken, bara kompetenser, bara orter, bara anställningsformer eller
bara antal års erfarenhet. I dag öppnar en enda 'Lägg till' (`match-preferences-card.tsx`) en dialog med alla fyra
väljarna (`match-preferences-dialog.tsx`; erfarenheten ligger under kompetenserna). Spara skickar hela mängden
(`PUT /api/v1/me/match-preferences`, full replace), och det gör chip-borttagningen också. Formrundan
(dotnet-architect + design-reviewer) avgör två saker: hur varje del får en egen ingång till redigering av bara den
delen, och om skrivningen förblir full replace eller blir en skrivning per del. Väg in replay-beteendet i ADR 0146.
Förstagångsflödet (`match-setup-rail-modal.tsx`) ska fortsätta fungera. Issue 1914 (ett långt chip spiller ut vid
≤375 px) gäller samma kort." Klas's standing preference (2026-06-20): "modern civic utility" — removable chips and
clear add surfaces are wanted.

## r0 renders (C:/tmp/jobbliggaren-visual/1918/r0/, facts.json) — main, measured 2026-09-28
- The card: h2 "Matchning", five facet heads as `<p class="jp-popover__title">` (not headings): Yrken, Kompetenser,
  Orter, Anställningsformer, Antal års erfarenhet ("5 år", read-only), one "Lägg till" and one card-level status.
- Overflow (#1914): page scroll +170 at 320, +115 at 375, +100 at 390 (the long occupation "Undersköterskor,
  hemtjänst, hemsjukvård, äldreboende och habilitering" and "Tillsvidareanställning (inkl. eventuell
  provanställning)" run past the card). 0 at ≥768.
- The dialog ("Lägg till i matchning") holds all four sections; its body scrolls 1125 px in a 636 px box at 1280
  (1360 in 584 at 390).
- A chip removal (Behovsanställning) → "Sparat 13:34" and a PUT carrying all eight fields; with the write failing →
  "Ändringen kunde inte sparas. Försök igen." (the card's generic string, not the mapped one).

## A read-only design pass already laid out the options (input, not a decision)
Verified in code: `persist`/`removeChip` (card:242-266, 292-354), the dialog's full save (dialog:226-266; ExperienceField
inside Kompetenser at :323-330), the handler building from the command alone (`SetMatchPreferencesCommandHandler.cs:48-60`),
the replay (`UnitOfWorkBehavior.cs:17,31-48`), the rail's PUT without `experienceYears` (`match-setup-rail-modal.tsx:397-405`),
the rail mounting only when no occupation is stated (`oversikt/page.tsx:173-177`).

Findings of that pass:
1. **Two removals in one tab never reach the database together:** Next "dispatches Server Actions one at a time per
   client" (`node_modules/next/dist/docs/01-app/02-guides/server-actions.md:26-32`, called "an implementation detail
   and may change", `07-mutating-data.md:207`). Only two tabs race in the database.
2. **Defect (d), delivered code:** each removal's revert restores the snapshot taken at click time (`card:298`,
   reverted `:332`, `:353`), and the next removal's payload is fixed at click time too — removal 1 fails + removal 2
   succeeds → the UI shows both chips again while the server has neither; both fail → the UI drops A, the server keeps
   it (reachable via the MeWrite limit, 30/60 s shared across /me writes, `RateLimitingOptions.cs:552-556`).
3. **Defects (a)/(b):** the rail's PUT omits `experienceYears` → a stated scalar is stored `null` from the rail
   (reachable: state years, remove the last occupation chip on the card, then the rail appears on /oversikt);
   `match-setup-launcher.tsx:80-94` passes no `persistedSkillGroups` → saved skills show as raw ids in the rail (the
   missing `onSaved` is harmless, the launcher refreshes on close, `:64-76`). (c) the card shows its generic error,
   the dialog/rail the mapped one.
4. **Precedents:** Notiser writes each control to its own endpoint with its receipt under the control
   (`notifications-section.tsx:39-41, 91-138, 215-234`; ADR 0145 D4); partial PATCH "absent = untouched, present =
   replaced" in `CompanyWatchCriteriaEndpoints.cs:243-260`, `SavedSearchesEndpoints.cs:81-101`,
   `UpdateMyProfileCommandHandler` (`Language is not null`); h3 groups in one card (`account-section.tsx`, ADR 0145 D5,
   `app.css:91-104`).
5. **Combining in Domain (for W1/W2):** five `MatchPreferences` instance methods — `WithOccupations(groups,
   overlay?)`, `WithSkills`, `WithLocations(regions, municipalities, remote)`, `WithEmploymentTypes`,
   `WithExperienceYears` — each calling the same `Create(...)` with the stored values for the other parts. The
   handler loads the job seeker tracked, applies the part(s), `UpdateMatchPreferences(next, clock)`; the ADR 0146
   marker contract holds; `JobSeekerWriterReplayGuardTests` (`:56-72`, ports `:39-46`) picks new handlers up.
   Per-occupation years belong to Yrken (subset rule `MatchPreferences.cs:183-191, 256-296`; edited on each
   occupation row, `occupation-section.tsx:79-107`). Years inside the Yrken part: (a) always sent, projected by the
   client; (b) optional — absent keeps the stored years of still-selected occupations, `[]` clears, present replaces
   (the card never shows years, so (a) lets a stale tab rewrite years the user cannot see; proposal (b)).

**Write shapes:**
| | W0 full PUT, per-part UI only | W1 five PUTs `/me/match-preferences/{part}` | W2 one PATCH, optional parts |
|---|---|---|---|
| Parts not edited | re-sent from the tab's copy | untouched | untouched |
| Two tabs, different parts | one update lost | both land | both land |
| Stale tab, no race | one update lost | safe | safe |
| Same part, two tabs | last wins (whole document) | last wins (that part) | last wins (that part) |
| Rail | PUT + pass experienceYears through (3 files) | PUT + pass experience through | one PATCH of four parts, (a) fixed by construction |
| Backend source files | 0 | ~17 | ~5 (−3 if the PUT retires) |
W2 details: body `{occupations?, skills?, locations?, employmentTypes?, experience?}`; inside a present part every
field required (missing → 400, `[]` clears), except the optional years (b) and `experienceYears` (null clears);
no part present → 400; the PUT then has no product caller (retire in this PR or the next; seven integration-test
setups move to the PATCH); parity test reshaped to "every value in exactly one part"; a rate-limit wiring test for
the new route (the PUT has none today). W1 matches Notiser's endpoint-per-control precedent; W0 is a stopgap only.
The pass proposed W2 + (b) + required fields + PUT retired, marked as a proposal for you.

**UI options:** U1 per-part groups — each part a `jp-settings-group` with an h3 (the Konto pattern), its chips (or
the experience value), ONE button "Ändra" (values) / "Lägg till" (empty) whose accessible name includes the part via
`aria-labelledby=[button, h3]` (GOV.UK summary-list "Change"; WCAG 2.5.3), ghost/link weight so five buttons do not
compete (ADR 0038); one dialog parameterised by `part` (title = the part, body = that part's section only with
`showHeading={false}`, re-seeded per part, `.jp-matchdialog` modifier not a base change — it is shared with
`watch-filter-dialog.tsx`/`criterion-dialog.tsx`); one lazy chunk (#748) on the first click of any part; experience its
own part (ExperienceField labelled by the dialog title); focus returns to the opening part's button (a ref set at
open), last-chip removal focuses that part's button (the button must not remount when its label flips); a
status/alert pair per part (#1391), the mapped error shown (closes (c)); empty-part copy unchanged; the single "Lägg
till" row and its card-level status removed. U2 inline editing — not proposed (editors built for the 840 px dialog,
the card column is 688). U3 — U1 with experience inline.

**#1914 routing (CTO, both options open):** (A) `.jp-chiplist > li { min-width: 0 }` in-block, with all five chip-list
consumers rendered (card, rail, `section-helpers.tsx:72`, `foretag-sok-searchbar.tsx:887`, `cv-complete-guide.tsx:2326`);
(B) its own small PR first.

## Ask — dotnet-architect
Decide the write shape (W0/W1/W2 or better), the years choice (a)/(b), required fields inside a part, empty PATCH
400 vs no-op, 204 vs returning the stored parts, retiring the PUT (this PR / next / never), where the combining
lives (Domain `With*` or elsewhere), the ADR home (new ADR vs amendment to 0146 or 0076), and the test list you
require (incl. an ADR-0146-style race row). Grade anything in the current code you find (Kritiskt/Viktigt/NtH).

## Ask — design-reviewer
Decide the entry per part (U1/U2/U3 or better), the button copy and accessible name, the dialog title per part,
where experience lives, focus return, receipts/errors per part, empty states, the loss of the single "Lägg till",
and what the rendered round must show. Grade the r0 renders (the overflow is #1914's; routing is the CTO's).

Report in your standard output format, capped per your charter. No edits.
