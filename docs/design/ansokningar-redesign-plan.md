# Plan — /ansokningar + /ansokningar/[id] redesign (FAS 3-reparation)

**Datum:** 2026-05-17
**Status:** STOPP 2 — plan-design, väntar Klas-GO före STOPP 3 (implementation)
**Scope-beslut (Klas 2026-05-17):** backend INKLUDERAS; ny radio-group-komponent OK.
**Bakgrund:** Klas underkände `/ansokningar`-ytorna live (v0.2.14-dev) — UUID-rader utan jobbidentitet, saknad visuell hierarki, fel status-mönster. Discovery (STOPP 1) fann att JobAd-data inte exponeras i Application-read-vägen → backend-utbyggnad krävs.
**Scope:** ENDAST `/ansokningar` (list), `/ansokningar/[id]` (detail), underliggande komponenter, och den backend-read-väg de kräver. EJ `/jobb` (separat tråd). Inga design-token/-skill-ändringar.

---

## 1. Backend-utbyggnad (förutsättning för målbilden)

JobAd-aggregatet (`Title`, `Company.Name`, `Url`, `Source.Value`, `PublishedAt`, `ExpiresAt`) är separat från Application (länk = `JobAdId?`, nullable). Tre read-handlers + DTO:er utökas.

> **Scope-rättelse (STOPP A, ersätter tidigare "ingen migration"-deklaration):** Read-vägen (§1.1–§1.4) är fortsatt ren projektion utan schema-ändring. MEN skrivvägen för manuella ansökningar (§1.5, Klas STOPP A-direktiv) **kräver migration** (Variant A `ManualPosting` value object på Application — dotnet-architect-beslut a4c1483aeaee7fcea). Klas har för-auktoriserat skrivväg+migration i FAS 3-scope (STOPP A-direktivets "I scope"-lista); architect-scope-flaggan kvarstår för Klas-bekräftelse vid STOPP A-granskning (genuin utvidgning mot ursprungsplanen, rör Fas-1-byggd CreateApplication-kod, ADR 0046 Beslut 1 D-konsekvens).

### 1.1 Ny delad read-DTO

```
JobAdSummaryDto(
    Guid JobAdId,
    string Title,
    string Company,
    string Url,
    string Source,          // "Platsbanken" | "Manual" | "LinkedIn"
    DateTimeOffset? PublishedAt,  // J1 (CTO rev2): nullable — null för manuell;
                                  // Application.CreatedAt får EJ renderas som
                                  // "Publicerad" (semantik-läcka, 2 change-reasons).
                                  // Manuell ansökan: "Publicerad"-raden utelämnas helt.
    DateTimeOffset? ExpiresAt)   // = sista ansökningsdag
```

`ApplicationDto` och `ApplicationDetailDto` får ett **nullable** fält `JobAd JobAdSummaryDto?` (null när `JobAdId == null` ELLER annonsen raderats/inte hittas — left join). `JobAdId` (rå Guid) behålls additivt för bakåtkompatibilitet.

### 1.2 Tre handlers — left join JobAd

`GetPipelineQueryHandler`, `GetApplicationByIdQueryHandler`, `GetApplicationsQueryHandler`: lägg LEFT JOIN mot `db.JobAds` på `Application.JobAdId == JobAd.Id` (DefaultIfEmpty — bevara ansökningar utan/med trasig JobAd-länk). Projektion till `JobAdSummaryDto?`. `.AsNoTracking()` bevaras (§3.6).

**Soft-delete-mekanism (CTO Beslut 2, skärpt):** JobAd har en EF global query filter (`JobAdConfiguration.cs` rad 82: `HasQueryFilter(j => j.DeletedAt == null)`). Soft-deletade JobAds exkluderas därmed **automatiskt av query-filtret FÖRE joinen**; `DefaultIfEmpty()` ger då `null` → fallback (§7). **Förbjudet i dessa 3 handlers:** `IgnoreQueryFilters()` (skulle exponera soft-deletad annons-metadata — regression mot ADR 0032) och manuell `DeletedAt`-predikat i handlern (dubblerar query-filter-invarianten — DRY/SPOT-brott). Fallback för soft-deletad JobAd sker via default-joinen, inte via egen predikat.

