# ADR 0021 — Master-version-strategi för Resume-aggregat (Fas 1)

**Datum:** 2026-05-08
**Status:** Accepted
**Kontext:** STEG 7a — Resume-aggregat backend
**Beslutsfattare:** Klas Olsson
**Relaterad:** BUILD.md §5.1, §5.6, §18

## Kontext

Resume-aggregatet (BUILD.md §5.1) modellerar användarens CV med en eller flera `ResumeVersion`-entiteter. Varje version har en `Kind`: `Master` (huvud-CV) eller `Tailored` (AI-anpassat per annons). Invarianten i BUILD.md §5.6: **"Resume måste ha exakt en Master-version"**.

I Fas 1 (BUILD.md §18, milstolpe "Du kan skapa CV manuellt") finns:

- ✅ Manuell redigering av Master via formulär
- ❌ AI-tailoring (Fas 4)
- ❌ PDF/DOCX-uppladdning + parsing (Fas 4)
- ❌ Export (Fas 4)
- ❌ Tailored-versioner (Fas 4)

Frågan: **hur ska Master-versionen hanteras vid uppdatering?**

Två alternativ:

**A. Mutera Master direkt** — vid edit skrivs samma `ResumeVersion`-rad över med ny `Content`. Endast en aktiv Master-rad i DB per Resume.

**B. Versionera Master** — varje edit skapar en ny `ResumeVersion` med `Kind=Master`, gamla flaggas som inaktiva (kräver ytterligare fält som `IsCurrent` eller `Replaced=true`). Audit-trail "för fri".

## Beslut

**Alt A — mutera Master direkt i Fas 1.**

`Resume.UpdateMasterContent(content, clock)` ersätter `MasterVersion.Content` på plats. `UpdatedAt` uppdateras på både `Resume`- och `ResumeVersion`-raden. Domain event `ResumeContentUpdatedDomainEvent` raisas.

## Motivering

1. **KISS för Fas 1.** Inga UI-features kräver historikvy över Master-versioner. Ingen GDPR-, ansöknings- eller AI-koppling i Fas 1 behöver tidigare snapshots.

2. **Versionering kommer naturligt i Fas 4.** När Tailored introduceras (AI-skräddarsytt CV per jobbannons) blir varje Tailored-version per definition immutable — det är poängen att kunna se hur olika annonser fick olika anpassningar. Master-mutation kvarstår; Tailored-versioner staplas på.

3. **Ingen prematur abstraktion.** Versionering kräver `IsCurrent`/`Replaced`-fält + index på `(resume_id, kind, is_current)` + filter i alla queries. Att bygga det innan användningsfall finns leder till sämre design (vi vet inte exakt vilka frågor som kommer ställas).

4. **Audit-trail finns ändå.** `ResumeContentUpdatedDomainEvent` raisas vid varje master-uppdatering. När audit-log-infrastruktur införs (BUILD.md §5.5, planerat Fas 1) loggas eventet — vi har "vem ändrade vad och när" utan att behöva versionera själva datat.

5. **xmin concurrency token** (`ResumeConfiguration`) skyddar mot Lost Update vid samtidiga edits.

## Konsekvenser

**Positiva:**
- En Master-rad per Resume — enkel mental modell, enkla queries.
- Migrationsschemat för `resume_versions` klarar både Master och Tailored utan ändring.
- Fas 4 kan introducera Tailored utan att röra Master-flödet.

**Negativa:**
- Ingen historik över Master-versioner. Om en användare av misstag raderar fältdata och saveat går det inte att rulla tillbaka via produkten. Mitigeras av: PostgreSQL backups (BUILD.md §15 driftsplan), vid behov kan användaren bygga upp innehållet igen från originalkällor (LinkedIn, gamla CV:n).

**Neutrala:**
- Om vi senare vill versionera Master — det är en additiv migration: lägg till `IsCurrent`-flag, sätt `true` för existerande rader, börja skapa nya rader istället för UPDATE. Inga data tappas.

## Implementation

- `Resume.UpdateMasterContent(ResumeContent content, IDateTimeProvider clock)` (`src/JobbPilot.Domain/Resumes/Resume.cs`)
- `UpdateMasterContentCommand` + handler (`src/JobbPilot.Application/Resumes/Commands/UpdateMasterContent/`)
- API-endpoint: `PUT /api/v1/resumes/{id}/master` (`src/JobbPilot.Api/Endpoints/ResumesEndpoints.cs`)

## Status

**Accepted** för Fas 1. Omvärderas vid Fas 4 när Tailored-flödet konkretiseras — men intentionen är att Master-mutation kvarstår även då (Tailored är additiva snapshots, inte ersättare för Master).
