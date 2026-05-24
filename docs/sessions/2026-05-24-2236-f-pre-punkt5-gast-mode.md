---
session: F-Pre Punkt 5 — gäst-mode ("Utforska som gäst") + header-stats label-paritet
datum: 2026-05-24
slug: f-pre-punkt5-gast-mode
status: levererad
commits:
  - f02524e feat(web): F-Pre Punkt 5 — gäst-mode (Utforska som gäst) + header-stats label-paritet
  - 6db0c26 docs(readme): synka mot Fas 3-stängning + ADR-count + pre-Fas-4-vertikaler
tags:
  - v0.2.70-dev (på `f02524e`)
---

# F-Pre Punkt 5 — gäst-mode ("Utforska som gäst") + header-stats label-paritet

## Sammanfattning

Klas-prompt 2026-05-24: leverera read-only gäst-mode-vertikalen ur F-Pre Steg 5
(registreringsdelen stängdes redan i föregående leverans `afd8467`;
HANDOVER-spec "Utforska som gäst": read-only gäst-mode på alla sidor med
tydligt markerad mockdata "Exempel TEST-data"). Plus in-scope-snabbfix för
header-stats label-paritet ("aktiva" → "aktiva annonser").

Två commits pushade på `origin/main`:

1. `f02524e` feature-commit (27 filer, +1706/-12) — gäst-mode + label-paritet
2. `6db0c26` Klas-uppdaterad README (1 fil, +21/-15) — separat, oberoende av
   gäst-mode-batchen (synka mot Fas 3-stängning + ADR-count +
   pre-Fas-4-vertikaler), bundlas i samma status-block för historik-konsistens

Tag `v0.2.70-dev` på `f02524e` → deploy-dev-workflow triggad. Inga
BE-ändringar, inga migrations, inga nya dependencies.

## Mål

1. CTO-rond på gäst-mode-arkitektur (Variant A egen route-grupp vs Variant B
   feature-flag vs Variant C demo-subdomän).
2. Implementera vald variant + mock-data + demo-banner + welcome-modal.
3. /gast/{oversikt, ansokningar, cv, jobb} med tydligt markerad "Exempel
   TEST-data".
4. Landing-CTA-grenar: anonym vs inloggad.
5. In-scope-snabbfix: header-stats label "aktiva" → "aktiva annonser" för
   paritet med landing-topbar.

## Fasindelning under sessionen

1. **Discovery** — kartlägga existerande landing-yta, AuthCard, route-struktur
   under `(marketing)/` och `(app)/`.
2. **STOPP A — CTO-rond (Variant A/B/C + Alt 1/2 för /gast/jobb-yta)**.
3. **FE-batch** — `lib/guest/` (mock-data + cookie + actions) +
   `app/(guest)/gast/` (layout + 4 routes) + `components/guest/` (shell, banner,
   welcome-modal, page-komponenter).
4. **CSS-batch** — `globals.css` `.jp-demo-banner*` + `.jp-guest-resume*` +
   `.jp-guest-applist__empty` (token-baserade).
5. **Reviews** — cto + code-reviewer + security-auditor + design-reviewer
   parallellt; alla blockers + majors in-block-fixade.
6. **STOPP B — Klas-GO commit + push** (2 separata commits per CLAUDE.md §1.5).
7. **STOPP C — Klas-GO tag-push v0.2.70-dev på `f02524e`**.

## Klas-STOPP-val

- **STOPP A:** Variant A egen `(guest)/gast/*` route-grupp + Alt 2 (`/gast/jobb`
  = konverterings-CTA, ingen LIVE — undviker auth-policy-amendment +
  säkerhetsyta i denna leverans).
- **STOPP B:** GO commit + push, 2 separata commits.
- **STOPP C:** GO tag-push `v0.2.70-dev` på `f02524e`.

## Beslut

### CTO-dom Variant A (egen route-grupp)

`docs/reviews/2026-05-24-fpre-punkt5-cto.md`. Motivering:

- **SRP / dependency-rule** (Martin 2017): gäst-mode delar inga BE-portar med
  autentiserad shell och får aldrig läcka mock-data till persistens-yta.
- **Bounded-context-isolering** (Evans 2003): mock-data är ett bounded
  context som är tydligt separat från BE-aggregat.
- Feature-flag-varianten (B) bryter dessa principer genom att blanda mock-data
  och persistens i samma handler-yta.
- Demo-subdomän (C) lägger driftbörda + DNS + cert + IaC-yta utan motsvarande
  värde.

### Reviews-fixar in-block

- **security M-1:** `httpOnly:true` på guest-mode-cookie (cookie hanteras av
  server-action, ingen FE-läsning behövs).
- **design B1:** `aria-current="page"` på aktiv gäst-nav-länk.
- **design B2:** single primary CTA i landing-CTA-grenarna (inte konkurrerande
  CTAs).