**N+1 (CTO Beslut 2):** joinen måste uttryckas som single LINQ-join projicerad till DTO **före** `ToListAsync()` så EF genererar en `LEFT JOIN job_ads` i samma query (Pipeline: join före in-memory-gruppering som idag). dotnet-architect-gaten verifierar genererad SQL = en query med en LEFT JOIN (ej post-materialiserings-lookup per rad). ADR 0045 perf-budget-relevant (CLAUDE.md §2.5).

### 1.3 Frontend Zod-DTO (ADR 0020 single source)

`lib/dto/applications.ts`: `jobAdSummaryDtoSchema` + `applicationDtoSchema`/`applicationDetailDtoSchema` får `jobAd: jobAdSummaryDtoSchema.nullable()`. `lib/types/applications.ts` re-export.

### 1.4 Gates (Klas-spec)

dotnet-architect (join-design, **explicit SQL-verifiering = en LEFT JOIN**, query-filter-disciplin, DTO-gräns, Clean Arch; + skrivväg-VO §1.5) **INNAN kod** · test-writer (handler-tester: med JobAd / utan (jobAdId null) / med ManualPosting / **soft-deleted JobAd via default-join utan IgnoreQueryFilters → fallback** / cross-user; + create-command ManualPosting + invariant `JobAdId ⊕ ManualPosting`) FÖRST/TDD · **db-migration-writer (Variant A — `ManualPosting`-kolumner, alla NULL default på befintliga rader, §1.5)** · security-auditor BLOCKING (JobAd publik metadata ADR 0032 §8; Application jobSeeker-scopad → ingen cross-user-läcka; auditor bekräftar join ej kringgår ADR 0031, soft-deletad metadata ej läcker, **create-command ManualPosting cross-user/input-validering**) · code-reviewer.

**ADR 0048 (Accepted direkt — Klas-beslut 2026-05-17, kontext "in-block del av STOPP 2 plan-godkännande"):** Första cross-aggregat-joinen i Application-läsvägen. ADR 0043 Beslut C löste cross-context-läsning via dedikerad `ITaxonomyReadModel`-port specifikt för att INTE införa cross-aggregat-koppling — in-handler-join här är medvetet precedensval i kontrast mot ADR 0043 → ADR-värt (Nygard 2011; CLAUDE.md §8.9 DoD; Klas: Proposed→Accepted-cooldown onödig formalism när motiveringen finns och Klas är beslutsfattaren). ADR 0048 fastställer: (a) join-i-handler som mönster för enkla samma-DbContext 1:0..1-aggregatlänkar, (b) kontrast/avgränsning mot ADR 0043 port-val (anti-corruption + ADR 0009 gällde där, ej här), (c) query-filter-disciplin (§1.2), (d) **skrivvägen håller sig inom Application-aggregatet (ManualPosting VO, §1.5) — ADR 0048 vidgas EJ till write-side multi-aggregate-create** (Variant B avvisades av architect just för att inte vidga detta). Skrivs i samma touch (adr-keeper).

### 1.5 Skrivväg — manuella ansökningar (`jobAdId == null`)

> **dotnet-architect-beslut (a4c1483aeaee7fcea, STOPP A — entydigt mot principer, ingen Klas-STOPP för A/B):** **Variant A som `ManualPosting` value object** på Application. Variant B (lokal JobAd Source=Manual) **avvisad** — 3 kod-verifierade invariant-brott: (1) `JobAd.ValidateCore` (`JobAd.cs:177-183`) tvingar obligatorisk absolut http(s)-URL även i `JobAd.Create`; manuell ansökan har ingen URL → skulle kräva TD-80-XSS-whitelist-luckring (security-regression) eller placeholder-URL (data-lögn); (2) `ExternalReference.Create` failar by-design för `JobSource.Manual` (ADR 0032 §4) → ingen Import-väg passar; (3) ADR 0032 §8: JobAd = extern annons-katalog med rekryterar-PII-pipeline → job-seeker-data där = Aggregate Consistency Boundary-brott (Vernon 2013). Lösa nullable-kolumner avvisade som primitive obsession (CLAUDE.md §5.1; ADR 0032 §4 `ExternalReference`-VO-precedens).

