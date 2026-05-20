# ADR 0058 — CV v3-refactor (F6 Prompt 3) pausad pending Resume-DTO-utvidgning

**Datum:** 2026-05-20
**Status:** Accepted (paus-beslut)
**Kontext:** F6 Prompt 3 (CV-refactor till v3-design) startad 2026-05-20. Discovery + CTO-konsultation visar fem-fälts-mismatch mellan Klas-prompt-spec och live `ResumeListItemDto`. Klas-beslut 2026-05-20: pausa frontend-implementationen, öppna backend-prompt för DTO-utvidgning först.
**Beslutsfattare:** Klas Olsson; senior-cto-advisor 2026-05-20 (6 multi-approach-val); CC discovery
**Relaterad:** ADR 0056 (Landing v3), ADR 0057 (Inställningar v3 — föregångare i F6-batchen), ADR 0009 (inga repositories — direkt IAppDbContext), ADR 0020 (frontend DTO-validering), HANDOVER-v3 §7.4 + målbild `09-cv-light.png`

---

## Kontext

F6 Prompt 3 ska refaktorera `/cv`-listan + `/cv/[id]`-detaljvyn till v3-design enligt HANDOVER §7.4 + målbild 09-cv-light.png. Discovery 2026-05-20 visar:

**Live `ResumeListItemDto`:**
```ts
{ id: string; name: string; versionCount: number; createdAt: string; updatedAt: string }
```

**Klas-prompt CV-kort-fält:**

| Fält | Status |
|------|--------|
| `id` | ✅ Finns |
| `name` | ✅ Finns |
| `role` (t.ex. "Backend-utvecklare") | ❌ Saknas |
| `primary` (boolean för Standard-pill) | ❌ Saknas |
| `skills` (string[]) | ❌ Saknas på list-item (finns i `version.content.skills` som object[]) |
| `sections` (number) | ❌ Saknas (det finns `versionCount` med annan semantik) |
| `language` ("sv"/"en") | ❌ Saknas helt i Resume-domänen |
| `updated` (ISO) | ✅ Finns som `updatedAt` |

5 av 8 fält saknas. N+1-fetch per CV för att resolve `role`/`skills`/`sections-count` från `versions[latest].content` bryter CCP (Martin 2017, kap. 13) + är N+1-anti-pattern (Fowler 2002, PoEAA).

Klas-prompt-direktiv (verbatim, samma prompt som listar fälten):

> "Om DTO saknar fält → ADR-stub med vad som krävs i nästa fas. SKAPA INGA fält som inte redan finns i live-API:t (no-mock-doktrin)."

Två delar av samma prompt är i konflikt:
- Del A: rendera 6 fält per CV-kort enligt målbild
- Del B: skapa inga fält som inte finns i live-API

## Beslut

### Beslut 1 — F6 P3 frontend-implementation pausas pending backend DTO-utvidgning

CC skriver INGEN kod i denna prompt. Live `/cv/page.tsx` + `/cv/[id]/page.tsx` förblir v2-design tills backend-team levererat utvidgad `ResumeListItemDto`. Klas-direktiv 2026-05-20 (efter CTO-rek): "Stoppa F6 P3 — öppna backend-prompt för DTO-utökning först."

Motivering: 4 av 6 visuella fält saknas i DTO. Tunnad v3-kort (skippa allt som saknas — CTO-Val 1A/2A/4B/5A) skulle bryta target-mot-målbild på ett sätt som inte är acceptabelt visuell-paritet. Backend-utvidgning är frontend-only-grindens enda förlösning.

### Beslut 2 — Resume-DTO-utvidgning krävs i backend (separat prompt/STEG)

Lista över utvidgningar som behövs i `JobbPilot.Domain.Resumes.Resume`-aggregatet + `GetResumesQuery` + `ResumeListItemDto`:

1. **`Resume.IsPrimary: bool`** — markerar "Standard-CV" (för Standard-pill i list-vyn). Domain-invariant: exakt 1 primary per JobSeeker (eller 0–1 om primary är optional). Migration kräver default `false` + initiering av en primary per existerande JobSeeker (t.ex. äldsta CV-versionen).

