---
session: Batch A — TD-10 + TD-11 frontend-säkerhet + TD-batching-plan
datum: 2026-05-11
slug: batch-a-fas1-rensning
status: klar
commits:
  - 0560718 feat(web) Batch A — TD-10 + TD-11 frontend-säkerhet
fas-1-rensning:
  - TD-10 STÄNGD (GDPR Art. 5(1)(f))
  - TD-11 STÄNGD (test-isolation)
  - TD-30 retroaktivt arkiverad
  - TD-63 lyft (Minor, Fas 2+)
  - TD-64 lyft (Minor, Trigger)
---

# Session: Batch A — Fas 1-rensning påbörjad

Stationär-CC-session ~20:00–21:00 efter ny TD-batching-plan-design. Sessionen
levererade tre saker:

1. **TD-batching-plan** för Fas 1-rensning (6 batches A–F + parallell-spår TD-30)
2. **Batch A stängd** (commit `0560718`) — TD-10 + TD-11 frontend-säkerhet
3. **TD-30 retroaktivt arkiverad** + två nya TDs lyfta (TD-63, TD-64)

## Mål

Sessionen startade som planeringsfas (TD-batching-plan för Fas 1-rensning enligt
`STARTPROMPT-STATIONAR-2026-05-11.md`). Klas gav GO på Batch A direkt efter
plan-leverans och flaggade att TD-30 (domänköp + ACM-cert) redan var levererad
men stod kvar som öppen TD.

## Plan-leverans (Discovery + design)

Inventering av 14 aktiva Fas 1-TDs (3 Major + 10 Minor + 1 Major Nu) →
6 batches:

| Batch | TDs | Scope | Beslutsbehov |
|-------|-----|-------|--------------|
| A — Säkerhet-hard | TD-10 + TD-11 | ~1 CC-session | Inget |
| B — UI-konvention | TD-41 → TD-57 | ~1 CC-session | Klas-beslut native vs shadcn |
| C — A11y-pass | TD-1 + TD-2 + TD-40 | ~0,5 CC-session | Inget |
| D — UX-pass /mig | TD-3 + TD-4 + TD-5 | ~0,5 CC-session | Design-beslut för TD-3 + TD-4 |
| E — Me-flöde | TD-6 + TD-28 | ~1 CC-session | Inget |
| F — Backend test | TD-12 | ~0,5 CC-session | Inget |

Rekommenderad ordning: A → B → C → D → E → F (risk-först, beslutsblocker, kategori-pass).

## Batch A-leverans

### TD-10 (Major, GDPR Art. 5(1)(f))

PII-läckage via `body?.detail` / `body?.title` från ASP.NET ProblemDetails
borttagen från 10 Server-Action-sites:

- `applications.ts` — 4 sites (create, transition, follow-ups, notes)
- `me.ts` — 1 site (profile)
- `resumes.ts` — 5 sites (create, rename, master-content, delete, delete-version)

**Implementation:** ny helper `_action-error.ts` med
`mapActionError(res: Response, fallback: string): string` — sync, läser
ALDRIG body, mappar status → svensk text:

| Status | Text |
|---|---|
| 401 | "Du är inte inloggad." |
| 403 | "Du saknar behörighet för åtgärden." |
| 404 | "Resursen hittades inte." |
| 409, 422 | "Resursen är i ett otillåtet tillstånd. Ladda om sidan och försök igen." |
| 429 | "För många försök. Vänta en stund och försök igen." |
| övrigt | per-action fallback |

**Säkerhetsinvariant:** body läses ALDRIG på error-path. Verifierad i test:
`expect(res.json).not.toHaveBeenCalled()` (`_action-error.test.ts:72`).

### TD-11 (Major, test-isolation)

E2E-helper `tests/e2e/helpers/auth.ts` härdad:

1. **Lösenord:** `TEST_USER_PASSWORD` env-var med dev-fallback `E2eTestPass123!Dev`
2. **Test-domän:** `@jobbpilot.se` → `@e2e.jobbpilot.test` (RFC 6761 reserverad TLD, garanterad non-resolvable)
3. **`assertSafeBaseURL`-guard:** URL-hostname-parse med whitelist `localhost / 127.0.0.1 / *.staging.jobbpilot.se / *.dev.jobbpilot.se`. Anropas i både `loginAs` (efter `page.goto`) och `ensureTestUser` (på `baseURL`-argument).

