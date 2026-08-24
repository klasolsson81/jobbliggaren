# Spec rationale — derivations, incidents and dated measurements

> **Non-normative.** Every rule lives in `CLAUDE.md` (and, after the interop split, the
> shared spec file it names). This file carries the derivations, incident history and
> dated measurements those rules used to carry inline — moved out verbatim 2026-08-22
> (issue #1427) because the spec is loaded into every session and ~15 subagents per
> session, and 79 % of it was derivation. The § numbers below are the spec's shared
> namespace (CLAUDE.md today; CLAUDE.md + AGENTS.md after the interop split); this file
> defines none of its own. An entry is struck in the same PR as the rule it grounds —
> this file carries no entry whose rule no longer lives in the spec.
> Disclosure boundary: content here is limited to what
> CLAUDE.md itself already published (ADR 0072's curated façade) — never import
> gitignored-ADR content beyond what the spec already quoted. New entries arrive only as
> verbatim moves out of the spec, or as the dated ground of a rule landing in the same PR.

## §1.6

**The TD register's disposal (rule: the backlog is GitHub Issues, and nothing else).**
Its 44 live entries were disposed of in the same pass, and the breakdown lives in
**one** place — [#1172](https://github.com/klasolsson81/jobbliggaren/issues/1172) —
which also carries every parked entry **inline**, because both register files were
gitignored and "archived" would have meant deleted.

## §2

**ADR 0009's ratified coupling (rule: Application MAY reference the EF Core package).**
Its Konsekvenser/Negativt records "Handlers är direkt beroende av EF Core-interfaces
(via `IAppDbContext`)". Ratified trade-off, not drift.

**How `DomainLayerTests` enforces the provider ban:** NetArchTest reads type references
in IL, not `PackageReference` entries.

**Why `IAppDbContext` has `Detach`:** added later for the ADR 0032 §5 upsert-retry.

**Worked instance of the member-boundary port rule:** `IJobAdRequirementBackfillFilter`
— its doc comment cites this rule back.

## §3

**The predicate-free-reader verification behind §3.6's ANALYZE trigger** (verified
2026-07-25): the only **`src`** readers of `taxonomy_concepts`/`taxonomy_relations` are
two predicate-free `ToListAsync` calls in `TaxonomyReadModel.LoadAsync`.

**Why "check `last_autoanalyze`, never assume":** `company_register` held zero
statistics at a million rows.

## §4

**Why TanStack Query is more than a dependency add:** on the read-suggest surface
specifically, it would be a reversal of ADR 0042 Beslut C, which is a Klas-GO
supersession rather than a library choice.

## §5

**The measured case behind "going through a production entry point is no exemption":**
a hand-built `rawPayload` carrying the key the ingest sanitizer strips.

**Why seam parity is not provenance:** #843's fiction was authorised by explicit parity
with a legitimate seam whose SQL was identical.

**Why the Comments block exists:** comment mass is what turned review into rounds.

## §6

**How `is-pure-base-merge.sh` decides:** it compares the pushed tree against the tree an
automatic merge would produce and leaves the gate alone when they are identical, which
is what `gh pr update-branch` produces.

## §6.5

**The incident behind worktree-per-task** (real incident 2026-06-28): a parallel
checkout yanked an active branch mid-session; the commit survived only because it was
already pushed.

**`next-up`'s retirement:** `next-up` is on zero open issues as of 2026-08-02 and `mvp`
replaced it in practice.

**The measurement behind "the second clause is doing real work"** (mvp criterion):
measured 2026-08-02, 11 of 21 labelled issues carry `area:infra`/`area:auth` and no
product-surface `area:` — the deploy stack (#196), backup (#197), key rotation (#198),
the log sink (#1175). *(Area is a **proxy** for which clause applies, not an
adjudicator: #1171 is `area:auth` and is a clause-1 case — a user meets a missing
password reset — while #853 and #1033 are `area:docs` and are clause-2.)*

**Worked instance of the builder exception:** e.g. #1061, where `/cv` offers entry
points into the paused builder.

**The measurement behind the two-axis rule (`P0`–`P3` × `mvp`):** measured 2026-08-02:
three `mvp` issues are `P3` and eight non-`mvp` issues are `P2`; overloading `P0` to
mean MVP would destroy the severity information on nearly every open issue (55 of 58
carry a `P`, measured 2026-08-02). *(Klas put it both as "kärnfunktion slår
prio-siffra" and "MVP-kritiskt = hög prio"; these agree in practice — no non-`mvp`
issue carries `P0`/`P1` — but the two-axis split is how the spec resolves them, not a
quote.)*

**What "automerge does not rebase" looks like:** when a sibling lands, yours goes
`BEHIND` and then sits there forever with green `ci` and automerge on, and nobody is
told.

**The 2026-07-14 hygiene pass** (all measured): 44 dead local + 44 dead remote
branches; #800/#801 shipped and still `wip` two days on; 9 `wip` claims against 4
running CCs.

**Why `delete-merged-branches.yml` is a scheduled sweep and not a merge-event
handler** (measured): events triggered by `GITHUB_TOKEN` do not start workflow runs, so
the merges that leave branches behind — every app-merge — are exactly the ones whose
`pull_request: closed` event never fires. **Two mechanisms, one cause — don't collapse
them:** that suppressed *workflow run* is also why CodeQL stopped running on main,
whereas `delete_branch_on_merge` is a repo *setting* that never travels through the
workflow engine at all — it simply follows the merging identity, and the app is not it.
Same actor, different machinery; a fix aimed at the wrong one of the two does nothing.

**The reaper's marker measurement** (measured 2026-07-14): **0 markers across 13
worktrees, 1121 "no-marker" skips, one reap in the hook's entire history.**

**ADR 0094 on liveness proxies:** it rejected age/pid liveness proxies outright.

## §7

**The #1311 incident behind "read the `total:` line":** #1311 was not a quiet failure —
every form above says `Zero tests ran` or names the right flag. It survived because
nobody read the line.

## §8

**Why point 4 names non-resting states (2026-08-24):** rendered verification's trigger
was purely structural — the runbook's "När" section carried zero state words, so a
state no reviewer had ever SEEN could ship reviewed. The measured instance: RegisterForm's
registrationsClosed panel existed in zero captures, zero visual-verify runs, zero reflow
sweeps. The 2026-08-24 mapping session then rendered the class wholesale (adjudicated in
`docs/research/2026-08-24-error-surface-matrix.md`, 2026-08-24, local-only per ADR 0072)
and the rendering
overturned trace-level claims twice (a focus-to-field claim refuted on both arms of one
card; a Radix-Select survival question the source could not answer resolved to
"destroyed") — the gap was live, not theoretical.

**Why the cost bound is in the rule:** 14 section-load surfaces × the runbook's 6-image
matrix × states is an unpayable bill that would have made the rule dead on arrival; the
mapping itself rendered state-only scenarios at 1280 and reserved the full matrix for
composition-critical families. The rule encodes that split.

**Why the design-reviewer charter changed in the same PR:** the charter's Tools line
forbade Bash while she measurably rendered PR #1502 via a local dev server (five
viewports, DOM injection) — the prose ban was unenforced (no `tools:` frontmatter) and
false to practice. A must-render rule aimed at a reviewer whose charter forbids the tool
is inert; the line now permits local rendered measurement and keeps the report-only and
no-online-trends bans. **§12 gains no new class here** — an unrendered state fails DoD
point 4 and blocks through the ordinary review gate, not through a new STOPP class.

## §9.2

**Why the security-auditor trigger class is duplicated into the spec:** it is written
in her Triggers section, but a trigger only reachable from inside the file it triggers
has no invocation path, so the class belongs in the spec.

**The readerless-set derivation behind her audit-area-8 consumer role:** the guard also
runs in observe-only `audit` on every PR — but Dependabot PRs auto-merge without
invoking any agent, and no cadence consults the measurement, so on the auto-merged
patch/minor Dependabot PRs — the bulk of what drives that drift — **there is no reader
at all**. Nor is there an obligation to read it on the manually reviewed remainder: the
guard's `::warning::` does surface, in the Checks view, but `audit` is
`continue-on-error` and absent from `ci`'s `needs`, so a finding changes nothing in the
merge signal. The readerless set is therefore *larger* than the auto-merged one, not
equal to it. That gap is named in ADR 0065's amendment and triaged there as a follow-up
PR rather than a TD; **no owner is assigned**, and it is not closed.

**The background-subagent built-in tool set** (code.claude.com/docs/en/sub-agents, read
2026-08-03; a decaying external fact — re-read the page before relying on it): `Read`,
`Grep`, `Glob`, `Bash`, `PowerShell`, `Edit`, `Write`, `NotebookEdit`, `WebFetch`,
`WebSearch`, `TodoWrite`, `Skill`, `ToolSearch`, `EnterWorktree`, `ExitWorktree`,
`Monitor`, `TaskStop`, `SendMessage`, `Artifact`.

**The fork exception, as the docs put it:** a fork "skips both filters and receives the
main conversation's exact tool pool" (same page, same reading).

## §9.6

**The 2026-08-10 backlog measurement behind the filing cap:** the backlog grew +62 net
in the eight days after the register retired (4.3 filed per closed), and 48 of the 60
issues filed in the last week were `area:infra`, one of them user-facing — not because
the rule was wrong but because "fix in-block" was read as a router.

**The 2026-08-22 root-cause measurement behind the deletion-only fix rule and the
re-check cap** (PRs #1412–#1422, 127 findings, 21 rounds — a dated historical
measurement of finished PRs; the full ledger, `docs/reviews/2026-08-22-review-loop-root-cause.md`,
and its sources are gitignored or session-local, so the numbers are citable from here
but not regenerable from the public tree): 65 % of
all flagged findings sat in prose; 72,7 % of round-≥2 findings sat in prose an earlier
fix added; 61 of 98 closures added new claim-prose; every round-1 fix wave (27–122
added claim-lines) drew new findings on all five PRs; no substantive deletion-only or
code-only fix delta was ever submitted for re-review; 7 of 61 verified closures
recorded DELETION while the closing hunk wrote a replacement claim — which is why
mechanical closure requires all three checks. The earlier counter-measurement
(2026-08-09, PRs #1249/#1254): a scoped re-check returned 0 blocking findings in under
three minutes, against full rounds of twenty that generated fresh sentences to defend.
The restatement-drift warning's ground: #1173, where a retired rule lived on in a
satellite file for three months.

**§9.6 (3)'s derivation history:** ADR 0132's Amendment §1 derived the ground and ADR
0133 followed it: **one derivation, two homes**, which is the duplication the spec
paragraph ends. Both delivered instances rested on all three parts of the bound.
*(Not `company_register` — it holds no enskilda firmor by design; the ROPA's SCB entry
owns why.)* The criterion-first reading is the opposite of the lapse clause, where the
general sentence under-triggers and the enumeration governs — the two are not the same
shape, and applying one's lesson to the other gets it exactly backwards. The route
paragraph writes down the bound that was already applied, so a third instance cites it
instead of reinventing it — the duplication was the measured cost, not the standard.
The lapse-trigger rule's grading history: a draft carrying the ground sentence alone
was graded **under-triggering** (`security-auditor` M-1, ADR 0133). Measured against
both delivered instances: neither hit §5 `Security:` at all — that list is CODE
anti-patterns; a GDPR-implicated Major is typically a legal-posture finding with no
code form, so a reader who goes looking for a §5 class and finds none has not found a
problem. *(The spec paragraph's existence closes `code-reviewer`'s standing escalation,
raised on ADR 0132 and again on ADR 0133 (both 2026-08-16), that §9.6 offered no
positive route here. §13 is why it lands in the spec: the boundary is a CC boundary.
That it belongs there **rather than in each ADR** is ADR 0133's own preamble — an ADR
decides one processor's case, not a standing rule.)*

**Filing discipline's ground:** six of the retired register's entries turned out
already fixed the moment anyone measured them: they were true when written and rotted
in place, because nothing in that register's lifecycle ever required re-measuring an
entry, and an issue is re-read by no one on its own.

## §11

**The Seq-sink acceptance's incident history:** condition (1)'s auth was added
2026-08-04 (#1198) precisely because the binding alone had been *measured wrong for
months* while the compose file's own comment vouched for it. Condition (2) was
**measured FALSE on 2026-08-04**: 41 activation/confirmation links in plaintext plus
one real address; that sink was discarded in the same PR — enabling auth required an
empty volume — so the count was zero at that date and refills at the next dev
registration.

**The e-mail provider succession:** Resend, which Klas removed entirely on 2026-08-08;
then SES, which AWS confined to sandbox by refusing production access on 2026-08-14;
now Scaleway.

**The idempotency-key history:** the port lost its typed idempotency-key parameter in
the Resend removal, and no provider since has had an equivalent to restore it for:
neither SES v2 `SendEmail` nor Scaleway's `POST /emails` carries one. ADR 0103 already
states the claim-then-send spine works *"regardless of Resend's own idempotency-key
dedup"*.

**Why the fourth `WebApplicationFactory` was declined** (#1190): the Api suite sits
**one `WebApplicationFactory` below** EF's process-global
`ManyServiceProvidersCreatedWarning` ceiling, and the next host fells whichever
collection fixture initialises after it.

**The terraform-record inventory** (measured 2026-08-04): the same block injects two
`FieldEncryption__*` options #802 removed, injects no master key (so a re-apply
hard-fails at startup), and names `src/JobbPilot.*` Dockerfile paths that do not exist.
Renaming one string buys one-of-N consistency and makes a record read as maintained.

**The dev-boot contract's incidents:** `#544` org.nr-HMAC + `#692` CV-fingerprint
peppers both failed the next stack-owner's boot one crash at a time — measured
2026-07-19.

## §12

**The Comments carve-out's measurement:** the cost asymmetry is what 2026-08-04/05
measured.

**Why the manual pre-merge hold was retired:** a gate that is always pressed through
adds latency, not review.