2. **`Resume.Language: ResumeLanguage` (SmartEnum `Sv` | `En`)** — per-CV-språk (separat från `JobSeekerProfile.language` som är användarens app-default). Migration kräver default `Sv`.

3. **`ResumeListItemDto.LatestRole: string?`** — denormaliserad senaste version's primary-experience-role för list-vy. Update-trigger: vid commit av ny version eller experience-edit. Alternativ: härled i query (join + order-by-startDate desc + limit 1), men det är fortfarande projection av detail-data till list-DTO.

4. **`ResumeListItemDto.SectionCount: int`** — antal populerade content-sektioner i senaste versionen (`summary`/`experiences`/`educations`/`skills` populated → +1 var). Denormaliserad eller härledd via query.

5. **`ResumeListItemDto.TopSkills: string[]` (max 5)** — projection av senaste version's `content.skills[0..4].name`. Eller `IReadOnlyList<string>`. Denormaliserad eller härledd.

Backend-utvidgning är **out-of-scope för F6 P3 frontend-only-grind** — egen STEG/prompt med backend-arkitekturarbete + EF Core-migration + ADR för migration-strategi.

### Beslut 3 — När F6 P3 återupptas: detaljvyn använder Val 6D (behåll WYSIWYG, lägg v3-cosmetic-shell)

CTO-rek 2026-05-20 Val 6: behåll existerande `<ResumeContentForm />` (working WYSIWYG-form) istället för att rendera prototypens disclosure-Sektioner-kort med no-op-edit-knappar. Klas-godkänd 2026-05-20.

Motivering:
- Klas-prompt §I säger "WYSIWYG-redigering ... 'Redigera'-knappen per sektion öppnar inget i denna prompt, lämna no-op + TODO" — disclosure med no-op-knappar tappar funktionellt värde
- Två paradigm i samma route bryter CCP
- `ResumeContentForm` är existerande working flow

När F6 P3 frontend återupptas: lägg `jp-h1`/`jp-lede`/`jp-card`-shell runt befintlig `ResumeContentForm` + uppdaterad breadcrumbs/header-styling. Disclosure-paradigm sparas till framtida edit-prompt där "Redigera per sektion" faktiskt blir wired.

### Beslut 4 — När F6 P3 återupptas: list-kort använder CTO-rek (Val 1A/2A/3D/4B/5A)

Som referens när backend-utvidgning är klar och frontend återupptar:
- Standard-pill = wired mot `IsPrimary` (CTO Val 1A omvärderas → kan visas när fält finns)
- Role-rad = wired mot `LatestRole` (CTO Val 2A omvärderas)
- Skill-chips på list-vy = wired mot `TopSkills` (CTO Val 3D omvärderas — kan visas på list när DTO växer)
- "N sektioner" = wired mot `SectionCount` (CTO Val 4B omvärderas)
- Språk-pill = wired mot `Resume.Language` (CTO Val 5A omvärderas)

Notera: CTO-rek var betingad på "DTO saknar fälten". När fälten finns ändras motiveringen — alla 6 fält renderas enligt målbild.

### Beslut 5 — F6-batchen pausar P3, hoppar inte över till P4 (Sökningar)

F6-progression bevaras strikt: Landing ✅ → Inställningar ✅ → CV (paus) → Sökningar. P4 (Sökningar) öppnas inte förrän P3 är levererad. Klas-disciplin: kvalitet > tempo, inga out-of-order delyta-leveranser.

---

## Konsekvenser

### Positiva
- Inga AI-lure-stubs i live-app (no-mock-doktrin uppfylld)
- Backend-DTO-utvidgning blir egen STEG med rätt arkitekturarbete (Domain + Application + Infrastructure + Migration + tests) istället för förhastad frontend-fabrication
- F6 P3 återupptas med komplett scope — ingen "delvis levererad"-utgång

### Negativa
- F6-tempo bromsas — P3 hänger på backend-arbete
- Frontend `/cv` förblir v2-design tills backend-utvidgning + F6 P3 frontend återstartas
- Visuell paritet mot HANDOVER §7.4 + målbild 09-cv-light.png uppskjuten

