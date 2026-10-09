# ADR 0156 — User feedback: a rating or a text per page, plain storage, one notice per submission, and 90 days

**Date:** 2026-10-07
**Status:** Accepted
**Deciders:** Klas Olsson (product owner; his planning answers of 2026-10-07, quoted where they decide) ·
`senior-cto-advisor`, "the CTO" below (decision-maker under CLAUDE.md §9.2; plan review of 2026-10-07, decisions
2a–2f and must-change M1–M7, whose labels this ADR uses and explains where it uses them; the report,
`docs/reviews/2026-10-07-1979-plan-cto.md`, is local)
**Amends:** ADR 0124 (D6: `EmailDeliveryException` gains one closed member) and ADR 0125 Case 2 (grant 1: the
accepted restore-exposure list grows by three tables), each by a dated pointer line; ADR 0150 (D8) by rows.
Nothing is rewritten.
**Related:** ADR 0150 (the admin surface; #1979 owns feedback, D4) · ADR 0124 · ADR 0125 · ADR 0131 (the mail
provider) · ADR 0011 · ADR 0049 · ADR 0106 (recruiter erasure) · ADR 0139 (the form of "Klas beviljanden") ·
ADR 0154 (one box) · [#1972](https://github.com/klasolsson81/jobbliggaren/issues/1972) (the epic) ·
[#1979](https://github.com/klasolsson81/jobbliggaren/issues/1979) (the issue this ADR ships under)
**Measured against:** the head of `feat/feedback-backend-1979`, `c2c2c6cea`, four commits above `origin/main` at
`9e5c37129`, 2026-10-07. D1–D8 describe what that branch builds. **D9 and everything marked PR2 or PR3 is decided
and not built.**

> **Provenance.** Written by `adr-keeper` for the driving session, from its brief (CLAUDE.md §9.2, §13). The plain
> storage (D3), the unknown-outcome rule (D5), the 90 days (D7) and the delivery shape (D9) are Klas's. The rest are
> mechanics under his answers and the CTO's rulings, which the mandatory agents review in the PR.

---

## Context

[#1979](https://github.com/klasolsson81/jobbliggaren/issues/1979) asks for a way for the first real users of
jobbliggaren.se to say what they think of a page: a rating from one to five per page, an optional text and, later, a
screenshot; triage at `/admin/feedback`; a rating summary per page; and a mail to Klas for each submission. The issue
sat after the MVP until the CTO's direction of 2026-10-07 lifted it in. Klas set the shape in his planning answers
and the bar: "gör det inte bara för krångligt.. Det handlar om feedback, inga hemligheter!!!"

At `9e5c37129` nothing stored feedback or told the operator about it. ADR 0150 had reserved the surface for #1979
(D4) and recorded two of the handoff's elements as unbuilt, the screenshot thumbnail and the reply line (D8). And the
mail port could not tell a send the provider refused from one it may have taken: `EmailDeliveryException` carries the
email kind and the underlying exception's type name, with an empty `InnerException`, so that a recipient address in a
provider message cannot escape (ADR 0124's containment, kept by ADR 0131), and no caller had needed more.

## Decision

Nine decisions. Each names the code that carries it.

### D1 — The model: a submission, its notice and a suppression, as three aggregates with no foreign keys

- **`FeedbackSubmission`** (`feedback_submissions`) is a rating 1–5, a text of at most 2 000 characters, or both, for
  one page, owned by a job seeker. The invariant, a rating or a text, lives in `FeedbackSubmission.Submit`. The row
  also holds the client's idempotency key (a Guid, unique per owner), what the browser reported (D2), an app version,
  a status (D8) and its timestamps.
- **`FeedbackPage`** is a SmartEnum of 19 fixed keys, stored by name (`page_key`) and parsed by
  `FeedbackPage.TryFromKey`, so a page can only come from this list, never from a URL, a filter or an ad or CV id.
  *(2026-10-09, after Klas's live test: a 20th key, `general`.)*
- **`FeedbackNotification`** (`feedback_notifications`) is its own aggregate (2b: a different writer, lifecycle and
  invariants), created in the same save as its submission, one per submission (D5).
- **`FeedbackPromptSuppression`** (`feedback_prompt_suppressions`) is one row per user and page, written in the save
  of the first submission for that page and kept for the account's lifetime, so v1 never asks again, on any device.

The aggregates refer to each other and to the job seeker by strongly typed id only (ADR 0011), and the tables carry
no foreign key. A row leaves by an explicit delete: the retention job or the account hard delete (D7). The tables are
created by migration `20261007215002_AddFeedback`.

### D2 — Submission and idempotency

`POST /api/v1/me/feedback` (`MeFeedbackEndpoints`) takes multipart with one JSON `payload` field (2d). PR2 adds a
`screenshot` part; until then a file part is refused, not ignored. Parsing and the bounded `FormOptions` (64 KiB
body) stay in the endpoint, and `SubmitFeedbackCommand` takes typed values (M4). The `feedback-submit` bucket allows
10 per ten minutes per user. `GET /api/v1/me/feedback/prompt-state` answers whether feedback is open and which pages
the user has answered.

The handler checks the owner, then the key, then the gate (D4), then saves the submission, its notice and, if
missing, the suppression in one save.

- **A known key replays.** 200 and the same id, even if the gate has closed since (2f); a new submission is 201.
- **A unique violation** is either the same key saved concurrently or a concurrent submission for the same page
  claiming the suppression, and the inspector cannot tell the two indexes apart. The handler clears tracking,
  re-reads the key and replays if it is there, and otherwise saves once more, adding the suppression only if it is
  still missing (M1). Two keys for one page in parallel give two submissions and one suppression.
- **Not audited.** An audit row stores the raw user agent (`AuditBehavior`), which this feature must not collect. An
  administrator's triage is audited (D8).
- **Diagnostics are closed.** The browser reports viewport and screen size in CSS pixels, the pixel ratio and the
  theme, and the device class, OS family and browser family as enums (`ReportedClientContext`). A value out of
  range, or a name outside its enum, is dropped, never refused, so a strange browser cannot cost the user their
  feedback. No URL, query string or raw user agent is collected. The app version, stamped by the web server in PR3,
  must be a lowercase hex commit hash, so no name can be typed into it; any other shape refuses the submission.

### D3 — Plain storage (Klas's grant)

Feedback is not secret data, so it is not sealed. The comment is a plain `varchar(2000)` column. There is no DEK, no
cross-owner decryption for the administrator and no change to ADR 0049's mechanics. The screenshot (PR2) is plain
too; it is decoded and re-encoded without metadata to protect against a broken or hostile file, not for secrecy.
Klas: "gör det inte bara för krångligt.. Det handlar om feedback, inga hemligheter!!!" Two consequences are recorded
as grants under *Klas beviljanden*: the restore-exposure list grows by three tables, and the comment becomes a
searched surface of the recruiter erasure.

### D4 — The gate: one class, soft, and closed until PR3

`FeedbackGate` (`Application/Feedback/FeedbackGate.cs`, registered for both hosts by `AddFeedback`) opens feedback
only when `Feedback:Enabled` is true, `Feedback:NotificationRecipient` is a usable inbox address and
`IEmailSender.CanDeliver` is true. Submit and prompt state read all three: a closed submit answers 404
(`Feedback.Closed`) and the prompt state says `open: false`. The dispatch job reads the last two but not the switch,
so a notice queued before the switch was turned off still leaves. `GET /api/v1/admin/feedback/availability` tells
the administrator which of Open, Disabled, NoRecipient and CannotDeliver holds.

The gate is deliberately soft, with no `ValidateOnStart`: a missing recipient keeps the feature closed and visible on
the admin page instead of stopping the only production host (ADR 0154). The recipient is server configuration, the
controller's own address, and never part of a request or of the repository (`appsettings.Local.json.example` carries
a `.test` placeholder). `Feedback:Enabled` is off by default until PR3.
*(2026-10-09, PR3 amendment: the switch.)*

### D5 — One notice per submission, and the rule for an outcome nobody knows

The notice carries the page, the rating ("4 av 5" or "Inget betyg"), the time and a link,
`{Email:BaseUrl}/admin/feedback?id=…`, into the signed-in admin; never the text, a screenshot or anything about the
reporter, so nothing a user wrote leaves for the mail provider
(`IEmailSender.SendFeedbackReceivedNotificationAsync`, `EmailTemplates.FeedbackReceivedNotification`).

`FeedbackNotificationDispatchJob` runs every minute (`dispatch-feedback-notifications`), registered unconditionally
and idle while no recipient can be reached. It has no Hangfire retry (`AutomaticRetry(Attempts = 0)`) and a lock wait of 30 seconds, under the
interval (M7). A run sends at most ten notices. A due notice is claimed and saved as
`Sending` **before** the provider call, so a run that dies afterwards leaves a row the next run turns `Unknown`, and
the notice is not sent a second time.

| The provider call ends | The notice becomes |
|---|---|
| accepted | `Accepted`: the provider took it. No state claims delivery. |
| a refusal the provider proves (`NotAccepted`, D6) | `Queued` again after 1, 5, 15 and 60 minutes; the fifth refusal makes it `Failed` |
| anything else (`Unknown`) | `Unknown`, and it is never resent automatically (Klas: "Synlig åtgärd, ingen auto (Rek.)") |

- A claim still `Sending` after ten minutes belongs to a run that died near the provider call, so whether the mail
  left cannot be known: it becomes `Unknown`, never `Queued`.
- The run stops at the first send that is not accepted, so an outage costs one notice a run rather than a batch (M7).
- `xmin` is the concurrency token, because a claim and an administrator's requeue can meet on one row.
- An administrator requeues a `Failed` notice freely, since nothing was ever sent, and an `Unknown` one only after
  acknowledging that it may arrive twice. `FeedbackNotification.Requeue` checks the acknowledgement, not the
  endpoint. A requeue starts a new round of five attempts and is audited.
- The user's receipt will say that the feedback was saved, never that it was delivered (PR3).
- *(2026-10-09, PR3 amendment: the 24-hour budget, `FeedbackNotificationDispatchJob.DailyBudget`.)*

### D6 — Amends ADR 0124: `EmailDeliveryException` gains `Disposition`

ADR 0124 contained the provider's exception in `EmailDeliveryException`, which carries the email kind and the
underlying exception's type name, with an empty `InnerException`, and, in the class's own words, "deliberately
nothing else". This ADR adds one member, `Disposition` (`EmailDeliveryDisposition`: `NotAccepted` or `Unknown`,
default `Unknown`). It is a closed enum and carries no personal data (M6). `ScalewayEmailSender.DispositionOf` sets
it: any 4xx is `NotAccepted`, and so is a failure with no response that happened before the request could leave
(name resolution, connection, TLS, proxy tunnel), because the port describes delivery outcomes, not HTTP (2a). A
timeout, a 5xx and a response that ended early may follow an accepted message, and stay `Unknown`. Only the dispatch
job reads the field. Every other caller ignores it and the arm still registers no resilience handler, so no other
mail flow changes its retry behaviour.

### D7 — Retention: 90 days from submission, suppressions for the life of the account

Klas: "90 dagar från inskick (Rek.)". `FeedbackRetentionJob` deletes a submission and its notice 90 days after
`SubmittedAt`, daily at 04:50 UTC (`feedback-retention`), with `ExecuteDelete` and a log of counts only (the pattern
of `ParsedResumeRetentionJob`; no system-event audit). The period is the constant `FeedbackRetentionJob.Retention`,
not an option, because the privacy policy will state it (PR3). Suppressions are kept: they live as long as the
account and hold no text, so the prompt never returns on its own.

An account hard delete removes all three tables in the transaction of the rest of its cascade
(`AccountHardDeleter`), the notices first and queued ones included, so no send outlives the account.
`AccountHardDeleteCascadeFitnessTests` holds the build to it. A soft-deleted account is
not special-cased (2c): its rows go at hard delete, as other per-user aggregates do, and the admin detail reads the
owner with `IgnoreQueryFilters`, so an account inside its 30-day restore window still shows.

### D8 — The admin surface and the statistics

Everything is under `/api/v1/admin/feedback` (`AdminFeedbackEndpoints`), on the admin policy and the `admin-read`
and `admin-write` buckets.

- **List.** Newest first, 25 to a page, filtered by status and page, with the count per status inside the page
  filter. Each item carries the rating, an excerpt cut on the server (90 characters, 2e), the status and the
  notice's state, and no address.
- **Detail.** The full text, what the browser reported (shown as reported, not verified), the notice's state,
  attempts and next attempt, and the reporter's address, read on the server for that one item with
  `IUserAccountService.GetEmailAsync`. The account has no name (ADR 0150 D3); the address is the identity.
- **Triage.** The statuses are Ny, Pågår, Åtgärdad and Avstår (`FeedbackStatus`). Any may follow any other, so a
  closed item can be reopened. A status change and a requeue (D5) are `IAdminRequest` and audited
  (`Admin.FeedbackStatusChanged`, `Admin.FeedbackNotificationRequeued`).
- **Statistics.** `GET .../summary?days=7|30|90` gives, per page, the submissions, the raters, the 1–5 distribution
  and the mean. A user's latest rating per page in the window counts once, so a re-rating replaces the earlier one;
  an unrated submission counts only as a submission, never as a zero. The read needs `DISTINCT ON`, a provider
  feature, so it sits behind the Application port `IFeedbackRatingSummaryReader`, implemented by
  `SqlFeedbackRatingSummaryReader` in Infrastructure (M5; AGENTS.md §2.1).
- **Replies to the reporter** are not built. They are a later issue, and the reply area stays an honest unbuilt
  action (ADR 0150 D2).

ADR 0150's D8 register gains the rows this makes true, as a dated amendment with no row rewritten.

### D9 — Delivery: three PRs, closed until the last

Klas: "Tre PR, mörk till sist (Rek.)", one session per PR, and "Rent CC-flöde": the mandatory panel of CLAUDE.md §9.2
and the CTO's merge approval for the same revision, with no `codex-review` label and no native Codex review. The PR1
migration needs Klas's GO before `agents-done`. "Bygg nu, hotspots efter (Rek.)": the migration, DI and message-file
hotspots were taken once #2051 (#1976) had merged.

1. **PR1, `feat/feedback-backend-1979` (built):** D1–D8 and the admin page, with `Feedback:Enabled` off.
2. **PR2, the screenshot (decided, not built).** Klas: "ImageSharp 4.1.2 (Rek.)", so `SixLabors.ImageSharp` 4.1.2, a
   library outside BUILD.md §3.1 that gets its row and its licence ground there. One optional PNG, JPEG or WebP part
   of at most 5 MiB: magic bytes, a size and frame check, a decode with only those three codecs, `AutoOrient`, a
   re-encode without EXIF, ICC, XMP or IPTC, and a bound on concurrent decodes. It is stored plain in
   `feedback_screenshots` (D3) and read by an administrator through a proxy with a fixed content type, `no-store`
   and `nosniff`.
3. **PR3, the user surface and the switch (decided, not built).** The rating and text form on each mapped page, from
   one central route map with a guard test, the footer link, the receipt that says saved, the privacy copy and the
   register of processing, and the stamped app version. Then `Feedback:Enabled` defaults to true, the recipient is
   entered in the box's `.env` with Klas's GO, and Klas makes the first controlled test, signed in on the box; agents
   never sign in there (CLAUDE.md §9.2).

## PR2 amendment — 2026-10-08: optional screenshot, still closed

Klas approved this implementation plan on 2026-10-08, after the local senior-cto-advisor and CTO-chat plan reviews. This amendment replaces D9's PR2 delivery mechanics; PR3 remains unbuilt and `Feedback:Enabled` remains false. PR2 uses `automerge` + `codex-review`, the local §9.2 panel, native Code Review and Security Review, the final-head gate, CTO merge permission for that same commit and Klas's migration GO before `agents-done`.

- The Application-owned `IFeedbackScreenshotNormalizer` is implemented only in Infrastructure by ImageSharp 4.1.2. BUILD §3.1 owns the verified Apache-2.0 licence ground and the required community build credential. Provision the build credential only outside the local checkout or in the protected GitHub environments documented in local-dev-setup. PR builds require manual Klas review of the exact head/run; trusted main builds use the main-only environment. Keep no repository or Dependabot secret copy and no runtime credential.
- Input is at most 5 MiB, 16 million pixels and one frame. Magic bytes and a configuration containing only PNG, JPEG and WebP codecs restrict formats. Strict identify checks up to two frames; strict decode reads one. `AutoOrient` precedes removal of EXIF/ICC/XMP/IPTC. The encoder writes lossless 8-bit RGBA-PNG, without resizing or quantization. A bounded stream refuses immediately beyond 5 MiB, including a compressed input whose decoded pixels make a larger PNG.
- PNG metadata is checked before ImageSharp reads it: chunk boundaries and metadata CRCs, bounded zlib expansion, and legacy EXIF/IPTC lengths against actual hexadecimal data. Expanded metadata and decoded legacy profiles share a 5 MiB budget; cancellation is propagated. Metadata-heavy PNGs can therefore be refused despite valid pixels. This closes the 4.1.2 legacy-profile allocation path while preserving orientation (local CTO repair decision, 2026-10-08).
- One process-wide decode runs with no queue (busy is Conflict/409) and one internal thread. Allocator limits are pool 32 MiB, individual buffer 64 MiB, allocation group 128 MiB and total active allocations 256 MiB. The submission route admits two concurrent requests without a queue before reading the form, in addition to the existing per-user rate limit. Total body and in-memory form buffering are capped at 5 MiB + 64 KiB; no temporary upload file is created. Multipart requires exactly one UTF-8 `payload` of at most 64 KiB and at most one `screenshot`; unknown and duplicate parts are refused. Rating or comment remains required.
- The owner's submission key is checked before normalization. Replay returns the existing submission and cannot attach or replace an image. `FeedbackScreenshot` is a separate write-once aggregate: typed id, owner, submission id, PNG bytes, dimensions and the parent's `SubmittedAt`, with a unique submission index and owner index, no filename or client MIME. Submission, screenshot, notice and first suppression share the existing atomic save and unique-conflict recovery. The parent's `FeedbackSubmitted` event covers this submission; no separate image event is needed.
- Admin metadata projects dimensions from the actual screenshot row. List/detail metadata never load bytes. The dedicated protected binary endpoint and same-origin, session-backed BFF return fixed image/png only on success, bound response bytes, and stamp no-store/nosniff on successes and refusals. The same detail shows loading, absence, failure, thumbnail and keyboard-accessible full-size views; abort and blob revocation accompany detail changes/unmount.
- Retention uses the submission's time for the image and removes it before notices/submissions. Account hard deletion and the explicit cascade registry include it. Grant 1 also permits this plaintext personal-data table; it does **not** widen the retained logical-backup exposure: only `public.feedback_screenshots` **data** is excluded with `--exclude-table-data=public.feedback_screenshots`. Its schema and all other feedback remain. After restore, actual metadata reports no image. This exception supersedes any earlier suggestion that the screenshot returns with the other feedback rows.
- `feedback_screenshots.content` is `HeldButNotSearchable` in recruiter erasure. Pixels are plaintext, yet no OCR or corpus-wide image scan runs. Every response discloses the gap and offers manual review within the request deadline, even when comment search found no match. An identified image can be deleted alone; whole-submission erasure deletes image, notice and submission together. The runbook owns the operator procedure.

**PR3 prerequisites:** separately verify that the updated backup script is installed on the VPS under its existing operator procedure. An image release does not install systemd scripts. The privacy copy and processing register must explain that a screenshot is a **separate plaintext copy**, which is not automatically erased when its source CV is deleted. The feature stays closed until those steps and Klas's controlled launch test.
## PR3 amendment — 2026-10-09: the user surface, the disclosure and the switch

Klas approved the PR3 plan on 2026-10-09; the CTO reviewed it the same day (M1–M7). This amendment replaces D9's
PR3 delivery mechanics. PR3 follows D9's pure CC flow: the CLAUDE.md §9.2 panel and the CTO's merge permission for
the same revision, no `codex-review` label.

**Klas's decisions, 2026-10-09:** the device context is read and sent only when the user ticks an unticked box
(LEK 9 kap. 28 §, "Frivillig kryssruta"); the notice recipient is kontakt@jobbliggaren.se, the STRATO mailbox the
privacy policy already names.

- **Where the prompt appears.** `lib/feedback/page-keys.ts` holds the 19 keys and every `(app)` page route, each
  either mapped to a key or exempt with its reason; `page-feedback-coverage.test.ts` holds that list equal to the
  page files and requires exactly one `<PageFeedback pageKey>` in each mapped page's content. Intercepted modals have
  none. The footer's "Lämna feedback om sidan" resolves the key from the route's pattern and opens the same form.
  *(2026-10-09, after Klas's live test: the footer is general feedback.)*
- **The prompt state is read once per full load.** `(app)/layout.tsx` reads `prompt-state` beside the session and a
  client provider holds it with the pages answered during the visit, in memory only. A failed read shows no surface.
  The row never disappears while it holds a draft, a request in flight or a receipt.
- **One route handler carries the submission** (`app/api/feedback/route.ts`). A Server Action cannot: its body is
  capped at 1 MB, and a page's stale action never runs after a deploy (ADR 0148), which would discard a comment and
  an image the user just prepared. The route reads the body within a bound, accepts exactly one `payload` and at
  most one `screenshot`, validates the payload strictly, stamps the app version, and sends the image as a named file
  part. The browser gets a closed set of outcomes; the backend's body never travels.
- **The device context** carries no theme: the product is light-only, and the stored theme choice is browser storage
  the cookie policy says never leaves the device. D2's "reports … the theme" no longer describes the client. The
  consent record is the submission itself: the context exists only when the box was ticked, and the stamped app
  version names the label shown. So the route sends the context on only when the page was rendered by the version
  now answering (`renderedVersion` equal to `APP_VERSION`); otherwise it is left out and the rest still goes.
- **The screenshot is redrawn in the browser** as a PNG within a long edge of 2 560 px, 4 megapixels and 1.5 MiB,
  shrinking by 0.75 down to a 720 px floor, so the upload fits Caddy's 10 seconds and the server's PNG stays under
  5 MiB. The redraw leaves metadata such as a location on the device; an image the browser cannot redraw is refused,
  never sent as it came. A valid image can therefore be refused, by the browser or by the server's 5 MiB limit on the
  normalised PNG.
- **The app version** is the released commit: the release workflow passes it to the web image's runtime stage
  (`APP_VERSION`), and `env.APP_VERSION` passes only a lowercase hex hash of 7–40 characters.
- **The switch replaces D9's "defaults to true".** `FeedbackOptions.Enabled` stays `false` in code. Compose passes
  `Feedback__Enabled: ${FEEDBACK_ENABLED:-false}` to the api alone and the recipient through the `x-app-feedback`
  anchor to the api and the worker, defaulting empty. Klas's GO sets both in the box `.env`
  (`vps-deploy-stack.md` §3g). An explicit compose default would override a code default anyway, and a separate
  switch closes submissions without stranding queued notices: switching off sets `FEEDBACK_ENABLED=false` and keeps
  the recipient.
- **The 24-hour budget.** The dispatch hands at most 20 notices (`FeedbackNotificationDispatchJob.DailyBudget`) to
  the provider in any 24 hours, across every account. A notice over it stays `Queued` and goes when the window
  allows, so a burst of more than 20 submissions in a day delays the later notices by up to a day. The feedback
  itself is saved and listed at once.
- **A signed-out administrator keeps the notice's link.** The proxy forwards a return path for `/admin` paths, and
  for `/admin/feedback` only a GUID-shaped `id`; the admin layout sends a signed-out visit to
  `/logga-in?next=…` through `safeRedirectPath`. Because `next` now carries the id, the edge deletes `next` from its
  log as it deletes `id` (`login-next-edge-log-verdicts.ts`, `CaddyfileTokenScrubbingPinTests`).
- **The disclosure.** The privacy policy states what a submission holds, the consent for the device context and how
  to withdraw it, that text and image are kept outside the CV encryption and that a screenshot is a separate copy a
  CV's deletion does not remove, the notice to the STRATO mailbox, and the retention: 90 days from submission,
  account deletion, deletion on request, and the per-page marker for the life of the account. The processing
  register carries the same entry. The box's label and the withdrawal hint under it are ADR 0144 row 20.
- **Before the switch:** the installed backup script was read back with the screenshot data exclusion on
  2026-10-09 (session record); an image release installs no script.

### After Klas's live test — 2026-10-09

Klas tried the switched-on row and asked for less: *"Att trycka på en stjärna ska räknas som ett betyg direkt"*, the
optional field *"krävs inte för att spara en stjärna... och det är för mycket brus"*, and after the star *"en
bekräftelse ... Skicka gärna mer feedback ... men allt detta ska kunna stängas med ett kryss"*. So a star in the row
submits a rating at once and the row becomes a confirmation with a close button; its "Lämna mer feedback" opens the
footer's dialog with the rating filled in, and the comment, the screenshot and the device-context box live only in
that dialog. The stars fill up to the rating, DESIGN.md §7's scoped exception. The bullet above about a draft in the
row now holds for the dialog; the row holds only a request in flight or its confirmation.

The footer's link is no longer about a page either (*"länken i footern borde inte ha någon sida, bara rent allmän
feedback"*): it reads "Lämna feedback" on every `(app)` route and submits under a 20th key, `general`, which no route
maps to and which therefore never hides a page's row. It is stored by name like the others, so no migration.

## Klas beviljanden (2026-10-07)

Klas's grants, in the form of ADR 0139's section of the same name. The exposure registry's comment points here by
this name.

1. **Plain storage, and with it the restore-exposure list.** Klas, mid-planning: "gör det inte bara för krångligt..
   Det handlar om feedback, inga hemligheter!!!" The plan he approved says what follows: the text, and later the
   screenshot, are ordinary columns, and the tables are entered in
   `MappedPlaintextExposureRegistry.PersonGrainedTables` (`tests/Jobbliggaren.Architecture.Tests/`). That registry's
   row test classes every DEK-free text column on a person-attributable row as plaintext personal data, with no way
   to opt out, and all three tables pass it through `job_seeker_id`. After a database restore their rows are
   exposed, and a restore from a backup that predates an account's deletion brings them back. **The accepted
   restore-exposure list (ADR 0125 Case 2, #197, #1285) therefore grows by three entries:** `feedback_submissions`,
   `feedback_notifications` and `feedback_prompt_suppressions`. Backups are kept for 30 days (K4), which bounds the
   exposure. PR2's screenshot table is entered on the same ground in that PR.
   **This is the controller's decision, not the session's** (the CTO's M3, and the registry says so itself), and it
   is written here because it must live in the same ADR as the decision. It grades nothing and is not a CLAUDE.md
   §9.6 (3) acceptance; `security-auditor` reviews the entry in the PR.
   *Magnitude at the grant:* one text of at most 2 000 characters and bounded diagnostics per submission, at most ten
   submissions per user per ten minutes, kept 90 days (PR2 adds an image of at most 5 MiB). A change in what a
   submission holds, or in these bounds, is a change of scope that this grant does not cover.
2. **The comment is a searched surface of the recruiter erasure.** Not a separate statement of Klas's: it follows
   from 1, was routed by the CTO (M2), and is recorded here because it gives the operator a duty. A plaintext text
   can be searched, and a user describing an ad can name its recruiter, so the registry gets the channel
   `FeedbackComments`: `feedback_submissions.comment` is `MatchedHumanErases`, counted by
   `IRecruiterErasureMatchQuery.CountFeedbackCommentsAsync` and reported as `feedbackComments`. A human deletes a
   matched submission, with its notice, inside the Art. 12(3) month; the 90 days remove it regardless. The other
   columns are closed domains (`NotRecruiterData`). The runbook `docs/runbooks/recruiter-pii-erasure.md` carries the
   channel in its surface list, its disposition table and reply template B2 (ADR 0106).

## Alternatives considered

- **Seal the text and the screenshot under the user's DEK, with the administrator decrypting across owners.**
  Rejected by Klas: "inga hemligheter". It would have added key handling, a cross-owner read path and a change to
  ADR 0049's mechanics for data nobody called secret.
- **Upload the screenshot first and bind it to a later submission.** Rejected in favour of one multipart submission,
  which needs no temporary uploads, no expiry job and no guard against a foreign image id.
- **A generic outbox for operator mail.** None exists. One notice row per submission and a job that reads the
  committed rows is the smaller shape, and the one ADR 0139's criterion sweep already uses.
- **Resend an unknown outcome automatically.** Rejected by Klas ("Synlig åtgärd, ingen auto (Rek.)"): a notice the
  provider may already have taken could arrive twice, so a person sees it and decides.

## Consequences

### Positive

- One request and one save: no uploads to expire, no keys to hold, no cross-owner decryption (D2, D3).
- A double click, a lost response or a second tab cannot save twice (D2). An outage costs one notice per run, and a
  notice with an unknown outcome is never resent without a person (D5).
- Retention is one constant the privacy policy can state, and both ends of it, age and account, are executable
  (D7).
- A closed gate says why on the admin page, and the host still boots (D4).

### Negative and risks

- The text is plaintext in the database, and in every backup for up to 30 days after it is deleted (grant 1). A
  restore that predates an account's deletion brings it back (ADR 0125 Case 2).
- Free text can name a recruiter or anyone else, and erasing it is manual and the operator's (grant 2).
- An `Unknown` notice waits for a person. Five refused attempts end in `Failed` after about an hour and twenty
  minutes with no mail sent; the feedback is saved and listed either way.
- The soft gate can leave the feature closed with nothing failing at startup. The signal is the admin page (D4).
- The page set is closed in code: a new page needs a code change (D1).
- ADR 0124 stays as written while a dated pointer amends its exception contract, so a reader of ADR 0124 must follow
  the pointer.
- *(2026-10-09, PR3 amendment: how long a notice can wait under the 24-hour budget.)*

## Implementation

PR1 ships this ADR with its spec changes in the same PR: BUILD.md (the endpoint lists and the recurring-job table),
`docs/threat-model.md` (the notice to an external recipient), `docs/runbooks/recruiter-pii-erasure.md` (the
`feedbackComments` channel), `docs/runbooks/local-dev-setup.md` and `appsettings.Local.json.example` (the optional
`Feedback` keys), and `docs/decisions/README.md` (the index row). PR2 and PR3 are D9's.

## References

- Code: `src/Jobbliggaren.Domain/Feedback/` · `src/Jobbliggaren.Application/Feedback/` and `Admin/Feedback/` ·
  `src/Jobbliggaren.Api/Endpoints/` (`MeFeedbackEndpoints`, `AdminFeedbackEndpoints`) ·
  `src/Jobbliggaren.Infrastructure/` (`Feedback/`, `Admin/Feedback/`, `Email/`, `Auth/AccountHardDeleter.cs`) ·
  `src/Jobbliggaren.Worker/Hosting/` · `tests/Jobbliggaren.Architecture.Tests/MappedPlaintextExposureRegistry.cs`
- `docs/runbooks/recruiter-pii-erasure.md` · `docs/reviews/2026-10-07-1979-plan-cto.md` (local)
- ADR 0150 (D2, D3, D4, D8), 0124, 0125, 0131, 0139, 0106, 0049, 0011, 0154
- Epic #1972 · issue #1979
