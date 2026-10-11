# ADR 0164 — One switch pauses every notification mail and one switch per mail type pauses that mail: pauses inside the consents, never a consent or a withdrawal

**Date:** 2026-10-11
**Status:** Accepted
**Decider:** `senior-cto-advisor` under Klas Olsson's delegation. Klas's decision on #2107, 2026-10-11, asked at the end of the #2106
session: "Ett per mejltyp (Rek.)", one toggle per mail type under the master switch, and not today's two consents. On the page, asked in
the #2107 session when no Claude Design handoff existed, he answered: "kör utan, men den måste se modern och stilren ut, enkla toogles
etc enligt appens design tema." The CTO ruled the design before any code (`docs/reviews/2026-10-11-2107-form-cto.md`: D1–D7; its
conditions are this ADR's) on the reports of `dotnet-architect` (`docs/reviews/2026-10-11-2107-form-architect.md`), `security-auditor`
(`docs/reviews/2026-10-11-2107-form-security.md`: Approved, conditions V1–V12, signatures 1–3, and a scoped continuation that re-signs
row 14 per mail type) and `design-reviewer` (`docs/reviews/2026-10-11-2107-form-design.md`, with its final copy table). Klas answered the
one question the round left him, D7, "Ja, i #2107 (Rek.)".
**Amends:** ADR 0144, by its Amendment 2026-10-11 (b) (row 7's variant, row 14 per mail type). ADR 0161, by its Amendment 2026-10-11
(#2107) (D4 "Other senders" and the Consent section read through `NotificationMailGate`). It answers the question ADR 0162 D6 left open
(whether #2107's e-mail switch withdraws `BackgroundMatch`); ADR 0162 itself is not changed.
**Related:** [#2107](https://github.com/klasolsson81/jobbliggaren/issues/2107) (the issue this ADR ships under) ·
[#2118](https://github.com/klasolsson81/jobbliggaren/issues/2118) (the digest's two reads; fixed in the same PR, D9) ·
[#1662](https://github.com/klasolsson81/jobbliggaren/issues/1662) (the notification epic) · #1664 and #1665 (the senders still to come,
D10) · #1666 (the bell, ADR 0162) · ADR 0080 Beslut 5 (local-only) · ADR 0087 D2, D5 · ADR 0120 · ADR 0144 (rows 7, 8, 14) · ADR 0146
(replay on a concurrency conflict) · ADR 0154 (every merge is a deploy) · ADR 0161 · CLAUDE.md §9.2, §9.6 · AGENTS.md §5
**Measured against:** the #2107 worktree (branch `feat/email-notices-switch`): the form-round reports on `11750988b`, the delivered
code at `02f174c89` (PR #2135) and its working tree on 2026-10-11; `origin/main` at `eb4099abb` for the merged state of ADR 0162 and #2125.

> **Provenance.** Written by `adr-keeper` for the driving session from the CTO's ruling and the delivered code (CLAUDE.md §9.2, §13).
> The approach, the pause semantics, the stamps, the gate, the page form, the rule for accumulated rows and the policy version are the
> CTO's ruling; the texts in D8 are `security-auditor`'s signatures, quoted from her report; the mandatory agents review the
> implementation in the PR. This ADR is promoted with `git add -f` (D10): it sits in the ignored 0071–0199 range, and the sessions on
> #1664 and #1665 must be able to read the gate. The four form reports are local-only, except `…-form-security.md`, which is promoted
> with the PR (security V12).

---

## Context

Klas's directive on #2107: "man måste såklart kunna stänga av epost-notiser med. Så hela e-post notis-inställningen måste ju förbättras.
Så det blir ett supersmidigt UI/UX." `/mina-sidor/notiser` had three controls, each with its own write: the background-match consent (ADR
0144 row 7, which covers the matching, the Toppmatch mail and the summary of Strong matches), the followed-company consent (row 8, a
separate purpose, ADR 0087 D5) and the digest cadence (shared by the summary and the follow mail, ADR 0087 D2). The cadence has no "off".
To stop all mail a user had to find and turn off two consents, and nothing on the page said which mail comes when. Klas chose a master
switch above one control per mail type, so that Toppmatch can be on while the summary is off.

The open question was how those switches relate to the two consents. Five facts shaped the answer.

- **A consent and a delivery choice are different things.** A consent is a legal basis (GDPR Art. 6(1)(a)). Which of the mails inside it
  a user wants is a delivery preference, and the repo already models the cadence that way (ADR 0087 D2). One purpose gets one consent
  (EDPB Guidelines 05/2020 ¶42–45), and one consent covers different operations that serve the same purpose (¶57). `security-auditor` ruled
  in #2106 (Q8) that matching new ads against the profile and mailing the result is one purpose, and found the same here.
- **The default trap.** `Preferences` is a jsonb owned type, and a row written before a key existed has no such key. The due sets and the
  fences are SQL, where a missing key is `NULL`; the scan's check runs on the materialised CLR value. `dotnet-architect` graded this
  Kritiskt: with the obvious `bool X = false`, a legacy row reads "on" in memory, but `NOT NULL` is `NULL` in SQL and the user drops out
  of every due set, so every consenting user silently stops getting mail until they save their settings. No test in the repo ran a due
  set against a row that lacks the key.
- **Every sender decides consent through one definition.** `NotificationConsent.BackgroundMatch` and `.FollowedCompany` are referenced
  by every due set, every check before a claim and the scan, so no two of them can drift (Art. 5(1)(d)). Klas's acceptance criterion is
  that every sender honours the master switch. A gate that each sender must remember to build from two or three parts would reintroduce the
  drift that `NotificationConsent` removed.
- **The scan and the bell read the same consent.** The bell (#1666, ADR 0162 D6) is read only while `NotificationConsent.BackgroundMatch`
  holds, and ADR 0162 left open whether the e-mail switch withdraws it or pauses only the channel.
- **The privacy policy points at the switch.** Rows 90 and 144 of `content-legal.json` say a consent is withdrawn "by turning off the
  notifications". With a master switch that withdraws nothing, those sentences would send a reader to a control that does not do what
  they say (Art. 7(3) last sentence, 12(1), 13(2)(c)). That holds whatever consent model is chosen.

Two shapes were open. **A** splits row 7 into one consent per mail type and carries existing consents over in a jsonb migration. **B**
keeps row 7 as one purpose and adds the switches as channel choices inside it.

## Decision

**One switch pauses every notification mail, and one switch per mail type pauses that mail. A pause is a delivery choice inside the
consents: it is never a consent and never a withdrawal.** This is approach B. D1–D10 are this ADR's numbering; each names the CTO ruling
it carries ("CTO D3c" is the ruling's D3, point c).

### D1 — Approach B: the switches are channel choices inside the existing consents (CTO D1)

Row 7 stays one purpose. Rows 7 and 8 keep their consents and remain separate purposes (ADR 0087 D5). The master switch and the per-type
switches sit inside them and can only reduce what was consented to, never widen it. Row 8 stays the follow mail's consent and its only
control (security V9): an extra follow toggle without a consent would give two controls of which only one withdraws.

- **Ubiquitous language** (Evans 2003, ch. 2). A would turn a delivery preference into a legal-basis object.
- **Granularity.** A splits one purpose into three consents and gains no compliance for it. B needs no new consent and no new Art. 7(1)
  evidence, because the purpose, the data and the recipient are unchanged (EDPB ¶58 is not triggered).
- **Risk.** A needs a jsonb carry-over that cannot be reversed exactly, and a stale writer can silently drop a consent during it
  (architect, security VA2). B needs a zero-DDL snapshot migration and nothing that can lose a consent.
- **Klas.** "Ett per mejltyp" asks for controls per type, and B delivers that in full. "Enkla toogles" points the same way: design found
  that only B achieves it, because A gives four consent paragraphs plus status lines.
- **The scan and the bell are not mail.** They keep `NotificationConsent.BackgroundMatch`. The bell does not go quiet with the e-mail
  switch, and it reads neither the master nor the type switches. This answers ADR 0162 D6: the switch pauses the channel and does not
  withdraw `BackgroundMatch`, so that ADR's lapse trigger about #2107 does not fire. `NotificationConsent.cs` is not edited: its sentence
  "every due set … decide consent through these" stays true, because the gates reference those specifications.

### D2 — The pause: what it does, and three nullable stamps that say so (CTO D2; security Q2, V3; architect Kritiskt)

**Semantics.**

- **Off** stops every notification mail of that scope. It withdraws no consent and writes no consent timestamp; the matching, the bell and
  `/matchningar` go on.
- **On** lifts only the pause. It writes no consent, changes no type value and does not touch the cadence.
- **On is never a consent.** A click that consented to rows 7 and 8 at once would merge two purposes (EDPB ¶43–44; ADR 0087 D5) and make
  the consent invalid.
- **Neither direction is refused when the consents are off** (precedent: `SetDigestCadence`). Replay semantics stay simple, and a replay
  against a fresh read cannot become a 400 because another tab changed something else.
- **Saved type values are kept while the master is off**, so lifting the pause restores exactly the user's choices.

**Shape.** `Preferences` gains `DateTimeOffset? NotificationMailOffAt`, `TopMatchMailOffAt` and `MatchSummaryMailOffAt`. `null` means on.
Off stamps once and keeps the first stamp while the pause lasts; on clears it. New and existing users are on. The methods are
`JobSeeker.SetNotificationMail`, `SetTopMatchMail` and `SetMatchSummaryMail(bool on, IDateTimeProvider clock)`, and none of them writes a
consent field, a type value or the cadence (V3).

**Why stamps and not a bool.** A stamp read as `== null` is the one shape where a missing jsonb key, the CLR default, the record default
and SQL `NULL` all mean "on", exactly as `NotificationConsentWithdrawnAt` already works.

- A `bool` fails in SQL whichever way it is named: `NOT NULL` over a missing key is `NULL`, so a legacy row drops out of every due set.
  A flag that means "on" as `true` would also read off in memory, because a missing key reads the CLR default.
- A `bool?` needs `!= true`, a translation that is fragile under refactoring.
- An ordinal enum has `DigestCadence`'s trap: its record default is Weekly, but a legacy row missing the key reads Daily (ordinal 0).

**Data minimisation.** A stamp never leaves the aggregate except as a derived boolean: the profile DTO carries
`NotificationMailEnabled`, `TopMatchMailEnabled` and `MatchSummaryMailEnabled` (`OffAt == null`), and the audit payload, the logs and
any Hangfire argument carry no stamp (security, Minor). Art. 5(1)(e): the stamps are the same class of datum as
`NotificationConsentWithdrawnAt` with the same lifetime (the user's jsonb, deleted with the account). The time is already in the audit
row; the CTO accepted the duplication because it is the only null-safe shape, and security corrected her form report, which had assumed
booleans.

**Migration.** The snapshot lists every `Preferences` property, so the new properties still take a migration, but it is **zero DDL**:
`AddNotificationMailPauses` has an empty `Up` and `Down` (template: `DropRetiredPreferencesOwnedProps`). There is no data migration.

### D3 — The gate: `NotificationMailGate` holds the consent by reference and always the pause (CTO D3c)

`Domain/JobSeekers/NotificationMail.cs` holds `NotificationMailGate` and the static `NotificationMail`:

- A gate is built from a consent specification and an optional mail-type specification, and **always conjoins the all-mail pause**,
  defined once and private. Klas's criterion that every sender honours the master switch therefore holds by construction: a gate cannot
  be built without it.
- `Apply(IQueryable<JobSeeker>)` is **chained `Where` calls**: the consent, then the pause, then the type. `IsSatisfiedBy(JobSeeker)` is
  the same instances in compiled form.
- `NotificationMail.TopMatch` = `NotificationConsent.BackgroundMatch` ∧ pause ∧ Toppmatch mail on. `NotificationMail.MatchSummary` =
  `BackgroundMatch` ∧ pause ∧ summary on. `NotificationMail.FollowedCompany` = `NotificationConsent.FollowedCompany` ∧ pause, with no type
  switch because row 8 is that mail's control.
- **The consent is referenced, never copied** (V1, Art. 5(1)(d)). Each gate point makes one call, so no point can remember half of the
  condition (the architect's drift concern).
- **No combinator.** Chained `Where` is AND under every LINQ provider, nothing is rewritten, and `IQueryable` is BCL, which Domain may
  use. The 2026-07-18 decision against an *OrElse* combinator (`CompanyWatchScanJob`), which needs an `ExpressionVisitor`, is not reopened.
  This is Evans' composite Specification (2003, ch. 9) implemented as sequential filtering.
- **The gate does not know whether the account exists.** It is applied to the `JobSeeker` set, whose query filter excludes a
  soft-deleted account.
- **It is not part of `MailPacingRule`.** ADR 0161 D4: the rule decides when, never whether.

Nothing is copied, so no equivalence tests are needed. The tests are a truth table over the states the aggregate itself produces
(`IsSatisfiedBy`), the architect's Kritiskt pin of the query form in the Worker against a raw legacy row that lacks the new keys, and a
reflection guard that every `Preferences` constructor default equals `default(T)` except `Language` and `DigestCadence`.

### D4 — The gate points per sender (CTO D3c; architect Q5; security V1)

The digest's gate points go through `StillMailableAsync(gate, userId, ct)` (renamed from `ConsentStillGrantedAsync`, now taking a gate),
the due sets through `gate.Apply(db.JobSeekers)`.

| Sender | Gate | Due set | Read again before the claim | After the claim, before the send |
|---|---|---|---|---|
| Toppmatch, `TopMatchMailDispatcher` | `TopMatch` | the user subquery of the due read | none: the claim follows the read within the send budget | the fence (it logs `LogMailClosed` when it closes) |
| Summary of Strong matches, `DigestDispatchJob` | `MatchSummary` | with the cadence | yes | the fence, inside the send `try` |
| Followed companies, `DigestDispatchJob` | `FollowedCompany` | with the cadence | before the profile is built (it is derived from the CV) and before the claim | the fence, inside the send `try` |

- **The scan (`BackgroundMatchingJob`) and the bell keep `NotificationConsent.BackgroundMatch`.** They are not mail (D1).
- **Comments and log text that would turn false are corrected in the same PR** (AGENTS.md §5, Comments): `LogConsentGone` becomes
  `LogMailClosed`, and "consenting users due" becomes "users due".
- **Tests per sender:** a switched-off type or master gives zero sends; a raw legacy row is in every due set; a pause committed after a
  committed claim gives zero sends (`UserMailDeletionFenceTests`' shape).

### D5 — The API: one PUT per control (CTO D2/Q4; architect Q4; ADR 0146)

`PUT /api/v1/me/notification-mail`, `/top-match-mail` and `/match-summary-mail`, each with `RequireAuthorization()` and `MeWritePolicy`,
a strict `{enabled}` body (`[property: JsonRequired]`) and no user id in the route or body (V4). Each command is
`IAuthenticatedRequest`, `IAuditableCommand`, `IAuditPayloadCommand` (payload `{"enabled": bool}`, V11) and
`IReplayOnConcurrencyConflict`, with the stable event names `JobSeeker.NotificationMailUpdated`, `JobSeeker.TopMatchMailUpdated` and
`JobSeeker.MatchSummaryMailUpdated`. The follow mail's control is the existing `/followed-company-notification-consent`.

**Why one per control.** A replay re-runs the command against a fresh read (ADR 0146). With one write per control, a replay writes only
its own field. A combined settings object from a stale tab would, on replay, lay all its old values over the other tab's commit, which
re-grants a withdrawal: the case ADR 0146 closed and the lesson of ADR 0087 D2 → `SetDigestCadence`. A `JobSeekerWriteRaceTests` arm
covers a master-off that commits inside a type save.

### D6 — The page form (CTO D3a, D3b, D3e, D3f; design; security V2, V5, V10)

One `.jp-card`, h2 "Notiser", built from the existing `ToggleRow`, `Segment` and `Outcome`, with no new token and no change to
`globals.css` or `DESIGN.md`. The order is the same at every width:

1. **E-postnotiser** (master): the first control, with no description, fully visible at 375×667 on arrival (the mails' link lands at the
   top). On for new and existing users.
2. h3 **Matchningar**: **Matcha nya annonser åt mig** (row 7 as its description), then **Toppmatchningar via e-post** and
   **Sammanfattning via e-post**.
3. h3 **Företag du följer**: **Nya annonser via e-post** (row 8 as its description).
4. **Hur ofta** (Dagligen or Veckovis), the cadence.

The rules that matter:

- **The consent switches always render and are always usable (V2, Blocker).** Row 7's switch and row 8's switch are never hidden,
  disabled or conditioned by the master or by any other control; only their own pending write may disable them. Each shows its bound
  description without a click, and it is in the switch's `aria-describedby`, on and off. Withdrawal happens on the same surface as consent
  with no extra step (Art. 7(3); EDPB ¶113–115). While the master is off, the status line "Skickas inte medan e-postnotiser är av." joins
  row 8 in the follow switch's `aria-describedby` (CTO D3a). Matching continues, so row 7's switch gets no status line. A component test
  renders the master off and shows both switches active with their descriptions.
- **Toppmatch and summary hide.** They are not consent switches, so they render only while the master and row 7's switch are on. A hidden
  row is unmounted (never `opacity:0`), its value is kept and comes back unchanged, and no status line is shown for it.
- **The cadence** renders when master ∧ ((matching ∧ summary) ∨ follow). Its value is saved when it hides. It never reads as applying to
  Toppmatch.
- **Focus and stale outcomes.** If a change or a refused answer unmounts the focused control, focus goes to the switch that caused it,
  never to `<body>`. A receipt or error that lands after its control unmounted is not shown when the control returns.
- **No number for Toppmatch timing (CTO D3b).** The description says "Direkt när en ny annons kommer in." Security V5 sets the form
  ("direkt" or "inom några minuter", never "15 minuter"): `W` = 24 hours makes a digit false at every catch-up run (ADR 0120: a rendered
  number is true or absent), ADR 0161 D10 declares its bound conditional on four things the code does not control, and a page promise
  cannot carry a condition the reader never sees. With no digit on the page there is nothing to bind: design Major 4 is met, and design
  Minor 4 (a test that binds "15") does not hold.
- **The master's text speaks of notification mail only and carries no consent word (V10).** Sign-in codes and account mail are sent
  regardless (the policy, row 100).
- **Design Major 5 does not hold (CTO D3f).** It asked row 7 to drop its per-grade promise because switches 3 and 4 can make it false.
  Row 7 is a consent text, "Slår du på det samtycker du till att …": it states what the consent covers, not that a mail is sent now.
  Removing the promise would strip element (c) of row 7's bound set, which is security's (DESIGN.md §8 rule 7), and a consent text must
  stay fixed because the evidence pair refers to one text. The structure carries the status instead: switches 3 and 4 sit directly under
  switch 2.

### D7 — Accumulated rows keep today's behaviour (CTO D3d; security Q5)

While the master or a type is off, the matching continues and rows accumulate as Pending. When the pause lifts, the sender mails them
exactly as it does today: no age bound for Strong and follow rows, and no drain while off. Toppmatch is already bounded by `W` = 24 hours
from both the ad's and the row's `CreatedAt` (ADR 0161 D6), so an older Top row is never mailed and shows only in the app.

- **Lawful.** A pause is not a withdrawal, so the consent runs throughout and ADR 0080 Beslut 5 is not triggered. For the follow pass this
  is risk R-E6 in the DPIA Part E, which `security-auditor` approved on 2026-07-12 with M-E6: only unseen hits, in one mail, capped, with
  "och N till".
- **True.** "Sedan sist" holds for a backlog, and the summary copy promises no period.
- **Bounded anyway.** The display is Active-only, ads expire, and the list is capped by `MaxItemsPerDigest` and "och N till".
- **Carried condition.** Copy that promises a period ("veckans", "det senaste dygnet") binds the sender to that period in the same PR.
- **Rejected:** an age bound (no finding requires it; it adds a constant coupled to the cadence with its own invariant, changes the DPIA
  posture of the follow pass, which is a product decision outside #2107, would fold a policy change into #2118's race fix, and would widen
  the falsity of the `Sent` docstring); a drain while off (it processes paused users' rows in a due set, in the direction they opted out
  of, and writes `Sent` at scale); and putting Top rows into the summary while the Toppmatch mail is off (the digest would become a second
  claimer of Top rows, ADR 0161 D8, and V5 bars Top from the summary).

### D8 — The signed texts and the policy (CTO D3e, D3g, D5; security signatures 1–3 and the continuation)

The strings themselves are in ADR 0144's Amendment 2026-10-11 (b). In short:

- **Row 7, variant 3 (signature 3), included now.** Its sentence "Bra matchningar visas bara i appen" is true both before and after the
  bell shows Good matches, so row 7 no longer depends on the order in which #2125 and #2107 merge (V8). The cost, an amendment and two
  pins, is already owed for row 14.
- **Row 14 per mail type.** Each reason sentence names its mail by the page's own label and is true for a recipient who never touched a
  type switch, which defaults to on. A type switch is therefore named in state form ("… är på"), never as the act "du har slagit på". The
  Direct (Toppmatch) paragraph no longer promises "hur ofta", because no cadence applies to it. The follow mail names row 8, its only
  control. `security-auditor` signed all three paragraphs word for word as the session drafted them and found the label half legally
  unobjectionable, so the CTO's fallback (if she declined the label half on legal grounds, text 1 governs) did not trigger.
- **The privacy policy, rows 90 and 144 (signature 2), in this PR.** Not before the switch exists, because then the text describes a
  control that is not there; not after, because then rows 90 and 144 mislead on a public page. There is no accepted-risk route, because
  the policy is publicly readable (CLAUDE.md §9.6 (3)(iii)); rows 88, 89 and 101 stay true. The bound elements are: each consent is
  withdrawn with its own switch in the settings; what stops (for row 7 the matching and its mail, for row 8 the follow mail); that
  turning off all e-mail notifications, or one type, stops those mails but withdraws nothing and the matching continues in the app; and
  the sentence that processing before the withdrawal was lawful. Klas's pre-authorisation covers this edit (D7 of the ruling: "Ja, i
  #2107 (Rek.)").
- **The policy's version is the merge date (CTO D5).** `TermsAcceptance.CurrentPrivacyPolicyVersion` is an ISO date, `privacy.updated` in
  both locales must end with it (`TermsAcceptanceVersionsMatchPublishedPolicyTests`), and the column is `varchar(20)`. It identifies
  which text a registrant saw (Art. 5(2)), and 2026-10-11 is already #2114's. So the PR **does not merge on 2026-10-11**
  (Europe/Stockholm): in the last content head the session stamps both JSON files and the constant with the date it will merge, 2026-10-12
  or later. If the merge slips past that date, it re-stamps before `agents-done`, three lines, and `security-auditor` re-verifies
  report-only (a moved head, not a round). No suffix scheme: a suffix would break the constant's contract ("the ISO date the published
  copy carries") and put a non-date into "Senast uppdaterad".

### D9 — #2118 ships in the same PR, as its own commits (CTO D4)

The Strong digest read its claim set and its display set as two statements. The matching run writes Strong rows every five minutes and is
not serialised against the digest (ADR 0161), so a row committed between the two reads was listed, left Pending and mailed again by the
next digest. Now **one tracking read with a left join** carries each Pending Strong row with its ad's public title and company. The
claim set is every row (an archived or erased ad is still drained, #864); the display set is the rows whose ad is Active, decided in the
projection. The ordering is unchanged and the claim loop stays in `DispatchUserDigestAsync`, so the Cecil claimer pin stays valid.
There is no `AsNoTracking` anywhere in the query (the architect's reading of EF Core: it would apply to the whole query and turn the claim
into a no-op), and only scalars are projected from the ad. The lifecycle registry's site is reclassified as any-status with its reason; the `NotificationStatus.Sent`
docstring, which was false for a drained row, is corrected as a defect rather than filed; and a race test commits the scan's own write
after the digest's first read, shown red against `11750988b`. The follow pass has no twin of the race, because its mail items come from
the claim set via `claimedAdIds`. #2118 is not a consequence of the pause; it shares the file and the PR.

### D10 — Future senders, and where this decision lives

- **#1664, #1665 and any later notification mail build their gate on `NotificationMailGate`.** A new mail type adds a static to
  `NotificationMail` with its consent by reference and, if it has a type switch, a nullable stamp read as `== null`. The all-mail pause
  comes with the gate. A mail for a different purpose still needs its own consent (ADR 0087 D5), which the gate then holds by reference.
  ADR 0161 D4's "each sender keeps its own consent check" reads through the gate (ADR 0161, Amendment 2026-10-11 (#2107)).
- **Where it lives.** ADR 0164 is tracked through `git add -f` with a README row (precedent 0154, 0156–0161), because a gitignored
  decision is invisible to the #1664 and #1665 sessions (#1173). The security form report is promoted in the same PR (V12). ADR 0144 and
  ADR 0161 are tracked and get their amendments in the same PR.

### Conditions carried

`security-auditor`'s conditions; the grade applies if the PR breaks them. The design reviewer's Blocker 1–3, Major 1–4 and 6–8 and Minors
1–2, and the architect's Kritiskt, Viktigt (no `AsNoTracking`, the registry, one PUT per control) and Nice-to-have 1–3, are carried as the
CTO's "In-block-fixar" lists them; the reports hold their wording.

| | Condition | Grade |
|---|---|---|
| V1 | Each sender has its own gate: consent by reference ∧ pause ∧ own type, at the due set, before the claim, after the claim and before the send, and before the profile build. A test per sender: off gives zero sends, a pause after a committed claim gives zero sends | Blocker (due set), Major (fence), Minor (profile build) |
| V2 | The consent switches are always usable, with their bound description visible and in `aria-describedby` | Blocker |
| V3 | No pause write touches a consent field. On writes no consent and changes no type value; off stamps no withdrawal. Domain tests pin all three | Blocker, Major |
| V4 | The write paths follow their siblings: a route per control, `RequireAuthorization`, `MeWritePolicy`, a strict body, a 401 test | Blocker |
| V5 | The page agrees with the senders (the summary carries only Strong matches; the follow mail new, unseen, active ads; the cadence is not Toppmatch's; no fixed clock time, since 06:00 UTC is 07:00 or 08:00 Swedish time; a promised period is held; "direkt", not "15 minuter") | Major, Minor |
| V6 | Row 14 as signed; all three paragraphs pinned word for word in both parts, Direct and Digest in separate tests | Major, Minor |
| V7 | The policy texts in the same PR, and the version is the merge date | Blocker, Minor |
| V8 | Row 7's variant 3 (taken in D8) | Major, Minor |
| V9 | One control per mail type; the follow mail's control is row 8's switch | Major |
| V10 | The master's text speaks of notification mail only, with no consent word | Major |
| V11 | Each write is an `IAuditableCommand` with payload `{"enabled": bool}`; logs carry counts per run, no per-user line for a paused user, no address, and no ad id or title together with a `UserId` | Minor |
| V12 | The security form report is promoted with `git add -f`, and ADR 0144 gets an amendment | Minor |

## Alternatives considered

- **A — split row 7 into one consent per mail type.** *For:* every mail type has its own legal basis and a switch that is also a
  withdrawal; lawful. *Against:* not legally required; one purpose becomes three consents, each with its own evidence pair and bound text
  (four consent paragraphs plus status lines on the page, against "enkla toogles"); it needs a jsonb carry-over that cannot be reversed
  exactly and that a stale writer can drop silently; not carrying consents over stops mail the user validly consented to, and stamping a
  withdrawal at the carry-over would write an Art. 7(3) record the user never made. Rejected (D1).
- **B — the switches as channel choices inside the consents.** Chosen (D1–D6).
- **"Withdraw all" for the master.** *For:* one click ends everything. *Against:* it would stamp a withdrawal of row 7, stop the matching
  and the bell with no word on the label (Art. 5(1)(a)), write a 7(3) record the user did not choose, and be one-way, because switching it
  on would re-consent to several purposes at once (EDPB ¶43–44). Rejected (D2).
- **Booleans instead of stamps (`bool`, `bool?`, an enum).** Each fails a different way in SQL or on a legacy row (D2). Rejected.
- **Gate the consent switches behind the master (hide or disable switch 2 or 5).** *For:* the structure would carry every dependency
  itself (design's first form hid the follow switch while the master is off). *Against:* a consent switch behind a convenience switch is
  the extra step that EDPB ¶114–115 rejects; V2 is a Blocker. Rejected (D6).
- **An expression combinator for the gate.** *Against:* unnecessary, since sequential `Where` gives the same semantics and needs no
  `ExpressionVisitor` (D3). Rejected.
- **The architect's whole expressions duplicated per sender, pinned by equivalence tests.** *For:* each gate reads as one expression.
  *Against:* a copy pinned by tests is still a copy (DRY, Hunt and Thomas 1999), and the consent predicate would exist twice. Rejected
  (D3).
- **Accumulated rows: drain while off (Q5 b), an age bound (Q5 c), or Top rows in the summary.** See D7. Rejected.
- **A combined settings object with one PUT.** *For:* less code. *Against:* a replay from a stale tab re-grants a withdrawal (ADR 0146).
  Rejected (D5).
- **A suffixed policy version.** See D8. Rejected.

## Consequences

### Positive

- Klas's case works: Toppmatch on with the summary off, and one place to stop every notification mail, without touching a consent. Each
  pause is reversible and restores the user's exact choices.
- No new consent, no new evidence pair and no carry-over; the migration carries no DDL.
- A sender cannot check half of the condition. The gate holds consent and pause together, and the pause is structural.
- A legacy row reads as on by construction in the CLR, the record, the jsonb and SQL, and a Worker test pins the query form.
- The consent texts, the evidence pairs, the scan and the bell are unchanged; withdrawal stays one click on its own switch, on the same
  surface as consent.
- The mails, the page and the policy now point at the control that does what they say.
- #1664 and #1665 inherit one gate, and the Strong digest lists exactly the rows it claims (#2118).

### Negative

- **Rolling back to a pre-#2107 image drops the three keys on that user's next preference write, which lifts a pause.** An older image's
  `Preferences` type maps none of them, and a changed owned-JSON entry is written whole from the writer's own CLR type (the mechanics
  `Preferences.cs` records for the retired TD-115 keys, there on the read side). The older senders also read no pause at all. This is the
  architect's reading and is not measured as a rollback. Such a rollback also meets ADR 0130's schema-ahead gate in
  `Jobbliggaren.Migrate`, which refuses the database until `MIGRATE_ALLOW_SCHEMA_AHEAD` names this migration; the migration carries no
  DDL, so naming it changes no schema.
- **A backlog can reach the inbox after a long pause** (D7). It is counted truthfully with "och N till", but a user who paused for weeks
  can receive a list of everything still Active on the first run after the pause lifts. No age bound exists for Strong or follow rows.
- **A Top match found while the Toppmatch mail is off is not mailed once it is older than `W` = 24 hours** (ADR 0161 D6). Switched back
  on within `W`, a still-due row is mailed like any Pending row (D7); after `W` it shows only in the app and is never mailed.
- **The pause stamp stores a time the audit row already holds.** Accepted, because it is the only null-safe shape (D2).
- **Switches 3 and 4 are unmounted while the master or row 7 is off**, so a saved choice is out of sight until the switch above it is on
  again. The value is kept and comes back unchanged (D6).
- **A user can see the follow switch on while the master is off.** The status line says nothing is sent. This is deliberate (V2): the
  alternative is a consent switch behind a convenience switch.
- **Three commands and three writes** where one object would be less code. That is the price of a replay that cannot re-grant a
  withdrawal (D5).
- **The PR may wait up to one calendar day for its policy version** (D8).

## Implementation

One PR closes #2107 and #2118, in the CTO's order (D6 of the ruling): (1) `fix(matching)`, #2118; (2) `feat(jobseekers)`, the Domain
(`Preferences`, the three setters, `NotificationMail.cs`); (3) `build(persistence)`, the zero-DDL migration and snapshot, committed once
the #1662 migration token stands; (4) the three commands, the DTO and the three routes; (5) `feat(matching)`, every sender on its gate;
(6) `fix(email)`, row 14; (7) `feat(web)`, the page and its messages (variant 3 added, `backgroundMatch.cadenceHintDisabled` struck);
(8) `docs(privacy)`, the policy text and the date; (9) `docs(adr)`, this ADR, the two amendments and the promoted security report.
Before (8), the session greps the `content-*` message files and `BUILD.md` for wording like "stänger du av notiser" and records the
result in the PR body. The mandatory panel is `code-reviewer`, `dotnet-architect`, `security-auditor` (told that the fields are
timestamps), `design-reviewer` (renders per its list), `db-migration-writer` and `test-writer`. No follow-up PR and no issue is filed.

## References

- `docs/reviews/2026-10-11-2107-form-cto.md` · `docs/reviews/2026-10-11-2107-form-architect.md` ·
  `docs/reviews/2026-10-11-2107-form-security.md` (promoted with the PR) · `docs/reviews/2026-10-11-2107-form-design.md`. The CTO's
  cadence ruling, `docs/reviews/2026-10-11-1662-cadence-cto.md`, and the DPIA Part E, `docs/research/2026-07-12-dpia-follow-rail-part-e.md`,
  are in the main copy and local-only.
- Code: `src/Jobbliggaren.Domain/JobSeekers/` (`Preferences`, `JobSeeker`, `NotificationMail`, `NotificationConsent`) ·
  `src/Jobbliggaren.Application/JobSeekers/Commands/` (`SetNotificationMail`, `SetTopMatchMail`, `SetMatchSummaryMail`) ·
  `src/Jobbliggaren.Api/Endpoints/MeEndpoints.cs` · `src/Jobbliggaren.Application/Matching/Jobs/BackgroundMatching/TopMatchMailDispatcher.cs`
  · `src/Jobbliggaren.Application/Matching/Jobs/DigestDispatch/DigestDispatchJob.cs` · `src/Jobbliggaren.Infrastructure/Email/EmailTemplates.cs`
  · `src/Jobbliggaren.Infrastructure/Persistence/Migrations/*_AddNotificationMailPauses.cs` ·
  `web/jobbliggaren-web/messages/{sv,en}/content-legal.json`
- ADR 0080 Beslut 5 (local-only) · ADR 0087 D2, D5 · ADR 0120 · ADR 0144 (Decision 4 rows 7, 8, 14; Amendment 2026-10-11 (b)) · ADR 0146 ·
  ADR 0154 · ADR 0161 (Amendment 2026-10-11 (#2107)) · ADR 0162 D6 · AGENTS.md §5 (Tests, Comments) · CLAUDE.md §6.5, §9.2, §9.6
- EDPB, Guidelines 05/2020 on consent under Regulation 2016/679, v1.1, ¶42–45, 57, 58, 64, 113–117 (read 2026-10-11; the link may rot):
  https://www.edpb.europa.eu/system/files/documents/files/file1/edpb_guidelines_202005_consent_en.pdf
- Evans, *Domain-Driven Design* (2003), ch. 2 (ubiquitous language), ch. 9 (Specification) · Martin, *Clean Architecture* (2017), ch. 7
  and 13 · Hunt and Thomas, *The Pragmatic Programmer* (1999), DRY
