# ADR 0147 — Match preferences are written one part at a time: a PATCH with five optional parts, and a PUT that retires only after both images are live

**Date:** 2026-09-29
**Status:** Accepted
**Deciders:** Klas Olsson (the directive of 2026-09-28, and his answer on the PR split via
`AskUserQuestion`, 2026-09-28) · `dotnet-architect` (the write form, R1–R8,
`docs/reviews/2026-09-28-1918-form-dotnet-architect.md`) · `senior-cto-advisor` (the routing, points 3
and 6, `docs/reviews/2026-09-28-1918-form-cto.md`)
**Related:** [#1918](https://github.com/klasolsson81/jobbliggaren/issues/1918) (this ADR ships in PR A
of three) · #1921 (F6, outside this decision) · #1238 (the mixed set on the pull path, owed and not
delivered) · ADR 0146 (the mechanism this contract uses; a dated pointer at its D3) · ADR 0145 (D4:
Notiser writes each control to its own endpoint) · ADR 0079 (local; STEG 3 (c), and Fork B of its
Amendment 2026-06-23) · ADR 0076 (local; F4-12, the stated-preferences SSOT)
**Measured against:** `main` at `cb9f00bb`, 2026-09-28: the form round's own reads, and every line
range below that is not marked otherwise. PR A's own files were read in its worktree on 2026-09-29.

---

## Context

Match preferences are written by one endpoint, `PUT /api/v1/me/match-preferences` →
`SetMatchPreferencesCommand`, which replaces all eight fields at once: occupation groups, the
per-occupation years, skills, regions, municipalities, remote, employment types and the experience
scalar. Its handler builds the whole value object from the command alone. The settings card sends all
eight from its own tab's copy on every chip removal, so another tab's commit to any other dimension is
written over (dotnet-architect F1, Major). ADR 0146's replay cannot stop that: the stale values sit in
the request body, and the fresh read never reaches the column. The marker's contract is that the handler
"re-derives everything from what it reads" (`IReplayOnConcurrencyConflict.cs:10-12`); a handler that
reads nothing of what it writes turns the replay into the "client wins" form that `UnitOfWorkBehavior`
rejects (`UnitOfWorkBehavior.cs:41-43`). That follows from the full-replace contract and is no defect
in ADR 0146.

Klas's directive of 2026-09-28 asks for Matchning to be editable one part at a time — *"bara yrken, bara
kompetenser, bara orter, bara anställningsformer eller bara antal års erfarenhet"*. The brief that
carried it to the form round left two questions open: how each part gets its own entry, and whether the
write stays a full replace or becomes a write per part. It also asked for ADR 0146's replay behaviour to
be weighed in (D7) and for the first-time flow to keep working. design-reviewer's Bindning 7 fixes what
the write must be able to say: a save labelled with one part, and a removal inside it, may write only
that part, and the full PUT would re-send the tab's copy of the other four under a button that names one.

The directive also makes "Antal års erfarenhet" (`ExperienceYears`) a part of its own. ADR 0079's
Amendment 2026-06-23 (Fork B) kept that scalar "deprecated-in-doc"; the directive contradicts it, and
D10 records that. The first-time rail has a defect of the page-wipe class the profile DTO itself warns
about (F2, Major): its write omits `experienceYears`, its schema has `.optional()` with no default, the
key vanishes from the JSON, the command binds `null`, and a stated value is cleared. It is reachable:
remove the last occupation chip on the card, then open the rail through `?matchsetup=1` on /oversikt.