**`ManualPosting` value object** (`readonly record`, Domain): `Title` (obligatorisk, non-empty), `Company` (obligatorisk, non-empty), `Url` (nullable — manuell ansökan kan sakna URL; om satt: samma scheme-whitelist som TD-80), `ExpiresAt` (nullable DateTimeOffset = sista ansökningsdag). Validering i VO-factory (`ManualPosting.Create` → Result), ej i handler.

> **Source struken (Klas STOPP 3a-villkor 2026-05-17, in-block):** `Source` ingår INTE i ManualPosting-VO, `ManualPostingInput`, `CreateApplicationCommand` eller formuläret. Read-vägen projicerar `JobAdSummaryDto.Source` = literalen `"Manual"` när `ManualPosting != null` (ej lagrat fält — en manuell ansökan är per definition Source=Manual; att lagra det vore redundant). JobAd-kopplade ansökningar projicerar `JobAd.Source.Value` som idag.

**Aggregat-invariant (CLAUDE.md §2.2 — i `Application.Create`, ej handler):** `JobAdId` ⊕ `ManualPosting` ömsesidigt uteslutande. En ansökan är ANTINGEN JobAd-kopplad ELLER manuell, aldrig båda, aldrig ingen jobbidentitet utöver fallback. Befintliga rader (cover-letter-only, ingen ManualPosting) → behåller `"Ansökan #{kort-id}"`-fallback (§7) tills användaren ev. kompletterar (metadata-edit = egen framtida touch, §4-not — EJ denna).

**Read-väg-integration (§1.1/§1.2):** `JobAdSummaryDto`-fallback fylls från `ManualPosting` när `JobAdId == null && ManualPosting != null` (Source="Manual", JobAdId-fältet i DTO blir då null men Title/Company/Url/Source/ExpiresAt från VO). De 3 read-handlers projicerar: `jobAd != null` → JobAd-fält (PublishedAt satt); annars `ManualPosting != null` → ManualPosting-fält (**PublishedAt = null**, J1); annars `null` → `"Ansökan #{kort-id}"`-fallback (§7). `Application.CreatedAt` projiceras **aldrig** som PublishedAt (J1, CTO rev2 — fält med 2 change-reasons, Martin 2017 kap. 7; "Publicerad"-raden renderas ej för manuell).

**Migration (db-migration-writer):** lägg `ManualPosting`-kolumner (owned-entity/komplext typ-mappning per EF — t.ex. `manual_title`, `manual_company`, `manual_source`, `manual_url`, `manual_expires_at`), alla NULL default på befintliga rader. Ingen backfill. Idempotent.

---

## 2. Komponentträd — /ansokningar (pipeline-list)

```
AnsokningarPage (server)               // page.tsx — oförändrad datahämtning (getPipeline)
├─ <header> "Ansökningar" + jp-lede + [Ny ansökan]   // OFÖRÄNDRAD per Klas
└─ för varje icke-tom statusgrupp (sorterad PIPELINE_ORDER):
   └─ <section aria-label={statuslabel}>
      ├─ grupprubrik: <h2 jp-h2>{label}</h2> <span>{count}</span>   // oförändrad
      └─ ApplicationRow[]  (NY — ersätter ApplicationCard)
         └─ <Link href=/ansokningar/{id}> hela raden klickbar
            ├─ rad 1 (primär): {jobAd.title} — {jobAd.company}      text-base/lg font-semibold
            │   └─ FALLBACK (jobAd == null): "Ansökan #{id.slice(0,8)}"  font-mono kort-id
            └─ rad 2 (sekundär, text-sm text-secondary):
                StatusDot (ej fylld pill — §8 Area 1-mönsterval) · "Uppdaterad {sv-SE}"
                · (jobAd?.expiresAt) "Sök senast {sv-SE}"
```

