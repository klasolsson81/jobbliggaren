# ADR 0069 — Product rename: JobbPilot → Jobbliggaren

**Datum:** 2026-06-13
**Status:** Accepted

---

## Kontext

This ADR is the interpretive bridge for every pre-0069 reference to "JobbPilot" in historical session logs and prior ADRs. Those records remain correct and are not rewritten — they described the product as it was called at the time. ADR 0069 records when and why the name changed.

### Why rename at all

Two independent discovery threads converged to make the name untenable:

**Collision and cliché.** "JobPilot AI" (jobpilotapp.com) is an active direct competitor in 2026 offering AI-driven job-application autofill and tracking — a nearly identical product under a near-identical name. Separately, "Jobpilot" was a major European job board (acquired by Adecco, merged into Monster 2004, a reported €74.5M transaction). Both precedents mean the name carries existing brand baggage in the exact market segment JobbPilot occupies. Additionally, "Job + Pilot/Copilot" is the most clichéd AI-product suffix pattern of the early 2020s and actively fights the civic-utility positioning (1177, Digg, GOV.UK) that is a hard architectural constraint per ADR 0016.

**Trademark and domain availability.** PRV (Svensk varumärkesdatabas) and WIPO Global Brand Database return zero trademark hits for "Jobbliggaren". (EUIPO/TMview was under maintenance; PRV + WIPO used as substitute.) Domains `.se`, `.com`, `.ai`, `.eu`, and `.store` were confirmed available via STRATO. No existing company, app, web presence, or social handle was found under the name.

### Why Jobbliggaren

