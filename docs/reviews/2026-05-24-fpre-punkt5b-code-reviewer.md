# Code-review — F-Pre Punkt 5b Gäst-mode-fördjupning

**Status:** Approved (med 2 in-block-fix:ar rekommenderade före push)
**Granskat:** 2026-05-24
**Granskare:** code-reviewer
**Auktoritet:** CLAUDE.md §1 (civic-utility), §4 (TS/Next strict), §5 (anti-patterns), §9.6 (in-block-fix-default), §10.3 (svenska)
**Scope:** Frontend — gäst-tree (3 commits, 17 filer, +727/-56 LOC)
**Commits:** `65b6111` (commit 1, jobb mock + nav + banner) · `08ff285` (commit 2, modal-paritet) · `a9b4941` (commit 3, översikt-utbyggnad)
**HEAD vid granskning:** `a9b4941` (ej pushad)
**CTO-dom ref:** `docs/reviews/2026-05-24-fpre-punkt5b-cto.md` — 7 beslut, alla implementerade
**Tester:** vitest 703/703 PASS oförändrat. pnpm build PASS — alla 7 nya routes registrerade.

---

## TL;DR

Inga Blockers, inga Critical, inga Major, 2 Minor (båda in-block-fix-kandidater
per §9.6). Reuse-disciplinen följer CTO Beslut 6 exakt: `<JobAdDetail>` /
`<TodayCard>` / `<SummaryRow>` / `<ApplicationModalShell>` / `<JobAdModalShell>`
återanvänds via `toJobAdDto`-adapter; `<GuestApplicationDetail>` +
`<GuestJobAdCard>` är egna varianter med dokumenterad motivering.

Gäst-tree är fortsatt isolerad — inga BE-anrop, ingen muterande yta, ingen
auth-yta exponeras. ADR 0053 modal-paritet (parallel-routes + interception)
är paritetslandad. Civic-utility-tonen är intakt: ingen emoji, inget
utropstecken, ingen disabled-knapp-teater (knappar döljs via `userActions ===
null`-narrowing i `<JobAdDetail>`).

---

## Blocker

Inga.

## Critical

Inga.

## Major

Inga.

## Medium

Inga.

## Minor

### Minor 1 — Stale kommentar i `guest-demo-banner.tsx`

**Fil:** `web/jobbpilot-web/src/components/guest/guest-demo-banner.tsx:7-8`
**Nuvarande:**

```tsx
// Ej rendered på `/gast/jobb` per Klas-direktiv (konverterings-CTA har egen
// rendering där).
```

**Problem:** Sedan commit 1 (CTO Beslut 4) renderas bannern explicit på
`/gast/jobb` (mockdata kräver banner, föregående exklusion gällde LIVE-data
som inte längre finns på sidan). Kommentaren motsäger nu verkligheten och
kan vilseleda nästa läsare.

**Föreslås:**

```tsx
// Renderas ovanför inre gäst-sidor inklusive `/gast/jobb` (mockdata kräver
// "DEMO = ej din riktiga data"-stämpel per CTO 2026-05-24 5b Beslut 4).
```

**Auktoritet:** CLAUDE.md §1 — pedagogisk kod ska inte ljuga om sitt eget
beteende.
**§9.6-press:** in-block-fix. Nuvarande fas, 1-rads-touch, ingen
fas-deferral motiverad.

### Minor 2 — Två separata hårdkodade referens-datum för gäst-mock

**Filer:**
- `web/jobbpilot-web/src/components/guest/guest-oversikt-page.tsx:22`
  → `GUEST_DEMO_TODAY = new Date("2026-05-24T08:00:00Z")`
- `web/jobbpilot-web/src/lib/guest/mock-adapters.ts:32`
  → `REF_NOW = Date.parse("2026-05-24T20:00:00Z")`

**Problem:** Två filer hårdkodar referensdatum för "när gäst-mocken anser sig
vara aktuell". Samma dag, olika tider (08:00Z och 20:00Z). När mocken ska
"rulla framåt" till nästa demo-period måste båda filerna uppdateras manuellt
och man kan glömma den ena → drift mellan "i dag"-stämpeln på översikten
och `isNew`-flaggan på jobb-annonser.

