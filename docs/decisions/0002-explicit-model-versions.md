# ADR 0002 — Explicit Claude-modell-ID i agent-frontmatter

**Datum:** 2026-04-18
**Status:** Accepted (agent-frontmatter-scope superseded by Amendment 2026-06-12 — aliases)

## Kontext

JobbPilot:s `.claude/agents/`-agenter konfigureras med ett `model`-fält i
YAML-frontmatter. Claude Code stödjer kortforms-alias som `"opus"` och
`"sonnet"`, men dessa mappas internt till den senaste versionen i respektive
familj vid exekvering — ett icke-deterministiskt beteende. Samma `opus`-alias
kan peka på `claude-opus-4-5` idag och `claude-opus-5-0` om sex månader, med
möjliga regressioner i prompt-beteende.

Klas kör **Claude MAX 5x-plan** (flat-rate, ingen per-token-kostnad).
Modellval drivs av **latency** och **usage-limits**, inte kostnad.
Opus 4.7 är djupare analytisk; Sonnet 4.6 är snabbare och tillräcklig för
pattern-matching-tunga uppgifter som triggas ofta.

## Beslut

1. Alla agenter specificerar **full explicit modell-ID** i frontmatter — aldrig alias.
2. Modellvalet per agent grundas på uppgiftens karaktär, inte kostnad.
3. Denna ADR är auktoritativ; avvikelse kräver uppdatering här.

**Förbjuden syntax:**

```yaml
model: opus    # förbjudet — icke-deterministiskt
model: sonnet  # förbjudet
```

**Korrekt syntax:**

```yaml
model: claude-opus-4-7
model: claude-sonnet-4-6
```

## Agentmappning (final, 2026-04-18)

| Agent | Modell | Kategori | Motivering |
|-------|--------|----------|------------|
| `code-reviewer` | `claude-opus-4-7` | Kvalitetskritisk | Identifierar subtila anti-patterns och DDD-brott; kräver djup analys |
| `security-auditor` | `claude-opus-4-7` | Säkerhetskritisk | BYOK-kryptering, GDPR-PII, OAuth — hög insats, inga missar |
| `dotnet-architect` | `claude-opus-4-7` | Komplexa beslut | Aggregatgränser, DDD-invarianter, Clean Arch-lager |
| `nextjs-ui-engineer` | `claude-opus-4-7` | Komplexa beslut | Ny stack (Next.js 16 + Tailwind 4.2), civic-utility-design, BE→FE-typning |
| `ai-prompt-engineer` | `claude-opus-4-7` | Komplexa beslut | Prompt-kvalitet styr EU-Bedrock-inferensflöden direkt |
| `test-writer` | `claude-opus-4-7` | Komplexa beslut | TDD kräver domän-invariant-förståelse, inte bara test-syntax |
| `design-reviewer` | `claude-opus-4-7` | Kvalitetskritisk | Civic-utility-ton och DESIGN.md-nyanser kräver omdöme |
| `test-runner` | `claude-sonnet-4-6` | Latency-känslig | Triggas ofta i CI; tolkar testoutput — strukturerad pattern-matching |
| `db-migration-writer` | `claude-sonnet-4-6` | Latency-känslig | EF Core migration-syntax är väldefinierad; Sonnet räcker |
| `docs-keeper` | `claude-sonnet-4-6` | Latency-känslig | Uppdaterar loggar, entity-maps — strukturerat, latency-prioriterat |
| `adr-keeper` | `claude-sonnet-4-6` | Latency-känslig | Redigerar ADR-stubs; inga komplexa beslut |

**Totalt:** 7 × Opus 4.7 + 4 × Sonnet 4.6 = 11 agenter.

## Agent-roller: rådgivare vs scaffolder

JobbPilots agent-arkitektur följer "advisor + implementer"-mönstret.
Arkitektur- och review-agenter är **read-only rådgivare**
(`Read`, `Grep`, `Glob`, `WebSearch`, `WebFetch` — ingen `Edit`/`Write`/`Bash`).
Detta gör dem till säkra kritiker utan förmåga att själva modifiera kod.
Implementation sker via:

- Klas direkt i editorn (vanligast under inlärningsfasen)
- Default Claude Code-capabilities utan namngiven agent
- Specialiserade skrivande agenter där det är motiverat
  (`db-migration-writer`, `test-writer` är undantag — de är scaffolders med
  `Write`-access)

Denna separation:

- Förhindrar cirkulära review-loopar (agenten kan inte råka "fixa" det den
  precis flaggade)
- Gör att Klas lär sig .NET-idiom genom egen kod-skrivning
- Isolerar feedback från implementation — tydligare ansvarsgränser

Avviker medvetet från SESSION-2-PLAN.md §1.3.3 som specificerade
`dotnet-architect` som scaffolder med `Write`/`Edit`-access.

## Avvikelse från SESSION-2-PLAN.md §1.1

