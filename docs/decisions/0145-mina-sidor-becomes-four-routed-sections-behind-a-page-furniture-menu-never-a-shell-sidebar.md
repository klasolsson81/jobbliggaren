# ADR 0145 — Mina sidor becomes four routed sections behind a page-furniture menu, never a shell sidebar

**Date:** 2026-09-27
**Status:** Accepted (D1's current-item marking, and its ≤900px `.jp-subnav`-form
cross-reference, superseded by Amendment 2026-09-28 (#1916) — the section menu in its
settled form)
**Deciders:** Klas Olsson (the three directive answers below, 2026-09-27, via `AskUserQuestion`) ·
`senior-cto-advisor` (`docs/reviews/2026-09-27-1891-form-cto.md`) · `security-auditor`
(the row 7/8/14 signatures and the new row-19 export-contact line, `docs/reviews/2026-09-27-1891-form-security.md`) ·
`design-reviewer` (the shell and menu form, one Blocker and four Majors,
`docs/reviews/2026-09-27-1891-form-design.md`)
**Related:** [#1891](https://github.com/klasolsson81/jobbliggaren/issues/1891) ·
[PR #1896](https://github.com/klasolsson81/jobbliggaren/pull/1896) (PR 1 of #1891, merged) ·
#1903, #1908, #1911 (merged between PR 1 and PR 2; D3) · ADR 0054 (header-menu shell boundary —
unchanged by this ADR) · ADR 0057 (Decision 2 superseded in part here; Decision 1 already
superseded by ADR 0142) · ADR 0142 (Amendment
(5) — the two-column page this ADR replaces; Amendment (6) — the pointer-style precedent for how
that correction is recorded) · ADR 0144 (Decision 4 rows 7, 8, 14, and the new row 19) · ADR 0047
(design-reviewer's flow-comprehension mandate) · ADR 0053 (the `@modal` intercepting-route
mechanism this page reuses)
**Measured against:** PR 2's branch on `main` at `43c29641`, 2026-09-27. The delivered
`web/jobbliggaren-web/src/app/(app)/mina-sidor/**`, `.../@modal/mina-sidor/[...rest]/page.tsx`,
`src/components/settings/mina-sidor-{shell,nav}.tsx`,
`src/components/settings/{notifications-section,account-section,privacy-card}.tsx`,
`src/lib/nav/mina-sidor-hrefs.ts`, `src/Jobbliggaren.Infrastructure/Email/EmailTemplates.cs` and
`tests/Jobbliggaren.Application.UnitTests/Email/NotificationMailLinksLandOnServedRoutesTests.cs`
were read directly for this record, alongside DESIGN.md §5/§6 as already delivered on this branch.

---

## Context

ADR 0057 (2026-05-20) built `/installningar`: one page, five cards in order (Personuppgifter,
Visning, Aviseringar, Sekretess och data, Logga ut), all orchestrated by one client component,
`SettingsForm`, which held every direct-apply preference's state and sequenced every
`updateMyProfileAction` call through a single `useTransition`.

ADR 0142's passwordless-auth epic retired that page. Its Amendment (5) (#1740, part 3b PR A,
2026-09-22) replaced `/installningar` with `/mina-sidor` — rebuilt as a V3-native page, but still
one page, in two columns: column 1 held Matchning, Matchningsnotiser and Notiser om företag du
följer; column 2 held Visning, Byt e-postadress, Sekretess och data and Logga ut.
`/installningar` and `/mig` became
permanent 308s to `/mina-sidor`, because the Art. 7(3) withdrawal link in every notification mail
already sent pointed at one of them and no measurement can show that no inbox still holds one
(security-auditor, at Amendment (5)).

As the auth epic's later parts landed on that same single page — re-authentication by code for
delete and change-email (Amendment (6)), the account losing its name (D7, Amendments (7)/(8)),
six-box code entry (Amendment (9)) — column 2's content kept growing. #1891 asked to turn the
dense two-column page into a proper sectioned settings surface: real routes instead of scroll
targets, grouped the way `/installningar` once grouped cards, but addressable the way ADR 0142's
own withdrawal-link argument already assumes a settings surface must be.

Klas ran an `AskUserQuestion` round on 2026-09-27 (his three answers are recorded verbatim in
substance below). `senior-cto-advisor`, `security-auditor` and `design-reviewer` ran a form round
the same day against this branch's already partly-built tree, before any PR 2 was opened
(`docs/reviews/2026-09-27-1891-form-{cto,security,design}.md`). This ADR is
PR 2's. PR 1 (#1896, merged) carried the Notiser section's consent-copy rewrite
(`NotificationsSection`) on the pre-#1891 two-column page, and its own ADR 0144 amendment for the
rows it re-signed — kept deliberately separate so the copy surface's own test harness
(`settings-form.test.tsx`'s "exactly two toggles" / "exactly one cadence choice" assertions) was
never deleted in the same diff as the change it verifies.

## Decision

`/mina-sidor` becomes four static Next.js routes — Matchning (the bare path), Notiser, Konto and
Sekretess och data — sharing one server-rendered shell and a page-furniture section menu, in place
of the single two-column page ADR 0142 Amendment (5) delivered. The menu lives inside this one
route family only; it is not, and does not become, the application shell ADR 0054 Decision 3
forbids.

### D1 — Four routes, no `layout.tsx`, one shared shell

Four routes under `src/app/(app)/mina-sidor/`: `(matchning)/page.tsx` (a route group, so Matchning
renders at the bare `/mina-sidor` path every link sent before #1891 already carries),
`notiser/page.tsx`, `konto/page.tsx` and `sekretess/page.tsx`, each with its own `loading.tsx` and
its own `generateMetadata` (`pages.minaSidor.sections.<section>` — one title per section).
`@modal/mina-sidor/[...rest]/page.tsx` closes the parallel modal slot for every path under the
four routes, the same slot ADR 0053 introduced.

There is no `mina-sidor/layout.tsx`. Each page, and its `loading.tsx`, calls the shared
`MinaSidorShell`/`MinaSidorLoading` component itself — the same shape `/foretag` and `cv/(hub)`
(#1385) already use — because `v3-native-routes.test.ts` reads pages, not layouts, and a layout
would owe an error boundary of its own (`route-boundaries.test.ts`). `MinaSidorShell` renders the
pagehero band (title only, no lede — Klas's third answer below), then places `MinaSidorNav` — a
server component with no client JS, taking `active` as a prop the way `ForetagSubnav` does —
beside the section's own content, inside `.jp-settings-layout`.

The menu itself is content-width furniture, not shell chrome: a 232px column under the pagehero,
scoped to these four routes only, never the product's own destinations (those stay in the header,
ADR 0054). The current item is marked by ink-1, semibold and a 3px left bar, with no
background fill (a filled current-item row is the app-sidebar's own idiom, and is exactly what the
furniture/shell boundary below has to keep out). Section content itself is capped at 688px
(`.jp-settings-section`), so a switch never sits ~600px from its label. At ≤900px the menu
collapses into one wrapping row, the same form `.jp-subnav` already uses at the same breakpoint,
and Logga ut — which is otherwise part of the menu, under a hairline — drops out of it entirely,
since the header's user menu already carries Logga ut at every width.

Each section is a real, bookmarkable, shareable URL. `safeRedirectPath` carries a
`next=` through a login redirect to any of the four unchanged, while a crafted
`//evil.example/mina-sidor` is rewritten to `/oversikt` (security-auditor, measured against
`safe-redirect.ts` directly, 2026-09-27) — a guarantee neither a query parameter nor a hash
fragment gives, and exactly the guarantee the mailed Art. 7(3) withdrawal link, itself a deep link
from a logged-out inbox, depends on. Each write action calls `revalidatePath` against its own
section's `MINA_SIDOR_HREF` constant, never a shared path (`updateMatchPreferencesAction` for
Matchning, `updateMyProfileAction` for Konto, and for Notiser `notificationSettingOutcome`, which
its three writes share). Leaving Konto unmounts
`ChangeEmailSetting`, so a change-email challenge in flight is dropped by construction — the
guarantee #1740's S1 finding required of the old page, now a property of the route change itself
rather than of a manual unmount effect.

### D2 — Where this decision is written down

This ADR, not an ADR 0142 amendment. ADR 0142's amendments record the auth epic's own parts,
delivered one authentication mechanism at a time; #1891 changes the page's information
architecture for an unrelated reason (Common Closure Principle, Martin 2017 ch. 13), and ADR 0144
already rejected exactly this form — "an ADR 0142 amendment" — for its own, adjacent copy-density
rule, on the same ground.

The menu (`.jp-settingsnav`) is furniture inside this one route family, the way the CV guide's
step rail (`.jp-guide__rail`) is furniture inside its own family — not the shell sidebar ADR 0054
Decision 3 forbids. ADR 0054 governs where the product's *own* destinations live (the header); this
ADR does not amend it. DESIGN.md §5 and
§6 now say so explicitly (delivered on this branch): §5 points from "ingen sidebar (ADR 0054)" to
§6's own boundary line, and §6 names `.jp-settingsnav` as page furniture, citing this ADR by
number, so the next reviewer meets the boundary in the design system itself rather than having to
infer it from a 232px column.

`/mina-sidor` stays a page, and `/mina-sidor/notiser` becomes a second permanent mail
target: renaming either needs a fresh 308 with no removal condition, the same rule Amendment (5)
already applied to `/installningar` and `/mig` — a mail already sent cannot be recalled, and no
measurement can show that no inbox still holds one. `NotificationMailLinksLandOnServedRoutesTests`
is the register: its `PathsSentMailsCarry` theory already names both `/mina-sidor` and
`/mina-sidor/notiser`.

ADR 0057's Decision 2 is superseded in part — its one `SettingsForm` orchestrating every card: this
PR deletes `SettingsForm` along with the two-column page it orchestrated. Decision 1 (already
superseded by ADR 0142) stays superseded. Every section here still applies changes directly,
through the same primitives, one action per control.

Klas's second answer below — keep the data-export button, marked "Kommer snart" rather than
hidden — is recorded here in full, because ADR 0057 Decision 3 and its rejected Alternative C
forbid exactly this shape ("civic-utility apps do not show 'coming soon' elements") on a civic-tone
argument. Without this ADR naming Klas's explicit exception, the delivered button reads as an
unflagged violation of ADR 0057, not as a considered deviation from it. The button stays visible,
`aria-disabled`, its accessible name carrying "Kommer snart" as text (never a `title` tooltip, the
form ADR 0142's inactive OAuth buttons already established), and is joined by a new line naming the
route that still works today — bound as ADR 0144's row 19 (security-auditor, Major, closed on this
branch):

- sv: *"Vill du ha en kopia av dina data kan du mejla kontakt@jobbliggaren.se."*
- en: *"If you want a copy of your data, email kontakt@jobbliggaren.se."*

Art. 15/20 give the right now, not once the button is wired, and Art. 12(2) requires facilitating
its use; "Kommer snart" with no other route reads as though the right itself can wait. The contact
route is the same one the privacy policy already names for the same rights, so pairing it with the
stub closes the Major without granting a self-service export that does not exist.

### D3 — Two PRs, in this order

- **PR 1** (part of #1891, merged as #1896): `NotificationsSection` replaces the two notification
  cards on the *old* two-column page; `SettingsForm` loses `cadence` and `followEnabled` but is not
  yet deleted. The Notiser copy (ADR 0144 Decision 4 rows 7 and 8) is signed by `security-auditor`
  and pinned in `legally-bound-copy.test.ts` in the same PR.
- **PR 2** (closes #1891, this branch): the four routes, the shared shell and menu, the CSS, the
  Konto section (D5), the Sekretess section and its row-19 line, the remaining copy, e2e coverage,
  `EmailTemplates.cs`'s settings link at both call sites, `NotificationMailLinksLandOnServedRoutesTests`'s
  register, this ADR (published with `git add -f`, since `docs/decisions/01[0-9][0-9]-*.md` is
  gitignored and an unreadable ADR cannot bind the sessions it is meant to — #1173), and the
  DESIGN.md §5/§6 lines D2 describes.

Three changes to the consent writes and the digest landed between PR 1 and PR 2 as PRs of their
own: #1903 (the cadence's own write), #1908 and #1911 (ADR 0146). PR 2 is built on all three.

One change-reason per changeset (Martin 2017 ch. 7; Winters, Manshreck & Wright 2020 ch. 9, "Write
Small Changes"). The consent-writing surface's own test harness stays around `NotificationsSection`
in PR 1 and verifies it; folding both changes into one PR would delete that harness in the same
diff as the change it is meant to check (Fowler 2018 ch. 4). `NotificationMailLinksLandOnServedRoutesTests`
also makes the mail link and the route that serves it one invariant: the link landing before the
route fails CI, and the route landing without the link leaves new mail one menu step further from
the withdrawal surface than it needs to be.

### D4 — Notiser: two switches, one shared cadence, under them

Klas's first answer below: Notiser holds exactly two switches (the background-match consent, the
followed-company mail consent) and exactly one cadence selector, placed under both rather than
duplicated per switch. One component, `NotificationsSection`, owns all three values. Each control
writes only its own value, to its own endpoint, through its own `useTransition` (#1903):
`PUT /api/v1/me/background-match-notification-consent` and
`PUT /api/v1/me/followed-company-notification-consent` carry `{enabled}`, and
`PUT /api/v1/me/digest-cadence` carries `{cadence}`. Each control keeps its own save receipt,
reported under the control that started the write (#1391's established channel discipline).

### D5 — Konto: one card, groups instead of a second card

Konto is one card whose `h2` is the menu's own label, holding the address-change group
(`ChangeEmailSetting`, renamed from `ChangeEmailCard` since it no longer renders a card of its own —
Martin 2008 ch. 2, "Avoid Disinformation") and the language group, each `h3`-headed, the same shape
Sekretess already had in `DeleteAccountSection`. This keeps all four sections at one card each, so
Konto is not the one exception in a four-part structure with two cards and no heading that matches
the menu.

### Klas's directive (2026-09-27, `AskUserQuestion`, recorded verbatim in substance)

1. Four sections, in this order: Matchning · Notiser · Konto (e-postadress, språk) · Sekretess och
   data (export, radera konto). Logga ut sits at the foot of the menu. Notiser is two switches and
   exactly one cadence selector under them.
2. *"Behåll, vi ska bygga funktionen senare. Du kan ev. skriva 'kommer snart' eller liknande."* —
   the export button stays, visibly marked "Kommer snart" rather than hidden or removed.
3. *"Band utan ingress"* — the green pagehero band carries the title "Mina sidor" only, with no
   lede.

## Alternatives considered

### Alt A — Four static routes, no `layout.tsx` — selected

Per D1. **For:** a section survives a login redirect through `next=`; matches the established
`/foretag`/`cv/(hub)` route-group pattern, so all existing fitness functions apply unmodified
and no guard needs rewriting; one page fetches only what its own section needs (Matchning reads
the profile, taxonomy and skills; Sekretess reads only the session); a real, unique document title
per section (WCAG 2.4.2); #1740's S1 finding (a live change-email challenge surviving where it
should not) is closed by construction. **Against:** switching sections re-renders the menu, so
focus drops to the document body rather than staying on the link (measured 2026-09-27 in
Chromium: the route announcer reads the new section's title, and the next Tab lands on the menu's
first item); a change-email challenge in
flight is dropped on navigating away from Konto, costing a fresh mail if the user returns to finish
it.

### Alt B — A `?avsnitt=` query parameter — rejected

RFC 3986 §3.3–3.4: the sections are hierarchical children of Mina sidor and belong in the path, not
the query string. A query parameter survives the login redirect only by breaching a
security-pinned invariant of `next` (`proxy.test.ts:387-400`); a bare
`#hash` never reaches the server at all and is lost across the same redirect. The mailed Art. 7(3)
withdrawal link is exactly this kind of deep link from a logged-out inbox, so it is the case Alt B
would fail first.

### Alt C — One route, client-side tabs — rejected

Fetching every section's data on every visit is wasted work. Hiding the inactive sections instead
of unmounting them keeps a live change-email challenge alive across a "tab" switch and reopens
#1740's S1 finding; unmounting on switch makes this Alt A again, minus the URLs. The menu is
navigation (`aria-current`, a real link per section), not a WAI-ARIA APG Tabs widget, and does not
carry that pattern's keyboard contract.

### Alt D — Alt A with its own `layout.tsx` — rejected

Needs a client-side nav wrapper, its own `error.tsx`, and a rewritten `v3-native-routes` guard
(which reads pages, not layouts) — for a benefit, the menu link keeping DOM focus across a section
switch, that `/foretag` has already forgone.

### Alt E — A dynamic `[avsnitt]` segment — rejected

Fails `NotificationMailLinksLandOnServedRoutesTests` (which reads literal `page.tsx` directories
under `src/app/(app)/`) and the per-section document-title guard (which reads literal keys), for no
gain over four literal folders.

## Consequences

### Positive

- Every section is a real, bookmarkable, shareable, deep-linkable URL that survives a login
  redirect — the guarantee ADR 0142's own withdrawal-link argument already assumed a settings
  surface would have.
- The route shape matches `/foretag` and `cv/(hub)`, so the existing fitness
  functions (including `v3-native-routes.test.ts`) apply unchanged.
- A live change-email challenge cannot leak across sections (#1740 S1), by construction.
- The menu/shell boundary is written down in DESIGN.md §6, naming this ADR, rather than left for
  the next reviewer to infer from a 232px column that could otherwise be misread as ADR 0037's
  shell sidebar returning.
- Mail already sent keeps working: `/mina-sidor` still resolves — it is Matchning's own URL, not a
  redirect — and `NotificationMailLinksLandOnServedRoutesTests` pins both the old and the new
  withdrawal-link path, so a future rename cannot silently drop either.
- Klas's "Kommer snart" exception for the export button is written down against ADR 0057 Decision 3
  and its rejected Alt C, so the button reads as a named, considered deviation rather than an
  unflagged one.
- The two-PR split kept PR 1's consent-copy test harness intact through its own change, and lets PR
  2's routing change be reviewed by its own primary reviewers without the copy surface's churn in
  the same diff.

### Negative

- `/mina-sidor` and `/mina-sidor/notiser` are now permanent URLs with no removal condition — the
  same standing maintenance obligation `/installningar` and `/mig` already carry.
- A section switch drops focus to the document body, and a change-email challenge in flight is
  dropped on leaving Konto, costing a fresh mail if the user returns to finish it. Accepted per
  Alt A's trade-off; no mitigation is planned.
- Four `page.tsx` files each call `MinaSidorShell` and repeat a small amount of session/redirect
  boilerplate, instead of sharing one `layout.tsx`. Accepted because Alt D's cost — a client nav
  wrapper, an extra `error.tsx`, a rewritten route guard — was judged higher than four short files.
- Any future change to a Notiser or Sekretess copy string ADR 0144 Decision 4 binds needs
  `security-auditor`'s signature and a `legally-bound-copy.test.ts` pin in the same PR — an ongoing
  review-gate obligation this page now carries by holding that copy, not a one-time cost of this
  change.
- The data-export right (Art. 15/20) stays not-self-service. The visible "Kommer snart" plus the
  row-19 contact line is the interim discharge of Art. 12(2)'s facilitation duty, not the right
  itself, and is accepted as an explicit, named, time-bound gap rather than a silent one.

## Implementation

- **PR 1** (#1896, merged): `NotificationsSection` on the pre-#1891 two-column page;
  `SettingsForm` loses `cadence`/`followEnabled`; ADR 0144 rows 7/8 re-signed in PR 1's own
  amendment.
- **PR 2** (this branch, closes #1891): the four routes (`(matchning)/page.tsx` + `loading.tsx`,
  `notiser/`, `konto/`, `sekretess/`), `@modal/mina-sidor/[...rest]/page.tsx`,
  `MinaSidorShell`/`MinaSidorNav`/`MinaSidorLoading`/`ProfileUnavailable`, `MINA_SIDOR_HREF`, the
  `.jp-settings*`/`.jp-settingsnav*` rules in `app.css`, `AccountSection` (D5), `PrivacyCard`'s
  export line (ADR 0144 row 19), `EmailTemplates.cs:77,168`'s settings link (now
  `/mina-sidor/notiser` at both call sites), `NotificationMailLinksLandOnServedRoutesTests`'s
  `PathsSentMailsCarry` register, this ADR, DESIGN.md §5/§6, and the frontend-visual-verification
  runbook.
- `security-auditor`'s form-round verdict: 0 Blockers, 1 Major (the export-contact line, closed by
  the code above), 1 Minor (a pre-existing session-lifetime gap that loses the target section on an
  expired-but-not-yet-cleared session cookie — not introduced by #1891, filed as #1893).
- `design-reviewer`'s form-round verdict: 1 Blocker (the Notiser switches' hit area, addressed in
  PR 1) and 4 Majors (Logga ut hidden at ≤900px, section content capped at 688px, the
  `matchningar.emptyBody` copy fix, and the DESIGN.md §6 pointer this ADR's D2 describes). Of its
  five Minors, `ToggleRow`'s description colour is #1894; PR 2 addresses the other four.

## References

- [#1891](https://github.com/klasolsson81/jobbliggaren/issues/1891) ·
  [PR #1896](https://github.com/klasolsson81/jobbliggaren/pull/1896)
- `docs/reviews/2026-09-27-1891-form-cto.md`, `-security.md`, `-design.md`
- ADR 0047, 0053, 0054, 0057, 0142, 0144
- Martin, *Clean Architecture* (2017), ch. 7 and 13; *Clean Code* (2008), ch. 2
- Fowler, *Refactoring*, 2nd ed. (2018), ch. 4
- Winters, Manshreck & Wright, *Software Engineering at Google* (2020), ch. 9
- RFC 3986 §3.3–3.4
- GDPR Art. 7(3), 12(2), 15, 20
- WCAG 2.4.2

## Amendment 2026-09-28 (#1916) — the section menu in its settled form

**Supersedes.** D1's own description of the current item — *"The current item is marked
by ink-1, semibold and a 3px left bar, with no background fill (a filled current-item row
is the app-sidebar's own idiom, and is exactly what the furniture/shell boundary below has
to keep out)."* — and, inside D1's ≤900px sentence, the clause *"the same form
`.jp-subnav` already uses at the same breakpoint"*. The row still wraps at the same 900px
breakpoint, and Logga ut still drops out of it, but in the menu's own row form (wrapping
rows, every item at least 44px) rather than `.jp-subnav`'s underline form. #1891's
form-round design-reviewer Minor 1 rationale — the accent-50 fill removed as the
shell-sidebar's own idiom (`docs/reviews/2026-09-27-1891-form-design.md`) — is withdrawn: a
filled current item is no longer read as that idiom (see "What still separates the menu
from a shell sidebar" below). This ADR's Context, D2, D3, D4, D5, the 2026-09-27 directive,
Alternatives, Consequences and Implementation were checked against the bar, the no-fill
rule, a medium weight, green inactive items and `.jp-subnav`'s form as the menu's current
form, and none of them makes any of these five claims — D1 itself is silent on
inactive-item colour and weight, so this amendment states them for the first time rather
than correcting a prior claim there.

### Klas's directive (2026-09-28, start prompt, recorded verbatim in substance)

> Sektionsmenyn på /mina-sidor. Rör inte bannern eller innehållskortet i den här PR:en.
> Förebild: Claudes egna Settings; vänster och höger ska sitta ihop.
> - Bredd ca 220–240 px och ca 24–32 px glapp mot kortet. Menyns topp i linje med kortets
>   topp. Sticky vid scroll, med en offset som klarar den sticky appheadern.
> - En lucide-ikon (ca 18 px) till vänster om varje val: `Target` Matchning, `Bell`
>   Notiser, `User` Konto, `Shield` Sekretess och data.
> - Rader ca 40 px höga, ca 12 px horisontell padding, samma radie som kortet (§5: max
>   8 px; pill bara för piller).
> - Text i neutral mörkgrå, inte grön. Ikoner i muted grå. Hover: diskret grå bakgrund.
> - Aktivt val: ljusaste gröna plattan, text och ikon i primärgrönt, weight 600. Den tunna
>   vänsterlinjen tas bort. `aria-current="page"` och en synlig fokusring behålls.
> - Logga ut: tunn avdelare och luft, samma radstil i muted färg, hover med svag röd eller
>   neutral ton.
> - Mobil: en horisontellt scrollbar rad i samma pill-stil ovanför innehållet. Logga ut
>   läggs sist i raden eller döljs (den finns också under profilikonen). Sidan själv får
>   aldrig scrolla i sidled.
> - Uppdatera ADR 0145 och DESIGN.md §6, som beskriver dagens vänsterlinje. Menyn förblir
>   sidmöbel, aldrig shell-sidebar (ADR 0054).
> - Sitter vänster och höger fortfarande inte ihop efter bytet (kortet är kapat vid 688 px
>   medan bannern går i full bredd), ta det till Klas. Ändra inte kortet på eget bevåg.

Klas placed the mobile row "under md"; the layout already stacks at 900, so the form round was
asked to bind the breakpoint. The sideways-scrolling shape was his own, and design-reviewer's
Blocker 1 put it back to him (answer 1 below).

### Klas's answers (2026-09-28, `AskUserQuestion`, after the form round)

1. Mobile row: *"A: Raden bryts (Rekommenderat)"* — the row wraps at ≤900 and never
   scrolls sideways. This answers design-reviewer's Blocker 1: a sideways-scrolling row in
   `<main>` fails WCAG 1.4.10 (no horizontal scrolling at 320px for main content), and the
   only script-free scroll affordance would have been a fade, which is a gradient (AGENTS.md §5).
2. Konto's icon: *"UserRound (Rekommenderat)"* — the glyph the header's avatar button,
   user menu and drawer already use, not `User`.
3. Icon size: *"16 px (Rekommenderat)"* — DESIGN.md §7 (16px inline with text) is
   unchanged; the answer replaces the directive's "ca 18 px".
4. Menu and card (2026-09-29, after the panel round, with the page open live): *"Det räcker så
   (Rekommenderat)"* — the card stays at 688px and the 192px to the band's right edge stays.

### The settled form (design-reviewer, report-only form round, 2026-09-28)

`docs/reviews/2026-09-28-1916-form-design-reviewer.md` (gitignored, local). Rulings:

- **Breakpoint 900, not 768.** The header changes form at 900 and the menu follows it.
  With two columns between 769 and 900, the card would be 449–580px wide, under DESIGN.md §5's 640px
  form measure.
- **Radius `--jp-r-md` (6px), not pill.** Klas: *"pill bara för piller."* Above
  Matchning's pill chips, a pill row would read as more removable chips (ADR 0047).
- **Gap 24px** (the low end of Klas's 24–32 range). **Width 232px** unchanged. Sticky
  offset unchanged.
- **Rows:** `min-height: 40px`, `padding: 8px 12px`. At ≤900 every item is at least 44px
  (as `.jp-subnav__item` already is), and the old bottom line goes.
- **Inactive text `--jp-ink-1`, not ink-2** — repo doctrine (DESIGN.md §4, `.jp-subnav`'s
  #549 WS1 note), and ink-2 would sit only 1.04:1 from the current item's green. Klas's
  *"neutral mörkgrå"* is therefore the repo's neutral text token, not a new one.
- **Icons `--jp-ink-2`**, `--jp-accent-700` on the current item, 8px gap, `aria-hidden`.
  Hover (`--jp-surface-3`) changes neither text nor icon colour.
- **Current item** = `--jp-accent-50` plate + `--jp-accent-700` text and icon +
  `--jp-fw-semibold`, plate kept on hover. Neither the bar nor a darker plate is needed, so
  this drew no question to Klas.
- **Blocker 2 (WCAG 1.4.1).** Without the bar, weight was the only non-colour cue left,
  and the colour step alone measured under 3:1: plate vs. canvas 1.06:1 (dark 1.19:1);
  accent-700 vs. ink-1 2.31:1 (dark 1.43:1). Fix: inactive items and Logga ut drop to
  `--jp-fw-regular` (400), the current item stays 600 — the 400→600 step is the state's
  non-colour cue and must never be evened out. A grayscale render showed the step alone
  still points out the current item.
- **Other measured contrasts:** accent-700 text on the plate 6.62:1 (dark 9.98:1); ink-1
  on canvas 16.14:1 (dark 17.03:1); icon ink-2 7.24:1 (dark 11.60:1). In light the hover
  background is as strong as the plate (1.03:1 between them) — accepted, because weight
  and colour, not this gap, carry the state.
- **Logga ut.** `LogOut` at 16px; text and icon `--jp-ink-2`, weight 400; the same 40px
  row, `8px 12px` padding; a `--jp-border-soft` hairline with 12px of air above. Hover is
  neutral (`--jp-surface-3` + `--jp-ink-1`, 14.84:1), as in the header's own user-menu
  Logga ut — a red hover would be a Major, since danger is reserved for destructive
  actions ("Radera konto" already uses danger on the same page) and logging out is not
  one. Hidden at ≤900 (#1891 Major 1); the header's user menu carries it at every width
  regardless.
- **Major 2 — the global link rule.** `globals.css`'s `a:not(…)` rule is (0,1,1)
  specificity and beat the item's (0,1,0) colour, painting every inactive item
  accent-green. Fix: `.jp-settingsnav__item` joins the rule's one `:not()` list, so
  specificity stays (0,1,1); pinned by `globals-link-rule.test.ts`.
- **Binding 6 — unity with the card.** The menu and card read as one unit at
  1280/1920/3440 (24px gap, shared top line, the plate ties them). The group (944px) still
  ends 192px before the band's right edge from 1280 up, because the band is capped at 1136
  and the card itself is capped at 688 — that cap is #1891 Major 2 (640px form measure,
  ~68ch consent copy, the GOV.UK two-thirds form) and the
  card is unchanged. Right-aligning menu and card would be a separate decision about the
  card; the only a11y-safe form of it would hold the card's frame at 880px with its content
  still held at 640px.
- **The icon principle.** `.claude/skills/jobbpilot-design-principles/SKILL.md` §3
  forbade icons that "'smyckar' varje rad"; its "Korrekt" line now allows an icon that
  signals a destination in a navigation menu (user menu, drawer, this menu) — one fixed
  icon per destination, never per data row. DESIGN.md §6's `.jp-settingsnav` line was
  rewritten to this delivered form in the same PR; this amendment does not restate it.

**What still separates the menu from a shell sidebar** is placement and scope, not the
rows' own style: it sits under the pagehero, inside the content width; it exists only on
these four routes and shows only this family's own sections; it has no surface or frame of
its own; it is sticky within the page grid, never fixed to the viewport. ADR 0054 is
unchanged by this amendment, as it was unchanged by D1. The header, the drawer and
`.jp-subnav` keep ink text plus the accent bar (E2f, ADR 0068).

The menu stays a server component with no client JS; Klas's answer A (the row wraps) needed
none.

### Rendered measurement (DoD #4)

Commit `6b9b42df8` on `feat/mina-sidor-menu` (base `cb9f00bb`), 2026-09-28: a production
build (`next build` + `next start`) over a local stub backend, Playwright Chromium, light
and forced dark (`data-theme="dark"`). Renders and `facts.json` are in the local render set
`C:/tmp/jobbliggaren-visual/1916/r1/` (the "before" set, on `cb9f00bb`, is
`.../1916/r0/`). Widths covered: all four sections at 1280 in both themes; Matchning at
390/768/769/900/901/1920/3440; Sekretess at 320/375/390/600/768/900 for the ≤900 row (390
also dark); 200% zoom at 1280.

`getComputedStyle`/`getBoundingClientRect` readings: nav 232px wide at x=72, section card
at x=328 (gap 24), tops aligned (Δy 0), sticky top 113 (header bottom 89 + 24). Rows 40px,
padding `8px 12px`, radius 6px, no left border. Inactive text rgb(12,26,46) light /
rgb(244,247,252) dark at weight 400; hover background rgb(232,237,244) light /
rgb(40,60,94) dark, text unchanged. Current item rgb(21,96,63) on rgb(233,242,237) light,
rgb(110,231,168) on rgb(14,42,30) dark, weight 600, plate kept on hover. Icons 16×16,
`aria-hidden="true"`, rgb(69,83,102) light / rgb(194,207,226) dark, accent-700 on the
current item. Logga ut rgb(69,83,102) at weight 400, 40px row, hover rgb(12,26,46) on
`--jp-surface-3`; `display: none` at ≤900. Keyboard focus on the current item: a 2px solid
accent-700 ring at 2px offset, `:focus-visible` true, visible around the plate. At ≤900:
two rows at 320/375/390, one row at 600/768/900, every item at least 44px, menu
horizontal overflow 0 at every width measured, the current item in view, the plate kept,
the old bottom line gone.


**Amendment 2026-09-28 (#1917):** D1's "title only, no lede" clause and Klas's third answer, "Band utan ingress", are superseded by ADR 0144 Amendment 2026-09-28 (#1917); the band-line rule lives in DESIGN.md §6, §8 rule 8 and §11.