- **Tomma grupper:** redan filtrerade (`g.count > 0` i page.tsx) — kravet "dölj tom grupp / visa inte 'Utkast 0'" är **redan uppfyllt**, bekräftat i STOPP 1. Ingen ändring behövs; planen bevarar beteendet.
- `ApplicationCard` → byts mot `ApplicationRow` (samma fil eller ny; CTO/plan-review avgör namn). Gammal `ApplicationCard` raderas om orphaned (§9.6 dead-code, som transition-form-precedensen).
- Endast tokens: `text-text-secondary`, `border-border-default`, `hover:bg-surface-tertiary`, `font-mono` för id/datum. Inga hex/px inline.

## 3. Komponentträd — /ansokningar/[id] (detail)

```
AnsokningDetailPage (server)           // [id]/page.tsx — getApplicationById (nu m. jobAd)
├─ <nav Brödsmulor>  Ansökningar / {jobAd.title ?? "Ansökan #{kort-id}"}
├─ <header>
│   ├─ <h1>{jobAd.title}</h1>          // FALLBACK: "Ansökan #{kort-id}"
│   └─ <p text-secondary>{jobAd.company}</p>   // utelämnas helt om jobAd == null
└─ split-layout (≥ md: 2 kол; < md: stack — se §6)
   ├─ VÄNSTER — JobInfoPanel (NY, read-only TLDR; hela panelen utelämnas om jobAd==null,
   │            ersätts av civic not "Ingen kopplad annons — manuellt skapad ansökan")
   │   └─ <dl> Företag · Publicerad {sv-SE} · Sista ansökningsdag {sv-SE el. "—"}
   │            · Källa {Platsbanken|Manuellt|LinkedIn}
   │   └─ [Visa annonsen] extern länk (L5 bindande): endast om jobAd.url;
   │      target=_blank rel="noopener noreferrer"; `↗`-glyf `aria-hidden`;
   │      aria-label="Visa annonsen hos {källa} (öppnas i ny flik)" — ikon
   │      aldrig enda signalen (synlig text "Visa annonsen" + glyf)
   │   └─ Personligt brev — collapsed by default (<button aria-expanded> disclosure)
   └─ HÖGER — StatusEditCard (ERSÄTTER StatusCard, persistent — ej inline-disclosure)
       ├─ "Nuvarande status:" StatusPill (förankrad, alltid synlig)
       ├─ StatusRadioGroup (NY shadcn radio-group, se §5) — tillåtna övergångar
       │   (0–3 st beroende på status; om 0: "Den här ansökan är i ett slutläge."
       │    ingen radiogrupp, ingen Spara)
       ├─ destruktiv övergång (Rejected/Withdrawn) vald → konsekvenstext inline
       └─ [Spara] primary, högerjusterad, disabled tills val ≠ nuvarande status
full-width under split:
├─ <section> Uppföljningar — lista (etiketterad <dl> Utfall/Anteckning, behålls från v1)
│            + RecordFollowUpOutcomeForm (Pending, tvåstegs bekräftelse, behålls)
│            + "Lägg till uppföljning" eget block (AddFollowUpForm, behålls)
└─ <section> Noteringar — lista + AddNoteForm (behålls)
```

Behåller v1:s vinster (etiketterad dl, separerade add-flows, konsekvens-bekräftelse, sektionskort) — bygger ovanpå, river inte.

## 4. Save-strategi detail-page — Variant A vs B (CTO avgör)

> Per CLAUDE.md §9.6 + memory `feedback_cto_decides_multi_approach` ger CC ingen egen rekommendation. Båda varianter presenteras neutralt; **senior-cto-advisor read-only-pass (STOPP 2-gaten) producerar planens rekommendation**, foldas in här före Klas-GO.

