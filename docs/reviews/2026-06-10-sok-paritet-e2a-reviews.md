# Agent-domar — Platsbanken sök-paritet Fas E2a (yrke-nivå-skifte yrkesgrupp)

**Datum:** 2026-06-10
**Branch:** `feat/sok-paritet-fe-yrkesgrupp-e2a`
**Scope:** Atomisk korrekthets-batch — FE-yrke-filter occupation-name → yrkesgrupp (ssyk-level-4), `?ssyk=`→`?occupationGroup=`, recent-shim, cap 10→400. 20 filer.

---

## dotnet-architect — E2a FE-kontrakts-spec

| Fråga | Dom |
|---|---|
| 1. occupations i FE-DTO | **DROP** → `occupationGroups`. ACL modellerar vad pickern behöver (Evans kap. 14); occupation-name = backend recall-substrat, noll FE-konsument. |
| 2. Municipality-scope | **DROP från E2a → E2b.** Ingen konsument i E2a; zod strippar wire-key. Modellera vid integrationspunkten. |
| 3. buildJobbHref-rename | **ATOMISK** (Fowler Rename Field), ingen dual-param. Delad buildJobbHref tvingar atomiciteten; TS-compiler säkrar fullständighet. |
| 4. Cap 10→400 | **Ja** — matchar verifierat backend `SearchCriteria.MaxConceptIds=400`. Uppdatera stale kommentar. FE får aldrig vara strängare än backend. |
| 5. "Välj alla" | Behåll mönster; vid 400-tak är overflow edge-case. "Markera alla" = tom lista backend-side (separat global-select, ej per-fält). |

## code-reviewer — ✓ Approved (0 Block / 0 Major / 1 Minor in-block)

Atomisk komplethet **verifierad** (greppade `ssyk` i hela src): inga aktiva `?ssyk=`-skickningar eller stale-fält-läsningar kvar (återstående = kommentarer, saved-searches utanför scope, test-conceptId-värden). Alla fyra korrekthets-axlar bekräftade mot backend on-disk (cap 400, param-bindning, recent-shim camelCase, occupations-drop). Picker-nivå-skifte korrekt, labels konsekventa. **1 Minor:** stale kommentar `page.tsx:125` (`ssyk[]`→`occupationGroup[]`) — **åtgärdad in-block**.

## security-auditor — ✓ APPROVED (0 fynd)

Ren param-plumbing på redan härdad pipeline. conceptId-injektion: tre lager intakta (FE `conceptIdListSchema` regex+cap, `URLSearchParams.append` auto-encode, backend `ListJobAdsQueryValidator` sista barriär). Cap 400 = legitimt tak (ssyk-level-4-universum), ingen ny DoS. Render: labels som React-text (auto-escape), conceptId i key/href (encodat). Ingen PII/secrets/auth-påverkan. **Uppföljnings-flaggor (ej E2a):** E2b municipality (samma audit-checklista), E2d chip-conceptId-validering om chips blir självständig inmatningsyta.

## design-reviewer — ✓ Approved (0 fynd)

(1) "Yrkesgrupper"/"Välj alla yrkesgrupper" = korrekt svensk civic-copy (verb+objekt, inga AI-fraser). (2) Nivå-skiftet matchar Platsbankens yrkesgrupp-picker (andra kolumnen = SSYK-4-grupp-etiketter) — TD-100-paritet trogen. (3) **Pill-label "Yrke" behålls korrekt** — pillen namnger dimensionen (vardagsord för 55-åringen i Alingsås), kolumn-rubriken namnger taxonomi-nivån. Splitten pedagogiskt rätt + Platsbanken-1:1. (4) Civic-ton ren. **Kontroll-punkt till Klas (ej finding):** verifiera via Vercel-preview att andra kolumnen renderar ~400 SSYK-4-etiketter (backend-datakontrakt).

## Empirisk backend-data-verifiering (CC, live-endpoint)

`GET /api/v1/job-ads/taxonomy` (dev-test-session): **21 yrkesområden, 400 yrkesgrupper** (ssyk-level-4), 2323 occupation-namn (korrekt droppade ur FE), 290 kommuner (E2b). Sample: "Administration, ekonomi, juridik" → 42 grupper ("Advokater", "Affärs- och företagsjurister", "Arbetsförmedlare"). **Pickern renderar populerade yrkesgrupper — design-reviewers data-kontroll-punkt löst.**

## Status

tsc rent (0 prod-fel), 93 vitest gröna, eslint rent, pnpm build grön. **Klas-GO på renderad UI kvarstår** (ADR 0067 Beslut 7 rad 104 — Vercel-preview).