## Beslut

### CTO-beslut TD-10 (senior-cto-advisor 2026-05-11)

Tre varianter eskalerades:

- **Variant A:** Inline per-action whitelist (10× switch-block i 3 filer)
- **Variant B:** Central helper `mapActionError(res, fallback)` — body kastas helt
- **Variant C:** ActionResult som kind-union (parallell ADR 0030 för writes)

**CTO valde Variant B.** Motivering:

1. **DRY** (Hunt/Thomas 1999): 10 sites är samma policy uttryckt 10× — en helper räcker
2. **SoC** (Dijkstra 1974, Martin 2017 kap. 7): "översätt backend-fel till svensk text" är ett tvärsnitts-ansvar — hör inte i varje action-handler
3. **OCP** (Martin 2017 kap. 8): ny statuskod (t.ex. 423 Locked) → en edit, inte 10
4. **Säkerhets-granskbarhet** (OWASP ASVS V8.2): en reviewer ska kunna verifiera "body läses aldrig" på ett ställe — inte 10
5. **ADR 0030-konsekvens** (Ford/Parsons/Kua 2017): Variant C är arkitektoniskt rätt slutläge men fel scope för säkerhets-TD (bryter commit-SRP, expanderar till 8+ konsument-touch). Lyfts som TD-63.

**Body läses ALDRIG** motiveras av "secure by default" (OWASP). Att läsa body
och sedan bestämma sig för att kasta innehållet är onödig attack-yta.

**i18n-readiness:** inline svenska strängar OK för Fas 1. Helhets-i18n-migration
lyfts som TD-64 (omnibus).

### CTO-rekommendation efter beslutet (Klas-godkännande ej krävt)

Per §9.6 CTO-auto-follow: CC gick direkt till implementation. Klas-STOPP
triggades inte — varken fas-skifte, ADR-amendment, eller deploy-beslut.

## Reviews

### code-reviewer

**0 Blocker / 0 Major / 2 Minor / 3 Nit.** Approved.

Minor:
- M1: `assertSafeBaseURL` substring-bypass (`localhost.evil.com` etc.) — **fixad in-block** via URL-hostname-parse
- M2: `ensureTestUser` magic string-match `body?.title.includes("Duplicate")` — **skippad** (test-helper, ingen GDPR-implikation, security-auditor "OK att lämna")

Nit:
- N1: 409 + 422 dela konstant `STATE_CONFLICT_MSG` — **fixad in-block**
- N2: Doc-precision "tar inte body" → "läser inte body" — **fixad in-block**
- N3: 400-fallback-doc — **skippad** (test täcker, marginalvärde)

### security-auditor

**Approved.** Båda TDs stängningsklara. GDPR-veto passerad utan blocker.

- TD-10 invariant solid: 0 träffar för `body?.detail` / `body?.title` i `src/lib/actions/`, test asserterar `res.json never called`
- TD-11 `.test`-TLD korrekt val (RFC 6761), URL-hostname-parse eliminerar substring-bypass
- Defense-in-depth (praise): `parseResponse.redactIssues` strippar `received`-fältet ur Zod-issues innan log — proaktiv §5.1-disciplin
- **Observation utanför scope:** `src/lib/auth/actions.ts:96-104` (`registerAction`) läser fortfarande backend-body på 400 → notera för framtida auth-flow-touch, inte ny TD nu

## TD-30 retroaktiv arkivering

Klas-discovery 2026-05-11 efter plan-leverans: jobbpilot.se redan registrerad,
ACM-cert validerat 2026-05-10 (cert-arn `f72a79d7-...`), STEG 13c HTTPS-flip
levererad, ADR 0027 supersession av ADR 0026 dokumenterad. TD-30 stod kvar i
aktiv-listan som "Major Nu" trots leverans.

**Verifiering:**
- `infra/terraform/environments/dev/terraform.tfvars` rad 15: `alb_https_enabled = true`
- `infra/terraform/environments/dev/terraform.tfvars` rad 16: `alb_acm_certificate_arn = "arn:aws:acm:eu-north-1:710427215829:certificate/f72a79d7-f964-49c7-abb5-cf81b8639d6a"`
- 16 terraform-filer refererar `jobbpilot.se` / `route53` / `acm`
- ADR 0027 finns

