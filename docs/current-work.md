# Current work — JobbPilot

**Status:** STEG 6 (frontend för ansökningar) — KLAR. Nästa: STEG 7 (tbd — se steg-tracker).
**Senast uppdaterad:** 2026-05-08
**Långsiktig bana:** `docs/steg-tracker.md` — single source of truth för STEG/fas-progression
**Tech debt:** `docs/tech-debt.md`

---

## Aktivt nu

**STEG 6 klar.** All kod committad och pushad (SHA: se nedan).

**Vad som genomfördes:**

- TypeScript-typer + server-only API-klient (`lib/types/applications.ts`, `lib/api/applications.ts`)
- Utility-funktioner: statuslabels, badge-variants, allowed transitions + 13 Vitest-tester
- Zod v4 schemas för alla Server Actions + 15 Vitest-tester
- Server Actions för create, transition, follow-up, note
- UI-komponenter: ApplicationStatusBadge (`role="status"`), ApplicationCard, TransitionForm (med dialog för destruktiva transitions), AddNoteForm, AddFollowUpForm
- Pages: `/ansokningar` (pipeline-tabell-vy), `/ansokningar/ny` (formulär), `/ansokningar/[id]` (detaljvy)
- Nav-integration i `(app)/layout.tsx`
- Testinfrastruktur: Playwright + Vitest (från scratch)
- 28/28 Vitest unit tests gröna
- 13/13 Playwright E2E tests gröna
- `pnpm build` grön

**Infrastruktur-fix:**
- `docker-compose.yml`: postgres-dev portändrad från 5432→5435 (dojo-future-be-db-1 upptar 5432 på lokalmaskinen)
- `appsettings.Development.json`: Port=5435 synkad med compose
- Båda AppDbContext + AppIdentityDbContext migrationer körda på ny databas

**Viktiga tekniska beslut:**

- `role="status"` på ApplicationStatusBadge (accessibility + Playwright-selektorer)
- Unik test-email per test-run (`Date.now()`) för E2E-isolering
- `getByRole("textbox", { name: "Notering" })` istället för `getByLabel` (undviker substring-match mot `aria-label="Noteringar"`)

## Senaste commits

| SHA | Beskrivning |
|-----|-------------|
| 3e6cff1 | feat(web): STEG 6 — frontend för ansökningar (pipeline, formulär, detaljvy) |
| 80ab1d7 | docs(sessions): session-logg för STEG 5 — Application-aggregat klart (2026-05-07) |
| 82af5fa | feat(applications): implementera Application-aggregat med full CQRS-stack (STEG 5) |

## Open follow-ups

Se `docs/tech-debt.md` för aktuella poster (TD-numrering).

## När nästa session startar

1. Kör `git log --oneline -10` — verifiera HEAD = ny STEG 6-commit
2. Verifiera `dotnet test` — 280 tester gröna
3. Verifiera `pnpm test` — 28 tester gröna
4. Verifiera API snurrar: `curl http://localhost:5049/health`
5. Kontrollera `appsettings.Local.json` — ska ha Port=5435
6. Läs `docs/steg-tracker.md` för nästa STEG
7. Läs `docs/sessions/2026-05-08-*-steg6-*.md` för detaljer

## Kända begränsningar

Se **ADR 0006** för Claude Code-hooks-begränsningar.

**postgres-dev** snurrar nu på port **5435** (inte 5432) — `appsettings.Local.json` måste ha `Port=5435`.

**Middleware-deprecation-varning** i Next.js: `The "middleware" file convention is deprecated. Please use "proxy" instead.` — ej kritisk, men ska åtgärdas i STEG 7 eller som tech-debt.