- **design M1-M6:** inline-styles + hex + magic-värden eliminerade,
  token-baserade CSS-klasser i `globals.css`.

## Leverans

### Nya filer (18)

- `web/jobbpilot-web/src/lib/guest/mock-data.ts` (centraliserad mock-data)
- `web/jobbpilot-web/src/lib/guest/mock-data.test.ts`
- `web/jobbpilot-web/src/lib/guest/guest-mode.ts` (cookie-läsning)
- `web/jobbpilot-web/src/lib/guest/guest-mode-actions.ts` (server-actions)
- `web/jobbpilot-web/src/app/(guest)/gast/layout.tsx`
- `web/jobbpilot-web/src/app/(guest)/gast/oversikt/page.tsx`
- `web/jobbpilot-web/src/app/(guest)/gast/ansokningar/page.tsx`
- `web/jobbpilot-web/src/app/(guest)/gast/cv/page.tsx`
- `web/jobbpilot-web/src/app/(guest)/gast/jobb/page.tsx`
- `web/jobbpilot-web/src/components/guest/guest-shell.tsx`
- `web/jobbpilot-web/src/components/guest/guest-demo-banner.tsx`
- `web/jobbpilot-web/src/components/guest/guest-welcome-modal.tsx`
- 4 page-komponenter under `components/guest/` (oversikt/ansokningar/cv/jobb)
- 4 reviews-filer under `docs/reviews/2026-05-24-fpre-punkt5-*.md`

### Ändrade filer (5 FE + 1 CSS)

- `web/jobbpilot-web/src/app/(marketing)/page.tsx` — Promise.all +
  getServerSession för isAuthenticated-branch
- `web/jobbpilot-web/src/components/landing/landing-hero-section.tsx` —
  isAuthenticated-prop + token-färger
- `web/jobbpilot-web/src/components/landing/landing-page.test.tsx` — mockad
  session
- `web/jobbpilot-web/src/components/app/header-stats.tsx` — label "aktiva" →
  "aktiva annonser"
- `web/jobbpilot-web/src/components/app/header-stats.test.tsx`
- `web/jobbpilot-web/src/app/globals.css` — +~90 rader: `.jp-demo-banner*`
  + `.jp-guest-resume*` + `.jp-guest-applist__empty` (token-baserade, ingen
  hex, inga magic)

## Tester

- vitest 686 → **703 PASS** (+17 nya: mock-data + guest-mode-cookie +
  landing-page session-branch + header-stats label).
- pnpm build PASS — 4 nya `/gast/*`-routes registered.
- Inga .NET-tester rörda (inga BE-ändringar).

## Commits

1. `f02524e` `feat(web): F-Pre Punkt 5 — gäst-mode (Utforska som gäst) +
   header-stats label-paritet` — 27 filer, +1706/-12.
2. `6db0c26` `docs(readme): synka mot Fas 3-stängning + ADR-count +
   pre-Fas-4-vertikaler` — 1 fil, +21/-15. Klas-uppdaterad README, separat
   batch.

## Inga TDs lyfta. Inga nya ADRs. Inga BE-ändringar.

Per CLAUDE.md §9.6 — alla fynd pressade mot fas-regeln och fixade in-block.

## Disciplin

- CC gav inte egen rekommendation vid multi-approach-val (CTO decision-maker
  per §9.6).
- Klas-STOPP-kedja A (Variant A + Alt 2) + B (GO commit + push, 2 separata
  commits) + C (GO tag-push v0.2.70-dev på `f02524e`) — alla väntade GO.
- README-commiten (`6db0c26`) bundlad i samma status-block för
  historik-konsistens, men separat commit-batch (oberoende av gäst-mode).
- Docs-commit separat från feature-commits (per CLAUDE.md §1.5).

## Pending för Klas

1. Verifiera deploy-dev-workflow stable-verify för `v0.2.70-dev`.
2. Post-deploy visual-verify:
   - `/gast/oversikt`, `/gast/ansokningar`, `/gast/cv`, `/gast/jobb` (light +
     dark)
   - landing-CTA-grenarna (anonym + inloggad)
   - header-stats "aktiva annonser"-label
3. Pending Punkt 5-vertikal kvar (kräver framtida session):
   - `/jobb` LIVE för gäst (inte `/gast/jobb`)
   - ADR 0005-amendment + drop `.RequireAuthorization()` på job-ads-GET
   - JobAdsPublicReadPolicy
   - security-auditor + dotnet-architect-ronder

## Nästa session

Två val:

- **Alt 1:** /jobb LIVE-vertikal för gäst (egen session, lägre prio, kräver
  ADR-amendment + auth-policy).
- **Alt 2:** TD-94 + TD-95 från grunden per F6 P5 P4 svans-direktivet
  2026-05-24 1050 (dotnet-architect + db-migration-writer + perf-test-writer
  + web-searches; inga symptom-fixar; restoration av "(N nya)"-affordance
  förutsätter TD-94-rotlösning).