**Stängd retroaktivt** + flyttad till `tech-debt-archive.md` med stängningsbevis +
lärdom om TD-livscykel-disciplin.

## Nya TDs lyfta

### TD-63 (Minor, Fas 2+): ActionResult kind-union för writes

Variant C-defererad från TD-10 CTO-triage. Migrera `ActionResult = { success: true } | { success: false; error: string }` till discriminated kind-union analog med ADR 0030 `ApiResult<T>`:

```ts
type ActionResult =
  | { kind: "ok" }
  | { kind: "validation"; fieldErrors?: Record<string, string> }
  | { kind: "unauthorized" }
  | { kind: "forbidden" }
  | { kind: "conflict" }
  | { kind: "error"; message: string };
```

Konsumenter (RHF + `useActionState` i 8+ komponenter) → exhaustive switch + `assertNever`.

**Trigger:** backend börjar exponera `ValidationProblemDetails.errors`, 3:e konsument efterfrågar typad disambiguation, eller naturlig komponent-touch som rör flera action-call-sites.

### TD-64 (Minor, Trigger): i18n-migration av inline svenska error-strängar

Helhets-omnibus-migration av inline svenska strängar till `next-intl messages/sv.json`. CLAUDE.md §5.2 säger `next-intl` är slutläget — men `_action-error.ts`-strängarna är 11 av tusentals inline-svenska. Att migrera bara dessa skapar inkonsekvens.

**Trigger:** andra språk på roadmap, Klas/användare upptäcker inkonsekvens, komponent-bibliotek refactoreras ändå, eller paras med TD-63 kind-union-migration.

## Tester

| Suite | Antal | Diff |
|-------|-------|------|
| Frontend vitest | **227** | +10 (TD-10 helper-tester) |
| tsc --noEmit | grön | — |
| Architecture.Tests | 32 | oförändrat |

## Commits

| Commit | Scope |
|--------|-------|
| `0560718` | `feat(web): Batch A — TD-10 + TD-11 frontend-säkerhet (GDPR Art. 5(1)(f) + test-isolation)` |

## Workflow-disciplin-anteckningar

Sessionen följde nya §9.6-policyn entydigt:

1. **Plan-leverans utan kod-touch** — startprompt-direktivet ("inget kodarbete denna session") respekterat fram tills Klas gav GO
2. **CC rekommenderar inte vid multi-approach** — TD-10 Variant A/B/C eskalerades direkt till senior-cto-advisor utan CC-förslag
3. **CTO-auto-follow** — efter CTO valde Variant B gick CC direkt till implementation utan extra Klas-GO
4. **In-block-fix-default per fas-regel** — alla Minor + Nit-fynd fixade in-block, inga TDs lyfta för dem
5. **Defererade TDs lyftes där fas-regeln gäller** — Variant C (för writes) hör till Fas 2+ → TD-63. i18n-omnibus hör till Trigger → TD-64.

## Lärdom: TD-livscykel-disciplin

TD-30 stod kvar som "Major Nu" trots att den var levererad i STEG 13c.
CLAUDE.md §9.7 etablerad 2026-05-11 säger att stängda TDs ska flyttas till
arkivet i leverans-commit — men §9.7 skrevs **efter** STEG 13c. Lärdom: när
framtida STEG/Block stänger en TD, flytta TD-blocket till arkivet i samma
docs-commit som leveransen. Annars hopar sig "de facto stängda" TDs och
översiktstabellens sanningshalt bryts.

## Nästa session

**Klas-prioritering behövs:**

- **Batch B (TD-41 + TD-57):** Klas + design-reviewer-beslut om native vs shadcn Select-konvention. Tre varianter. Efter beslut: CTO motiverar val → CC implementerar.
- **Batch C (TD-1 + TD-2 + TD-40):** a11y-pass, ingen beslutsblocker.
- **Batch D, E, F:** efter B–C.

**Öppna frågor kvarstår från plan-leverans:**

- Q1 ADR 0027-luckor (TD-32–TD-36)
- Q2 TD-22 + TD-17 operativa apply

Hanteras vid nästa session-start eller vid första naturliga touch.