### Mitigering
- Backend-prompt-scope dokumenterad ovan (Beslut 2) — direkt input till nästa STEG
- Befintlig `/cv`-yta är funktionell (ResumeCard + ResumeContentForm fungerar), bara visuellt v2
- ADR är "Accepted" paus, inte "Proposed" — flagget kommer återkomma med konkret backend-leveransbas

---

## Alternativ övervägda

### Alternativ A — Skippa 5 saknade fält + leverera tunnad v3-kort (CTO-rek per individuellt val)
Avvisat (Klas-beslut 2026-05-20). Tunnad kort ("name + versionCount + updatedAt + Redigera/Förhandsgranska") avviker dramatiskt från målbild 09-cv-light.png. Visuell paritet skulle vara obetydlig. CTO-rek var "second-best" om backend-arbete inte var möjligt.

### Alternativ B — Stuba alla 5 fält med synlig FAS-DEFERRAL-not
Avvisat (Klas-beslut). Bryter no-mock-doktrin från F6 P1+P2 precedensen. AI-lure-anti-pattern.

### Alternativ C — N+1-fetch per CV för att resolve role/skills/sections-count
Avvisat (CTO). N+1-anti-pattern (Fowler 2002 PoEAA). M+1 HTTP-calls för list-vy med M CV-items.

### Alternativ D — Härled fält (primary = senast uppdaterad, language = JobSeekerProfile.language)
Avvisat (CTO + no-mock-precedens). Härledning som låtsas vara real data är mock förklädd som heuristik.

---

## Implementation

**Ingen kod i denna ADR** — F6 P3 frontend pausas. Backend-utvidgning krävs som separat STEG/prompt.

### Backend-prompt-scope (förslag till Klas för nästa STEG)

```
F6 P3 BACKEND-DEL: Resume-DTO-utvidgning

SCOPE:
- JobbPilot.Domain.Resumes.Resume: lägg IsPrimary (bool) + Language (ResumeLanguage SmartEnum Sv|En)
- Domain-invariant: exakt 1 IsPrimary=true per JobSeeker (eller 0..1 om optional)
- JobbPilot.Domain.Resumes.SetAsPrimaryCommand + SetLanguageCommand
- ResumeListItemDto utvidgas: LatestRole (string?), SectionCount (int), TopSkills (string[]), IsPrimary (bool), Language (string)
- GetResumesQuery: projection som hämtar dessa via join + content.skills.Take(5)
- EF Core migration: lägg kolumner, defaulta IsPrimary=false initialt + en init-script
  som markerar äldsta version per JobSeeker som primary; defaulta Language=Sv
- Tester: domain-invariant (en primary), command handlers, query projection

FAS:
- Branch-by-abstraction: nya DTO-fält är optional på frontend (kan flippa när rollout klart)
- Migration kör mot dev-DB först, sedan staging, sedan prod

EFTER MERGE:
- F6 P3 frontend återupptas: rendera Standard-pill, role-rad, skill-chips, sections-count,
  språk-pill enligt målbild 09-cv-light.png. Detaljvy får v3-cosmetic-shell runt
  ResumeContentForm (Beslut 3).
```

---

## Acceptanskriterier (för paus-beslutet)

- [x] CC har INGEN frontend-kod ändrad på `/cv`-yta i denna prompt
- [x] ADR dokumenterar 5 DTO-fält som behöver utvidgas
- [x] CTO-rekommendationer för list/detaljvy-paradigm bevarade för framtida implementation
- [x] F6-batch-ordning bevarad (Landing ✅ → Inställningar ✅ → CV paus → Sökningar)
- [x] Klas levererar backend-DTO-utvidgnings-prompt (separat STEG)
- [x] Backend-leverans merge:as till main (commit 19cde94, tag v0.2.46-dev, ADR 0059)
- [x] F6 P3 frontend återöppnas med komplett scope

---

## Amendment 2026-05-20 — F6 P3a frontend levererad efter backend-merge

