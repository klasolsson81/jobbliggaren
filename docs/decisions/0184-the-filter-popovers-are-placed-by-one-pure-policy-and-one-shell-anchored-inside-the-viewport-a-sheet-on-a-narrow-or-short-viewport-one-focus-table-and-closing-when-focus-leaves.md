# ADR 0184 — The filter popovers are placed by one pure policy and one shell: anchored inside the viewport, a sheet on a narrow or short viewport, one focus table, and closing when focus leaves

**Date:** 2026-10-24
**Status:** Accepted 2026-10-24 (the CTO's binding ruling (a) to (z) under CLAUDE.md §9.2, and Klas's answer of the same day on `hotspot:css`, D5). The mandatory agents review the delivered diff
in the PR (the CTO (z)).
**Deciders:** Klas Olsson (controller and product owner; "Direkt efter PR 2 (Rek.)" on 2026-10-23, which placed this work, and "Ja, dela raden (Rek.)" on 2026-10-24, recorded in D5) ·
`senior-cto-advisor`, "the CTO" below (decision-maker under CLAUDE.md §9.2; ruling (a) to (z), report-only, `docs/reviews/2026-10-24-2212-plan-cto.md`) · five plan reads of the draft, all report-only:
`design-reviewer`, "DR" (`docs/reviews/2026-10-24-2212-plan-design-reviewer.md`: Blocked, six Blockers) · `code-reviewer`, "CR" (`…-plan-code-reviewer.md`: Blocked) · `test-writer`, "TW"
(`…-plan-test-writer.md`: 2 Blocker, 9 Major, 8 Minor) · `dotnet-architect`, "DA" (`…-plan-dotnet-architect.md`: 0 Kritiskt, 7 Viktigt, 7 Nice-to-have) · an independent design, "ALT"
(`…-plan-alt-design.md`: no Blocker, five Majors, nine Minors)
**Amends:** ADR 0055 (Beslut 1's width, Beslut 2, and the focus-on-open sentence of its amendment of 2026-10-23) and ADR 0180 (a note on D8's delta, D5 in a sheet and the Ort guard test), each by a
dated amendment at the end of that file. It extends ADR 0067 E2h VAL 2 (D12). Nothing above those amendments is rewritten.
**Related:** [#2212](https://github.com/klasolsson81/jobbliggaren/issues/2212) (`bug`, `mvp`, P1; the issue this ADR ships under) · [#2215](https://github.com/klasolsson81/jobbliggaren/issues/2215)
(the results toolbar, filed from a measurement, D12) · [#2214](https://github.com/klasolsson81/jobbliggaren/issues/2214) (held `hotspot:css`, D5) · [#2198](https://github.com/klasolsson81/jobbliggaren/issues/2198)
(PR 2, ADR 0180, which measured the overflow and handed it on) · ADR 0067 (E2b, E2c, E2f, E2h) · ADR 0053 (its amendment of 2026-10-03: the detail modal's sheet) · ADR 0162 (the bell's sheet) ·
ADR 0047 · ADR 0117 (Bransch) · #2033 (the browser-floor question, D9)
**Measured against:** `origin/main` `5ad51c9c1` (the base: code read with `git show`, and the "before" readings rendered), 2026-10-24; the "after" readings are on the branch `fix/popover-viewport` in
`C:/tmp/jbl-popover-viewport`, the same day. Every reading is headless Chromium against a local stub, sv unless it says en, with no real account and never the box (CLAUDE.md §9.2 (d)). Nothing here is
merged.

> **Provenance.** Written by `adr-keeper` for the driving session from its brief (CLAUDE.md §9.2, §13); it adds no decision of its own. D1 to D12 follow the CTO's ruling and the branch as the brief
> describes it; the plan reads are marked "(DR)", "(CR)", "(TW)", "(DA)" and "(ALT)" where each changed or added to the ruling. The six reports are local files. Facts about the code were read at
> `5ad51c9c1`, and line numbers are that commit's. The numbers under *Measurements* are the driving session's of 2026-10-24, and none was re-run for this ADR. What no source measures is listed under
> *What is not measured, and what is owed*. The ADR lies in the ignored 0071–0199 range and needs `git add -f` and a README row (precedent 0167–0183).
>
> **Numbering.** 0184 was measured free on 2026-10-24, before this file was written: the highest number in `docs/decisions/` of this worktree was 0183.

---

## Context

#2212 is the defect that ADR 0180's second pull request measured and handed on. The filter popovers on `/jobb`, `/foretag/sok` and the hero chips run past the right edge of the viewport, by up to
385 px on a phone, and a phone has nothing smaller for them to become. Klas placed the fix in a pull request of its own, directly after PR 2 ("Direkt efter PR 2 (Rek.)", 2026-10-23; ADR 0180,
amendment of 2026-10-23, point 7). Nine facts shape the decision.

- **The overflow is measured, and it is wide.** On the base (R1) the Yrke popover's right edge is 45 px past a 1280×720 viewport, 53 px with a classic scrollbar and 58 px in en; Ort and Matchning
  are 92 px past at 1024 px; `/foretag/sok` Ort is 156 to 188 px past between 768 and 800 px; on a phone Ort and Yrke are 249/304 px and 330/385 px past at 375 and 320 px. Nothing clamps a panel
  to the viewport.
- **One position, three copies.** `usePopoverPosition` (`jobb-filter-popover.tsx:151-183`) and `usePanelPosition` (`use-panel-position.ts`) are the same body: `top = r.bottom + 8 + scrollY`,
  `left = r.left + scrollX`, with no clamp. `HeroChip` does it a third way, in CSS under a `position: relative` wrapper (`hero-chip.tsx:101,123-129`). The CTO had already called the two hooks the same
  knowledge in two copies in his #2198 ruling (e). Both hooks also listen to a capture-phase `scroll`, which re-renders the whole popover on every scroll of a column inside it: 10 commits for 10 scroll
  events (R1).
- **A panel paints over the header.** `.jp-popover` is z 40, the sticky header is z 30, and the hero chips set an inline z 30 of their own (`hero-chip.tsx:128`), equal to the header's. On the
  keyboard path the page scrolled to 360 and the popover painted over the 89 px header (R1).
- **The keyboard can leave a panel and still see it.** Tab past the last control focused the results sort select under the still-open popover. On `/foretag/sok` at 1280×720 both popovers opened with
  their first control below the fold (the first row ending at 774.9 and 816.8 px), so the panel looks as if nothing happened (R1).
- **A pick moves the page.** A pick at scrollY 200 landed at 0 on `/jobb`, and `/foretag/sok` kept 200 (R1): `foretag-sok-searchbar.tsx:371-372` already passes `{ scroll: false }`.
- **The house has the pieces for a phone.** The bell's panel is a fixed sheet under the header at `top: calc(var(--jp-header-h) + 8px)` (`globals.css:4834`, ADR 0162), and non-modal (DESIGN.md:148). The
  detail modal is a sheet at ≤ 768 px and at `(max-height: 500px)` (`globals.css:3020`, ADR 0053 amendment of 2026-10-03). `lib/applications/anchored-top.ts` is a pure placement function. The house has
  no `scroll-behavior` rule (`git grep`, 2026-10-24).
- **The form is bound from outside.** ADR 0067 E2b: no prop names a mode (`:171`). ADR 0180 D8: `JobbFilterPopover` has one optional composition prop, `search`. `useDismissable`'s signature is settled
  (ADR 0180, amendment of 2026-10-23, point 1). A new package is a §12 STOPP. `hotspot:css` was held by #2214, with `wip` and an assignee (the CTO, measured 2026-10-24).
- **A covered pill keeps focus.** ADR 0055's amendment of 2026-10-23 says a popover without a search moves no focus on opening (`:146`). A sheet from y 97 to the bottom inset covers the pill the user
  just activated, so focus would sit under the sheet (WCAG 2.4.3, 2.4.7 and 2.4.11; the house names Focus Not Obscured itself at `globals.css:486-495`).
- **What the CTO put to Klas.** One question, Q1 on `hotspot:css` (answered, D5), and five notices with no question attached: N1, the two hero chips are in scope although the issue's host list names six;
  N2, a phone gets the bell's form and not Platsbanken's side drawer; N3, a new close path on desktop (D8); N4, the results toolbar may still jump to the top (D12); N5, the soft keyboard and the height
  on a real iPhone and Android are Klas's live check (D7).

## Decision

**A pure function, `placePanel`, decides where a filter popover goes and whether it is a sheet at all; `usePanelPosition` is the one DOM adapter, and only `resize` re-measures. An anchored panel is
clamped inside the viewport at a preferred width, in document coordinates; a viewport at most 640 px wide or at most 500 px high gets a sheet under the header instead, and CSS keys only on
`data-placement="sheet"`. Once per opening the page scrolls the pill into view when the panel's first control would be below the fold. `.jp-popover` is z 20, under the header. One shell,
`PopoverShell`, is the single implementation for the four `.jp-popover` roots: dismissal, position, focus on open, focus-out, and the sheet's band with its close button. Focus on open is one
table of placement × pointer; a popover closes when keyboard focus leaves it; a sheet shows one column at a time; every control in a popover is at least 44 px at ≤ 768 px; and a hero pick keeps the
page where it is.**

**Scope.** The scope is the CTO's (a) to (t) as they touch the filter popovers and the two hero chips. A change to the breakpoint, the focus table, the sheet's form or the shell's ownership is a new
decision and not an edit of this one. The results toolbar's sort and chip-×, the settings `.jp-popover-row` touch floor and Platsbanken's chevron on the phone pills are not in it (D12; the CTO (x)).

### D1 — The placement policy is a pure function, and it is the one decider (CTO (a), (g); DR Major 1; ALT; CR 3, DA, TW m4)

- **The function.** `placePanel` in `web/jobbliggaren-web/src/lib/popover/placement.ts`, after the precedent `lib/applications/anchored-top.ts`: no React and no DOM. Input: the trigger's
  `{ left, bottom }`, the viewport's `{ clientWidth, clientHeight, scrollX, scrollY }` and the preferred width. Output: `{ placement: "anchored", top, left, width }` or `{ placement: "sheet" }`. The
  constants are named in the file: the gap 8, the gutter 16, and the sheet's two limits.
- **Anchored.** `width = min(preferred, clientWidth - 32)`; `left = max(16, min(trigger.left, clientWidth - 16 - width)) + scrollX`; `top = trigger.bottom + 8 + scrollY`. These are document
  coordinates, and they hold under the precondition in D3.
- **A sheet if and only if `clientWidth <= 640` or `clientHeight <= 500`.**
  - *Why 640* (DR Major 1). Above it a clamped 580 px panel always fits (641 - 32 = 609 ≥ 580), so ADR 0180 D5's container query (`app.css:546`) never stacks the results inside an anchored panel;
    640 is 1280 px at 200 % zoom; and it is the header's own step (`globals.css:858`).
  - *Why 500* (ALT). A landscape phone, and 1366×768 at 200 % (683×384), have no room under the pill for a panel's own minimum heights plus the slot and the footer. The house's other sheet already
    uses `(max-height: 500px)` (`globals.css:3020`).
- **One decider.** The sheet is decided from the same measurement as the clamp. CSS keys only on `.jp-popover[data-placement="sheet"]`, and the attribute exists only in a sheet: an anchored panel
  carries none, because nothing would read an `anchored` value (YAGNI) and leaving it out keeps the anchored DOM's difference from the base to its style attributes (ADR 0180's note of today). There
  is no `@media` and no `matchMedia` for placement.
- **Why `clientWidth` and not a media query** (CR 3, DA, TW m4). A media query counts a classic scrollbar and `clientWidth` does not, and one measurement then serves the clamp and the placement
  alike.

### D2 — A document without layout is anchored and unclamped, and that is a declared degradation (CTO (c); CR Blocker 1(b); TW M2)

- A zero `clientWidth` or a zero `clientHeight` is `placePanel`'s first branch and runs before the sheet decision. The result is anchored with `width` equal to the preferred width, finite, and never
  a sheet. It is checked on both axes: jsdom returns 0 for both, and with D1's height rule a guard on the width alone would render every host suite as a sheet.
- **This is not production behaviour.** A laid-out document never reports a zero client size. The branch exists so that nothing throws, divides or hides the panel where there is no layout. The test
  for it is declared jsdom-only and asserts only that: anchored, not a sheet, finite, width ≥ 0, no throw. It never asserts it as what production does (AGENTS.md §5, Tests). The reveal in D4 does
  nothing under it.

### D3 — `usePanelPosition` is the adapter, and only `resize` re-measures (CTO (a), (b), (d); CR 2, 9, 12, 13; DA)

- **The hook.** `usePanelPosition(open, triggerRef, panelRef, preferredWidth)` in `src/lib/hooks/use-panel-position.ts` returns `{ style, placement, ready }`. The file and the name stay, because the
  name is still true in a sheet. `style` holds the off-screen sentinel before the first measurement, `{ top, left, width }` when anchored and nothing positional in a sheet; `placement` is null until
  the panel is placed. Hosts spread `style` and never branch on a position. The panel ref is the one `useDismissable` returns, and `useDismissable`'s signature did not change.
- **What it reads.** The trigger's rect, `window.scrollX/scrollY` and `document.documentElement.clientWidth/clientHeight`, so a scrollbar is excluded. It measures in `useEffect`: a layout effect
  with no dependency array would force a layout on every Yrke keystroke, and the sentinel hides the first frame. The file carries a `"use client"` reason line.
- **Only `resize` re-measures, and the capture-phase `scroll` listener is deleted.** Under the precondition below, a page scroll moves the trigger's rect and the scroll offset by the same amount, so
  the listener recomputed a constant, and its only effect was a commit per scroll event in a column. State updates only when a number or the placement changes. Measured after: 0 commits during a
  column scroll (R2), against 10 of 10 on the base (R1).
- **The precondition**, in the hook's docblock and here: the panel's containing block is the initial containing block, and no scroll container sits between the trigger and the document. It is checked
  in the rendered matrix, for every host at every anchored width: `getBoundingClientRect().left` equals `parseFloat(style.left) - scrollX` within 0.5 (the CTO (b), (t)). On the base it was measured on
  `/jobb` and only derived on `/foretag/sok`.
- **No `ResizeObserver`.** The CTO ordered two cases measured and a `ResizeObserver` added in this PR if either failed (guarded as at `ad-description-excerpt.tsx:50`). The reflow after a pick was
  measured at 1024 px in en and the trigger did not move (R2). The other case is listed under *What is not measured*.
- **The width is a number.** A string width cannot be compared with `clientWidth`, and `100vw` includes a classic scrollbar. `JobbToolbarPopover` takes `width?: number`, default 320, and Bransch passes
  560 (CTO (d)).
- **Removed.** `usePopoverPosition` is deleted. The one focusable selector moves to `src/lib/popover/focusable.ts` (`FOCUSABLE_SELECTOR`), because D4 and D7 use it too. The comments that the change
  makes false (the no-clamp paragraphs in `jobb-toolbar-popover.tsx` and `use-panel-position.ts`, the inline-cap comment and the second clause of `bransch-popover.tsx`'s) are deleted in the commit that
  makes them false (AGENTS.md §5, Comments; CR 9).

### D4 — Once per opening the pill is scrolled into view when the panel would open below the fold (CTO (f); DR Major 2; CR 7; DA)

- **Rule.** Once per opening, before the first anchored placement: if the panel's first focusable element would end below the viewport, `trigger.scrollIntoView({ block: "start" })`. The html scroll
  padding (`globals.css:492-495`) then puts the *trigger's* top at 105 px, so the pill stays visible as the panel's anchor, and the header offset keeps its one home in CSS.
- **Bounds.** It is instant (the house has no `scroll-behavior` rule, so no reduced-motion branch is owed), it never runs on a re-measure, never in a sheet and never without layout (D2).
- **The condition is the first focusable element, not the first row.** The shell cannot name a body's rows (DA). Where it fires, the field and the first row are both visible; the in-between case, the
  field visible and the first row not, is rendered for DR to grade, and a Blocker or Major there is closed by a code-only change to the condition (CTO (f); CLAUDE.md §9.6).
- **At rest it does not fire:** not on `/jobb` at 1280×720, in sv or in en (R2).
- **Why.** WCAG 2.4.7. A panel that opens below the fold looks as if nothing happened (DR Major 2).
- **Measured after (R2).** The trigger's top is at 105.0 and exactly one scroll event occurs on `/foretag/sok` at 1280×720 and at 1024×768, and on Matchning at 800×600.

### D5 — Stacking: `.jp-popover` is z 20, under the header, and the relation is pinned (CTO (e); CR 6, DA, ALT; DR; Klas 2026-10-24)

- **The value.** `.jp-popover { z-index }` goes from 40 to 20, edited in place at `globals.css:1743`: under `.jp-header`'s 30 and above the hero search list's `z-10`. `HeroChip`'s inline `zIndex: 30`,
  which equals the header's, is removed. An anchored panel is page content: it scrolls under the sticky header, and the header's menus (z 50 inside the header's stacking context) then paint above it.
- **The relation, not the number.** `src/app/(app)/popover-stacking.test.ts` reads `globals.css` and then `app.css` in load order and asserts that the effective `.jp-popover` z is above 10, the
  typeahead's, named in the test, and below `.jp-header`'s.
- **`hotspot:css`.** #2214 held it. The CTO asked Klas whether #2212 may share it for exactly one declaration, and Klas answered **"Ja, dela raden (Rek.)"** on 2026-10-24. No other line of
  `globals.css` is touched, the label did not move, and #2214 did not wait. The sheet's rules, the column cap and the touch floor are in `app.css`.
- **Measured after (R2).** At the header line the header is on top; at the panel's centre and at its inset corners the panel is.

### D6 — One shell is the single implementation for the four `.jp-popover` roots (CTO (i), (o); DA; ADR 0067 E2b)

- **Home.** `src/components/popover/popover-shell.tsx`, `PopoverShell`, for the four roots `JobbFilterPopover`, `JobbKlass2Panel`, `JobbToolbarPopover` and `HeroChip`. It owns: the root (`role="dialog"`,
  `data-placement`, `style` and a conditional `tabIndex`), dismissal through the unchanged `useDismissable`, the position (D3), focus on open (D7), focus-out (D8) and the band with its close button in
  a sheet.
- **Constraints** (the CTO (i), binding). Each behaviour has exactly one implementation. No prop names a placement or a mode (ADR 0067 E2b; Fowler 2018, "Remove Flag Argument"), and the body gets the
  placement through a render-prop child. `JobbFilterPopover` gained no public prop (ADR 0180 D8). `useDismissable`'s signature did not change. When anchored, Ort's DOM differs from the base in style
  attributes only (ADR 0180's note of today).
- **The band.** In a sheet there is exactly one band, showing the dialog's existing name, with an X on the right: `.jp-icon-btn` (44 px at ≤ 768, `globals.css:1013-1017`), a 16 px `X` with
  `aria-hidden`, named `common.dialog.close` ("Stäng"). The X closes the popover and returns focus to the pill. Toolbar and `HeroChip` already show a band at rest, and the X goes into that band in a
  sheet only. Where a band shows, it names the dialog through `aria-labelledby`, and the name is the same string in both placements (a test checks it). "Visa N annonser" stays where it exists; the
  hosts without a footer (`/foretag/sok` Ort, Matchning, Bransch) get the X and no footer. A touch user has no Escape key, and with 8 px insets "tap outside" is a strip or the header (DR Blocker 2;
  TW M5), which is why the X exists. ADR 0055 `:149` already superseded "ingen Stäng-knapp".
- **`HeroChip`** (the CTO (o)) composes the shell with preferred width 320. Its wrapper's `position: relative` and its inline position, top, left, width and z-index are deleted, the gap goes from
  6 px to 8 px, and below the breakpoint it is a sheet with the X in its existing band. It is in scope although the issue's host list names six (notice N1): it is the same kind of popover, it paints
  over the header today, and one pill row should open one kind of surface (DR Major 1).
- **No new message key.** `pages.common.back` ("Tillbaka") and `common.dialog.close` ("Stäng") exist, in namespaces the `(app)` layout already loads. `hotspot:i18n` is not needed.

### D7 — Focus on open is one table of placement × pointer (CTO (j); DR Blocker 1; CR, DA, ALT)

The table is a pure function, `src/lib/popover/focus-on-open.ts`, and the shell's one effect replaces the old `JobbFilterPopover` effect (`:313-318`).

| Placement | Pointer | With a field (Yrke) | Otherwise |
|---|---|---|---|
| anchored | fine | the field | no move |
| anchored | coarse | the dialog | no move |
| sheet | fine | the field | the dialog |
| sheet | coarse | the dialog | the dialog |

- **The coarse pointer** is `(pointer: coarse)`, the query the house already uses for touch-only rules (`globals.css:3371,6462`), read at opening, guarded by
  `typeof window.matchMedia === "function"`, with a fine pointer as the default. A stub in a test names a touch device as its actor and pins the exact query string (TW m5).
- **When.** The table applies once the panel is placed, and again when the placement changes, so a resize that turns an open anchored popover into a sheet applies the sheet row. Focus already on a
  shown element inside the panel stays: a field that has focus never loses it. Focus inside a `hidden` ancestor does not stay, which is the case of a resize to a sheet at level two (D10).
- **`tabIndex=-1`** sits on the root only where the table can focus it.
- **Closing.** Escape, the footer and the X return focus to the pill, with its scroll (`use-dismissable.ts:86-87`, `jobb-hero-filters.tsx:423-428`).
- **Why two inputs.** A sheet covers its pill (Context). Whether a soft keyboard opens is a property of the input method and not of the width. Keying on placement alone gets two cases wrong, a zoomed
  keyboard user (sheet, fine pointer) and a tablet (anchored, coarse pointer); keying on the pointer alone gets the covered pill wrong.
- **Supersedes** the focus-on-open sentence of ADR 0055's amendment of 2026-10-23 (`:146`), "a popover without a search moves no focus on opening", for a sheet. The amendment of today says so there.
  Ort's guard test, "Ort opens without moving focus", applies to the anchored placement (ADR 0180's note of today).

### D8 — A popover closes when keyboard focus leaves it, in both placements (CTO (k); DR Blocker 3)

- **The hook.** `useCloseOnFocusOut` in `src/lib/hooks/use-close-on-focus-out.ts`, composed by the shell. A `focusout` from the panel closes it, without moving focus, when its `relatedTarget` is not
  null and is outside the panel, outside the trigger and outside the portaled layers that `useDismissable` already exempts. A null `relatedTarget` never closes it.
- **The portaled layers** are now one exported selector, `PORTALED_LAYER_SELECTOR` in `use-dismissable.ts`, taken from the hook's former inline list. The exemption is load-bearing: Matchning holds
  three Radix `InfoDialog`s (`jobb-match-grade-filter.tsx:239,275,313`), and focus moving into `[data-slot="dialog-content"]` leaves the panel open (tested).
- **Both placements, one keyboard model.** On the base, Tab past the last control lands on the results sort select under the still-open panel (R1), and the panels follow their pills in the DOM
  (`jobb-hero-filters.tsx:536-621`). WCAG 2.4.7 and 2.4.11. This is a new close path on desktop too (notice N3): a popover that stayed open when Tab left it now closes.
- **Not a focus trap**: the bell is non-modal (DESIGN.md:148), and so are these. Not a flag on `useDismissable`: a boolean fifth argument is a flag argument, for the same reason as the CTO's
  #2198 ruling (a).

### D9 — The sheet: fixed under the header, the bell's form, one scroller (CTO (h); DR Majors 1 and 5, Blocker 6; ALT)

- **Placement.** `position: fixed` at `top: calc(var(--jp-header-h) + 8px)` (97 px, the bell's: `globals.css:4834`), 8 px from both sides, `width: auto`. `--jp-shadow-pop` and `--jp-r-md`. No scrim,
  no motion, no scroll lock, no portal and no `:has()` (#2033, the browser-floor question, is open). A tap on the inset margins or on the header closes the sheet, as today; a scroll does not.
- **Inside.** A flex column. The band and the footer are fixed; the list is `flex: 1; min-height: 0` with a three-row floor (144 px); the sheet itself scrolls (`overflow-y: auto`) as a last resort.
  A cascade host asks for the whole sheet height, `calc(100dvh - var(--jp-header-h) - 16px)` with the `vh` fallback declared first (`globals.css:3017-3018`); a single-column host takes its content
  height up to that.
- **Minimum heights and caps.** The panel's minimum heights (`.jp-popover__body` 360, `.jp-popover__frame` 60vh, the 360 px results and frame body) are reset by rules in `app.css` scoped to the
  attribute, without `!important`. The browse columns' `60vh` cap moves from an inline style (`jobb-filter-popover.tsx:378,443`) to one `app.css` rule with the same computed value when anchored and a
  reset in a sheet; the inline `overflowY` duplicated `globals.css:1807` and is dropped.
- **`overscroll-behavior: contain`** on every scroller in a sheet (`globals.css:4734`).
- **Yrke's stacked answer** (ADR 0180 D5, the container query below 560 px) has one scroller in a sheet: at most one vertical scroller sits between the band or the field and the footer, the stacked
  columns lose their caps, and the results box scrolls as one (DR Major 5).
- **The anchored DOM** carries no sheet-only attribute, class or element; they exist only in a sheet.

### D10 — The drill-down: a sheet shows one column at a time (CTO (l); DR Blocker 4, Majors 3 and 4; TW M4; DA)

- **One markup path.** In a sheet, `JobbFilterPopover` shows the left column at level one (`activeLeft === null`) and the right column at level two. Both come from the same `columns` JSX, and the
  inactive one carries the `hidden` attribute, computed from the placement and `activeLeft`. Anchored panels never carry it. It is an attribute and not a CSS class because jsdom cannot see a class
  (TW M4, over DA).
- **Level two** has a band: Tillbaka on the left (`pages.common.back`, a `ChevronLeft` with `aria-hidden`, `.jp-textaction`) and the column's Rensa on the right. Below it the group's name is the
  column's heading: data, sentence case, `--text-ui`, semibold, ink-1, `tabIndex=-1`. The column is a `role="group"` that heading names, and `rightTitle` is not shown at level two. Level one has no
  right column and no "till vänster".
- **Level one only.** Distans and Yrke's search field appear at level one only (DR; TW M4 superseded).
- **Focus** goes through `useFocusAfterCommit`. Drilling in focuses the level-two heading (ADR 0180 D5). Tillbaka focuses the left row of the group just left, in view.
- **Escape closes from either level.** `keepOpenOnEscape` (ADR 0180, amendment of 2026-10-23, point 1) is unchanged.

### D11 — At ≤ 768 px every control in a `.jp-popover` is at least 44 px (CTO (m); DR Blocker 5; WCAG 2.5.5)

- **Scope.** Every row kind, Distans and Välj alla, the chip rows (`globals.css:7176-7190`), Rensa, Tillbaka and the X, the foot link and the footer button.
- **How.** Only the hit area grows. Rows and links grow; Rensa, Tillbaka and Matchning's switch get an invisible `::before` (`ToggleRow`'s idiom, and `.jp-criterionrow`'s pattern,
  `globals.css:1873-1880`). The rule lives in `app.css`, under `@media (max-width: 768px)` and scoped to `.jp-popover`, with no top-level `:focus-visible` duplicate
  (`occupation-search-css.test.ts`).
- **A touch rule and not a placement rule.** It is keyed on the width alone, so it also reaches an anchored panel between 641 and 768 px (DA). D1's ban on `@media` is on deciding placement, and this
  rule decides none.
- Pinned by `src/app/(app)/popover-touch-floor.test.ts`.

### D12 — A hero pick keeps the page: `{ scroll: false }` (CTO (n), (p); ADR 0067 E2h VAL 2)

- **The change.** `JobbHeroFilters.commit` (`jobb-hero-filters.tsx:228-251`) passes `{ scroll: false }` inside the transition. It reaches Ort, Yrke, Filter, Matchning, Distans and Dölj ansökta. The
  reason is ADR 0067 E2h VAL 2's, which already passes `scroll: false` for the search field's commits, applied to the popovers: a pick narrows the list the user is already reading. `/foretag/sok`
  already did (`foretag-sok-searchbar.tsx:371-372`). Measured on the base: a pick at scrollY 200 lands at 0 on `/jobb`, and `/foretag/sok` keeps 200 (R1).
- **The contract is untouched:** the URL, `buildJobbHref`, `search-params.ts`, `SearchCriteria` and `messages/`.
- **The results toolbar is not changed here.** Sort and chip-× were measured landing at 0 and are filed as #2215 (a defect in delivered code, so outside the session's net backlog cap, CLAUDE.md
  §9.6). Whether a sort keeps the position or jumps to the results is its own design decision, next to ADR 0067 E2h VAL 2's documented asymmetry (the
  CTO (p)). The toolbar's current assertions stay as the pin of its current behaviour.

## What is not measured, and what is owed

**Not measured, and not claimed:**
- The soft keyboard and `dvh` on a real iPhone and a real Android. Klas checks them live (notice N5): Yrke must not raise the keyboard when it opens on a phone (D7).
- The real dark theme. Klas checks it live (the CTO (t)).
- The scrollbar renders count only where `clientWidth < innerWidth`; elsewhere they are recorded as "not reached", and Klas checks in Windows Chrome (the CTO (t)).

**Owed in the PR:**
- `design-reviewer`'s verdict on the rendered matrix (the CTO (t)): the sheet viewports 640×360, 600×900, 375×812, 320×640, 320×256, 812×375, 844×390 and 683×384; the boundaries 640×900 and 641×900,
  1280×500 and 1280×501; the anchored viewports including 769×900, 768×1024 and 700×900; the in-between band of D4. This ADR records no reading of them.
- The panel (the CTO (z)): `code-reviewer`, `dotnet-architect` (more than 5 files, the new shared shell, the ADR and the spec edit), `design-reviewer` (the a11y veto) and `test-writer`. Not
  `security-auditor`: no PII, auth, secret, external integration or accepted vulnerability. Klas tries the UI live before the panel. The review cycle is §9.6's: one batched round, then at most one
  scoped, report-only re-check per issuing agent.

## Measurements

Each reading below is dated and has its source. Nothing is estimated.

| Reading | What | Recorded |
|---|---|---|
| R1 | the base: overflow at the widths below, a pick's scroll, the keyboard path, the fold, and render counts | 2026-10-24, `5ad51c9c1`, headless Chromium against a local stub, sv unless it says en, no real account, never the box |
| R2 | the branch after the change: the readings that a decision names | 2026-10-24, the branch `fix/popover-viewport`, the same setup |

**R1, before the change:**
- **Right-edge overflow.** Yrke ran past the right edge by 45 px at 1280×720, by 53 px with a classic scrollbar (`clientWidth` 1265) and by 58 px in en. At 1024 px: Ort 92 px, Matchning 92 px. Filter
  112 px at 800. `/foretag/sok` Ort 156 to 188 px between 768 and 800. On a phone: Ort 249 and 304 px and Yrke 330 and 385 px at 375 and 320 px; Filter, Matchning and both hero chips also ran over at 320.
- **A pick's scroll.** A pick at scrollY 200 landed at 0 on `/jobb`. `/foretag/sok` kept 200.
- **Stacking and keyboard.** On the keyboard path the page scrolled to 360 and the popover (z 40) painted over the 89 px sticky header (z 30). Tab past the last control focused the results sort select
  under the still-open popover.
- **The fold.** On `/foretag/sok` at 1280×720 both popovers opened with their first control below the fold (the first row ending at 774.9 and 816.8 px).
- **Commits.** A column scroll caused one React commit per scroll event (10 for 10).

**R2, after the change:**
- **D3.** 0 commits during a column scroll. The trigger did not move after a pick at 1024 px in en. A classic scrollbar that appears after a popover opens was not reached: with Chromium's
  scrollbars shown, `/foretag/sok`, the shortest of the two pages, already carried one before any popover opened at 1920×1080, 1280×1440 and 800×1400, so no `ResizeObserver`.
- **D4.** Trigger top 105.0 and exactly one scroll event on `/foretag/sok` at 1280×720 and at 1024×768, and on Matchning at 800×600. The reveal never fires at rest on `/jobb` at 1280×720, in sv or in
  en.
- **D5.** At the header line the header is on top; at the panel's centre and at its inset corners the panel is.
- **Ort's DOM.** Anchored, equal to the base once style attributes are stripped; the reading and its limits are in ADR 0180's note of today.

## Alternatives considered

### Alt A — The chosen design (D1–D12)
**For:** one place for the position and one decider for the placement, so a fifth `.jp-popover` host gets every behaviour by composing the shell; no panel crosses the viewport at any measured width and
the header stays on top; a touch user gets the bell's known form, a visible close and a floor of 44 px; a keyboard user cannot Tab behind an open panel; the anchored DOM differs from the base in style
only; no package, no backend change, no new message key and no change to the URL contract.
**Against:** the trade-offs listed under Consequences.

### Rejected: the alternatives to the design as a whole
- **`@floating-ui/*`.** A new dependency, a §12 STOPP (the CTO (r)).
- **Radix Popover.** It reopens ADR 0180's amendment point 1: the `/jobb` popover is not a Radix component, and its Escape rule is a `useDismissable` predicate.
- **A CSS-only clamp.** A string width cannot be compared with `clientWidth`, and `100vw` includes a classic scrollbar (the CTO (d)).
- **A portal.** Fixed as "no portal" by the CTO (h); the panels follow their pills in the DOM (D8), and nothing in D1 to D12 needs a layer outside them.
- **A `matchMedia` or `@media` decider.** A media query counts a classic scrollbar, and one measurement serves both the clamp and the placement (D1; CR 3, DA, TW m4). A `matchMedia` decider through
  `useSyncExternalStore` (ALT) has the same defect.
- **A smooth scroll.** The house has no `scroll-behavior` rule, so a smooth scroll would add a reduced-motion branch; the reveal is instant (D4).
- **Platsbanken's side drawer of about 300 px.** A modal in all but name (DR). The phone gets the bell's form instead (notice N2): the interaction is Platsbanken's, one column at a time with Tillbaka and
  the group's name at level two, and the sheet is the house's. Platsbanken was observed on 2026-10-24 (ALT's evidence base).
- **A breakpoint at ≤ 480.** From 481 to 611 px an anchored panel would narrow below 560 and stack Yrke's results inside an anchored panel nobody has rendered (the CTO (g)). Nor ≤ 768, nor CR's
  per-host threshold, which would open two kinds of surface from one pill row (DR Major 1).
- **Keeping z 40 and holding the panel's top below the header.** It detaches the panel from its pill (DR).
- **An `app.css` override of the z value.** Two declarations, load order decides the winner, and `globals.css:1743` would show a 40 that applies nowhere (CR 6, DA).
- **Escape stepping back a level.** Escape closes from either level (D10).

### Rejected: the alternatives to its parts (the CTO's ruling)
- **Renaming the hook to `useAnchoredPanel`** (draft, DA, ALT). The name would be false in a sheet.
- **A filtered `scroll` listener** (draft, CR 2, DA). It filters a listener that has nothing to measure (D3). **Coordinates relative to `offsetParent`** (DA): YAGNI, the precondition is stated and
  probed instead.
- **`block: "nearest"` on the panel** (DA). The 538 px panel is taller than the 519 px padded viewport, so it would scroll about 290 px on every opening. **`panel.scrollIntoView({ block: "start" })`**
  (CR, ALT): the pill would end under the header. **`window.scrollTo(…105)`** (DR): it puts the header's height into TypeScript. **A 200 px threshold** (ALT): a magic number.
- **Focus on open keyed on placement alone** (CR, DA), **on the pointer alone** (draft), or **"anchored with a coarse pointer: no move"** (ALT), which is wrong when a search is present (D7).
- **A `sheet`, `compact` or `showClose` prop on the shell** (DA), and **relying on Escape and "tap outside"** (draft): a touch user has no Escape key (DR Blocker 2, TW M5).
- **Hiding the inactive column with a CSS class** (DA). jsdom cannot see it (D10).
- **Hero chips that stay anchored at every width** (ALT), and **CR 8's "z only, if no chip overflows"** (the CTO (o)).
- **`bottom: 8px` for every host, and a 150 ms fade** (ALT). The sheet's height is the content's up to the cascade's full height (D9), and there is no motion.

## Consequences

### Positive
- The position has one home and the placement one decider. A host spreads `style` and never branches on a position; a future `.jp-popover` host composes `PopoverShell` and inherits placement, focus,
  focus-out and the sheet without copying any of it (OCP, Martin 2017, ch. 8).
- No measured panel crosses the viewport, and the header stays on top of an anchored panel.
- A phone gets the bell's known form, a close control that does not depend on a key the device lacks, a floor of 44 px and a drill-down that matches Platsbanken's interaction.
- A keyboard user cannot Tab behind an open panel in either placement, and a screen-reader user's focus is never left under a sheet.
- A column scroll costs no commit, and a pick on `/jobb` keeps the page like `/foretag/sok` does.
- The release adds no package, no backend change, no new message key and no change to the URL contract.
- The records are in one place each: this ADR for the decision; amendments at the end of ADR 0055 and ADR 0180 that point here; a DESIGN.md §6 bullet extending the bell's, written by the driving session and reviewed by the spec-edit agents of CLAUDE.md §9.2; and README rows for all three.

### Negative and accepted trade-offs
- **D1's coordinates rest on a precondition** that nothing enforces automatically: a future host whose panel sits inside a scroll container or a positioned ancestor gets wrong coordinates. It is
  checked by the rendered probe of D3, not by a test. Coordinates relative to `offsetParent` (DA's alternative) were rejected as YAGNI while the precondition holds.
- **Only `resize` re-measures.** A reflow that moves the trigger while the panel is open is not followed. It was measured not to move after a pick at 1024 px in en; any other cause would need the
  `ResizeObserver` of D3.
- **Two branches of `placePanel` are unreachable for today's hosts:** a left edge below 16 px, and a shrunk width (preferred ≤ 580 < 609). They are defensive; their tests are declared so and assert
  only safe degradation (the CTO (s)).
- **The reveal moves the page.** On `/foretag/sok` at 1280×720 a popover scrolls the page once on open (R2), by design (D4). On `/jobb` at rest it does not.
- **A sheet covers its pill and locks nothing.** No scrim and no scroll lock, so a tap on the margins or the header closes it, as it does today; a scroll does not.
- **The focus-out close is new behaviour on desktop** (notice N3): a popover that stayed open when Tab left it now closes.
- **The touch floor reaches anchored panels between 641 and 768 px**, so rows there are taller than on the base (D11).
- **The results toolbar still jumps to the top** on a sort or a chip-× (#2215, notice N4), so `/jobb` has two behaviours until that issue is decided.
- **`HeroChip`'s gap changes from 6 px to 8 px** (D6).
- **Three things are Klas's live check:** the soft keyboard, `dvh` on a real phone, and the real dark theme.

## Implementation

One PR on `fix/popover-viewport`, lane FE, #2212. It adds no package and no backend change. `globals.css` gets D5's one declaration under the shared `hotspot:css`; the sheet's rules, the column cap, the
minimum-height resets and the touch floor are in `app.css`. The CTO's planned commit order (w) is: the one-off characterization test; `{ scroll: false }` with its assertions (D12); `JobbFilterPopover`
onto `usePanelPosition`; the placement contract with its tests and the deleted comments (D1 to D3); z 20 with its relation test (D5); the reveal (D4); `HeroChip` onto the hook; the shared shell (D6);
focus-out (D8); the touch floor (D11); the sheet with its host tests (D9, D7, D10), which deletes the characterization test; and then the docs: this ADR, the amendments to ADR 0055 and 0180, the
README rows and a DESIGN.md §6 bullet extending the bell's. Each commit passes lint, `tsc` and `pnpm test`, and `pnpm build` runs before the push.

**Test premises (AGENTS.md §5, Tests), as the CTO set them in (s); the delivered tests are reviewed in the PR.**
- Every stubbed rect and viewport value is a measurement of 2026-10-24, and the test names who produced it ("the layout engine at 1280×720, sv, unselected pills, measured 2026-10-24 on
  `5ad51c9c1`"). No `C:/tmp` path appears in a tracked file. A `matchMedia` stub names a touch device as its actor.
- The reachable clamp cases are anchored widths above 640 px, at the lefts measured on the base at 1280 and 1024 px and on `/foretag/sok` at 768 and 700. The two unreachable branches and the no-layout
  case (D2) are declared so.
- The pure function has table tests without DOM stubs, plus the boundaries 640/641 and 500/501. The hook is tested for a re-measure on `resize`, no re-render on a column or a page scroll (a render
  counter), listeners removed on close, and the reveal firing or not (never in a sheet, never without layout, `scrollIntoView` stubbed).
- The host tests cover: in a sheet at 375 the attribute and no inline top, left or width; the drill-down with `activeElement` asserted after each transition and never `body`; Distans and the field at
  level one only; the X and the footer returning focus to the pill on every sheet host; all four cells of D7 and an absent `matchMedia`; D8 including the `InfoDialog` exemption and a null
  `relatedTarget`; typing in a sheet; the accessible name equal in both placements; Bransch passing 560; `HeroChip` with no inline z.
- D12: the 19 existing assertions on `commit` take the two-argument form with exact equality, and one new Filter pick test with real concept IDs is red on the base.
- A one-off characterization test hashes the normalised outerHTML of the open Ort popover against a base constant from `5ad51c9c1`; it is added before the refactor and deleted in the sheet commit,
  whose cap move is the intended change.

## What comes back to the CTO

Nothing detects these automatically. Each is read when the event is proposed or reported.
- A new package (a §12 STOPP).
- A `ResizeObserver`, if a reflow or a scrollbar is found to move the trigger while a panel is open (D3).
- The PR splitting after the touch floor, if the sheet's design verdict needs a second design round: only by the CTO's routing, with Klas told (the CTO (w)).
- A Blocker or Major on D4's in-between band, closed by a code-only change to the condition and `design-reviewer`'s re-measurement (the CTO (f); §9.6).

## References

- **Code, at the base `5ad51c9c1` unless it is new** (`web/jobbliggaren-web/src/`): `lib/popover/placement.ts`, `lib/popover/focus-on-open.ts` and `lib/popover/focusable.ts` (new) ·
  `lib/hooks/use-panel-position.ts`, `lib/hooks/use-close-on-focus-out.ts` (new) and `lib/hooks/use-dismissable.ts` (`PORTALED_LAYER_SELECTOR`) · `components/popover/popover-shell.tsx` (new) ·
  `components/job-ads/jobb-filter-popover.tsx`, `jobb-klass2-panel.tsx`, `jobb-toolbar-popover.tsx`, `hero-chip.tsx`, `jobb-hero-filters.tsx` and `jobb-match-grade-filter.tsx` ·
  `components/company-criteria/bransch-popover.tsx` and `foretag-sok-searchbar.tsx` · `lib/applications/anchored-top.ts` (the precedent) · `app/globals.css` (`.jp-popover` z 20) and `app/(app)/app.css`.
- **Tests:** `app/(app)/popover-stacking.test.ts` and `app/(app)/popover-touch-floor.test.ts` · the pure functions' tests beside `placement.ts` and `focus-on-open.ts`.
- **The plan reads (local, ADR 0072):** `docs/reviews/2026-10-24-2212-plan-cto.md` (the ruling, (a) to (z)) · `…-plan-design-reviewer.md` · `…-plan-code-reviewer.md` · `…-plan-test-writer.md` ·
  `…-plan-dotnet-architect.md` · `…-plan-alt-design.md`; the earlier ruling `docs/reviews/2026-10-23-2198-pr2-plan-cto.md` ((e), (k), (o)).
- **ADRs:** 0047 · 0053 (amendment of 2026-10-03) · 0055 (`:42`, `:69`, `:146`, `:149`, and the amendment of today) · 0067 (E2b, E2c, E2f, E2h VAL 2) · 0117 · 0162 · 0180 (D5, D8, and the amendments of
  2026-10-23 and today) · #408 (Matchning) and #999 (Bransch).
- **Standards:** WCAG 2.1 and 2.2, SC 1.4.10, 2.1.1, 2.4.3, 2.4.7, 2.4.11 and 2.5.5.
- **Spec:** CLAUDE.md §6.5, §9.2, §9.6 · AGENTS.md §1, §2.4, §5 (`Tests:`, `Comments:`), §7, §8 points 4 and 9, §12 · DESIGN.md §5, §6 (`:148`) and §10 · the web `AGENTS.md` (visual-verify, `pnpm build`).
- **Books:** Martin, *Clean Architecture* (2017), ch. 7, 8 and 13 · Fowler, *Refactoring*, 2nd ed. (2018), "Remove Flag Argument" and "Remove Dead Code" · Hunt/Thomas, *The Pragmatic Programmer* (1999),
  DRY · Feathers, *Working Effectively with Legacy Code* (2004) · Winters/Manshreck/Wright, *Software Engineering at Google* (2020), ch. 12.
- #2212 · #2215 · #2214 · #2198 · #2033