**Föreslås:** Lyft till en gemensam konstant i `mock-data.ts`:

```ts
// Stabil referens-stämpel för gäst-mock — alla "i dag" / "är nyligt" /
// "matchar nu"-beräkningar grundar sig på detta. Uppdateras manuellt vid
// demo-data-refresh (default: senaste mock-application-uppdatering).
export const GUEST_MOCK_REF_DATE = "2026-05-24";
export const GUEST_MOCK_REF_NOW = new Date(`${GUEST_MOCK_REF_DATE}T20:00:00Z`);
```

Sedan importera båda i adapters + översikten. Single source of truth.

**Auktoritet:** CLAUDE.md §5.1 — magic strings för "samma sak" i flera filer
är felkälla (även om TS-strict inte fångar drift mellan två litterals).
DRY på knowledge-piece-nivå (Hunt/Thomas 1999) — referensdatumet är *en* fakta
som idag finns på två platser.
**§9.6-press:** in-block-fix. Nuvarande fas, ~5 min-fix, naturlig
opportunistisk DRY-touch.

---

## Praise

### CTO Beslut 6 reuse-disciplin är exemplarisk

- **Adapter-strategi:** `toJobAdDto` är pure + sync + utan side effects, gör
  *en* sak (mock-shape → DTO-shape). Hårdkodad `REF_NOW` har dokumenterad
  motivering (vitest-snapshot-stabilitet, gäst-mockdata är frozen).
- **`<JobAdDetail>` återanvänds utan dual-shape-bloat:** `initialSaved` +
  `initialApplied` lämnas `undefined` → `userActions === null`-narrowing
  döljer `SaveJobAdToggle` + `HarAnsoktButton` automatiskt. Ingen
  disabled-knapp-teater (civic-utility, frånvaro framför affordance-fiktion).
- **`<GuestApplicationDetail>` är egen variant med korrekt motivering:** live
  `<ApplicationDetail>` har muterande StatusEditCard/AddForms som inte kan
  "passa undefined". Kommentaren förklarar varför adapter-mönstret inte
  räcker här (Klas-direktiv §F + Evans Bounded Contexts).
- **`<TodayCard>` återanvändning trivial:** `googleSynced={false}` är
  korrekt mock-kontext-flagga, inte hack.

### ADR 0053 modal-paritet är komplett

- `(guest)/gast/@modal/(.)ansokningar/[id]`, `(.)jobb/[id]`,
  `[...catchAll]`, `default.tsx` speglar `(app)/@modal/*`-struktur exakt.
- `layout.tsx` utvidgad med `modal: React.ReactNode`-slot på rätt segment-
  nivå. Slot-monteringspunkt är `(guest)/gast` — korrekt förståelse av
  intercepting-routes-segment-matching.
- Fullsida-fallback (`/gast/ansokningar/[id]/page.tsx`,
  `/gast/jobb/[id]/page.tsx`) finns för hard-nav / refresh / delad länk →
  WCAG 2.4.5 Multiple Ways uppfylld. Båda renderar `<GuestApplicationDetail>`
  / `<JobAdDetail>` (DRY: samma komponent i båda kontexter).

### Säkerhets-isolering av gäst-tree är intakt

- `findGuestApplication` + `findGuestJobAd` är in-memory mock-uppslag — inga
  `fetch`-anrop, inga server actions, ingen session-check (anonym tree).
- `GuestJobAdCard` länkar till `/gast/jobb/[id]` (gäst-tree-isolering) —
  aldrig till `/jobb/[id]` (auth-gated).
