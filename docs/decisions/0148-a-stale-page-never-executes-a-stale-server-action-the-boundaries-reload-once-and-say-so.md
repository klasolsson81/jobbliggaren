# ADR 0148 — A stale page never executes a stale Server Action: the boundaries reload once on an unrecognised action id, and say so after the reload

**Date:** 2026-10-03
**Status:** Accepted
**Deciders:** Klas Olsson (the plan of 2026-10-03, whose done-when accepts the reload outcome) ·
`senior-cto-advisor` (the routing, `docs/reviews/2026-10-03-1948-form-cto.md`, local) ·
`dotnet-architect` (the form, R1–R12, `docs/reviews/2026-10-03-1948-form-dotnet-architect.md`, local) ·
`security-auditor` (form 2 signed; the key's classification, `docs/reviews/2026-10-03-1948-form-security-auditor.md`,
local) · `design-reviewer` (the blank-while-reloading conditions and the line after the reload,
`docs/reviews/2026-10-03-1948-form-design-reviewer.md`, local)
**Related:** [#1948](https://github.com/klasolsson81/jobbliggaren/issues/1948) (this ADR ships in its PR) ·
[#1949](https://github.com/klasolsson81/jobbliggaren/issues/1949) / PR #1953 (the retry prop and the focus
hand-off; the measured "Försök igen" recovery) · [#1951](https://github.com/klasolsson81/jobbliggaren/issues/1951)
(the consent-version skew, outside this decision) · ADR 0147 (the mixed image set on the pull path, #1238,
outside this decision) · vercel/next.js#99165 and PR #99213 (upstream, both open on 2026-10-03)
**Measured against:** `main` at `e9797b6a4` and `bd33a17c6`, 2026-10-03, Next 16.3.6; every line range below
was read in `web/jobbliggaren-web/node_modules/next/dist` of that install.

---

## Context

**The defect.** Klas, signed in on `/oversikt` in a page rendered before the reconcile replaced the web
image (2026-10-03 11:50:04Z → `sha-e6ece9f`), pressed "Logga ut" and got "Sidan kunde inte visas" with
`UnrecognizedActionError: Server Action "00f1b2d8…" was not found on the server` in the console. Four web
images rolled that day; he met it twice.

**Why it happens.** Next salts every Server Action id with the build's encryption key: the id is a type
byte followed by `sha1(encryptionKey + "<file>:<export>")` (`build/webpack-config.js`
`serverReferenceHashSalt: encryptionKey`; Turbopack passes `encryptionKey` into `createProject`), and
`server/app-render/encryption-utils-server.js` returns `process.env.NEXT_SERVER_ACTIONS_ENCRYPTION_KEY`
when set, else a random 32-byte key. The web image is built in CI with `.next/` excluded by
`.dockerignore` and only `NEXT_PUBLIC_SITE_URL` passed (`release-images.yml`), so every image carries a
fresh key and fresh ids. `release-images` runs hourly and the box's reconcile pulls hourly: every merge
to `main` is a rollout, and every open page meets it at its next Server Action.

**What the client sees.** The server answers an unknown id with 404 `text/plain` and
`x-nextjs-action-not-found: 1` (`server/app-render/action-handler.js`, `handleUnrecognizedFetchAction`);
the client constructs `UnrecognizedActionError` before any build- or deployment-id check
(`client/components/router-reducer/reducers/server-action-reducer.js:102-109`). It has no `digest`, and
`next/navigation` exports `unstable_isUnrecognizedActionError` (an `instanceof` guard). The rejection
reaches the nearest error boundary above the `<form action>`: the header's "Logga ut" sits in `AppShell`
inside `(app)/layout.tsx`, so it lands in `global-error.tsx`; the login form lands in `(auth)/error.tsx`.

**Measured locally before the form round (2026-10-03 12:53–13:05Z, three production builds).** Builds A
and C with one key and B with another: `logoutAction`'s id is `00a89fc0…` in A and C and `00c65eb7…` in B.
A page from A against B: `POST /oversikt` → 404 + the header, the `global-error.tsx` surface, no reload.
A page from A against C: 200, the logout EXECUTED on the new server, and the response's build id made
the router discard the flight data and navigate hard to `/logga-in`.

**Next's own position.** *"actions can only be invoked for a specific build"*
(`docs/01-app/02-guides/data-security.md`, Closures and encryption); the env key's documented purpose is
consistency across several instances of ONE build (`self-hosting.md`, Multi-Server Deployments).
`deploymentId` is not a remedy in 16.3.6: the client throws before any check, the one build-id
comparison in `server-action-reducer.js:176-187` runs after a SUCCESSFUL response and only discards flight
data, and Server Action responses omit the `x-nextjs-deployment-id` header (vercel/next.js#99165, open;
PR #99213, open, server-side only). Next does not route on `?dpl=`.

**The key is public anyway.** `flight-client-entry-plugin.js` writes `encryptionKey` into
`server-reference-manifest.json`, the standalone output copies `.next/server`, and
`ghcr.io/klasolsson81/jobbliggaren-web` answers an anonymous pull (tags list and manifest 200 on
2026-10-03). Whatever key a build uses is readable by anyone who pulls the image; a fixed key would be a
published constant, not a secret (security-auditor (a)).

**After PR #1953 (#1949).** The retry button works again, and on the stale-action surface "Försök igen"
already reloads the page into the new build (the refresh's response carries the new build id; `load` 2,
measured 2026-10-03 14:05Z at 1280 and 3440). The stale action still never ran, and nothing told the user.

## Decision

**D1 — Form 2.** Every Server Action that reaches a boundary or is caught reloads the document once: a
stale page never executes a stale Server Action. One core (`src/lib/stale-build/stale-build-reload.ts`),
one hook (`src/lib/hooks/use-reload-on-stale-build.ts`), one seam (`reload-document.ts`).

**D2 — The predicate is the router's own class.** `unstable_isUnrecognizedActionError(error)` from
`next/navigation`, an `instanceof`; never a message, a name or a digest. A look-alike by name is refused.
If the export disappears, `tsc` fails the import — the failure is loud, where form 1's would be silent.

**D3 — The guard is fail-closed on read AND write.** A `sessionStorage` stamp under a constant key
(`jp-stale-build-reload-at`), a 60 s window: within it a second stale error shows the error surface,
whose "Försök igen" reloads into the new build by itself (#1949). Unreadable storage → no reload; a stamp
that cannot be written → no reload (the seam is never called). A loop breaker that cannot keep its state
does not act. A corrupt stamp counts as absent, because the guard can still write. No Server Action runs
at mount in the codebase today (measured, 0), and fail-closed keeps that an invariant rather than an
assumption.

**D4 — Two stamps, both constants, both values a timestamp and nothing else.** The reload stamp (D3) and a
notice stamp (`jp-stale-build-reloaded-at`) the line removes once shown. One key cannot serve both: either
the loop guard weakens or the line repeats on every load inside the window. Both are written by one
function, `stampAndReload`, shared by the hook and the core. No url, no action id, no message, no form
content is stored (security-auditor m-3).

**D5 — Decided once per mount, in render.** The hook's lazy initialiser reads the predicate and the stamp
and nothing else, so the first commit already renders nothing when a reload is coming and the error
surface never flashes before it; the effect's own stamp cannot flip a later render back to "refuse". The
effect writes the stamps and calls the seam; a failed write flips the state to the surface (scheduled,
the house form for a state change an effect must make). The predicate runs first, so a server-thrown
error — never an `UnrecognizedActionError` — touches no storage, and the decision is SSR-safe.

**D6 — Nothing renders while the document is being replaced.** The six segment boundaries return
`null`; `global-error.tsx` keeps `<html lang="sv">` with the font variables, the body's surface classes
and `<title>` = the site's own name (`titleDefault`, never the error title) with an empty body. No live
region, no focus move, no line in that window: the browser's own loading indicator is the status, and a
line that lives a second reads as flicker (design-reviewer Major 2). The blank is never the end state —
when the guard refuses, the surface renders.

**D7 — The line after the reload.** `ReloadedAfterUpdateNotice` reads the notice stamp after mount,
removes it, and shows `common.reloadedAfterUpdate` — *"Jobbliggaren har uppdaterats och sidan laddades
om. Gör om det du senast gjorde."* — as a `.jp-banner` inside a `role="status"` container that is in the
DOM from the first paint, at the top of the content in every group layout that owns an `error.tsx`
(`(admin)`, `(app)`, `(auth)`, `(guest)/gast`, `(marketing)`, `(marketing-inner)`; the root layout owns
none). The next client navigation clears it, because the line is about THIS page's reload (ADR 0047); a
manual reload shows nothing. The key lives in `common`, not `fallback`: the boundary is gone by then,
and `fallback` changes for another reason. The retry path (#1949) stamps no notice key, so no line follows
a retry: the surface already said something failed.

**D8 — A caught call site calls the core.** `match-preferences-card.tsx` catches its action's rejection
(a module-level `startTransition`, which React reports through `reportGlobalError`, never to a
boundary): its catch calls `reloadIfStaleBuild(e)` first and shows no inline error on a stale id. The
applications view toggle (`applications-pipeline.tsx`, `void setApplicationsViewAction(next)`) is
fire-and-forget and does NOT reload: a reload would undo a visible, successful toggle for a background
failure; the view cookie simply is not persisted for that rollout (a named residual).

**D9 — The boundaries read `ErrorInfo` from `next/error`** (#1949), so the prop names are Next's and a
rename fails `tsc`.

**D10 — Wording.** "A stale page never executes a stale Server Action." Route handlers execute against
new code under every form, and the two consent actions (`updateNotificationConsentAction`,
`updateFollowedCompanyNotificationConsentAction`) are Server Actions and therefore covered.

**Pins.** The core and the hook (H1–H8, H-seq, H5b, the exact-writes row); the B-table over all seven
boundaries cross-checked against a filesystem walk, plus C2 through Next's own `ErrorBoundary`; S1/S2 on
the card; N1–N3 and the navigation row on the line; two fitness rules in `route-boundaries.test.ts` —
every `error.tsx` and `global-error.tsx` calls `useReloadOnStaleBuild(error)`, every layout with a
sibling `error.tsx` renders the line — computed from the filesystem with the scanner's own controls.

## Rejected

- **Form 1 alone (a fixed `NEXT_SERVER_ACTIONS_ENCRYPTION_KEY` at image-build time).** It fails its own
  first measurement: the first keyed rollout gives today's 404 to every open page. It covers only an
  action whose `file:export` is unchanged; a renamed or moved action, or a changed client chunk, keeps
  today's surface. It rests on an undocumented detail (the id derivation), where form 2 rests on an
  export. And it makes cross-build execution the normal case: identity is `file:export`, not semantics,
  so a stale page's arguments run against new server code, a renamed action whose old name is reused
  points elsewhere, and a consent given on stale text is stamped after the deploy (security-auditor (b)).
- **Form 3 (form 1 + form 2) now.** design-reviewer's preference, and the gain is real — an unchanged
  action keeps the user's input (step 1, page A against C: the logout executed and the redirect became a
  full load). The gain is unmeasured with users; the cost is a "secret" that is a published constant, an
  environment Klas must own, three CI guards, and the cross-build semantics above. Form 3 is additive:
  taken up later under the entry conditions below, nothing here is redone.
- **`deploymentId`.** Not a remedy in 16.3.6 (Context). Revisit when it is (below).
- **Intercepting `fetch` for the action POST.** A monkey-patch of a Next internal for what the error
  boundary already receives.
- **A reload on the applications view toggle.** D8.
- **The line's key in `fallback`.** D7.
- **Fail-open on a storage error** (the brief's first shape). D3.

## Entry conditions if form 1 or 3 is taken up later (written out, not pointed at)

From `security-auditor` (Major 1, m-1, m-2, m-5) and `dotnet-architect` (A1–A6), both local reports:

1. **One home classifies the key** as *published in every public image, no confidentiality control,
   never reused for anything else*; states what it binds — **the id salt only**; Next's documented
   encryption of closures does NOT protect anything here, so the rule is no inline `"use server"`
   closures and no `"use cache"` that captures values; names the rotation conditions — the GHCR image or
   the repository becomes private (rotate the same day); the key appears outside the secret store and
   the image; a Next release changes the derivation or adds a consumer (review before the bump); a
   proposal that rests on closure encryption (reject it, do not rotate); names the reader (Klas); and
   lists form 1's residuals — stale execution including the two consent actions, renamed actions,
   positional closure names.
2. **Storage:** a new GitHub environment with exactly one branch rule, `main` (the measured
   `Production`/`Preview` environments carry none); the key reaches the Dockerfile only through
   `--mount=type=secret` on `RUN pnpm build`, never `ARG`/`ENV`; never the box's runtime env (the
   manifest already carries it, and a runtime variable wins over the manifest and adds a copy —
   `encryption-utils.js:78`). Read the branch policy back through the API; a guard that the Dockerfile
   names the key only in a secret mount; `git grep NEXT_SERVER_ACTIONS_ENCRYPTION_KEY -- deploy/` = 0.
3. **A closure guard** in the `scripts` job failing on a `"use server"`/`"use cache"` that is not a
   module's first statement (today 0), with a red fixture and a green tree.
4. **The seam navigates with GET** to `location.pathname + location.search` instead of `reload()`, or
   the residual is written here: with a stable id, `reload()` on a document loaded by a POST re-sends it.
5. **`required=false` on the mount makes a misspelled id or an unset secret a silent random key with
   exit 0.** The real guard compares the built image's manifest key hash with the secret's hash in
   `release-images.yml` and prints only equal/unequal. Secret mounts are not part of BuildKit's cache
   key — write it in the comment and the guard before any `cache-from` is added. The SHA idempotence
   means a rotation lands only with a new SHA — the runbook needs a forced-rebuild path. `env=` on the
   mount needs a frontend that supports it (unmeasured); `/run/secrets/<id>` with an existence test is
   the alternative. The PR `images` job and the `frontend` job keep building without the key, which is
   correct (the PR image already differs from the release image, as with `NEXT_PUBLIC_SITE_URL`).
6. **Form 2 stays in place:** the first keyed rollout, and every rotation, is itself one more skew.

## Consequences and residuals

- **One redone action per open page per rollout**, and the line says so. Every merge to `main` is a
  rollout (four web images on 2026-10-03). The user's unsaved input in a form is lost with the reload;
  form 3 is the remedy if that is measured to matter.
- **The applications view toggle is not persisted** across a stale rollout (D8).
- **Chunk skew** — a page that needs a client chunk the new image no longer serves — stays with Next's
  navigation failure handling; this decision does not touch it.
- **The per-build key is a published salt**, not a secret; nothing here changes that.
- **global-error's reload branch is pinned in jsdom only for what it renders**, not for the reload
  itself (M13: the branch returning `null` is caught by the local reading and the box, never by vitest).
- **A C1/B-table row with the right label but the wrong import** passes both the count row and `tsc`.
- **Form 1/3's entry conditions above are this ADR's; the reports they come from are local.**

## Revisit triggers (reader: Klas)

- **Next ships skew handling for Server Actions in an installed version** (vercel/next.js#99165 closed
  and the fix in `package.json`'s pin): re-measure with the two-build harness
  (`docs/sessions/2026-10-03-1948-server-action-skew.md`, step 1) and consider deleting the hook and the
  line.
- **A test user loses input, or meets the line routinely:** try form 3 under the entry conditions.
- **The GHCR image becomes private:** re-read security-auditor's (a).2 — a fixed key would then be a
  secret with a burnt history; a per-build key leaks one build at most.
