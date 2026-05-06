# Current work — JobbPilot

**Status:** Session 9 KOMPLETT — STEG 4a (Frontend-bootstrap) klar. Nästa: session 10 — STEG 4b (frontend auth-helper + login/register/me-sidor).
**Datum:** 2026-05-06

---

## Aktivt nu

**STEG 4a (Frontend-bootstrap) KOMPLETT.** Next.js 16 + Tailwind v4 CSS-first + shadcn nova + civic design-tokens + demo-page. ADR 0015-0016 skrivna.

**Fas 0-milstolpe:**
- ✅ Backend auth-stack (STEG 3): Identity + JWT + Redis, 75 tester
- ✅ Frontend scaffold (STEG 4a): Next.js 16, civic-tokens, shadcn, demo-page
- 🔲 Frontend auth-flöden (STEG 4b): login/register/me-sidor, auth-helper

**Ny chatt rekommenderas för STEG 4b** — session 9 har 4+ STOPPs, ny chatt ger renare kontext.

**Vad som är på plats (Fas 0 bootstrap + STEG 1-4a):**
- AWS-foundation, Docker-compose, Claude Code-agenter/skills/hooks, GitHub-integration, docs
- ADR 0001-0016
- .NET Solution: 5 src-projekt, 4 test-projekt
- Domain: JobAd aggregate + JobSeeker aggregate
- Infrastructure: AppDbContext, AppIdentityDbContext, JwtTokenGenerator (RS256), Redis-stores
- Application: 4 pipeline-behaviors, auth commands, JobSeeker queries
- API: `/api/v1/job-ads` + `/api/v1/auth` + `/api/v1/me`
- Tests: 75 tester (21 domain, 37 application, 6 arch, 11 integration)
- Frontend: `web/jobbpilot-web/` — Next.js 16.2.4, Tailwind v4 CSS-first, shadcn nova (Radix + Lucide), Hanken Grotesk, civic design-tokens, Button/Input/Card

**Commits session 9:**

| Commit | Innehåll |
|--------|----------|
| *(pending GO)* | feat(web): scaffold Next.js 16 + pnpm + Tailwind v4 |
| *(pending GO)* | chore(web): återställ Next.js AI-scaffolding (CLAUDE.md + AGENTS.md) |
| *(pending GO)* | feat(web): civic-tokens i globals.css + Hanken Grotesk |
| *(pending GO)* | feat(web): shadcn nova-preset init + Button/Input/Card med civic-styling |
| *(pending GO)* | feat(web): demo landing-page i (marketing)/page.tsx |
| *(pending GO)* | docs(decisions): ADR 0015 frontend-stack |
| *(pending GO)* | docs(decisions): ADR 0016 civic-design-language |
| *(pending GO)* | docs(reviews): design-reviewer + code-reviewer-rapporter STEG 4a |
| *(pending GO)* | docs(session): session 9 — STEG 4a komplett |

**Tekniska iakttagelser session 9:**
- shadcn v4.7 CLI omstrukturerat: inga `--style new-york`/`--base-color zinc` — presets heter Nova/Vega/Maia/etc. Nova vald (Radix + Lucide). Vega = närmast klassiska "new-york".
- shadcn init modifierar globals.css med `@theme inline` + `:root` OKLCH-defaults — kräver full reconciliation mot civic tokens
- `@theme inline` placeras EFTER `@theme` och åsidosätter radius/färger — lös med explict civic-värden i `@theme inline`
- Turbopack workspace root-varning: `pnpm-lock.yaml` i repo-roten + `pnpm-workspace.yaml` i web-mappen → fix: `turbopack.root` i next.config.ts
- pnpm 10 skapar `pnpm-workspace.yaml` automatiskt med `ignoredBuiltDependencies`
- `var(--font-sans)` i next/font: Hanken Grotesk måste ha `variable: "--font-sans"` för att shadcn `@theme inline { --font-sans: var(--font-sans) }` ska fungera korrekt

**Follow-up tasks för Klas (separat, ej STEG 4b-scope):**
- a) Granska `pnpm-lock.yaml` i repo-roten — sannolikt oöversedd
- b) Justera `guard-spec-files.sh` — skyddar alla `CLAUDE.md`, bör bara skydda rot-relativa spec-filer
- c) Hantera Dependabot PR #1 (Bump nuget-all) — skapa labels "dependencies"/"nuget" först
- d) Uppdatera BUILD.md §3.1 — CSS-first Tailwind (inte hybrid-mode)

**När nästa session startar (STEG 4b):**

1. Kör `git log --oneline -10` — verifiera 9 nya commits från session 9
2. Läs `docs/sessions/2026-05-06-1200-session-9-frontend-bootstrap.md`
3. Verifiera `pnpm build` och `pnpm lint` gröna i `web/jobbpilot-web/`
4. Verifiera `dotnet test` — 75 tester gröna
5. Sätt upp STEG 4b: auth-helper design + login/register-sidor

## Klart senaste sessioner

- Session 1: research
- Session 2: plan
- Session 2.5: Design-research
- Session 3–5: AWS, Docker, agents, skills, hooks, GitHub, docs
- Session 6 ✅: ADR 0008-0011 + .NET Solution STEG 1
- Session 7 ✅: Domain, Infrastructure, Application, API, Tests (35 tester — JobAd)
- Session 8 ✅: STEG 3 — ADRs 0012-0014, Auth-stack, JobSeeker-aggregate, 75 tester
- Session 9 ✅: STEG 4a — ADRs 0015-0016, Next.js 16, civic-tokens, shadcn, demo-page

## Kända begränsningar

Se **ADR 0006** för Claude Code-hooks-begränsningar.

**DesignTimeDbContextFactory** använder hårdkodade `postgres/postgres`-credentials för `migrations add`. Ej ett problem i runtime — bara för design-time verktyg.

**guard-spec-files.sh** matchar alla `CLAUDE.md` i repo:t (inte bara rot-relativa). Behöver justeras — se follow-up (b) ovan.
