# ADR 0154 — One box, one domain: every merged release goes live on jobbliggaren.se

**Date:** 2026-10-05
**Status:** Accepted
**Amended:** 2026-10-05 (#1960, the read rule) — see the Amendment at the end.
**Deciders:** Klas Olsson (decisions (1)–(4) below, 2026-10-05, in #1961's planning session) ·
`senior-cto-advisor` (the routing: PR order, the scope of the override, the reading of A2;
`docs/reviews/2026-10-05-1961-plan-cto.md`, local) · `dotnet-architect`
**Related:** [#1960](https://github.com/klasolsson81/jobbliggaren/issues/1960) (the epic, re-scoped by this
ADR) · [#1961](https://github.com/klasolsson81/jobbliggaren/issues/1961) (re-scoped to hardening the
unattended delivery) · [#1768](https://github.com/klasolsson81/jobbliggaren/issues/1768) (the domain move) ·
[#734](https://github.com/klasolsson81/jobbliggaren/issues/734) (registration) ·
[#1901](https://github.com/klasolsson81/jobbliggaren/issues/1901) (the deploy specification; closed by this
ADR's PR) · #1238 (A2's home) · #197 (backup) · ADR 0149 (the release record) · ADR 0050 (the deploy stack,
§4 K1–K4) · ADR 0122 (host sizing) · ADR 0143 (Redis boundaries) · ADR 0133 (lapse triggers)
**Measured against:** `main` at `6566dceed`; the box (`jp-vps`), read-only, 2026-10-05.

---

## Context

Epic #1960 asked for two environments: `dev.jobbliggaren.se` receiving every merge, and
`jobbliggaren.se` receiving a tested release only on Klas's GO. #1238 (ADR 0149) delivered the first half:
the box applies the record its `dev`
channel names. #1961 was to build the second half.

Measured on the box on 2026-10-05:

- one stack's memory caps add up to 6,560 MiB of 7,945 MiB;
- about 1.4 GiB of what it uses cannot be reclaimed;
- a second stack with the same caps would bring the total to 165 % of RAM.

The planning panel ruled that production belongs on its own freshly installed host.

Klas then decided against a second host.

## Decision

### 1. Klas's decisions, verbatim

- **(1) Placement:** "Jag har just nu ingen budget att köra 2 lådor eller uppgradera. Därför kommer vi flytta
  allt till jobbliggaren.se Dvs, en server, en domän. Alla mergade PR går direkt live vid image-byggnad etc på
  jobbliggaren.se Ev kommer jag i framtiden, skaffa en låda till, för dev, men inte just nu."
- **(2) A2 on that box:** "Det gäller direkt och tillsvidare. Dvs jag vill så snart som möjligt ta in riktiga
  användare, utan en extra server. Och såklart på domänen jobbliggaren.se då. Säkerhetsagent kan inte
  blockera mitt beslut."
- **(3) Basic auth:** "Ta bort vid domänflytten".
- **(4) Identity migrations:** "Stående GO täcker §3c".

### 2. Topology

The one box (Netcup RS 1000 G12) is the production environment. #1768 moves it to the apex:

| Name | Behaviour |
|---|---|
| `jobbliggaren.se` | Serves the app. ADR 0050 Option B is unchanged: Caddy → `web:3000`. |
| `www.jobbliggaren.se` | 308 to the apex. |
| `dev.jobbliggaren.se` | 302 to the apex, with the path and query kept. It is temporary on purpose, so that a future dev box can take the name back. |

- **IPv4 only.** There is no AAAA record until the edge keeps IPv6 client addresses: the Docker networks have IPv6 disabled (measured 2026-10-05).
- **Everything moves with the box:** the database, the Data Protection keyring, the master key and the accounts.
- **The domain move is the public moment** (3). Caddy's whole-site basic auth (ADR 0050 §4 K2) comes off at the move, while registration stays closed until #734.
- **Names stay.** The compose project keeps the name `jobbliggaren-prod`, and the release channel keeps the name `dev`.

### 3. Delivery

Every merged PR is a production deploy. Its release record reaches the box (ADR
0149), and **the merge is the approval**. No second publisher exists, and no promotion step exists.

### 4. A2 for the one box

A2's origin, Klas on #1238, comment 5982420396, 2026-10-04:

> A2 för dev, med fortsatt stopp vid inkompatibilitet. Ansvarig session får stående GO att uppdatera dev
> enligt runbooken och verifiera resultatet. Mandatet omfattar inte produktion, ändringar av gemensamma
> resurser som påverkar produktion eller åtgärder som kräver särskilt GO. Produktion ska köra en uttryckligen
> godkänd release.

(2) supersedes two of these limits:
- "Mandatet omfattar inte produktion, ändringar av gemensamma resurser som påverkar produktion";
- "Produktion ska köra en uttryckligen godkänd release", by (1).

Two parts stand: "med fortsatt stopp vid inkompatibilitet" and "åtgärder som kräver särskilt GO".

**The reading, by its criterion.** A2 covers what a merged, recorded release contains. The session whose
merge needs it may:
- advance the box's checkout to the release commit the unit names, including a release that only changes scripts;
- refresh, through the runbook's install block, any unit file that release changes;
- run §3c's `bootstrap` for the Identity migrations that release adds (4).

**Outside A2, each needing Klas's GO:**
- one-off writes on the box;
- `deploy/.env`;
- release pins;
- manual compose commands outside the runbook;
- DNS and provider consoles;
- GHCR and repository settings;
- an escrow sitting.

**No advance passes a commit that changes a Redis ACL template** (`deploy/redis/*.acl.template`) before
Klas has re-published the ACL from escrow. Past that commit, every reconcile refuses until he has.

### 5. The override

`security-auditor`'s placement finding, as she titled it:

> Samma värd + A2 = riktiga användares PII bakom en agentnåbar root utan lösenord

**The finding stands, unsigned. Its remedy, production on its own freshly installed host, is withdrawn by
Klas (2).**
- The override covers this one finding on the PRs that implement this ADR. Their verdict tables carry it as
  overridden, with no `blocked` label and no "GO merga" per PR.
- It covers no other finding; each routes by CLAUDE.md §9.6 as before.

Her conditions for a separate host go as follows:

| Condition | What happens to it |
|---|---|
| C1 (no agent credentials to production), C2 (fresh host), C4 (fresh key material), C5 (pin required) | Withdrawn with the remedy. |
| C3 (production backups apart from dev's) | Becomes M-4 as it stands (#197). |
| C6 (M-5a read on Caddy's own responses; the Strato zone read before the apex sends `includeSubDomains`) | Goes to #1768. |
| C7 (production refuses the dev tools) | Goes to the dev-tools removal before registration opens. |
| C8 (image retention before production) | Goes to the disk-retention fix. |

### 6. The site URL

`NEXT_PUBLIC_SITE_URL` stays a build input. At the domain move Klas sets the repository variable to
`https://jobbliggaren.se`. ADR 0149 R9 (an environment-neutral web artifact) has nothing to act on without a
promotion, so it is **deferred to a second box: not in scope, not verified.**

### 7. Deferred, as scheduling

A second box and #1961's two-environment design are deferred until Klas buys a second box:
- promotion by record digest;
- per-environment `:applied` tags, receipt, lock and pin, or compose digest references (C2-D);
- R9.

#1961's history keeps the design.

## Consequences

**Positive.**
- The delivered release path becomes the production path unchanged.
- No hosting cost.

**Accepted costs.**
- Every merge is a production deploy, and the merge is its only approval.
- caddy is recreated on every release.
- These freeze delivery, and an auto-merge landing wakes no session:
  - a merge that changes compose;
  - a release pin.
- The `dev` channel and the `jobbliggaren-prod` project name both describe the wrong thing.
- There is no environment for trying a release before production. CI is the only rehearsal.

## Alternatives considered

- **Two stacks on today's 8 GB.** Rejected:
  - the caps reach 165 % of RAM;
  - every single-environment constant collides (container names, `:applied`, ports 80/443, bridge names);
  - a shared edge;
  - the security Blocker.
- **Production on a second RS 1000 G12.5.** The panel's choice: 4 dedicated cores, 8 GB and 128 GB, at
  €21.73 a month on a 12-month term incl. German VAT (netcup.com, read 2026-10-05). Declined by Klas (1) for
  budget.
- **Upgrade the box to RS 2000 G12** (16 GB, €18 a month excl. VAT for the whole plan, irreversible).
  Declined by Klas (1). It also left the shared root as it was.
- **An environment-neutral web image first.** Deferred (§6): there is no promotion for it to serve.

## Amendment 2026-10-05 — reading the box (#1960)

**Findings:** `security-auditor`, #1961's planning, 2026-10-05 —
`docs/reviews/2026-10-05-1961-plan-security-auditor.md`, round 1, Del 1, finding 1, whose title §5
quotes.

**Grounds.** Art. 5(1)(f) and 32(1)(b): root without a password, which every agent process on the
workstation can take, guards personal data about people other than Klas. A conditional arm —
Art. 13(1)(e) and 14(1)(e), 28, 30(1)(d) and Chapter V — becomes a breach the moment an agent reads
such data, because its transcript then leaves the box for a model provider that neither the
processing register nor the privacy policy names (measured 2026-10-05: the finding above, its
Motivering (a); `docs/spec-rationale.md` §9.2). That arm is why CLAUDE.md §9.2 carries the read rule.

**Boundary.** The override in §5 covers access, never disclosure (CLAUDE.md §9.2). What any
processing of user data by an agent would first need is that rule's leg (f).

**The controller's decision.** `senior-cto-advisor` asked: "Är beslut (2) ditt beslut som
personuppgiftsansvarig enligt Art. 24(1), utanför §9.6:s tre vägar, med ADR 0154 §5 som enda hem?"
Klas answered on 2026-10-05: "Ja, mitt beslut enligt Art. 24(1)"
([#1960](https://github.com/klasolsson81/jobbliggaren/issues/1960#issuecomment-5994169157)).
