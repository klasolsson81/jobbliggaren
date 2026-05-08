# Code Review — STEG 6 Frontend (/ansokningar)

**Status:** Godkänd (blockers fixade i detta session-pass)
**Datum:** 2026-05-08
**Auktoritet:** CLAUDE.md §4.1, §4.2, §4.3, §5.2, §5.4

---

## Ursprungliga findings (pre-fix)

### Blockers (båda fixade)

1. **Middleware skyddar inte /ansokningar** → `PROTECTED_PREFIXES = ["/mig", "/ansokningar"]` tillagd
2. **`COOKIE_NAME` + `getSessionId` duplicerade i tre filer** → extraherade till `session.ts` som `SESSION_COOKIE_NAME` och `getSessionId()`

### Majors (alla fixade)

1. **`"use client"` utan kommentar på `ny/page.tsx`** → kommentar tillagd
2. **Redundant `as ApplicationStatus`-cast** → caster borttagna (typen var redan korrekt)
3. **`CHANNEL_LABELS` + `FOLLOW_UP_OUTCOME_LABELS` duplicerade** → extraherade till `status.ts`, importerade i konsumenter
4. **`useActionState` utan explict generics** → `useActionState<ActionResult | null, FormData>` tillagd i `add-note-form.tsx` och `add-follow-up-form.tsx`

### Minors (öppna — tech debt)

- **`PIPELINE_ORDER` duplicerar `ApplicationStatus`-unionen** — noterat, ej kritisk
- **`shadow-md` i `dialog.tsx`** — delegeras till design-reviewer
- **`ny/page.tsx` hanteras av layout-guard** men bör explicit dokumenteras

---

## Bra gjort

- `server-only` på API-klient, `"use server"` på actions — korrekt Server/Client-separation
- Server Components som default för alla tre sidor
- Inga `any`-typer genomgående
- Zod v4-scheman med svenska felmeddelanden
- 28 Vitest + 13 Playwright — full testtäckning för kritiska flöden
- `revalidatePath()` efter alla mutationer
- `ALLOWED_TRANSITIONS` som källa till sanningen
- Destructive transitions med confirmation dialog
- `ActionResult`-pattern konsekvent

---

## Kvarvarande tech debt (ny)

- **TD-10:** PII-läckage via `body?.detail` i Server Actions (security Major 1)
- **TD-11:** E2E testlösenord hårdkodat + testemail på produktionsdomän (security Major 3)