**Variant A — globalt save (topp) för status+metadata; sub-listor egna add-flows.**
Status (+ ev. framtida metadata) sparas via en [Spara]-knapp i StatusEditCard. Uppföljningar/Noteringar har som idag egna self-contained add-flows (egen submit per item).
- För: matchar Klas målbild ("Spara-knapp disabled tills ändring"); en tydlig commit-punkt för status; sub-listor redan korrekt isolerade (append-only, ingen "spara delmoment"-förvirring — det var defekt 4 i v1, redan löst).
- Emot: två mentala modeller på sidan (top-save vs per-item-add) — men de är visuellt åtskilda (defekt 4-fix) så modellerna krockar inte.

**Variant B — per-sektion save.**
Varje sektion (status, uppföljningar, noteringar) har egen save.
- För: konceptuellt enhetligt "varje sektion sparar sig själv".
- Emot: status är ett enkelt enum-val — en egen "sektion-save" är overhead; uppföljningar/noteringar är append-listor, inte redigerbara formulär → "save" är fel verb för dem. Risk att återinföra defekt 4 (ser ut som delmoment).

Status idag = single `transitionStatusAction(id, target)` (en write). Ingen metadata-redigering finns ännu (cover letter redigeras ej här; datum-fält i målbilden = N/A tills metadata-edit finns).

> **REKOMMENDATION (senior-cto-advisor STOPP 2, ac00cccfcd6962a67 — entydig):** **Variant A.** Motivering: YAGNI/KISS (status = single write, Variant B = spekulativ generalitet mot icke-existerande metadata-edit, Fowler 2018 kap. 3); SRP/SoC (uppföljningar/noteringar = append-only-listor, "save" fel verb — Variant B påtvingar formulär-semantik, Martin 2017 kap. 7); regressionsskydd (Variant B återinför ADR 0047 defekt-4-mönstret). Variant B avvisad. **Datum-fält i målbildens höger-panel = N/A nu** (ingen metadata-edit-command finns); StatusEditCard sparar endast status tills metadata-edit specas (egen framtida touch, ej denna).

## 5. StatusRadioGroup — ny shadcn radio-group (Klas-godkänd)

- Ny `components/ui/radio-group.tsx` (shadcn Radix RadioGroup-primitiv, civic-utility-tokenstil — a11y-granskas av design-reviewer render-VETO). Inga nya design-tokens.
- **(b)/design-reviewer bindande:** nuvarande status visas **EN gång** som förankrad `StatusPill` (detaljhuvud-accent). Radiogruppen innehåller **endast tillåtna övergångar** (0–3, `ALLOWED_TRANSITIONS` — ej fast lista). **Ingen låst self-radio** för nuvarande status (dubbelrendering = oväljbar affordans, bryter components-skill "never both for same datum").
- **L1 bindande:** synlig instruktionsrad ovanför radiogruppen ("Välj ny status. Nuvarande status är {label}.") — bevaras från v1 (`status-card.tsx:137-143`). `StatusRadioGroup` har `role="radiogroup"` + `aria-labelledby` pekande på den **synliga** rubriken/instruktionsraden (ej sr-only — sighted förstagångsanvändare behöver samma ledtext).
- **L2 bindande (design-reviewer designdom):** destruktiv övergång (Rejected/Withdrawn) — **behåll v1:s Dialog-bekräftelse** (`status-card.tsx:169-214`: DialogTitle "Markera som {label}?", konsekvenstext, åtgärdsspecifik knapp). Inline konsekvenstext när alternativet väljs = additiv förvarning, **ersätter ej** dialogen. Inline-istället-för-dialog = Block (components-skill kräver dialog för destruktivt).
- **1-övergångsfall** (Draft→Submitted, Ghosted→Submitted): renderas som **enskild primär åtgärdsknapp** ("Markera som Skickad"), ej 1-items radiogrupp (Krug, mindre kognitiv last — design-reviewer rådgivande, CTO Variant A-konformt). Terminala (0 övergångar): ingen radiogrupp/region, civic `<p text-secondary>` "Den här ansökan är avslutad och kan inte ändras." (ej intern term "slutläge").
- Persistent synlig (Klas: inline-expand bröt flödet) — ingen disclosure.