SESSION-2-PLAN.md specificerade ursprungligen 9 agenter. Diff mot final mappning:

| Agent | Plan | Final | Förändring |
|-------|------|-------|------------|
| `code-reviewer` | alias `opus` | `claude-opus-4-7` | Explicit ID |
| `security-auditor` | alias `opus` | `claude-opus-4-7` | Explicit ID |
| `dotnet-architect` | `sonnet` | **`claude-opus-4-7`** | Uppgraderad |
| `nextjs-ui-engineer` | `sonnet` | **`claude-opus-4-7`** | Uppgraderad |
| `test-writer` | `sonnet` | **`claude-opus-4-7`** | Uppgraderad |
| `db-migration-writer` | `sonnet` | `claude-sonnet-4-6` | Explicit ID |
| `ai-prompt-engineer` | `sonnet` | **`claude-opus-4-7`** | Uppgraderad |
| `docs-keeper` | `sonnet` | `claude-sonnet-4-6` | Explicit ID |
| `design-reviewer` | `sonnet` | **`claude-opus-4-7`** | Uppgraderad |
| `test-runner` | *(ej i plan)* | `claude-sonnet-4-6` | Ny agent |
| `adr-keeper` | *(ej i plan)* | `claude-sonnet-4-6` | Ny agent |

Uppgraderingarna (5 agenter) och de 2 nya agenterna godkändes 2026-04-18 av Klas
(STEG 5.1, session 3). Motivering: MAX 5x-plan eliminerar per-token-kostnad som
begränsande faktor; usage-limits hanteras via degraderingsordning nedan.

## Konsekvenser

**Positiva:**
- Deterministiskt beteende — exakt modell-version i varje agent-fil
- Enkelt att audita vilka agenter som kör Opus vs Sonnet
- Uppgraderingar är explicita, inte tysta alias-ändringar

**Negativa/risker:**
- Modell-ID:n måste uppdateras manuellt när nya versioner görs relevanta
- Ingen automatisk uppgradering vid familj-releasar

**Degraderingsordning vid usage-limit-problem (Opus → Sonnet):**

1. `design-reviewer` — lägst kritikalitet bland Opus-agenter
2. `test-writer`
3. `nextjs-ui-engineer`
4. `code-reviewer`, `security-auditor`, `dotnet-architect`, `ai-prompt-engineer`
   behåller Opus — dessa är projektets kvalitetsankare

---

## Amendment 2026-06-07 — explicit ID i config vs tier-referens i prosa

**Status:** Accepted (amendment till ADR 0002)
**Beslutsfattare:** Klas Olsson (GO 2026-06-07); senior-cto-advisor decision-maker
`a86e76f7f560689ac` (riktning A); kontext: AWS-cleanup-städsession, PR
`chore/aws-cleanup-refit-cert`.