Backend-utvidgning levererades i commit `19cde94` (tag `v0.2.46-dev`) — alla 5 fält wirede:
`IsPrimary`, `Language` (Ardalis.SmartEnum Sv|En), `LatestRole`, `SectionCount`, `TopSkills`. ADR 0059 (Accepted) dokumenterar denormaliseringsstrategin.

Frontend återupptogs efter Klas-rapport i öppen chat-session och levererade:

**Listvy (`/cv/page.tsx`):**
- Grid `.jp-cvgrid` med `<ResumeCard />` per CV (refactor av tidigare linjär-list-stil)
- Hero-header med "+ Nytt CV"-primärknapp (→ `/cv/ny`)
- `<AnpassaCvBanner />` under grid:en när listan inte är tom

**`ResumeCard`-uppdatering:**
- jp-cv-mönstret: titel + role-rad + Standard-pill + skill-chips (max 5 från `topSkills`) + meta-rad
- Meta: "N sektioner" (non-mono override per HANDOVER §3) + "SV"/"EN" (mono) + "Uppd. YYYY-MM-DD" (mono)
- Actions: Redigera (secondary, ikon Edit) + Förhandsgranska (ghost, ikon Eye, `aria-disabled` — PDF-pipeline FAS-DEFERRAL)

**`AnpassaCvBanner`** (ny komponent):
- Edit-ikon (INTE Sparkles per HANDOVER §0 punkt 4)
- Navy-50-bg via token (auto-mappar mörkare navy i dark mode)
- "Öppna"-knapp `aria-disabled` — AI-anpassnings-flöde FAS-DEFERRAL

**Detaljvy (`/cv/[id]/page.tsx`)** — CTO Val 6D realiserat:
- Tillbaka-länk + v3-cosmetic-shell (jp-h1/jp-lede)
- Befintlig `<ResumeContentForm />`-WYSIWYG kvar (working flow)
- `<RenameResumeForm />` + `<DeleteResumeDialog />` i header
- Disclosure-Sektioner-kort från Klas-prompt §H **rendras INTE** (Klas-prompt §I säger edit ska vara no-op denna prompt → disclosure utan funktionellt värde + bryter CCP)

**FAS-DEFERRAL bevarade till framtida fas:**
- WYSIWYG-redigering per sektion (disclosure-paradigm)
- AI-anpassnings-flöde bakom AnpassaCvBanner
- PDF-render-pipeline (Förhandsgranska-knapp)
- Skill-chip-edit
- Drag-reorder av sektioner

CSS-tillägg i `globals.css`:
- `.jp-cv__meta__sections` (non-mono override för sektioner-spannet)
- `.jp-cv__skills` + `.jp-skill-chip` (rektangulära soft-border-chips)
- `.jp-cv-banner` + `.jp-cv-banner__icon/__text/__title/__body`

Frontend-tester (Vitest + RTL): +10 nya för `<ResumeCard />` (10 testfall för isPrimary/language/sectionCount/topSkills/Redigera/Förhandsgranska-states).

Total backend+frontend efter leverans:
- Backend: 1386 gröna (per Klas backend-rapport 19cde94)
- Frontend: 580 gröna (+10 nya för ResumeCard v3)

**Commits i F6 P3 (backend + frontend):**
- `19cde94` — Backend Resume DTO-utvidgning + ADR 0059
- (frontend-commit-SHA — fylls i efter push, denna amendment)

---

## Referenser

- Martin, *Clean Architecture* (2017), kap. 13 (CCP)
- Fowler, *PoEAA* (2002), "N+1 Selects"-anti-pattern
- HANDOVER-v3.md §7.4 + målbild `handover/09-cv-light.png`
- ADR 0056 (Landing v3), ADR 0057 (Inställningar v3) — F6-precedens för no-mock-doktrin
- CLAUDE.md §1 (civic-utility), §9.4 (no-mock), §9.6 (in-block-fix vs TD-creation)
- Memory: `feedback_v3_designspec_veto_scope`, `feedback_design_reviewer_deferral_manifest`, `feedback_td_lifting_discipline`