## 6. Mobile-breakpoint

Split-layout vänster/höger vid `≥ md` (768px, Tailwind `md:` token-backat). `< md`: single column, ordning: header → StatusEditCard (höger-panelen först — primär uppgift) → JobInfoPanel → Uppföljningar → Noteringar. Motivering: på mobil är status-ändring den primära handlingen; läs-TLDR sekundär. Grundas i jobbpilot-design-principles (utility-först) — bekräftas av design-reviewer.

## 7. Skrivväg + fallback — manuella ansökningar (konkret create-flöde)

**Tre identitets-tillstånd** (read-handlers projicerar i denna ordning, §1.5):
1. `JobAdId != null` + JobAd finns/ej soft-deletad → JobAd-fält (Platsbanken/LinkedIn-kopplad).
2. `JobAdId == null` + `ManualPosting != null` → ManualPosting-fält (manuell ansökan, NY skrivväg).
3. Varken eller (befintliga cover-letter-only-rader; soft-deletad/saknad JobAd) → `"Ansökan #{id.slice(0,8)}"`-fallback.

**Create-flöde end-to-end (Variant A `ManualPosting`):**
- **`/ansokningar/ny`-formuläret** utökas: utöver `coverLetter` läggs fält **Jobbtitel** (obligatorisk för manuell), **Företag** (obligatorisk), **Annonslänk** (frivillig, scheme-validerad TD-80), **Sista ansökningsdag** (frivillig datum). **Inget Källa-fält** (Source struken — manuell ansökan är implicit Source=Manual, read-vägen projicerar literalen). Civic-copy, ingen placeholder-exempel (Klas input-regel 2026-05-17), hint via `aria-describedby`.
- **Validering (formulär + command-validator + VO-factory, defense-in-depth):**
  - `jobAdId != null` (kopplad annons — framtida "ansök från /jobb"-flöde): de nya fälten **N/A — göms/disablas** (ansökan ärver JobAd:s metadata).
  - `jobAdId == null` (manuell, nuvarande `/ansokningar/ny`): **Jobbtitel + Företag obligatoriska**; Annonslänk/Sista ansökningsdag frivilliga. `coverLetter` fortsatt frivillig.
- **`CreateApplicationCommand`** utökas: `Guid? JobAdId, string? CoverLetter` + `ManualPostingInput? Manual` (Title/Company/Url?/ExpiresAt? — **ingen Source**). Handler: om `Manual` satt → `ManualPosting.Create(...)` (Result), skickas till `Application.Create` som medierar aggregat-invarianten `JobAdId ⊕ ManualPosting`. Validator: `jobAdId == null` ⇒ Manual.Title/Company required; `jobAdId != null` ⇒ Manual måste vara null (motstridigt annars).
- **Resultat efter implementation:** manuell ansökan får list-rad `"{ManualPosting.Title} — {ManualPosting.Company}"` + detaljsida H1/JobInfoPanel från ManualPosting (Källa="Manuellt", **"Publicerad"-raden utelämnas helt** — J1, ingen `CreatedAt`-som-Publicerad-läcka; "sök senast" = `ManualPosting.ExpiresAt` om satt). Permanent "Ingen kopplad annons"-noten visas **endast** för tillstånd 3 (äldre cover-letter-only-rader utan ManualPosting) — civic-not "Ingen kopplad annons — manuellt skapad ansökan" + (om metadata-edit senare byggs) väg att komplettera.
- **Soft-deleted/saknad JobAd trots `JobAdId != null`:** tillstånd 3-fallback (left join → null), ingen trasig rad.

## 8. Token-disciplin + spec-stängningar (STOPP 2 bindande)