The Swedish word "liggare" means an official register or ledger — a precise semantic match for the product's function (tracking job applications in an organised record). It encodes the civic-trust thesis directly in the name and is consistent with the product's own table design language (`.jp-table--flat`). The suffix "-liggare" is adjacent to "personalliggare" (Skatteverket's staff register) — judged on-tone and not disqualifying.

Swedish brand precedents of distinctive invented or compound words (Blocket, Tradera, Hemnet, Klarna) confirm that non-descriptive, ownable names perform well in Swedish markets. On five of seven moat dimensions (ownability, trademark strength, positioning coherence, product-metaphor coherence, AI-entity recognisability in ChatGPT/Perplexity/AI Overviews) Jobbliggaren outperforms JobbPilot. JobbPilot scores better only on brevity and international scalability — both irrelevant for a Swedish civic utility. SEO analysis (2026 context: post-2012 EMD update, March-2026 core update) confirms that keyword-in-domain is a minor signal; branded search and AI-entity recognition favour distinctive names.

Full research and alternative analysis are recorded in `docs/reviews/2026-06-13-rename-jobbliggaren-cto.md`.

### Timing

The rename executes pre-launch, localhost-only, with no production environment, no issued auth tokens, and no external consumers. This is the minimum possible blast radius for a namespace rename — it increases monotonically with project age and contributor count. Klas arranged a quiet tree (the only parallel work merges first) before the rename PR runs.

## Beslut

Rename the product from **JobbPilot** to **Jobbliggaren** across all living code, configuration, and spec documentation, executed as a single atomic PR on a quiet tree. Historical session logs and prior ADRs (0001–0068) are not rewritten — they are dated records, correctly interpreted via this ADR.

The execution covers seven sub-decisions (D1–D7), each with a CTO verdict (see `docs/reviews/2026-06-13-rename-jobbliggaren-cto.md`):

- **D1-A:** Full namespace rename `JobbPilot.*` → `Jobbliggaren.*` (all ~836 C# files, `.sln`, `.csproj`, project folders).
- **D2:** Rename physical database name, PostgreSQL roles, and container/volume names to `jobbliggaren*`; rename JWT `Audience` from `jobbpilot-api` to `jobbliggaren-api`. No EF Core migration required (no schema or table carries the brand; DbContexts are already brand-neutral `AppDbContext` / `AppIdentityDbContext`).
- **D3:** Exclude retired AWS Terraform and `deploy-dev.yml` from the rename. Delete them in a separate teardown PR under ADR 0066 / TD-104.
- **D4:** Rename GitHub repo `klasolsson81/jobbpilot` → `…/jobbliggaren` (GitHub 301-redirects the old slug); update in-code `UrlFormat` assembly attributes and README/runbook URLs.
- **D5:** Rename `web/jobbpilot-web` → `web/jobbliggaren-web` and the `package.json` `name` field accordingly.
- **D6:** Update only living/spec docs (`BUILD.md`, `CLAUDE.md`, `DESIGN.md`, `docs/current-work.md`, `docs/steg-tracker.md`, this ADR index, runbooks). Do not rewrite historical session logs or prior ADRs.
- **D7:** Execute D1, D2, D4-code-URLs, D5, and D6-living-docs as a single atomic PR. D3 is explicitly excluded.

## Alternativ som övervägdes

### Alt A — Full rename `JobbPilot.*` → `Jobbliggaren.*` (chosen)
**För:** Preserves Ubiquitous Language (Evans 2003) — code, conversation, and product share one name. Passes the Mastercard-level test (CLAUDE.md §1): a stranger opening the repo post-rename sees a coherent system, not an abandoned migration. Cost is at its absolute minimum pre-launch. Churn (~836 files) is mechanical and uniform, fully verified by `dotnet build` + architecture tests + coverage gates.
**Emot:** Largest diff of the three options. One-time conflict surface for the parallel session. Both bounded by the quiet-tree timing.

### Alt B — Keep `JobbPilot.*` as internal codename, rename only user-facing surface
**För:** Lowest churn. Namespace is not user-facing; no user benefit from renaming it.
**Emot:** Institutionalises a permanent translation layer between product name and codebase — Evans's "Ubiquitous Language" treats this gap as a defect, not a convenience. A decoupled codename is a legitimate pattern only when it is *chosen as an asset* (a neutral, deliberate internal identifier). Keeping the dead brand name as the codename is not a deliberate choice — it is the fossil of the name being actively buried. Fails the Mastercard test: an outside reviewer reads it as an unfinished migration. Defers the rename to a moment when the blast radius is strictly larger and the chance of it being declared permanent is high. **Explicitly rejected.**

### Alt C — A short neutral codename (e.g. "Pilot", "JP")
**För:** Avoids the dead-brand-as-fossil problem of Alt B while keeping namespace churn low.
**Emot:** Introduces a *third* name into the system (product name, domain name, internal codename) — strictly worse cohesion than either A or B. Fails Ubiquitous Language for the same reason as B, plus the extra cognitive layer. **Explicitly rejected.**

### Alt D — Incremental, multi-PR rename (rename in batches)
**För:** Smaller individual diffs per PR.
**Emot:** Every intermediate state has namespaces saying one thing and CI/config another — a broken build can merge during any batch. Multiplies rebase events for the parallel session. `build.yml`'s coverage-gate identifiers, `JobbPilot.sln` references, and `web/jobbpilot-web` paths must change in lockstep with namespaces and the frontend directory — splitting them means at least one PR has a red `ci` by construction. Ford/Parsons/Kua (2017) reserve incremental migration for changes that can be safely partial under live traffic; this is the precise opposite case. **Explicitly rejected in favour of D7 atomic PR.**

## Konsekvenser

### Positiva
- Full Ubiquitous Language coherence: the name in the product, the code, the spec docs, and the database is one word.
- Eliminates collision risk with "JobPilot AI" (active direct competitor) and Jobpilot/Adecco brand residue.
- Clean trademark and domain position: zero PRV/WIPO hits, `.se/.com/.ai/.eu/.store` available.
- "Jobbliggaren" encodes the civic-trust thesis in the name itself — on-brand with the product's design language and Swedish administrative vocabulary.
- JWT `Audience` renamed at the only safe (pre-launch) time; any post-launch rename would require a rollover window.
- ADR-0036 IaC gate does not fire for the rename PR (D3 excludes IaC), keeping the rename PR's review scope clean.

### Negativa
- One large, mechanical diff (~836 C# files + frontend + config + CI). Mitigated: the change is uniform and fully verified by `dotnet build` + architecture tests + coverage gates; reviewers verify the pattern, not 836 individual lines.
- Historical docs (session logs, ADRs 0001–0068) permanently say "JobbPilot". Correct and intended — they are dated records.
- `web/jobbpilot-web` directory rename: git sees a move. Pre-launch, this is not a material loss of history.
- The ADR-0036 mandatory `dotnet-architect` gate **moves to** the separate AWS-teardown PR (D3). That PR must not proceed without it.
- `deploy-dev.yml` remains an armed `v*-dev` trigger pointing at retired AWS infrastructure (ADR 0066) until the teardown PR runs. Pre-existing debt, not introduced by this rename; first task of the teardown PR is to disable/delete that workflow.
- `jobbpilot.se` must 301-redirect to `jobbliggaren.se` at deploy time (legacy domain owned, handled operationally).

### Mitigering
- Quiet-tree timing (the only in-flight parallel session merges first) bounds the conflict surface.
- GitHub 301-redirects the old repo slug — existing bookmarks and references keep resolving during the transition.
- ADR 0069 serves as the interpretive bridge: every historical "JobbPilot" reference in pre-0069 documents is correctly understood as pre-rename.
- The wordmark "JobbPilot" → "Jobbliggaren" is in scope for the rename PR. The logo *mark* redesign (compass icon) is a separate downstream task; ADR 0068's note "logotypen byter inte färg/kompassen förblir navy+guldprick" must be revisited when that work begins.

## Implementation

The rename executes as one atomic PR on a quiet tree, queued behind the parallel session's in-flight "nästa steg" PR.

**Scope of the PR (D1–D7 composite):**
1. Rename project folders, `.sln`, all `.csproj` files (`JobbPilot.*` → `Jobbliggaren.*`).
2. Update all C# `namespace` declarations and `using` directives (~836 files).
3. Rename `docker-compose.yml` DB name, PostgreSQL roles, container and volume names.
4. Update `appsettings.Development.json` (connection strings, JWT `Audience`), `appsettings.Production.json` (Worker), `JobbPilot.Migrate/Program.cs` provisioning comments.
5. Rename `web/jobbpilot-web` → `web/jobbliggaren-web`; update `package.json` `name`.
6. Update `build.yml` (`.sln` reference, per-assembly coverage-gate identifiers, `working-directory` + `cache-dependency-path` frontend paths).
7. Update `UrlFormat` assembly attributes and README/runbook URLs to `…/jobbliggaren/…`.
8. Update living spec docs: `BUILD.md`, `CLAUDE.md`, `DESIGN.md`, `docs/current-work.md`, `docs/steg-tracker.md`, `docs/decisions/README.md`.
9. Write this ADR.

**Excluded from this PR:**
- AWS Terraform (`environments/dev/`, `environments/prod/`) — deferred to AWS-teardown PR under ADR 0066 / TD-104.
- `deploy-dev.yml` — first task of the AWS-teardown PR (disable/delete the workflow).
- Historical session logs (`docs/sessions/`) and prior ADRs (0001–0068).

**Gates for the rename PR:**
| Gate | Status |
|---|---|
| `code-reviewer` + `dotnet-architect` | Required (>5 files, structural change — CLAUDE.md §9.2) |
| `dotnet-architect` mandatory-for-IaC (ADR 0036) | Not fired here — moves to AWS-teardown PR |
| `db-migration-writer` | Not triggered — no EF schema change |
| `security-auditor` | Light/optional — diff grazes JWT Audience + DB roles; pre-launch, no issued tokens; not a hard gate |
| approval-hook (BUILD/CLAUDE/DESIGN edits) | Required — D6 edits all three spec files |

## Semantisk notering

"-liggare" is adjacent to "personalliggare" (Skatteverket's mandatory staff attendance register, used in industries such as construction, restaurants, and hairdressing). This adjacency is judged to be on-tone — it reinforces the civic-utility register — and not disqualifying. The word does not carry negative connotations in Swedish administrative or professional vocabulary.

## Referenser

- `docs/reviews/2026-06-13-rename-jobbliggaren-cto.md` — CTO verdict covering D1–D7 in full, with discovery corrections and gate confirmation
- Eric Evans, *Domain-Driven Design* (Addison-Wesley, 2003) — "Ubiquitous Language" (D1 rationale)
- Winters/Manshreck/Wright, *Software Engineering at Google* (O'Reilly, 2020) — living docs vs dated records (D6 rationale)
- Ford/Parsons/Kua, *Building Evolutionary Architectures* (O'Reilly, 2017) — incremental-vs-atomic migration criteria (D7 rationale)
- jobpilotapp.com — active direct competitor "JobPilot AI" (2026)
- adeccogroup.com — Jobpilot/Monster acquisition history (2004, €74.5M)
- PRV (Svensk varumärkesdatabas) + WIPO Global Brand Database — zero hits for "Jobbliggaren"
- Skatteverket — "personalliggare" (semantic note)
- ADR 0016 — Civic design language as architectural requirement
- ADR 0036 — dotnet-architect mandatory for IaC (gate moves to teardown PR)
- ADR 0051 — Bedrock retired / Anthropic Direct API
- ADR 0065 — PR flow with CI gate
- ADR 0066 — AWS dev-stack teardown (D3 deferred work lives here)
- ADR 0068 — Green accent identity + logo mark (wordmark in scope; mark redesign separate)
