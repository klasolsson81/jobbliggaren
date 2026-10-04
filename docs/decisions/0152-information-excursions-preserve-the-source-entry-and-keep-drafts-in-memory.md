# ADR 0152 — Information excursions preserve the source entry and keep drafts in memory

**Date:** 2026-10-04
**Status:** Accepted
**Decider:** Klas Olsson, approval of the #1986 implementation plan
**Scope:** Frontend information navigation
**Related:** [ADR 0053](./0053-detail-modal-intercepting-parallel-route.md), BUILD.md §10.2–10.6, [#1986](https://github.com/klasolsson81/jobbliggaren/issues/1986)

---

## Context

Reading privacy, help or contact information can interrupt an email draft, an
application list or an intercepted detail modal. A pathname alone cannot describe
the list behind a modal or the component state that navigation unmounts. A public
information page must also work when opened directly, reloaded or opened in a
separate tab, where no trustworthy task context exists.

Klas approved one history entry per information excursion and a home fallback
after hard reload or loss of context. The complete public pages remain canonical;
legal copy, consent rules and authentication decisions are outside this change.
The existing consent-step links already announce that they open in another tab.
The CV consent dialog holds a temporary file and pending parse in its source tab.

## Decision

Use a document-lifetime `InformationReturnProvider` with typed, component-owned
snapshots and controlled information links. The first information visit pushes
one entry; subsequent information visits replace that entry and retain its
original task. Native Back returns to the source entry and Forward opens the
latest information page.

The contract is bounded as follows:

- Permit contextual `back()` only when a live excursion record, its own harmless
  identifier/step markers and the expected route agree. Preserve the router's
  history state opaquely; do not inspect private Next.js fields or infer an origin
  from `history.length` or `document.referrer`.
- Preserve verified proof across a same-entry public `replaceState` call when
  its same-origin route is unchanged and its supplied object has no explicit
  excursion marker. A delayed view-cookie response can otherwise discard custom
  state during its router refresh. Validate the live record and existing marker
  before the call; never recreate proof after it is lost or replace an explicit
  mismatching marker. Clear the in-memory record when the provider unmounts.
- Build the ordinary return `href` from validated canonical routes and resource
  identifiers. This destination policy is separate from auth's `safe-redirect.ts`.
  External, malformed and self-referential targets are rejected. The existing
  source entry retains its query, legitimate `next` and intercepted route tree;
  these are not copied into a return parameter.
- Include the four established demo tasks and their bounded `gj-`/`ga-` resource
  identifiers in the same explicit destination policy. Name demo returns as demo
  tasks; no arbitrary guest URL or authentication destination is admitted.
- Related information links, footer links and fragment navigation replace the
  excursion entry. A link to its origin returns to that entry. A standalone help
  centre becomes the origin when a guide opens; an existing task origin takes
  precedence. Fragment links remain ordinary anchors without JavaScript and move
  focus to their destination when enhanced.
- A link to the information page already being read updates its fragment or
  moves to the page heading in the same entry. It does not start a pending route
  transition that requires a remount to complete.
- Keep the email draft and existing email-step error, list controls, selection,
  expanded ad text, focus and scroll only in memory. Restore component UI before
  focus and scroll; existing rules for changed rows and invalid selection still
  apply. If the trigger is absent, focus the restored surface's heading.
- Never serialize snapshots into URLs, history state or web storage. Do not
  retain server DTOs, contact details, CV data, authentication codes or consent
  decisions. The provider is a narrow UI continuation mechanism, not a server
  state store or an authentication cache. Keep `cacheComponents: false` and the
  existing unmount boundary for re-authentication state.
- Retain the record for Back/Forward within the excursion; discard it on a new
  task, submitted/completed flow, logout or document change. After reload, new-tab
  navigation or lost context, old markers alone confer no return authority:
  show “Till startsidan” linking to `/`.
- Preserve the consent step's announced `noopener noreferrer` new-tab links.
  Apply the same narrow, disclosed exception in the CV dialog so its temporary
  file and pending consent remain in the source tab. The information tab directs
  the user to continue in the upload tab and offers home; it does not close itself
  or reconstruct that tab's state.

All thirteen `(marketing-inner)` pages use a server-rendered
`InformationPageFrame` with one `main`, the existing skip target and hero, the
reading column, and identically named return links above and after the content.
`/integritet`, `/villkor`, `/cookies`, `/matchning`, `/cv-granskning` and
`/tillganglighet` also receive contents navigation and a page-top link. Section
identifiers are explicit and language-independent; existing usable fragments,
metadata, FAQ structured data and full content are preserved. New navigation copy
exists in both product locales. The root return provider needs no translations.

## Alternatives considered

### A — One excursion entry with memory-only continuation (chosen)

**For:** Returns to the existing source history entry, including its modal/list
context; keeps private draft data out of durable navigation surfaces.
**Against:** Requires explicit component adapters and verification of history,
mount, focus and scroll ordering. It intentionally cannot recover after reload.

### B — Ordinary history for every information visit, with reconstructed return state

**For:** Preserves every information visit and permits a return URL to survive
reload when persisted.
**Against:** Back can enter a policy chain instead of the task. A reconstructed
pathname loses intercepted presentation; storing drafts or arbitrary source URLs
extends sensitive-data retention. A general history journal is outside scope.

### C — Retain entire route trees or enable Cache Components

**For:** Avoids writing individual UI adapters for retained components.
**Against:** Broadens retained state beyond this task and weakens the deliberate
unmount boundary for re-authentication. The approved change keeps that boundary.

### D — General policy dialogs or new tabs for information links

**For:** Leaves the original component tree mounted while information is read.
**Against:** Changes the public reading and navigation model across the product.
The approved scope keeps complete canonical pages and limits new tabs to the
announced consent/CV cases.

## Consequences

### Positive

- Return labels identify the task instead of relying on a browser-control guess.
- Policy chains do not build return loops or copy authentication destinations.
- The shared frame makes reading navigation consistent without a shell redesign.
- Draft continuation adds no durable storage or backend processing.

### Negative and accepted

- Forward exposes only the latest information page in the excursion.
- Reload and independent tabs intentionally lose contextual return and drafts.
- Restoring local controls needs route and browser tests; ordinary canonical
  fallback navigation cannot promise the same state as a verified source entry.

## Implementation status

Implemented in #1986: the thirteen-page frame, six contents navigations,
validated memory-only return record and scoped source-state adapters. The
[delivery evidence](../reviews/2026-10-04-1986-evidence.md) records browser and
automated checks; the PR binds final review verdicts to its immutable head. This
decision does not attest merge or manual screen-reader verification. General
modal closing/focus work in #1968 and the shell-container change in #1852 remain
separate scopes. No existing ADR is superseded.

## References

- [Next.js `useRouter`](https://nextjs.org/docs/app/api-reference/functions/use-router): push, replace and back navigation contracts.
- [Next.js Intercepting Routes](https://nextjs.org/docs/app/api-reference/file-conventions/intercepting-routes): soft navigation, direct navigation and modal history.
- [GOV.UK back link](https://design-system.service.gov.uk/components/back-link/): returning to a previous task and restoring its state.
- [ADR 0053 — modal and canonical detail routes](./0053-detail-modal-intercepting-parallel-route.md).
- BUILD.md §10.2–10.6: server data ownership, local UI state, accessibility and product locales.