> **Livscykel-not:** Skriven 2026-06-07 av Claude Code på explicit Klas-GO ("GO
> enligt rek") under städsessionen, grundad verbatim i senior-cto-advisor-domarna
> `abc7a9aeb0d711cea` + `a86e76f7f560689ac` — inga nya beslut konstruerade
> (medveten override av §9.4 webb-Claude-verbatim per memory
> `feedback_klas_can_override_adr_verbatim_source`).

### Bakgrund

Klas observerade 2026-06-06 att modell-versioner hårdkodas i docs/spec (t.ex.
`opus-4-7`) och ruttnar när nya modeller släpps (Opus 4.8 var live medan specen
sa 4.7). Frågan blottlade två separata sanningar som behöver olika behandling.

### Beslut

**1. Separation per dokumenttyp (DRY — ett auktoritativt hem per modell-ID,
Hunt/Thomas 1999):**

- **Operativ konfiguration** — agent-frontmatter `model:`-fält, `appsettings`
  `Ai:Anthropic:Models`, och prompt-fil-frontmatter (`prompts/*.prompt.md`):
  **explicit pinned modell-ID** (ADR 0002:s ursprungliga kärnbeslut — determinism,
  ingen tyst alias-drift). ADR 0002:s "Förbjuden/Korrekt syntax"-block scopas
  hädanefter explicit till denna kategori.
- **Illustrativ prosa** — BUILD.md §8.2-tabeller, README-tabeller, agent-fil-
  förklaringar: **tier-referens (Fast/Deep/Premium = Haiku/Sonnet/Opus) + EN
  pekare** till config/`https://docs.claude.com`. Versionssträngar upprepas inte
  i prosa (de saknar runtime-effekt och ruttnar).

**2. On-disk-drift erkänd (öppen punkt, egen touch):** senior-cto-advisor-
verifiering 2026-06-07 visade att agent-filerna i `.claude/agents/` **saknar
`model:`-fält** on-disk — ADR 0002:s mappning (11 agenter, 7×Opus + 4×Sonnet)
beskriver ett avsett läge som aldrig applicerades (eller strippades). Dessutom
finns 13 agenter on-disk (`senior-cto-advisor` + `perf-test-writer` tillkom efter
2026-04-18). **Återställning av explicit `model:`-fält på alla 13 agenter +
ev. Opus 4.7→4.8-bump är en egen medveten touch/PR** (SoC: modell-strategi ≠
AWS-städning; modell-byte ändrar agent-beteende → förtjänar isolerad, observerbar
diff — Martin 2017 kap. 7/13, Winters et al. 2020 kap. 9). EJ i denna städ-PR.

**3. Senaste-modell-status (web-verifierat 2026-06-07, §9.5):** `claude-sonnet-4-6`
är fortfarande senaste Sonnet; Opus 4.8 (`claude-opus-4-8`) är live. Premium-tier
pinnas mot Opus 4.8 i config-exempel.

### Konsekvens

- Docs slutar ruttna — tier-strategin (det stabila beslutet) står i prosa, exakt
  ID i config (källa).
- Determinism bevarad där den räknas (operativ config), per ADR 0002-kärnan.
- Ingen tyst motsägelse mellan ADR 0002 och BUILD.md §8.2 (Ford/Parsons/Kua 2017
  — granskningstrail).

---

## Amendment 2026-06-12 — Model aliases in agent frontmatter (supersedes the explicit-ID rule for `.claude/agents/`)

**Status:** Accepted (amendment to ADR 0002)
**Decision-maker:** Klas Olsson (approved plan 2026-06-12, CC cold review of the
Claude Code setup). Written in English per the same session's language decision
(new docs in English going forward).

> **Lifecycle note:** Authored by Claude Code on explicit Klas GO (approved plan
> = instruction trail), following the precedent of Amendment 2026-06-07
> (memory `feedback_klas_can_override_adr_verbatim_source`).

### Background

The 2026-06-07 amendment acknowledged on-disk drift and deferred the frontmatter
restoration to "a deliberate separate touch" — this is that touch. Verification
2026-06-12 sharpened the picture: **0 of 13** agent files carried a `model:`
field (the previously reported `ai-prompt-engineer` hit at line 91 is a prompt
*template example* in the body, not frontmatter). All agents therefore ran with
the implicit default `inherit` — the main conversation's model — which is the
most expensive possible configuration and was never a deliberate choice.

Two facts changed the calculus since 2026-04-18:

1. **Official semantics verified** (code.claude.com/docs/en/sub-agents,
   2026-06-12): `model:` accepts the aliases `sonnet` / `opus` / `haiku` /
   `fable`, full IDs, or `inherit`; omitted = `inherit`. Aliases track the
   latest model in each family, maintained by Anthropic.
2. **The original "non-determinism" concern proved more costly than the risk.**
   Two years of explicit IDs produced exactly the rot the 2026-06-07 amendment
   documented (spec said 4.7 while 4.8 was live) and zero observed
   alias-regression incidents. The maintenance burden is real; the protected
   risk was hypothetical.

### Decision

1. **Agent frontmatter (`.claude/agents/*.md`) uses tier aliases** — `opus`,
   `sonnet`, `haiku` — never full IDs and never omitted. This supersedes the
   original "Förbjuden syntax"-block for this category only.
2. **Runtime AI configuration is unchanged** (`appsettings`
   `Ai:Anthropic:Models`, `prompts/*.prompt.md` frontmatter): still explicit
   pinned IDs per Amendment 2026-06-07 — production inference behavior must not
   drift silently. The two categories have different blast radii: a silently
   upgraded review agent is observable and reversible in one frontmatter edit;
   a silently upgraded production prompt changes user-facing output.
3. **Tier mapping (13 agents, applied on-disk in this PR):**

   | Tier | Agents | Rationale |
   |------|--------|-----------|
   | `opus` | senior-cto-advisor, dotnet-architect, security-auditor, code-reviewer, design-reviewer, ai-prompt-engineer, nextjs-ui-engineer, test-writer | Decisions, vetoes, quality gates, code production — quality over cost |
   | `sonnet` | db-migration-writer, adr-keeper, perf-test-writer | Structured, well-templated work |
   | `haiku` | test-runner, docs-keeper | Mechanical: run/parse tests, sync doc indexes. 1/5 of Opus price |

4. **Subagent model choice never affects the main loop.** A subagent's `model:`
   applies only to its own isolated context window; no "switch back" exists or
   is needed. (Klas concern raised 2026-06-12, resolved by design.)

### Consequences

- Zero maintenance on model releases — aliases advance automatically.
- Cost control: high-frequency mechanical agents (test-runner, docs-keeper)
  drop from inherit (Opus-tier or above) to Haiku.
- Rollback path: any perceived quality regression on a sonnet/haiku agent is a
  one-line frontmatter edit.
- The degradation ladder in the original ADR (Opus → Sonnet under usage limits)
  remains valid, now expressed in aliases.