Alla färger/spacing/typografi via jobbpilot-design-tokens (Tailwind-utilities token-backade). Inga hex, inga inline-px (utom Tailwind-spacing-utilities). Svensk copy per jobbpilot-design-copy ("du", ingen emoji/utropstecken, sv-SE datum). Civic-utility per -principles. a11y per -a11y.

**L3 (avgränsning, bindande):** varje detalj-sektion = `<section>` med synlig `<h2>` + `border-strong` informationsbärande avskiljare (≥3:1, ej `border`) — bevara/förstärk v1:s sektionskort-mönster (`[id]/page.tsx:125-128`), riv ej. Split vänster/höger: åtskilda med kolumn-gap (`gap-6`/`gap-8`) + panelerna `border border-border-default rounded-md` — **aldrig** shadow/floating cards (regel 1 papper-ej-glas). Inget "rakt upp och ner"-stapel (defekt 5).

**L4 (typografi-tokens, bindande):** detaljsidan använder **samma** token-system som list-sidan — `jp-h1` (sid-H1 jobtitel), `jp-h2` (sektionsrubriker), H3/`text-h3`-ekvivalent (panelrubriker) per tokens-skill scale. **Ingen** `text-h1`/`text-h3`-blandning från v1 (ADR 0037/0038 hierarki-konsekvens).

**L6 (jobAd==null layout, bindande):** vid jobAd==null faller detaljsidan tillbaka till **single-column** (ingen tom vänsterkolumn) — civic-not "Ingen kopplad annons — manuellt skapad ansöken" ersätter JobInfoPanel-positionen, StatusEditCard + listor full-width. Ingen obalanserad tom canvas (regel 3).

**Area 1-mönsterval (bindande):** list-rad (§2) status = `StatusDot` (dot + text, ingen fyllning — lägst visuell vikt i tät lista, components "first choice in tables"). Detaljhuvudets förankrade nuvarande-status = `StatusPill` (entitets-accent). Ej fylld pill i listan.

## 9. Filer som rörs (estimat — exakt i STOPP 3)

**Backend läsväg (§1.1–§1.4):** `ApplicationDto.cs`, `ApplicationDetailDto.cs`, ny `JobAdSummaryDto.cs`, 3 QueryHandlers (GetPipeline/GetApplicationById/GetApplications — left join + ManualPosting-fallback-projektion), handler-tester.

**Backend skrivväg (§1.5/§7, STOPP 3a):**
- Domain: ny `ManualPosting` value object (`readonly record` + `Create`→Result) i `JobbPilot.Domain/Applications/`; `Application.cs` — `ManualPosting?`-property + invariant `JobAdId ⊕ ManualPosting` i `Create` (+ ev. konstruktor-signatur).
- Infrastructure: EF-mappning (owned entity / komplex typ) i `ApplicationConfiguration`; **migration** (db-migration-writer — `manual_*`-kolumner NULL default, ingen backfill, idempotent).
- Application: `CreateApplicationCommand` (+ `ManualPostingInput`), `CreateApplicationCommandHandler`, `CreateApplicationCommandValidator` (jobAdId⊕Manual-regler).
- Tester: ManualPosting VO-invarianter, Application.Create-invariant, create-command happy/validation-fail/cross-user, read-handler ManualPosting-fallback.

**Frontend (STOPP 3b):** `lib/dto/applications.ts` (+ `jobAdSummaryDtoSchema`, `manualPosting`-write-schema), `lib/types/applications.ts`, `(app)/ansokningar/page.tsx`, `[id]/page.tsx`, `ny/page.tsx` (nya fält + jobAdId-villkorad validering/göm), ny `ApplicationRow`, ny `JobInfoPanel`, `StatusEditCard` (ersätter `status-card.tsx`), ny `components/ui/radio-group.tsx`, ev. radera orphaned `application-card.tsx`/`status-card.tsx` (§9.6, grep-bevis 0 ref), tester. `add-follow-up-form`/`add-note-form`/`record-follow-up-outcome-form` oförändrade (v1-vinster behålls).

