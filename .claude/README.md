# .claude/

Konfiguration för Claude Code i detta repo.

Klas kör Auto mode. Se [docs/runbooks/claude-code-setup.md](../docs/runbooks/claude-code-setup.md) för hur klassificeraren interagerar med listorna.

## Filer

| Fil | Syfte |
|---|---|
| `settings.json` | Team-delad konfiguration (committad) |
| `settings.local.json` | Personliga overrides (gitignored, kopiera från `.example`) |
| `settings.local.json.example` | Mall för personlig konfiguration |

## Kataloger (gitignored)

| Katalog | Innehåll |
|---|---|
| `auto-memory/` | Automatisk minne mellan sessioner |
| `logs/` | Session-loggar från hooks |
| `backups/` | Pre-compact snapshots |
| `tasks/` | Aktiva uppgiftslistor |

## Agents

Specialiserade sub-agenter som Claude Code delegerar till. Filerna finns i `agents/`. 13 agenter totalt: 8 × opus, 3 × sonnet, 2 × haiku. Modellfälten är **tier-alias** (`opus`/`sonnet`/`haiku`) per ADR 0002 Amendment 2026-06-12 — aliaset följer alltid senaste modellen i familjen; exakta versioner pinnas aldrig i agent-frontmatter.

| Agent | Tier | Roll |
|---|---|---|
| `senior-cto-advisor` | opus | Decision-maker vid multi-approach-val, fynd-triage och TD-skapande |
| `dotnet-architect` | opus | Clean Architecture-väktare och DDD-rådgivare — read-only, analyserar och föreslår |
| `test-writer` | opus | Skriver unit- och integrationstester (xUnit v3, Shouldly, NSubstitute, Testcontainers) — TDD-first |
| `nextjs-ui-engineer` | opus | Next.js 16 App Router, shadcn/ui, Tailwind 4.2 — enforcar civic-utility-estetiken |
| `ai-prompt-engineer` | opus | Prompt-design och versionering, Anthropic Direct API (Bedrock utgår, ADR 0051), `/prompts/`-biblioteket |
| `design-reviewer` | opus | DESIGN.md-enforcer med veto-makt — blockerar PRs som bryter civic-utility-estetiken |
| `code-reviewer` | opus | CLAUDE.md-enforcer med veto-makt — sista kvalitetsgranskning innan merge |
| `security-auditor` | opus | GDPR-väktare, granskar PII, secrets, auth och tredjelandsöverföringar (AI) — inga MVP-undantag |
| `db-migration-writer` | sonnet | EF Core 10-migrationer mot PostgreSQL 18.3, GDPR-kompatibla scheman |
| `adr-keeper` | sonnet | Författar ADRs, hanterar status-livscykeln (Proposed → Accepted → Superseded) |
| `perf-test-writer` | sonnet | NBomber-loadtester + Lighthouse-CI mot ADR 0045-budgetar — builder, inte reviewer |
| `test-runner` | haiku | Kör `dotnet test`, tolkar xUnit-output, rapporterar status — delegerar aldrig uppåt |
| `docs-keeper` | haiku | Håller dokumentation synkad med verkligheten — skriver inte ny spec |

Lägg till ny agent: skapa `agents/<name>.md` med YAML frontmatter (`name`, `model` som tier-alias, `description`) och instruktionstext.

## Skills

Kunskapspaket som laddas vid behov. Filerna finns i `skills/<name>/SKILL.md`. Alla fem skills täcker designsystemet.

| Skill | Innehåll | Referensfiler |
|---|---|---|
| `jobbpilot-design-principles` | Civic-utility-filosofi, do/don't, designriktning | — |
| `jobbpilot-design-tokens` | Färger, typografi, spacing, radius, dark mode | 4 filer |
| `jobbpilot-design-components` | Button, Card, Table, Dialog, Form m.fl. — varianter och kompositionsmönster | 2 filer |
| `jobbpilot-design-copy` | Svensk copy, microcopy-bibliotek, felkoder, locale-formatering | 3 filer |
| `jobbpilot-design-a11y` | WCAG 2.1 AA-krav, screen reader-testning, testverktyg | 3 filer |

Lägg till ny skill: skapa `skills/<name>/SKILL.md` och eventuella referensfiler i `skills/<name>/references/`.
