# Plan — /ansokningar + /ansokningar/[id] redesign (FAS 3-reparation)

**Datum:** 2026-05-17
**Status:** STOPP 2 — plan-design, väntar Klas-GO före STOPP 3 (implementation)
**Scope-beslut (Klas 2026-05-17):** backend INKLUDERAS; ny radio-group-komponent OK.
**Bakgrund:** Klas underkände `/ansokningar`-ytorna live (v0.2.14-dev) — UUID-rader utan jobbidentitet, saknad visuell hierarki, fel status-mönster. Discovery (STOPP 1) fann att JobAd-data inte exponeras i Application-read-vägen → backend-utbyggnad krävs.
**Scope:** ENDAST `/ansokningar` (list), `/ansokningar/[id]` (detail), underliggande komponenter, och den backend-read-väg de kräver. EJ `/jobb` (separat tråd). Inga design-token/-skill-ändringar.

---

## 1. Backend-utbyggnad (förutsättning för målbilden)

JobAd-aggregatet (`Title`, `Company.Name`, `Url`, `Source.Value`, `PublishedAt`, `ExpiresAt`) är separat från Application (länk = `JobAdId?`, nullable). Tre read-handlers + DTO:er måste utökas. **Ingen migration** — endast projektion (läsväg), inget schema ändras.

### 1.1 Ny delad read-DTO