**ADR:** ny `docs/decisions/0048-*.md` (Accepted direkt, §1.4). **Reviews:** datamodell-architect + cto-rev2 + (STOPP 3) gates.

## 10. STOPP 2 review-utfall (genomfört 2026-05-17)

- **senior-cto-advisor** (ac00cccfcd6962a67) → `docs/reviews/2026-05-17-fas3-ansokningar-plan-cto.md`: Variant A (entydigt, §4); backend-arkitektur korrekt; **BLOCKER** query-filter skärpt (§1.2/§1.4); **ADR 0048 (Proposed) krävs** (§1.4) — cross-aggregat-join-precedens vs ADR 0043; **Klas-STOPP för ADR-precedensbeslutet**.
- **design-reviewer** (afec597ccb5bb3d2e) → `docs/reviews/2026-05-17-fas3-ansokningar-plan-design.md`: plan godkänd i riktning, inga Plan-Block; 5 spec-luckor **L1–L6 instängda i planen** (§5 L1/L2/(b)/1-övergång, §3 L5, §8 L3/L4/L6/Area1). Radio-group bekräftat rätt mönster (1–3 övergångar). Bindande **render-VETO (light+dark+interaktion, ADR 0047 Area 5)** vid STOPP 3 före FAS 3-stängning.

## 10b. STOPP A review-utfall (skrivväg tillagd, genomfört 2026-05-17)

- **dotnet-architect** (a4c1483aeaee7fcea) → `docs/reviews/2026-05-17-fas3-ansokningar-datamodell-architect.md`: skrivväg = **Variant A `ManualPosting` value object** (entydigt; Variant B avvisad — 3 kod-verifierade invariant-brott + GDPR-ACB; lösa kolumner = primitive obsession). Ingen Klas-STOPP för A/B. Scope-flagga: migration + Fas-1-kod-touch.
- **senior-cto-advisor rev2** (ad50e53e28872171d) → `docs/reviews/2026-05-17-fas3-ansokningar-plan-cto-rev2.md`: plan rev2 godkänd, 3 bindande justeringar, ingen ny Klas-STOPP:
  - **J1 (infoldat):** `JobAdSummaryDto.PublishedAt` → `DateTimeOffset?` nullable; manuell → null + "Publicerad"-rad utelämnas; `Application.CreatedAt` aldrig som PublishedAt (2 change-reasons, Martin 2017 kap. 7). §1.1/§1.5/§7.
  - **J2 (infoldat):** ADR 0046 Beslut 1 D-konsekvens: skrivvägen rör Fas-1-byggd `CreateApplicationCommand`-kod → **FAS 3-stängnings-DoD måste omfatta den utvidgade create-vägen** (ej bara läsväg). Notering, ej ADR 0046-amendment (Klas för-auktoriserade skrivvägen i STOPP A-direktivet).
  - **J3 (infoldat):** **STOPP 3a backend = EN atomisk batch** (Domain ManualPosting+invariant / EF-mappning+migration / CreateApplicationCommand / 3 read-handlers ManualPosting-fallback / tester) — får EJ splittas så write-utan-matchande-read når main (broken intermediate state = exakt defekten Klas underkände 2 ggr; memory `feedback_di_with_handlers_same_commit`). 3a (backend atomisk) ↔ 3b (frontend) -split bevaras med Klas-STOPP emellan.

**Status:** STOPP 2 + STOPP A infoldade (L1–L6, query-filter-blocker, Variant A save-strategi, ManualPosting-skrivväg Variant A, J1–J3). ADR 0048 = **Accepted direkt** (Klas-beslut, §1.4). **Väntar Klas STOPP A-granskning** av: reviderad plan + `datamodell-architect`- + `cto-rev2`-rapport + ADR 0048. Därefter STOPP 3a (backend atomisk) → STOPP Klas + SQL-verifiering → STOPP 3b (frontend) → STOPP Klas live-verify → deploy. /jobb = separat tråd efter /ansokningar godkänd.
