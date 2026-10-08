# ADR 0150 — The admin surface: a scoped dashboard, honest unavailability, and a preview no deployed build contains

**Date:** 2026-10-04
**Status:** Accepted
**Decider:** Klas Olsson (product owner). Sources: the Claude Design handoff `docs/design_handoff_admin/README.md`
(local and untracked, gitignored in the PR that carries this ADR; it records the "fullt dashboard" latitude Klas
approved on 2026-10-03, and is cited here only for what D2 and D8 name) · Klas's prompt of 2026-10-04, "implementera
Admin MVP enligt epic #1972" · his two planning answers of 2026-10-04, "E-post som identitet (Rekommenderas)" and
"Förhandsvisning i repot (Rekommenderas)"
**Amends:** DESIGN.md §1.2 ("Kort-layouter överallt") and §6 ("Inga stats-kort runt enstaka värden") with a scoped
exception for the `/admin` overview only (D1), and adds one §6 rule line for an unbuilt action (D2). ADR 0140 is not
amended (Alternatives considered).
**Related:** ADR 0140 (the `/oversikt` scoped exception: the form this ADR copies; it says its exception reaches no
other page) · ADR 0142 (D7: the account has no name; D8 and its Page form: the inactive provider row, the house
"Kommer snart" form) · ADR 0144 (copy density) · ADR 0131 (Scaleway Transactional Email) · ADR 0120 (a rendered count
is true, or it is absent) · ADR 0076 Decision 4 (cited by DESIGN.md §6 for the ban on a meter, percentage or ring as a
match grade; untouched) · ADR 0038 (one primary per screen) · ADR 0052 (the two button-height systems) · ADR 0028
(admin authorization) · BUILD.md §3.1 (no chart library) ·
[#1972](https://github.com/klasolsson81/jobbliggaren/issues/1972) (the epic) ·
[#1973](https://github.com/klasolsson81/jobbliggaren/issues/1973) (the issue this ADR ships under)
**Measured against:** `origin/main` at `7c21f8117`, 2026-10-04.
**Amended:** 2026-10-04 by ADR 0151 (#1974): four D8 rows for the account list's states, filters, counts and actions;
2026-10-05 by ADR 0153 (#1975): the receipt row's Ships cell rewritten, and one D8 row added for the edit form's primary;
2026-10-07 by ADR 0156 (#1979): five D8 rows for feedback's rating, statuses, list, page and filters, and a dated note on the screenshot and reply rows.

> **Provenance.** Written by `adr-keeper` for the driving session, from its brief (CLAUDE.md §9.2, §13). Klas's own
> words are the three sources above, and the substance of D1 (the latitude), D3 (the address as identity) and D5
> (the preview in the repo) is his. D2 and D4 apply the epic's rule that every layout ships first and nothing
> fabricated reads as real (#1972). D6–D8 are the driving session's mechanics under those decisions; the mandatory
> agents review them in the PR (CLAUDE.md §9.2).

---

## Context

**Two pages become seven.** At `7c21f8117` the admin surface is two pages, Granskning (`/admin/granskning`) and
Bakgrundsjobb (`/admin/jobb`), and `/admin` itself is a 404. The handoff grows it to seven: Översikt (`/admin`, its
variant A, a card grid), Användare, Feedback, Loggar, E-postleverans, Bakgrundsjobb and Granskning. Klas approved
"fullt dashboard" latitude for it on 2026-10-03: cards, charts and meters, where DESIGN.md allows a card grid on
`/oversikt` alone (ADR 0140).

**Most of the data behind the new pages does not exist.** Measured at `7c21f8117`: `/api/v1/admin` has no
account-administration endpoint; login events are log events, not rows (`AuthAuditLogger` writes through `ILogger`),
so the admin has no table to read them from; there is no email outbox; nothing in `src/` observes the host or the
backups. The epic requires every layout first and forbids fabricated users, metrics and successful actions, so most
regions ship unbuilt and must say so.

**The account has no name.** ADR 0142 D7 decided it. Measured at `7c21f8117`: `ApplicationUser` is
`IdentityUser<Guid>` plus `CreatedAt`; `JobSeeker` has no name field and no residence field; external logins are
stored with `providerDisplayName` null (`IdentityExternalLoginStore`). The handoff's names, initials and Ort have
nothing to read from.

## Decision

Eight decisions govern the seven pages.

### D1 — A scoped dashboard exception on the `/admin` overview, and on no other admin page

`/admin` renders a 12-column grid, gap 20px, of cards:

- four KPI cards, each a large number in `--jp-fs-oversikt-num` (40px); the token is reused and none is added;
- a trend region: a hand-built SVG and a text summary computed from the data. No chart library:
  BUILD.md §3.1 lists none, and CLAUDE.md §9.2 bars one outside it without discussion;
- a services list;
- a server card with CPU, memory and disk meters that show an observed reading or nothing (D2). DESIGN.md §6's ban on
  a meter, percentage or ring for a match grade (ADR 0076 Decision 4) is untouched: it binds a grade, and these meters
  show a resource reading;
- a backup card and an email card;
- an attention card, with a 6px `--jp-warning` left edge while something needs attention and `--jp-border-strong`
  when it is clear or unknown;
- a recent-events card.

A card is `--jp-surface` with a 1px `--jp-border`, `--jp-r-lg` and no shadow. ADR 0140's axis tints and solid fills do
not come with it.

Every other admin page keeps the ledger form. The feedback detail is one framed region, not a card grid. The account
panel is a modal side panel using DESIGN.md §10's 200ms slide. `design-reviewer` rejects a card grid on any other
admin page by reference to this decision.

*Why.* The latitude is Klas's (2026-10-03). The reasoning of ADR 0140 carries over: DESIGN.md §1.2 and §6 exist
because a card grid on every page turns a civic utility into a SaaS dashboard, so the exception is cut to the one page
whose job is to set counts, statuses and events side by side, and is written so that it does not spread.

### D2 — Honest unavailability: every data region is one state of a closed union

A data region renders exactly one of `unavailable`, `loading`, `empty`, `failed` or `loaded`; `loaded` may carry
`sampledAt` and `stale`. `unavailable` means that no source exists yet (D4), so "Kommer snart" is only ever said of a
capability that is not built. From this:

- An unknown value is an en-dash with its unit hidden, never 0: the doctrine ADR 0140 states for `/oversikt` and
  ADR 0120 for counts.
- An unknown list shows one "Kommer snart" line and no placeholder rows.
- A count in a filter or tab label is omitted while unknown, never "(0)".
- No health, backup, delivery-rate or verification claim without an observation behind it.
- An unbuilt action is, at control level, an `aria-disabled="true"` button that stays in the tab order and whose
  accessible name includes "Kommer snart": the form ADR 0142 gives an inactive provider row (never natively
  `disabled`, never dimmed with opacity), with no tooltip and no effect when activated. At region level the structure
  stays, its controls are natively `disabled`, and they point with `aria-describedby` to one visible "Kommer snart"
  line. An unavailable action is never a mutation, a success toast, or a solid primary or danger fill. This is the one
  rule line added to DESIGN.md §6.
- One failing source never blanks the rest of a page.
- The footnote "Alla åtgärder loggas…" appears only once it is true.

*Why.* The epic forbids fabricated users, metrics and successful actions (#1972), and all seven pages ship before most
of their data exists (Context).

### D3 — The email address is the account identity

The admin surface names an account by its email address alone: no name, no initials avatar and no Ort line; a neutral
`UserRound` icon where the handoff drew an avatar; search by address only; dialog titles that name the address.

*Why.* Klas chose it on 2026-10-04 ("E-post som identitet (Rekommenderas)"). The account stores no name (ADR 0142 D7;
measured in Context, by reading `ApplicationUser.cs`, `JobSeeker.cs` and `IdentityExternalLoginStore.cs` at
`7c21f8117`). A name or initials would have to be invented, and an Ort has no account field to read.

### D4 — One capability map

An issue flips only its own entries, and reviewers check a PR against this table.

| Issue | Makes real |
|---|---|
| #1974 | listing and inspecting accounts |
| #1975 | changing an account's email address |
| #1976 | suspending and reinstating an account |
| #1977 | scheduled account deletion |
| #1978 | the overview's counts, attention and events |
| #1979 | feedback |
| #1980 | logs |
| #1981 | email delivery |
| #1982 | services, host and backup |
| #1983 | restore, send login link, mark verified, role |
| #1984 | impersonation |
| #1985 | permanent deletion |

The table records ownership, never state: whether an entry is live is read from the code and the running page, so this
ADR does not go stale.

### D5 — A preview no deployed build contains

The interactions that need rows — the account panel, the edit form, the confirmations, the toast, the impersonation
banner, and the feedback, logs and email pages with rows — are shown with fictional data in a separate route group,
`(admin-preview)`, under `/admin/forhandsvisning`. Three locks and the build context keep it out of every deployed
build:

- **(a) Compile.** `next.config.ts` adds the page extension `preview.tsx` only when `ADMIN_PREVIEW_ENABLED === "true"`
  at config load, the house exact-`"true"` flag form (`DEV_TOOLS_RESET_ENABLED`, `src/lib/env.ts`). The preview's
  files are `page.preview.tsx` and `layout.preview.tsx`, which are not routes without the extension.
- **(b) Runtime.** The preview layout and every preview page are `force-dynamic` and redirect to `/admin` when the
  running server lacks the flag.
- **(c) Artifact.** A post-build assertion chained into `pnpm build` fails when a build without the flag contains the
  preview route or the fixture sentinel, and announces "never deploy" when the flag is on.
- **(d) Build context.** The web image's Docker build context (`web/jobbliggaren-web/.dockerignore`) excludes the
  preview files.

Fixtures use reserved domains only (example.com, example.org and example.net; `*.test`, `*.example` and `*.invalid`;
RFC 2606 and 6761) and the documentation ranges (192.0.2.0/24, 198.51.100.0/24 and 203.0.113.0/24, RFC 5737;
2001:db8::/32, RFC 3849). They run on a fixed clock, have no name field (D3), and are held to a
denylist: consumer mail domains and the prototype's infrastructure names. Every fixture row carries the sentinel the
assertion of (c) looks for, the reserved domain `forhandsvisning.invalid`.

The preview reads no backend, so it sits outside the `(admin)` auth gate by design. What keeps it from a user is that
no deployed build contains it, not a login. Implemented under #1973.

*Why.* D2 forbids fictional rows on a real page, so the interactions that need rows have no home on one. Klas chose to
show them in the repo rather than in an uncommitted local harness (2026-10-04, "Förhandsvisning i repot
(Rekommenderas)").

### D6 — A route-group stylesheet; shared tokens, not shared classes

The admin surface gets `src/app/(admin)/admin.css`: `.jp-admin*` classes over existing tokens only, a
`prefers-reduced-motion` block for any motion the file adds, and its path added to the `guard:css` script's file list.
The `/oversikt` `.jp-ov-*` classes stay in `(app)/app.css`.

*Why.* Shared tokens, not shared classes: the doctrine `(app)/app.css` and DESIGN.md §6 already state for the CV guide
rail and the Mina sidor menu.

### D7 — The admin header: one row from 1200px

The nav grows from two entries to seven, so the header is one row from 1200px. Below 1200px the brand takes the first
row and the nav and the account share the second, the account at its right edge; below 1024px the account takes a
third row, its address wraps instead of truncating, and the header stops being sticky (WCAG 1.4.10, at 400% zoom).
Reading order follows the DOM order (brand, nav, account) at every width, so focus never jumps back up the header
(WCAG 2.4.3). At 768px and below the nav wraps with 44px targets.

### D8 — The deviation register: the handoff against what ships

The handoff is a design reference, not production code. What ships departs from it as follows, each with its ground.

| Handoff | Ships | Ground |
|---|---|---|
| Names, initials avatars, an Ort line | The address alone and a neutral account icon | D3; ADR 0142 D7 |
| "Senast inloggad" and "Senast aktiv" values | Shown as unknown (an en-dash) | No login or activity telemetry exists; D2 |
| Counts in filter and tab labels | Omitted while unknown | D2; ADR 0120 |
| Instruction lines such as "Klicka på en rad…" | Cut | DESIGN.md §8; ADR 0144 |
| Deletion copy promising anonymisation in 30 days under Art. 17, and an undo until then | States the backend's earliest deletion date; no statutory claim and no undo claim | #1977 and the epic: the copy says only what the backend does; restore is #1983 (D4) |
| Mail "via SMTP (Strato)", named hosts and services marked healthy, a Seq retention period, a "Verifierad" backup, a delivery percentage | None of them | Truth: mail leaves through Scaleway Transactional Email over HTTPS, never SMTP (ADR 0131), and nothing observes the rest (D2) |
| Information-bearing digits and times in mono | Sans with `tabular-nums`; mono only for code identifiers | DESIGN.md §4 |
| Control heights of its own (32px period buttons, 34px icon buttons, 24–26px pills) | The two ratified systems: `.jp-icon-btn`, `.jp-pill` | DESIGN.md §6; ADR 0052 |
| Hex and rgba literals | Tokens only | DESIGN.md §3; the handoff's own rule of no new hex values |
| Loggar as a `role="tablist"`; the feedback list as `aria-pressed` buttons | Three `.jp-subnav` links, one URL each; the feedback list marks the open item with `aria-current` | `.jp-subnav` is the house view-switcher: links with `aria-current` |
| A 3.2 s toast | The house toast: 8 s, paused while hovered or focused | `ApplicationToastHost`; WCAG 2.2.1 |
| TanStack Query and a 60 s status poll | Neither | AGENTS.md §4; no observation exists to refresh, and "checked every 60 seconds" would be a claim without one (D2) |
| A clickable `<tr>` | A button inside the row | A native button carries the keyboard and screen-reader semantics a `<tr>` lacks |
| A role select in the edit form | The role read-only | Role change is #1983 (D4) |
| The edit form's receipt, "Ändringarna sparades och loggades." | "En kod har skickats till {email}. Adressen byts när kontoägaren har använt koden, tidigast {from}." *(rewritten 2026-10-05, ADR 0153 D5: it read "En bekräftelse har skickats till …")* | D2: when the request goes out nothing has changed yet (#1975) |
| The edit form's primary "Spara", a direct save of the address, with no step-up, no pending state and no cancel (README lines 143-145) | "Fortsätt", which opens the administrator's own step-up code, then the pending row with its earliest instant and "Avbryt adressbytet" | ADR 0142 D5: a change of the recovery address is proved by inboxes, never saved directly; #1975's form round (ADR 0153) |
| An Åtgärder column of two icon buttons per row, "Agera som användaren" and "Redigera" | No column: an account's actions are in its panel, named in words | A row holds one control, the button that opens the panel |
| Filter and period buttons with `aria-pressed` | The house `Segment`, a radio group | One choice among several; `components/ui/segment.tsx` is the house control for it |
| The impersonation banner's "Allt du gör loggas dubbelt i granskningsloggen." | Cut | Impersonation is #1984 (D4); D2 |
| A screenshot thumbnail in the feedback detail | None | No feedback store holds a screenshot (#1979, D4); D2 *(2026-10-07, ADR 0156 D9: still none after the backend PR; the screenshot is stored and shown from PR2)* **2026-10-08, ADR 0156 PR2 amendment: nullable actual-image metadata, protected thumbnail/full-size view in the same detail, loading/absent/error states and keyboard access replace None. The feature remains closed until PR3.** |
| The reply line "Skickas från kontakt@… till {e-post}" | "Svaret skickas till {e-post}." | The sending address is #1979's to decide (D4) *(2026-10-07, ADR 0156 D8: #1979 builds no replies, so the line is gone and the area stays an unbuilt action until a later issue decides the sending address)* |
| Live search, filters, period buttons and "Skicka svar" as a solid primary button | Natively disabled while their region is unbuilt, each pointing to its "Kommer snart" line, never with a solid primary fill | D2; DESIGN.md §6 |
| Bakgrundsjobb and Granskning restyled: grey identifier and time cells, a red error category, smaller status pills | Both pages as delivered, apart from the new header and a labelled scroll region around each table | The prompt keeps both working; the house table paints its cells in ink-1 (`.jp-table tbody td`), and the pill is the delivered `JobStateBadge` |
| Statuses Aktiv, Suspenderad, Ej verifierad and Under radering, one per account | Aktiv, Under radering and Ofullständig; "E-post ej bekräftad" as a line under the status | Measured: the address's confirmation can go with every state, and #1974 lists accounts without a profile (ADR 0151) |
| Filters Alla, Aktiva, Suspenderade, Ej verifierade and Under radering | Alla, Aktiva, Under radering and Ofullständiga | No account can be suspended before #1976 (D4); an option that overlaps the others breaks the counts' sum (ADR 0047) |
| Activity counts on every account | Active accounts only: "–" in the ledger, and no count rows in the panel | Measured: the counts are reliable only for an active account (ADR 0151); ADR 0120 |
| Actions on every account | None on an incomplete account, and one line instead | It has no profile to act on; D2 |
| A category on each feedback item (Fel, Förslag, Fråga) | A rating, "4 av 5" or "Inget betyg"; no category | A submission is a rating, a text or both for one of 19 pages and nothing asks for a category (ADR 0156 D1) |
| Statuses Ny, Pågår, Löst and Avfärdad | Ny, Pågår, Åtgärdad and Avstår | ADR 0156 D8 |
| The sender's address in every feedback list row | None in the list; the detail reads it on the server | The list carries no personal data (ADR 0156 D8); the account has no name (D3) |
| The page as the app path the report was sent from | The page's name from a closed set of 19; no URL or query string is collected | ADR 0156 D2 |
| Feedback's status filter and summary period as the house `Segment` | `.jp-subnav` links, one URL each | The page is driven by its URL, so the notice's link opens its item; a filter that is client state (Användare) keeps `Segment` |

## Alternatives considered

- **Amend ADR 0140 to widen its exception to `/admin`.** Rejected: ADR 0140 says its exception reaches no other page
  (and DESIGN.md §6 repeats it), so widening it would rewrite its scope. This ADR gives the admin overview its own
  scoped exception in ADR 0140's form.
- **Move the `.jp-ov-*` classes to `globals.css` so the admin can use them.** Rejected: `globals.css` is a hotspot
  (`docs/runbooks/parallel-sessions.md`: locked tokens, DESIGN.md-gated), it ships on every public page, and it would
  contradict ADR 0140's scope.
- **Import `(app)/app.css` into the admin layout.** Rejected: it is 2,670 lines at `7c21f8117`, and its shell rules
  could restyle the admin header.
- **A runtime-only preview flag.** Rejected: the fixtures would be bundled into the production build, with only the flag
  between them and a user. D5's compile lock, artifact assertion and build-context exclusion exist because of this.
- **The preview behind the admin gate, over a fixture backend.** Rejected: it needs a backend, which is the thing that
  does not exist.
- **An uncommitted local harness.** Rejected by Klas, 2026-10-04 ("Förhandsvisning i repot (Rekommenderas)").

## Consequences

### Positive

- Klas's latitude is spent where the form earns it: one page. Every other admin page keeps the ledger form, and
  `design-reviewer` has one decision to cite against a card grid elsewhere.
- Nothing fabricated reads as real. An unbuilt region says "Kommer snart" or shows an en-dash, an unbuilt action does
  nothing, and the interactions that need rows are visible in a preview no deployed build contains.
- The surface ships page by page: each backend issue flips only its own entries (D4), so no issue waits on another to
  be reviewable.
- No new token, dependency or chart library, and the new admin classes live in their own file under their own prefix.

### Negative and risks

- A second scoped exception to DESIGN.md §1.2 and §6 to police. It is no precedent for `/jobb`, `/ansokningar` or any
  other page.
- Until #1974–#1978 land, most of the surface reads "Kommer snart" and en-dashes: the honest state is also the
  unimpressive one.
- The preview adds a second route tree, a config flag, a post-build assertion and a `.dockerignore` rule to keep in
  step. A build with `ADMIN_PREVIEW_ENABLED` on contains a page that needs no session and is never to be deployed; its
  safety is the locks of D5, not a login, so a lock that quietly stops working is the failure to watch.
- Below 1200px the header spends a row on the brand alone, so it is taller than the one-row header (D7).
- An account can be found by its address only (D3); the account holds no other handle.

## Implementation

D5 is implemented under #1973, after this ADR. The spec changes ship in the same PR as this ADR:

- DESIGN.md §1.2 (the exception) and §6 (a pointer, and one rule line for an unbuilt action);
- the `jobbpilot-design-principles` and `jobbpilot-design-components` skills (pointers);
- `docs/decisions/README.md` (the index row);
- BUILD.md §10.1 (the admin routes);
- `.gitignore` (the untracked handoff folder).

## References

- `docs/design_handoff_admin/README.md` (local, untracked)
- DESIGN.md §1.2, §3, §4, §6, §8, §10 · BUILD.md §3.1, §10.1 · AGENTS.md §4 · CLAUDE.md §9.2
- ADR 0140, 0142, 0144, 0131, 0120, 0076, 0038, 0052, 0028
- `web/jobbliggaren-web/src/lib/env.ts` (the exact-`"true"` flag form) ·
  `web/jobbliggaren-web/.dockerignore` · `web/jobbliggaren-web/package.json` (`guard:css`) ·
  `web/jobbliggaren-web/src/components/applications/application-toast-host.tsx` (the house toast) ·
  `web/jobbliggaren-web/src/app/(app)/app.css` (the scoping doctrine)
- Epic #1972 · issue #1973 and the issues of D4

## Amendment 2026-10-08 — Actual overview observations (#1978)

VariantA now reads three existing populations with independent failure states. Retained Identity accounts include
profileless accounts; the lifecycle partition is `ProfileMissing > PendingDeletion > Suspended > Active`.
Active is lifecycle state. New registrations instead require a retained Identity account and profile, use the
profile registration timestamp, include suspension/pending deletion, and exclude missing profiles/hard erasure.

The overview shows today/yesterday/last 7/last 30 Swedish calendar registrations and a 7/30/90-day chart containing
the current partial day. Injected `IDateTimeProvider` and `ISwedishCalendar` calculate each UTC boundary separately,
including 23/25-hour DST days; windows are `[start,end)`, ending today's window at the observation instant.
No login series is synthesized. Activity/logins, host/services, backups and general mail delivery remain unavailable.

Accounts, the five newest stored audit rows (OccurredAt then Id descending), and current failed-job count are
read server-side in parallel via separate HTTP requests, with a 10-second source deadline. A successful source
has an observation instant; missing/invalid `X-Admin-Sampled-At` is a failure. Audit crosses the RSC/BFF boundary
only as event ID/time/type and aggregate type/ID, without subject correlation, actor lookup, address, IP or agent.
Only count/time crosses for jobs. The overview does not depend on #1958.

Initial page data is server data. The private/no-store `GET /api/admin/oversikt` checks its own cookie and relies
on the backend's current Admin authorization. Refresh is every 60 seconds while visible, one request maximum,
abort on hidden/unmount and immediate refresh on becoming visible. An ordinary update failure retains last-good
data in component memory with an immediate failure marker; each source shows its own time and an old-data
marker after 5 minutes. Any 401/403 clears every privileged region and stops polling, showing the existing
session/permission refusal wording. Attention links failed jobs and pending deletion separately; an unknown source
cannot produce zero or a general all-clear.

The dashboard's existing tokens/layout and the stored audit's existing retention remain unchanged. No migration,
dependency, Hangfire port, mutation or feedback expansion is introduced.