- `<input type="search" disabled>` + `<button disabled aria-disabled="true">`
  på `/gast/jobb`-hero är korrekt civic-utility-disciplin: disabled-tillstånd
  förklaras via hint-text ("Sökfunktionen är låst i demoläget. Logga in eller
  anmäl dig till väntelistan för att söka i hela korpus.") — inte ren teater.

### TypeScript-strict-disciplin

- Inga `any`, inga `as Type`-casts. Alla nya types är `readonly`.
- Adapter använder type-guards (`Number.isNaN`, `Date.parse`-fallback).
- Promise<params>-pattern för Next.js 16 RSC-params är genomgående korrekt.
- `"use client"` saknas på alla nya filer (alla är RSC) — korrekt.

### Civic-utility-tonen är intakt

- Ingen emoji någonstans. Inga utropstecken. Inga "Whoops!"/"Oj då!"-fraser.
- Microcopy är rak och informativ: "Detta är en exempelansökan i demoläget.",
  "Demo aktiv sedan · ej sparad", "Exempelannonser i demo".
- Datumformat (`toLocaleDateString("sv-SE")`) är konsekvent. Svensk
  locale-disciplin (CLAUDE.md §10.2) följs.

### Beck small-batches + CTO Beslut 7 batching

- 3 atomiska commits, en logisk enhet per commit (jobb-sida + nav + banner /
  modal-paritet / översikt-utbyggnad). Commit-meddelanden är beskrivande och
  scope-avgränsade.
- Inga bundlade docs-uppdateringar i feature-commits (CLAUDE.md §1.5).

---

## §9.6-press — sammanfattning

Båda Minor är **nuvarande fas, in-block-fix-default**. Ingen TD-lyftning
motiverad:

| Fynd | Fas-tillhörighet | Funktion-dep | Default | Action |
|------|------------------|--------------|---------|--------|
| Minor 1 (stale comment) | F-Pre Punkt 5b NU | Inga saknade | in-block-fix | Rekommendera fix innan push |
| Minor 2 (dual REF_DATE) | F-Pre Punkt 5b NU | Inga saknade | in-block-fix | Rekommendera fix innan push |

Båda är ≤5 min sammantaget och hör till samma touch som original-scopet.
TD-lyftning skulle vara JobbPilot anti-pattern ("spara TD så scope inte
växer" — CLAUDE.md §9.6 explicit anti-pattern).

---

## Verifierings-evidens

- `git -C "c:/DOTNET-UTB/JobbPilot" log --oneline -5` → 3 nya commits ovanpå
  `a00a74b` HEAD bekräftad
- `git diff HEAD~3 HEAD --stat` → 17 filer, +727/-56, alla under
  `web/jobbpilot-web/` (FE-only diff bekräftad — ingen BE-touch, inga
  Domain/Application-filer)
- Verifierat shape-kompatibilitet: `toJobAdDto`-output matchar
  `jobAdDtoSchema` (id, title, companyName, description, url, source, status,
  publishedAt, expiresAt, createdAt, isNew — alla fält finns och har rätt
  typ)
- Verifierat `OVERSIKT_MOCK.todaysEvents` finns i `lib/oversikt/mock-data.ts`
  och re-exporteras via `lib/guest/mock-data.ts` (rad 10 + 13)
- Verifierat `<JobAdDetail>` userActions-narrowing-logiken (rad 64-67) döljer
  knappar när `initialSaved`/`initialApplied` är `undefined`

---

## Rekommendation

**Approved med 2 in-block-fix:ar före push.** Fixa Minor 1 + Minor 2 i samma
batch (1 commit-amend eller ny commit `style(web): F-Pre 5b lint —
banner-comment + REF_DATE-konstant`), kör `pnpm build` igen, sedan push.

Inga STOPP-flaggor för Klas-GO. Disciplinen följer CTO-domen entydigt —
CLAUDE.md §9.6 låter CC fortsätta utan extra STOPP när entydig princip-
motivering finns.

---

## Referenser

- CTO-dom: `docs/reviews/2026-05-24-fpre-punkt5b-cto.md`
- CLAUDE.md §1 (civic-utility), §4 (TS/Next strict), §5 (anti-patterns),
  §9.6 (in-block-fix-default), §10.3 (svenska)
- ADR 0053 (modal-paritet intercepting routes)
- WCAG 2.1 AA SC 2.4.5 (Multiple Ways)
- Klas-direktiv §F (gäst får inte mutera), §G (DEMO-banner-konsistens)
- Memory `feedback_td_lifting_discipline` (TD-lyftning måste själv-veto:as
  mot §9.6)
