# ADR 0002 — Explicit Claude-modell-ID i agent-frontmatter

**Datum:** 2026-04-18
**Status:** Accepted

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

**Degraderingsordning vid usage-limit-problem (Opus → Sonnet 4.6):**

1. `design-reviewer` — lägst kritikalitet bland Opus-agenter
2. `test-writer`
3. `nextjs-ui-engineer`
4. `code-reviewer`, `security-auditor`, `dotnet-architect`, `ai-prompt-engineer`
   behåller Opus — dessa är projektets kvalitetsankare
