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

Specialiserade sub-agenter som Claude Code delegerar till. Filerna finns i `agents/`. 11 agenter totalt: 7 × claude-opus-4-7, 4 × claude-sonnet-4-6.

| Agent | Modell | Roll |
|---|---|---|
| `dotnet-architect` | claude-opus-4-7 | Clean Architecture-väktare och DDD-rådgivare — read-only, analyserar och föreslår |
| `test-writer` | claude-opus-4-7 | Skriver unit- och integrationstester (xUnit v3, Shouldly, NSubstitute, Testcontainers) — TDD-first |
| `db-migration-writer` | claude-sonnet-4-6 | EF Core 10-migrationer mot PostgreSQL 18.3, GDPR-kompatibla scheman |
| `nextjs-ui-engineer` | claude-opus-4-7 | Next.js 16 App Router, shadcn/ui, Tailwind 4.2 — enforcar civic-utility-estetiken |
| `ai-prompt-engineer` | claude-opus-4-7 | Prompt-design och versionering, AWS Bedrock EU-profiler, `/prompts/`-biblioteket |
| `design-reviewer` | claude-opus-4-7 | DESIGN.md-enforcer med veto-makt — blockerar PRs som bryter civic-utility-estetiken |
| `code-reviewer` | claude-opus-4-7 | CLAUDE.md-enforcer med veto-makt — sista kvalitetsgranskning innan merge |
| `security-auditor` | claude-opus-4-7 | GDPR-väktare, granskar PII, secrets, auth och cross-region dataflöden — inga MVP-undantag |
| `docs-keeper` | claude-sonnet-4-6 | Håller dokumentation synkad med verkligheten — skriver inte ny spec |
| `adr-keeper` | claude-sonnet-4-6 | Författar ADRs, hanterar status-livscykeln (Proposed → Accepted → Superseded) |
| `test-runner` | claude-sonnet-4-6 | Kör `dotnet test`, tolkar xUnit-output, rapporterar status — delegerar aldrig uppåt |

Lägg till ny agent: skapa `agents/<name>.md` med YAML frontmatter (`name`, `model`, `description`) och instruktionstext.

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