`web` and `api` ship as separate images. `release-images.yml` builds five cells with `fail-fast: false`
and no fan-in (`.github/workflows/release-images.yml:84-97`), and the reconcile timer can install a
mixed set, a web from one commit against an api from another, because each cell pushes when it finishes
(`deploy/systemd/jobbliggaren-reconcile.timer:10-28`). Closing that window on the pull path is owed,
not delivered (#1238).

The form round ran 2026-09-28: dotnet-architect on the write, design-reviewer on the entry per part,
senior-cto-advisor on routing. Below, R1–R10 and F1–F6 are dotnet-architect's numbering, and "CTO 3" and
"CTO 6.2" are senior-cto-advisor's points, in the two review files named in the header.

## Decision

Match preferences are written one part at a time through `PATCH /api/v1/me/match-preferences`
(`UpdateMatchPreferencesCommand`, five optional parts). A present part replaces that whole part; an
absent part is left as stored. The full PUT stays until the web image that no longer calls it is live,
and retires in a later PR (D5).

### D1 — One PATCH with five optional parts (R1)

The command carries five nullable parts. The members inside a part are named exactly as the value
object's properties:

```json
{ "occupations":     { "preferredOccupationGroups": [..], "preferredOccupationExperience": [..] },
  "skills":          { "preferredSkills": [..] },
  "locations":       { "preferredRegions": [..], "preferredMunicipalities": [..], "preferredRemote": false },
  "employmentTypes": { "preferredEmploymentTypes": [..] },
  "experience":      { "experienceYears": 5 } }
```

The command carries `IReplayOnConcurrencyConflict` and the route `MeWritePolicy`. The names are the
contract in three layers, the jsonb key, the wire key and the Zod key, and the parity guard is
name-based on purpose (`MatchPreferencesContractParityTests`); `GET /api/v1/me/profile` carries the same
keys. The part records are Application inputs (`*Input`, like `OccupationExperienceInput`), never Domain
types. A handler that sets one to five parts of a value object still does one thing (AGENTS.md §2.3).
Why not one PUT per part, and not the full PUT with a per-part UI on top: Alternatives.

### D2 — Years inside Occupations are the one optional member (R2)

In `occupations`, `preferredOccupationExperience` is optional, and its three states are the rule:

- **Absent** keeps the stored years for the occupation groups still chosen, filtered against the
  normalised group list.
- **`[]`** clears them.
- **Present** replaces them, and the subset rule in `MatchPreferences.Create` (every overlay entry must
  name a chosen group) checks against the new groups.

The rule behind it: **a surface writes only what it renders.** The dialog and the rail render years per
occupation and send them; the card renders no years and omits them. Always sending years, projected by
the client, would let a chip removal in a stale tab rewrite years the user cannot see. The design pass's
`WithOccupations(groups, overlay?)`, where `null` means "keep", is rejected: Domain gets two overloads
(D6), so the wire's three states stay in the handler.

### D3 — Every member of a present part is required, and an empty request is a 400 (R3)

- **Every member of a present part is `[property: JsonRequired]`**, except the years overlay of D2. That
  is the house form: `UpdateNotificationConsentCommand` refuses a body without `enabled` rather than
  bind `false`.
- **The lists also carry `NotNull` in the validator**, because `JsonRequired` lets an explicit `null`
  through. `[]` is the only way to clear a list.
- **`preferredRemote` must carry `JsonRequired`.** A missing bool binds to `false`, which reads as "does
  not want remote".
- **`experience: {}` is a 400**, since `experienceYears` is a required member; `{"experienceYears": null}`
  clears the stated years.
- **A request with no part present is a 400**, by a validator rule modelled on
  `UpdateSavedSearchCommandValidator`'s at-least-one-field rule. Three reasons: no product caller sends an
  empty request; a no-op would still bump `UpdatedAt` (`JobSeeker.UpdateMatchPreferences`), which is an
  UPDATE carrying `xmin` that can force a replay in a concurrent writer;
  and a flat, PUT-shaped body sent to the PATCH binds zero parts, because unknown members are ignored,
  and that must be a 400, never a silent 204.

### D4 — The response is 204, not the stored parts (R4)

- **House parity.** The four `/me` PUT writes and both existing PATCH precedents (company-watch
  criteria, saved searches) answer 204.
- **CQRS (AGENTS.md §2.3).** The read model is `GET /api/v1/me/profile`, and it is parity-pinned. A
  second read format in a write response would need its own guard.
- **The client already knows what it wrote.** Beyond normalisation, the one change the server derives is
  the years pruning of D2, and the web already computes it (`projectOccupationExperience`).
- **A whole document in the response is dangerous.** If the card adopted a whole document out of one
  part's response, it would overwrite another part's optimistic in-flight state. That happens as soon as
  Next stops serialising Server Actions, which Next calls "an implementation detail and may change"
  (`next@16.3.5`, `07-mutating-data.md:207`).

### D5 — The PUT retires in a later PR: expand, migrate, contract, with a measured gate between each (R5, CTO 3)

Matchning lands in three PRs. The PUT is not removed in PR A, and it is not kept for good either: it
retires in PR C. Klas's answer to senior-cto-advisor's question is recorded below; this is the shape it
chose.

1. **PR A, expand (BE, this PR).** The PATCH beside the PUT, and this ADR.
2. **G1.** The `api` container on the box runs the image `-api:sha-<short>` for a commit that contains
   PR A's merge. The image carries no revision label, so the measurement binds the revision through the
   digest. It is a read-only measurement.
3. **PR B, migrate (FE).** The card, the per-part dialogs and the first-time rail write through the
   PATCH; the rail sends four parts and no `experience`, which closes defect (a) (R9). If G1 is not
   measured when PR B is opened, it is opened as a draft and marked ready only once G1 is; `agents-done`
   does not replace the measurement.
4. **G2.** The `web` container on the box runs an image from a commit that contains PR B's merge,
   measured the same way.
5. **PR C, contract (BE).** Opened once G2 is measured. It retires the endpoint,
   `SetMatchPreferences{Command,Handler,Validator}` and their unit tests; moves the setup of seven
   integration tests to the PATCH (`GetCvSectionSuggestionsEndpointTests`,
   `RelatedSurfacingEndToEndTests`, `MatchTagBatchEndpointsTests`, `JobAdMatchDetailEndpointTests`,
   `CriterionMatchingAdCountApiTests`, `CompanyWatchesMatchCountApiTests` and
   `CompanyWatchesMatchCountCrossUserIsolationTests`); removes the parity guard's PUT fact; and deletes
   the sentences that motivate fields by the full-replace PUT (`JobSeekerProfileDto.cs:29-37,42-46,48-52,55-58`,
   `MatchPreferencesContractParityTests.cs:20-26,87-88` and `SetMatchPreferencesCommand.cs:24-26`).

Each gate is recorded with digest, sha and time in the respective PR's body. PR A and PR B say
"Part of #1918"; #1918 is closed by hand when PR C merges, since a squash drops `Closes`.

**Why.** The release workflow's five cells have no fan-in, so the web cell can push while the api cell
fails (`release-images.yml:84-97`), and the reconcile timer can then install that mixed set
(`jobbliggaren-reconcile.timer:10-28`). It breaks both ways. A web that calls the PATCH against an api
that lacks it fails every save in Matchning (405), in the card, the dialogs and the first-time rail,
until the api cell catches up, at least an hour if the cell fails, and nothing closes the window (#1238).
And removing the PUT while an old web still calls it fails the same saves. So neither the addition nor
the removal may land before the other side is live. This is parallel change (Sato 2014): a client
migrates when the new interface is in service, and with web and api rolling out separately, "in service"
means measured on the box.

### D6 — The combining lives on the value object (R6)

`MatchPreferences` gains `WithOccupations` (two overloads: one that keeps the stated years of the groups
still chosen, one that takes the years), `WithSkills`, `WithLocations(regions, municipalities, remote)`,
`WithEmploymentTypes` and `WithExperienceYears(int?)`. Each calls `Create` with the instance's own values
for the other dimensions, so normalisation and every invariant have one source.

The handler only sequences, as in the house idiom of `UpdateCompanyWatchCriterionCommandHandler`: it
reads the job seeker tracked, applies the parts that are present, returns the first `IsFailure` with
nothing set, and otherwise calls `UpdateMatchPreferences(next, clock)` once. The years pruning of D2 is
not in the handler; it follows from the value object's subset invariant, and a merge in the handler
would be an anemic domain (AGENTS.md §2.2). The aggregate gains no new methods: it need not know the
parts, and `UpdateMatchPreferences` stays the one mutator the IL sweep sees.

### D7 — The replay contract per part (R7)

ADR 0146's marker contract, that the handler re-derives everything from what it reads and has no side
effect before the commit, holds here and now carries weight. Until this write, the fresh read never
reached `match_preferences` (F1). The PATCH derives the untouched parts from the read: after
`ClearTracking()`, the replay reads the competing commit and lays only this request's parts over it.

- **Two tabs, two different parts:** both land.
- **The same part:** the last writer wins for that part. That is accepted.
- **Occupations without years:** the years are pruned against the fresh overlay.
- **A replay cannot fail validation the first attempt passed**, because the one cross-field invariant
  lies inside one part (D8).

After the cap of three attempts the write answers 409 (ADR 0146 D2). The handler's ports
(`IAppDbContext`, `ICurrentUser`, `IDateTimeProvider`) are in `JobSeekerWriterReplayGuardTests`'
allowlist, and its sweep picks the handler up because it calls `JobSeeker.UpdateMatchPreferences`. The
write rate is unchanged, so ADR 0146 D6 stands.

### D8 — The part-boundary rule (R6)

The parts are disjoint, and the one cross-field invariant, years within occupations, lies inside one
part, so the order in which parts are applied cannot change the result. **A future invariant that spans
two parts therefore moves the part boundary.** Otherwise a write of one part could fail on a part the
user did not touch. R10 requires the parity guard to pin the partition: every value-object dimension
appears in exactly one part, which is coverage and disjointness.

### D9 — What the set grammar requires of the client (CTO 6.2, F3)

A present part replaces the whole part, so a payload built from a stale view undoes a change that
already succeeded. When removal 1 fails and removal 2 succeeds, the card shows both chips while the
server holds neither; when both fail, the card has lost a chip the server still holds (F3, defect (d)).
PR B builds against these rules, and design-reviewer's Major 6 is the acceptance criterion: after every
outcome, the part shows exactly what the server holds.

1. Each part gets a serial write queue.
2. A write's payload is bound when the write runs, after the part's previous write has an outcome, never
   at the click.
3. A failure restores only its own members, never a snapshot taken at the click.
4. No write relies on Next serialising Server Actions per client: `next@16.3.5` calls that "an
   implementation detail and may change" (`07-mutating-data.md:207`; `server-actions.md:26-32`).
5. Chips are never `disabled` during a write: `disabled` drops keyboard focus (WCAG 2.4.3).

A payload bound at the click brings (d) back in one order or the other, however the payload is computed.
The tests are fail/ok and fail/fail within a part, with ok/fail and ok/ok as controls.

### D10 — Where this is written down, and what it leaves standing (R8)

- **A new ADR, published with `git add -f`.** ADR 0071 through 0199 are local by default (`.gitignore`),
  and 0143–0146 are published the same way.
- **Not an amendment to ADR 0076** (in the local series, not in the public index). A public API
  contract would then be decided where the PR diff and other lanes do not reach (CLAUDE.md §6.5).
- **Not an amendment to ADR 0146.** It owns the mechanism, the xmin token and the replay; this is a
  contract that uses it. ADR 0146 gets a dated pointer at D3 instead: its "Twelve command types" is no
  longer the whole list once `UpdateMatchPreferencesCommand` is a marked writer too. The sentence itself
  is not edited; it was true on its date.
- **ADR 0079 STEG 3 (c), "zero backend merge", still stands.** The skills part is always replaced whole,
  and the only thing the server keeps within a part is D2's years.
- **"Antal års erfarenhet" is a live part**, by Klas's directive of 2026-09-28. That contradicts ADR
  0079's Fork B ("deprecated-in-doc"). ADR 0079 is local, so this ADR is the public record.

### Klas's directive and answer (2026-09-28)

1. *"Matchning ska gå att ändra en del i taget: bara yrken, bara kompetenser, bara orter, bara
   anställningsformer eller bara antal års erfarenhet."* The directive, as quoted in
   `docs/sessions/2026-09-28-1918-matchning-form-brief.md`.
2. Asked by senior-cto-advisor, *"Får Matchning (#1918) landa i tre PR:er i stället för en?"*, Klas
   answered *"(a) Tre steg (Rekommenderat)"* (`AskUserQuestion`, 2026-09-28; recorded the same day as a
   comment on #1918). The alternative, (b), was backend and web in one PR with the old save path removed
   in a PR afterwards, which would have accepted that saving in Matchning may stop working for a while
   after the merge. D5 is the shape of (a).

## Alternatives considered

### The write form (D1)

- **W2, one PATCH with five optional parts — chosen.** The first-time rail saves four parts in one click,
  and here that is one atomic write and one MeWrite token. Parts not edited are untouched, so two tabs
  writing different parts both land, and the rail's defect (a) closes by construction. Against: two tabs
  writing the same part leave the last writer's version of that part (D7), and the PATCH has no caller
  until PR B.
- **W1, one PUT per part** (`/api/v1/me/match-preferences/{part}`). It matches Notiser's
  endpoint-per-control precedent (ADR 0145 D4), but that precedent rests on each control having its own
  purpose and audit event and on no caller writing two controls at once. The rail writes four parts in
  one click: four writes that are not atomic, where a partial failure leaves a half-saved first-time
  setup, and four MeWrite tokens for one click.
- **W0, keep the full PUT with a per-part UI on top.** It leaves F1 and F2 open and is only a stopgap. It
  re-sends the tab's copy of the other parts under a button that names one part (design-reviewer's
  Bindning 7).
- **Element operations** (`remove x`). They close the race inside one part, but give one value object two
  write grammars.
- **JSON Merge Patch.** It works per field, which breaks part atomicity (a region written without its
  municipalities), and it needs untyped binding.
- **If-Match per part.** ADR 0146 struck ETag/If-Match from BUILD.md §6.1 and rejected it among its own
  alternatives.

### Years inside Occupations (D2)

- **(b), optional years — chosen.**
- **(a), always send years, projected by the client.** The card renders no years, so a chip removal in a
  stale tab would rewrite years the user cannot see.
- **`WithOccupations(groups, overlay?)` with `null` meaning "keep".** Two overloads instead, so the
  wire's three states stay in the handler.

### The empty request (D3) and the response (D4)

- **An empty PATCH as a silent no-op.** Rejected for the three reasons in D3.
- **A response that returns the stored parts.** Rejected for the reasons in D4.

### Removing the PUT, and how the PRs are cut (D5)

- **Three PRs behind two measured gates — chosen** (Klas's answer (a)).
- **Backend and web in one PR, then a contract PR** (Klas's option (b)). It gives the mixed-set window
  described in D5 and the largest diff the panel could meet.
- **A web fallback to the PUT on a 405.** Two write grammars in the client and dead code after PR C; the
  measurement gives the same safety without code.
- **Removing the PUT in PR A, or never.** Not in PR A, because an old web would then fail every save.
  Not never either (R5): after PR B the PUT has no product caller, and PR C is what deletes the
  sentences that motivate fields by it.

### Where the combining lives (D6)

- **On the value object — chosen.**
- **In the handler.** A merge there would be an anemic domain (AGENTS.md §2.2).
- **Five new methods on `JobSeeker`.** The aggregate root need not know the parts, and
  `UpdateMatchPreferences` stays the one mutator the IL sweep sees.

### The ADR's home (D10)

- **An amendment to ADR 0076 or to ADR 0146.** Rejected for the reasons in D10.

## Consequences

### Positive

- Two tabs editing two different parts both land (D7).
- A first-time rail save will no longer clear a stated `experienceYears`: it sends four parts and no
  `experience`, so the defect closes by construction once PR B is live (R9).
- A save labelled with one part writes only that part (design-reviewer's Bindning 7).
- Normalisation and every invariant keep one source, `MatchPreferences.Create`, however many parts a
  request carries (D6).
- Neither direction of the mixed set can reach the box before the other side is live: the PATCH is
  measured live (G1) before anything calls it, and the web is measured live (G2) before the PUT goes
  (D5).
- The write adds no processing: the same fields, purpose and storage; the same `RequireAuthorization`,
  with a handler that reaches only the caller's own row (no id in the route, so no IDOR surface); the
  same `MeWritePolicy`. senior-cto-advisor found no security-auditor trigger on that ground (CTO 6), and
  she is brought in through F6's issue.

### Negative / out of scope

- **The same part in two tabs: the last writer wins for that part** (D7). Accepted.
- **Between PR A and PR B the PATCH has no caller**, and four defects that the card and the rail have
  today stay until PR B lands: the rail's omitted `experienceYears` (a), the card's generic error text
  (c), a revert that can show a state the server does not hold (d), and lost focus after Enter or Space
  on a chip's remove button (design-reviewer's Blocker 1). None gets worse (CTO's trade-offs).
- **Three PRs and two deploy measurements** for one change-reason, and more panel runs than one PR would
  have needed (CTO's trade-offs).
- **The client owes the set grammar** (D9): a serial write queue per part, and a payload bound when the
  write runs.
- **A future invariant that spans two parts moves the part boundary** (D8). That is a constraint on the
  shape of the domain, and it is accepted.
- **`PATCH /me/profile` has no rate-limit policy** (F6, #1921). It is outside this decision: the new
  route carries `MeWritePolicy`, and the policy choice for the other route is security-auditor's.

## Implementation

PR A, as delivered:

- **Domain.** `MatchPreferences.WithOccupations` (two overloads), `WithSkills`, `WithLocations`,
  `WithEmploymentTypes` and `WithExperienceYears`; each calls `Create` with the instance's own values for
  the other dimensions. The aggregate gains no method.
- **Application.** `UpdateMatchPreferencesCommand`, `UpdateMatchPreferencesCommandHandler` and
  `UpdateMatchPreferencesCommandValidator`; the part records `OccupationsPartInput`, `SkillsPartInput`,
  `LocationsPartInput`, `EmploymentTypesPartInput` and `ExperiencePartInput`. `OccupationExperienceInput`
  and its validator move to the namespace `Jobbliggaren.Application.JobSeekers.Commands`, shared by both
  writes.
- **Api.** `MapPatch("/match-preferences")` with `RequireAuthorization` and `MeWritePolicy`, answering
  204.
- **Tests** per R10, written by test-writer.
- **Outside this decision.** F6, that `PATCH /me/profile` has no rate-limit policy, is #1921.

PR B and PR C are scoped in D5 and D9.

## References

- `docs/reviews/2026-09-28-1918-form-dotnet-architect.md` (R1–R10, F1–F6),
  `docs/reviews/2026-09-28-1918-form-cto.md` (points 3 and 6, and the question put to Klas),
  `docs/reviews/2026-09-28-1918-form-design-reviewer.md` (Bindning 7, Blocker 1, Major 6),
  `docs/sessions/2026-09-28-1918-matchning-form-brief.md` (the directive as quoted; the W0/W1/W2 table).
- ADR 0146 (D2, D3, D6, and its ETag alternative), ADR 0145 (D4), ADR 0079 (STEG 3 (c); Amendment
  2026-06-23, Fork B), ADR 0076 (F4-12).
- [#1918](https://github.com/klasolsson81/jobbliggaren/issues/1918), #1921, #1238;
  `.github/workflows/release-images.yml:84-97`; `deploy/systemd/jobbliggaren-reconcile.timer:10-28`.
- `next@16.3.5`, under `node_modules/next/dist/docs/01-app/`: `02-guides/server-actions.md:26-32` and
  `01-getting-started/07-mutating-data.md:207`.
- Sato, "ParallelChange" (martinfowler.com/bliki, 2014-05-13, read 2026-09-28); Ford, Parsons & Kua,
  *Building Evolutionary Architectures* (2017), ch. 5 (expand/contract): both as cited by
  senior-cto-advisor.
- WCAG 2.1, 2.4.3.