```
JobAdSummaryDto(
    Guid JobAdId,
    string Title,
    string Company,
    string Url,
    string Source,          // "Platsbanken" | "Manual" | "LinkedIn"
    DateTimeOffset PublishedAt,
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

dotnet-architect (join-design, **explicit SQL-verifiering = en LEFT JOIN**, query-filter-disciplin, DTO-gräns, Clean Arch) **INNAN kod** · test-writer (handler-tester: med JobAd / utan (jobAdId null) / **soft-deleted JobAd via default-join utan IgnoreQueryFilters → fallback** / cross-user) FÖRST/TDD · security-auditor BLOCKING (JobAd = publik annons-metadata per ADR 0032 §8; Application redan jobSeeker-scopad → ingen cross-user-läcka, men auditor bekräftar att joinen inte kringgår ADR 0031-scoping och att soft-deletad metadata ej läcker) · code-reviewer. Ingen db-migration-writer (ingen migration).

**ADR 0048 (Proposed) KRÄVS (CTO Beslut 2 — avvisar tidigare "ingen ADR"):** Detta är första cross-aggregat-joinen i Application-läsvägen. ADR 0043 Beslut C löste cross-context-läsning via dedikerad `ITaxonomyReadModel`-port specifikt för att INTE införa cross-aggregat-koppling — in-handler-join här är ett medvetet precedensval i kontrast mot ADR 0043 → ADR-värt (Nygard 2011; CLAUDE.md §8.9 DoD). ADR 0048 fastställer: (a) join-i-handler som mönster för enkla samma-DbContext 1:0..1-aggregatlänkar, (b) kontrast/avgränsning mot ADR 0043 port-val (anti-corruption + ADR 0009 gällde där, ej här), (c) query-filter-disciplin (§1.2). **Accepted-flip = Klas-GO (Klas-STOPP, CTO Beslut 4).** In-block, ej scope-creep (ADR = arkitektur-DoD samma touch).

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

## 7. Fallback — Application utan JobAd (vanligt, ej edge)

`ny/page.tsx` skapar ansökningar med endast coverLetter → `jobAdId = null`. Vanligt återkommande fall.
- List-rad: `"Ansökan #{id.slice(0,8)}"` (font-mono), rad 2 = StatusBadge · Uppdaterad (ingen "sök senast").
- Detail: H1 = `"Ansökan #{kort-id}"`, inget företag, JobInfoPanel ersätts av civic-not "Ingen kopplad annons — manuellt skapad ansökan." Status/uppföljningar/noteringar oförändrade.
- Soft-deleted/saknad JobAd trots `JobAdId != null`: behandlas identiskt som null (left join → fallback). Ingen trasig rad.

## 8. Token-disciplin + spec-stängningar (STOPP 2 bindande)

Alla färger/spacing/typografi via jobbpilot-design-tokens (Tailwind-utilities token-backade). Inga hex, inga inline-px (utom Tailwind-spacing-utilities). Svensk copy per jobbpilot-design-copy ("du", ingen emoji/utropstecken, sv-SE datum). Civic-utility per -principles. a11y per -a11y.

**L3 (avgränsning, bindande):** varje detalj-sektion = `<section>` med synlig `<h2>` + `border-strong` informationsbärande avskiljare (≥3:1, ej `border`) — bevara/förstärk v1:s sektionskort-mönster (`[id]/page.tsx:125-128`), riv ej. Split vänster/höger: åtskilda med kolumn-gap (`gap-6`/`gap-8`) + panelerna `border border-border-default rounded-md` — **aldrig** shadow/floating cards (regel 1 papper-ej-glas). Inget "rakt upp och ner"-stapel (defekt 5).

**L4 (typografi-tokens, bindande):** detaljsidan använder **samma** token-system som list-sidan — `jp-h1` (sid-H1 jobtitel), `jp-h2` (sektionsrubriker), H3/`text-h3`-ekvivalent (panelrubriker) per tokens-skill scale. **Ingen** `text-h1`/`text-h3`-blandning från v1 (ADR 0037/0038 hierarki-konsekvens).

**L6 (jobAd==null layout, bindande):** vid jobAd==null faller detaljsidan tillbaka till **single-column** (ingen tom vänsterkolumn) — civic-not "Ingen kopplad annons — manuellt skapad ansöken" ersätter JobInfoPanel-positionen, StatusEditCard + listor full-width. Ingen obalanserad tom canvas (regel 3).

**Area 1-mönsterval (bindande):** list-rad (§2) status = `StatusDot` (dot + text, ingen fyllning — lägst visuell vikt i tät lista, components "first choice in tables"). Detaljhuvudets förankrade nuvarande-status = `StatusPill` (entitets-accent). Ej fylld pill i listan.

## 9. Filer som rörs (estimat — exakt i STOPP 3)

Backend: `ApplicationDto.cs`, `ApplicationDetailDto.cs`, ny `JobAdSummaryDto.cs`, 3 QueryHandlers, handler-tester. Frontend: `lib/dto/applications.ts`, `lib/types/applications.ts`, `(app)/ansokningar/page.tsx`, `[id]/page.tsx`, ny `ApplicationRow`, ny `JobInfoPanel`, `StatusEditCard` (ersätter `status-card.tsx`), ny `components/ui/radio-group.tsx`, ev. radera orphaned `application-card.tsx`/`status-card.tsx` (§9.6), tester. `add-follow-up-form`/`add-note-form`/`record-follow-up-outcome-form` oförändrade (v1-vinster behålls).

## 10. STOPP 2 review-utfall (genomfört 2026-05-17)

- **senior-cto-advisor** (ac00cccfcd6962a67) → `docs/reviews/2026-05-17-fas3-ansokningar-plan-cto.md`: Variant A (entydigt, §4); backend-arkitektur korrekt; **BLOCKER** query-filter skärpt (§1.2/§1.4); **ADR 0048 (Proposed) krävs** (§1.4) — cross-aggregat-join-precedens vs ADR 0043; **Klas-STOPP för ADR-precedensbeslutet**.
- **design-reviewer** (afec597ccb5bb3d2e) → `docs/reviews/2026-05-17-fas3-ansokningar-plan-design.md`: plan godkänd i riktning, inga Plan-Block; 5 spec-luckor **L1–L6 instängda i planen** (§5 L1/L2/(b)/1-övergång, §3 L5, §8 L3/L4/L6/Area1). Radio-group bekräftat rätt mönster (1–3 övergångar). Bindande **render-VETO (light+dark+interaktion, ADR 0047 Area 5)** vid STOPP 3 före FAS 3-stängning.

**Status:** spec-luckor + CTO-ändringar infoldade. **Väntar Klas-GO** på: (1) planen som reviderad, (2) **ADR 0048 (Proposed)-beslut** (Klas-STOPP, CTO Beslut 4), (3) STOPP 3-implementation enligt §1.4-gater + bindande render-VETO. /jobb = separat tråd efter /ansokningar godkänd.
