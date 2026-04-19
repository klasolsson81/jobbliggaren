# JobbPilot ADR-index

Architecture Decision Records (ADRs) dokumenterar arkitekturella val som påverkar fler än ett lager eller som skulle vara svåra att reversera. De är **immutable** — en beslutsändring skapas som ny ADR som superseder den gamla (se adr-keeper-agenten i `.claude/agents/adr-keeper.md`).

## Status-terminologi

- **Accepted** — aktivt beslut som styr projektet
- **Proposed** — under utvärdering, ej implementerat
- **Superseded** — ersatt av senare ADR (länkas till ersättaren)
- **Deprecated** — inte längre relevant, men historik bevarad

## Registrerade beslut

| # | Titel | Status | Datum | Fil |
|---|-------|--------|-------|-----|
| 0001 | Clean Architecture med DDD | Accepted | 2026-04-18 | [0001-clean-architecture.md](./0001-clean-architecture.md) |
| 0002 | Explicit modell-versioner för agenter | Accepted | 2026-04-18 | [0002-explicit-model-versions.md](./0002-explicit-model-versions.md) |
| 0003 | Design as skills (arkitektur) | Accepted | 2026-04-18 | [0003-design-as-skills.md](./0003-design-as-skills.md) |
| 0004 | GitHub Flow över GitFlow | Accepted | 2026-04-18 | [0004-github-flow.md](./0004-github-flow.md) |
| 0005 | Go-to-market + monetarisering (Fas 2-prereq) | Proposed | 2026-04-18 | [0005-go-to-market-strategy.md](./0005-go-to-market-strategy.md) |
| 0006 | Claude Code hooks — kända begränsningar | Accepted | 2026-04-18 | [0006-claude-code-hooks-known-limitations.md](./0006-claude-code-hooks-known-limitations.md) |
| 0007 | Branch protection för main (Fas 0) | Accepted | 2026-04-18 | [0007-branch-protection-fas0.md](./0007-branch-protection-fas0.md) |
| 0008 | Pipeline behavior order (Logging→Validation→Auth→UoW) | Accepted | 2026-04-19 | [0008-pipeline-behavior-order.md](./0008-pipeline-behavior-order.md) |
| 0009 | Inga Repositories; direkt IAppDbContext + IUnitOfWork | Accepted | 2026-04-19 | [0009-no-repository-pattern.md](./0009-no-repository-pattern.md) |
| 0010 | Worker som separat composition root | Accepted | 2026-04-19 | [0010-worker-composition-root.md](./0010-worker-composition-root.md) |
| 0011 | Strongly-typed IDs som `readonly record struct` | Accepted | 2026-04-19 | [0011-strongly-typed-ids.md](./0011-strongly-typed-ids.md) |
| 0012 | Auth-stack: ASP.NET Core Identity + JWT (RS256) | Accepted | 2026-04-19 | [0012-auth-stack-identity-jwt.md](./0012-auth-stack-identity-jwt.md) |
| 0013 | Separat AppIdentityDbContext för Identity-tabeller | Accepted | 2026-04-19 | [0013-separate-identity-dbcontext.md](./0013-separate-identity-dbcontext.md) |
| 0014 | Refresh tokens i DB + Redis för access-token jti (avviker från BUILD.md §11.2) | Accepted | 2026-04-19 | [0014-refresh-token-strategy.md](./0014-refresh-token-strategy.md) |

## Planerade ADRs

BUILD.md Bilaga B listar ADRs som ska skrivas när respektive tekniskt val blir aktuellt:

- `NNNN-postgresql-over-sqlserver.md` — när databasval diskuteras mot andra alternativ
- `NNNN-aws-over-azure.md` — när moln-val ifrågasätts
- `NNNN-bedrock-eu-for-system-key.md` — när EU-inference-profile-beslutet formaliseras
- `NNNN-byok-architecture.md` — BYOK-krypteringsflöde
- `NNNN-hangfire-background-jobs.md` — när bakgrundsjobb-val görs
- `NNNN-nextjs-app-router.md` — när frontend-arkitektur dokumenteras
- `NNNN-civic-design-language.md` — när design-språket får egen ADR utöver DESIGN.md

Numrering tilldelas löpande när ADR skrivs. BUILD.md Bilaga B är förslag på ordning, inte bindande.

## Skapa ny ADR

1. Claude Code-användning: `/new-adr <slug>` — triggar `adr-keeper`-agenten
2. Manuell: kopiera mall från existerande ADR (förslagsvis 0001 eller 0004 som är nya och följer nuvarande struktur)
3. Nästa lediga nummer = `ls docs/decisions/ | grep -oE '^[0-9]+' | sort -n | tail -1 | awk '{print $1+1}'`
4. Filnamnsmönster: `NNNN-kebab-case-slug.md` — alltid 4-siffrigt nummer med leading zeros

## Mall-struktur

Varje ADR bör ha följande sektioner:

- **Header:** `# ADR NNNN — <Titel>` + metadata (Datum, Status, Kontext, Beslutsfattare, Relaterad)
- **Kontext:** varför frågan är relevant, vilka krafter som spelar in
- **Beslut:** vad som valts, koncist
- **Konsekvenser:** positiva + negativa + mitigering
- **Alternativ övervägda:** minst 2-3 alternativ som avvisades, med motivering
- **Implementationsstatus:** vad som faktiskt är gjort vs planerat
- **(Frivilligt) Växtväg, Validering, Relaterade beslut** — som relevant

Se ADR 0001 eller 0007 för referens.

---

*Detta index underhålls av `docs-keeper`-agenten (`.claude/agents/docs-keeper.md`). Uppdateras automatiskt när ny ADR tillkommer via `/new-adr`-kommandot.*
