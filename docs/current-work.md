# Current work — JobbPilot

**Status:** Upptakten 2026-05-07 — Moment 5 (steg-tracker + denna fil) pågår. Nästa: STEG 5 (Application aggregate, Väg A) i ny chatt.
**Senast uppdaterad:** 2026-05-07
**Långsiktig bana:** `docs/steg-tracker.md` — single source of truth för STEG/fas-progression
**Tech debt:** `docs/tech-debt.md`

---

## Aktivt nu

**Upptakten 2026-05-07 — disciplin-uppgradering.** Moment 1-4 stängda, Moment 5 (denna fil + steg-tracker) pågår.

**Senaste klar-checkpoints:**

- STEG 4b Turn 1-4 (backend session-auth — ISessionStore, SessionAuthenticationHandler, IAuthAuditLogger, ADR 0017-0018, 153 tester) — Session 4b.1
- STEG 4b Turn 5+ (frontend auth — login/register/me-sidor, /(app)-layout, middleware) — Session 4b.2
- ADR 0019 etablerad (solo direct-push till main, superseder ADR 0004)
- CLAUDE.md uppgraderad (§6.1, §6.3, §9.1 punkt 8, §9.2 utökad, §9.4 ny, §9.5 ny)
- tech-debt.md etablerad
- Hook-vakt fix:ad för Agent SDK-läget (sentinel-fil-mekanism)
- Precompact-rapporter exkluderade från versionshantering

## Vad som är på plats (kondenserat)

För komplett historik och fas-mappning, se `docs/steg-tracker.md`. Kortfattat:

- **Infra:** AWS-foundation, Docker-compose, Claude Code-agenter/skills/hooks, GitHub-integration
- **ADRs:** 0001-0019 (se `docs/decisions/README.md`)
- **.NET Solution:** 5 src-projekt, 4 test-projekt
- **Domain:** JobAd + JobSeeker aggregates (Application aggregate kommande i STEG 5)
- **Infrastructure:** AppDbContext, AppIdentityDbContext, SessionAuthenticationHandler, ISessionStore (InMemory + Redis), IAuthAuditLogger
- **Application:** pipeline-behaviors, auth commands (Login/Register/Logout + SessionDto), JobSeeker queries
- **API:** `/api/v1/job-ads`, `/api/v1/auth`, `/api/v1/me`
- **Tests:** 153 tester (21 domain, 46 application, 6 arch, 80 integration)
- **Frontend:** Next.js 16.2.4 + Tailwind v4, civic design-tokens, shadcn nova, login/register/me-sidor, /(app)-layout, session-auth via cookie

## Senaste commits (sedan upptaktens början 2026-05-07)

Verifiera SHA via `git log --oneline -10`. Uppdaterad till och med Moment 4-stängning:

| Riktnings-SHA | Moment | Beskrivning |
|---------------|--------|-------------|
| 085210a | Moment 4 | docs(tech-debt) etablering + ev. README-fix (no-op om länk redan korrekt) |
| (förra) | Moment 3 | chore(gitignore): exkludera precompact-rapporter |
| 808792d | Moment 2 | docs(claude): uppdatera workflow per ADR 0019 + discovery + agent-krav |
| (förra) | Moment 2 | fix(hooks): aktivera spec-fil-vakt i Agent SDK-läget via sentinel-fil |
| d372a57 | Moment 1 | ADR 0019 + README-uppdatering |

## Open follow-ups

Tidigare poster i denna fil har migrerat till `docs/tech-debt.md` (TD-numrering). Se den filen för aktuella poster.

## När nästa session startar

1. Kör `git log --oneline -10` — verifiera senaste commit
2. Verifiera `dotnet test` — 153 tester gröna
3. Läs `docs/steg-tracker.md` för långsiktig bana
4. Läs senaste filen i `docs/sessions/` för senaste session-detaljer
5. För STEG 5 (Application aggregate): plan-design med webb-Claude i ny chatt först

## Kända begränsningar

Se **ADR 0006** för Claude Code-hooks-begränsningar.

**DesignTimeDbContextFactory** använder hårdkodade `postgres/postgres`-credentials för `migrations add`. Ej runtime-problem — bara design-time verktyg.

**guard-spec-files.sh** uppgraderad 2026-05-07 — kontrollerar nu sentinel-fil i `.claude/spec-edit-approved` istället för `CLAUDE_USER_PROMPT` (som inte sätts i Agent SDK-läge). Se `fix(hooks)`-commit i Moment 2.
