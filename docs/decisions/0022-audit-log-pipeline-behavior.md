# ADR 0022 — Audit log-strategi: pipeline-behavior med marker-interface

**Datum:** 2026-05-08
**Status:** Accepted
**Kontext:** STEG 8 — Audit log-infrastruktur (BUILD.md §18 Fas 1, stänger TD-9)
**Beslutsfattare:** Klas Olsson
**Relaterad:** ADR 0008 (pipeline-ordning, kompletteras), ADR 0009 (no-repository), ADR 0010 (Worker-projekt), BUILD.md §5.5, §7.1

## Kontext

GDPR Art. 5(2) kräver att behandling av personuppgifter kan redovisas (accountability). JobbPilots `Application`-aggregat (skapande, status­övergångar, noteringar, uppföljningar) och `Resume`-aggregat (skapande, omdöpning, master-uppdatering, radering) muterar PII utan audit-trail. `IAuthAuditLogger` finns men är bunden till auth-flödet (login/logout) och skriver bara strukturerad logg, inte till databas.

BUILD.md §18 listar "Audit log-infrastruktur" som Fas 1-leverans. BUILD.md §7.1 specar `audit_log`-tabellen.

Frågan är **hur audit-skrivningar ska triggas och kopplas till command-pipelinen**:

- **Alt A — Inline i handler.** Explicit `_auditLogger.Log(...)` per handler.
- **Alt B — Pipeline-behavior.** Ny `AuditBehavior` som inspekterar marker-interface på command och skriver audit-rad efter handler.
- **Alt C — Domain event subscriber.** Lyssnar på domain events som redan raisas på aggregaten.

Kontextuella krafter:

1. CLAUDE.md §2.2 säger "Ändringar raisar domain events — events är sanningen, handlers reagerar". Det pekar mot Alt C.
2. **Ingen domain event dispatcher finns idag** — domain events raisas på aggregat men plockas inte upp någonstans i kodbasen. Att införa en dispatcher är ett separat arkitekturbeslut (egen ADR + egen infrastruktur).
3. Pipeline-infrastrukturen är etablerad (ADR 0008): Logging → Validation → Authorization → UnitOfWork. UnitOfWorkBehavior anropar `SaveChangesAsync` efter handler.
4. Atomicitet är ett krav: audit-raden får inte skrivas separat från data-mutationen. Annars kan en lyckad handler följas av en misslyckad audit-write (eller vice versa) och accountability bryts.

## Beslut

**Alt B — pipeline-behavior med marker-interface**, placerad **innerst i pipelinen (registreras efter UnitOfWorkBehavior)** så att audit-raden läggs till `IAppDbContext` av AuditBehavior:s post-action och persisteras i samma `SaveChangesAsync`-anrop som data-mutationen.

### Pipeline-ordning (uppdaterar ADR 0008)

Registreringsordning i `Api/Program.cs` (yttersta först, innerst sist):

```
Logging → Validation → Authorization → UnitOfWork → Audit → Handler
```

Pipeline-flöde:

1. Pre-actions löper utifrån-och-in: `Logging → Validation → Auth → UoW → Audit → Handler`
2. Handler returnerar (data-entity ligger i DbContext, ingen SaveChanges än)
3. Post-actions löper inifrån-och-ut: `Audit.post → UoW.post → Auth.post → Validation.post → Logging.post`
4. **`Audit.post`** skapar `AuditLogEntry` och anropar `db.AuditLogEntries.Add(...)` — fortfarande ingen SaveChanges
5. **`UoW.post`** anropar `SaveChangesAsync` — handler-mutationen och audit-raden persisteras atomiskt i samma transaction

ADR 0008 förblir oförändrad enligt immutable-policyn. `AuditBehavior` är ett additivt 5:e behavior placerat innerst (närmast handler). Architecture tests verifierar registreringsordningen.

### Marker-interface

`IAuditableCommand` med två-fas-design för att hantera Create-fall där aggregate-ID inte är känt förrän handler kör:

```csharp
public interface IAuditableCommand
{
    string EventType { get; }
    string AggregateType { get; }
}

public interface IAuditableCommand<TResponse> : IAuditableCommand
{
    Guid ExtractAggregateId(TResponse response);
}
```

Commands implementerar `IAuditableCommand<Result<Guid>>` (Create-fall, läser ID från response) eller `IAuditableCommand<Result>` (mutation av befintligt aggregat, läser ID från command-fältet). `AuditBehavior<TCommand, TResponse>` constraint:as på `IAuditableCommand<TResponse>` så bara markerade commands triggar audit.

### Failure-paritet

Audit skrivs **endast på success** i Fas 1. Runtime-check i `AuditBehavior`:

```csharp
if (response is Result r && r.IsFailure) return response;
```

Mutations-failures (validation, NotFound, domain-invariant-brott) loggas redan strukturerat via `LoggingBehavior`. Failed-attempts-audit är säkerhetsrelevant först i Fas 6 (impersonation, admin-actions) och retro-fittas då.

### Audit-radens innehåll i Fas 1

Per BUILD.md §7.1-schema. Fält som **fylls** i Fas 1:

- `id` (Guid)
- `occurred_at` (DateTimeOffset, UTC, från `IDateTimeProvider`)
- `correlation_id` (Guid, från ny `ICorrelationIdProvider`)
- `user_id` (nullable Guid, från `ICurrentUser` — null vid system-jobb)
- `event_type` (string, från `IAuditableCommand.EventType`)
- `aggregate_type` (string, från `IAuditableCommand.AggregateType`)
- `aggregate_id` (Guid, från `ExtractAggregateId(response)`)
- `ip_address` (nullable string, från ny `IRequestContextProvider`)
- `user_agent` (nullable string, från `IRequestContextProvider`)

Fält som **lämnas null** i Fas 1:

- `impersonated_by` — fylls i Fas 6 när impersonation införs
- `payload` — JSONB-kolumnen reserveras men förblir `null`. PII-risken (CV-text, sökord) är reell och en sanerad payload kräver per-command `GetSafePayload()`-metod. Defereras till Fas 4 när retention-jobb och audit-detalj-krav konkretiseras.

### Nya abstraktioner

| Port | Lager | Implementation |
|------|-------|----------------|
| `ICorrelationIdProvider` | Application/Common/Auditing | `CorrelationIdProvider` i Infrastructure (läser `X-Correlation-Id`-header eller genererar Guid per request, stash:ar i `HttpContext.Items`). Worker får trivial impl när Worker-pipeline aktiveras (Fas 2). |
| `IRequestContextProvider` | Application/Common/Auditing | `RequestContextProvider` i Infrastructure (läser `Connection.RemoteIpAddress` + `Request.Headers.UserAgent`). |

`IAuditableCommand[<T>]` ligger i `Application/Common/Auditing/` (separat folder från `Common/Abstractions/` — audit är eget koncept med egna portar och behavior).

### AuditLogEntry som flat entity

`AuditLogEntry` placeras i `Domain/Auditing/` som **flat entity, ej aggregate root**. Inga invarianter, inga domain events, write-only från behavior. Strongly-typed `AuditLogEntryId` per ADR 0011. Static factory `Create(...)` tar alla värden inklusive `occurred_at` (sätts av behavior via `IDateTimeProvider`, inte av entity:n själv).

Domain-namespacet motiveras av att `AuditLogEntry` är en del av JobbPilots compliance-modell, inte ett infrastruktur-bekymmer. Den är dock medvetet enklare än övriga aggregat — Clean Arch-kravet är att `Domain` inte beror på något, vilket uppfylls.

### IAppDbContext-utökning

`DbSet<AuditLogEntry> AuditLogEntries { get; }` läggs till i `IAppDbContext`. Audit-läs-endpoints (admin-vy) introduceras i Fas 6 via egen ADR.

### Worker-projektet

Per ADR 0010 är `JobbPilot.Worker` separat composition root med tom shell i Fas 0–1 (no-op `Worker.cs`). Worker-projektet registrerar **inte** Mediator/Application/Infrastructure ännu — `Worker/Program.cs` är minimal `Host.CreateApplicationBuilder` + `AddHostedService<Worker>`. När första Worker-jobbet aktiveras (Fas 2 JobTech-sync, Fas 3 GhostedDetection):

- Worker registrerar Mediator + AddApplication + AddInfrastructure med samma pipeline-ordning som Api
- Worker:s DI-container får stub-implementationer av `ICurrentUser` (returnerar `IsAuthenticated = false`, `UserId = null`), `ICorrelationIdProvider` (genererar Guid per scope), `IRequestContextProvider` (returnerar null:er)
- Audit-rader från system-jobb får `user_id = NULL` cleant

Detta deferras till Fas 2 — denna ADR dokumenterar bara intentionen.

## Konsekvenser

### Positiva

- **Atomicitet:** audit-rad och data-mutation persisteras i samma `SaveChangesAsync` → garanterad accountability-paritet
- **Regression-skydd:** marker-interface gör opt-in explicit; nya audit-pliktiga commands missas inte (architecture test verifierar att alla `Commands.*`-namespacen följer mönstret där relevant)
- **Inga handler-modifieringar:** alla 10 befintliga commands får audit "gratis" genom att bara implementera marker-interface (inga ändringar i handler-kod)
- **Stänger TD-9** (Major GDPR Art. 5(2))
- **Naturlig växtväg:** Fas 4 lägger till `payload` (sanerad) via `GetSafePayload()`-metod; Fas 6 lägger till `impersonated_by`. Inga breaking changes.

### Negativa

- **Tightly coupled till Mediator-pipeline:** audit kräver att alla mutations går via Mediator. EF Core-direktanrop (om sådana smyger in) auditeras inte. Mitigeras av att JobbPilot redan har detta arkitekturkrav (CQRS via Mediator är icke-förhandlingsbart per CLAUDE.md §2.3).
- **Domain events utnyttjas inte:** även om events raisas på aggregaten används de inte för audit. När event dispatcher införs (för andra ändamål, t.ex. integration events i Fas 5) kan audit-strategin omprövas — men inte en regression.
- **Fas 1-audit saknar payload:** "vad ändrades exakt?" går inte att se från audit-raden ensamt. Måste korreleras med data-rader via `aggregate_id` + `occurred_at`. Acceptabelt för Fas 1 enligt GDPR Art. 5(2)-tolkningen "vem-gjorde-vad-på-vilket-aggregat-när".

### Mitigering

- Architecture tests verifierar pipeline-ordning + AuditLogEntry-isolering + IAuditableCommand-namespace-konvention
- Integration tests verifierar audit-paritet på alla 10 markerade commands (audit-rad skrivs vid success, ej vid failure)
- ADR-uppdatering vid Fas 4 om payload-modellen behöver utökas

## Alternativ övervägda

### Alt A — Inline i handler

Avvisat. Skulle kräva `_auditLogger.Log(...)` i 10 handlers. Regression-risk när nya handlers läggs till. Duplikation av extraktions-logik (UserId, CorrelationId, IP, UserAgent från ICurrentUser/ICorrelationIdProvider/IRequestContextProvider). Inget arkitekturellt vinst jämfört med Alt B.

### Alt C — Domain event subscriber

Avvisat på grund av att **ingen domain event dispatcher finns idag**. Att införa en dispatcher kräver:

- Hook i `SaveChangesAsync` som plockar upp events från `AggregateRoot.DomainEvents`
- Mediator.Publish-anrop (eller egen subscriber-registrering)
- Transactional outbox eller in-process atomicitet

Det är ett separat arkitekturbeslut med egen ADR. STEG 8-scopet är audit log, inte event-infrastruktur. När dispatcher införs (för Fas 5-integration events eller Fas 4 AI-jobb-trigger) kan audit-strategin omprövas — men det är en framtida fråga.

Tradeoff: Alt C hade gett bättre separation (audit lyssnar på events, inte commands) och täckt även mutations som inte går via Mediator. Men dispatcher-bygget är mer scope än Fas 1 audit-leveransen kräver.

### Alt D — AuditBehavior före UnitOfWork (yttersta sidan av UoW)

Övervägt och avvisat. Om Audit placeras *före* UoW i registreringen (= utanför UoW i call-stacken), skulle Audit:s post-action köra *efter* UoW redan har gjort SaveChanges. Att lägga audit-rad i DbContext då skulle kräva en separat SaveChanges-anrop — bryter atomicitet. Om audit-write misslyckas (DB tappar anslutning, constraint-fel) är data-mutationen redan committad. Korrekt placering är **innerst** (registreras efter UoW): Audit:s post-action lägger entity i DbContext, UoW:s post-action persisterar allt i en gemensam SaveChanges.

## Implementation

**Domain:**
- `src/JobbPilot.Domain/Auditing/AuditLogEntry.cs`
- `src/JobbPilot.Domain/Auditing/AuditLogEntryId.cs`

**Application:**
- `src/JobbPilot.Application/Common/Auditing/IAuditableCommand.cs`
- `src/JobbPilot.Application/Common/Auditing/ICorrelationIdProvider.cs`
- `src/JobbPilot.Application/Common/Auditing/IRequestContextProvider.cs`
- `src/JobbPilot.Application/Common/Auditing/AuditBehavior.cs`
- `src/JobbPilot.Application/Common/Abstractions/IAppDbContext.cs` (utökas med `DbSet<AuditLogEntry>`)
- 10 commands märks med `IAuditableCommand<TResponse>` (5 Application + 5 Resume)

**Infrastructure:**
- `src/JobbPilot.Infrastructure/Persistence/Configurations/AuditLogEntryConfiguration.cs`
- `src/JobbPilot.Infrastructure/Auditing/CorrelationIdProvider.cs`
- `src/JobbPilot.Infrastructure/Auditing/RequestContextProvider.cs`
- `src/JobbPilot.Infrastructure/DependencyInjection.cs` (registrerar nya portar)
- Migration `AddAuditLogTable` per BUILD.md §7.1-schema

**Api:**
- `src/JobbPilot.Api/Program.cs` (pipeline-ordning utökad: `..., AuthorizationBehavior, UnitOfWorkBehavior, AuditBehavior` — Audit innerst)

**Tester:**
- Domain unit tests (AuditLogEntry-invariant)
- Behavior unit tests (success, failure, exception, missing user, command-utan-marker)
- Integration tests för audit-paritet på alla 10 commands
- Architecture tests (AuditLogEntry-isolering, namespace-konvention)

**Migration-noteringar:**
- Tabellnamn: `audit_log` (per BUILD.md §7.1)
- Index: `audit_log (occurred_at DESC)` per BUILD.md §7.2
- Partitionering per dag — DDL-stub kommenterad i migrationen, aktiveras av Fas 4 retention-jobb
- Inga FK-constraints mot `users`/`job_seekers` — audit är write-only och får inte hindras av FK-cascades vid soft-delete

## GDPR-policy

### Art. 5(1)(c) — Data minimization (IP-adress)

`audit_log.ip_address` lagrar **inte** rå IP-adress. Per Breyer-domen (C-582/14) och WP29/EDPB-vägledning klassificeras IP som personuppgift och kräver minimering.

`RequestContextProvider` anonymiserar IP före lagring:

- **IPv4:** sista oktetten nollas (`/24`-mask). Exempel: `192.0.2.123` → `192.0.2.0`.
- **IPv6:** sista 80 bitarna nollas (`/48`-mask). Exempel: `2001:db8::1234:5678` → `2001:db8::`.

Geo-region-data bevaras (ASN, land, region kan härledas), unique fingerprint elimineras. Alternativet — att helt utelämna IP-fältet — avvisades eftersom incident-response (DDoS, brute-force, geo-restricted abuse) kräver minst region-granular data.

### Art. 17 — Right to erasure (kontoradering)

När en användare begär radering av sitt konto (`DELETE /me` i Fas 1, `POST /api/v1/admin/users/{id}/erase` i Fas 6) ska deras audit-spår hanteras enligt följande policy:

**Behålls i 90 dagar för accountability (Art. 5(2) + Art. 17(3)(b)):**
- `correlation_id`, `event_type`, `aggregate_type`, `aggregate_id`, `occurred_at`

**Anonymiseras till `NULL`:**
- `user_id`, `ip_address`, `user_agent`

Anonymiseringen sker i samma transaction som kontoraderingen (cascade-pattern via dedikerat Application-command). Efter 90 dagar partitioneras hela audit-raden bort av retention-jobbet (Fas 4).

**Implementations-status:** policyn är dokumenterad här men implementerad först i Fas 4 (tillsammans med retention-jobbet). Spåras som **TD-16** i `docs/tech-debt.md`. Ingen GDPR-radering är möjlig att utföra korrekt förrän både retention-partitionering och anonymiserings-cascade är på plats.

### Art. 5(1)(e) — Storage limitation (retention)

Audit-rader behålls i **90 dagar** per BUILD.md §7.1. Implementeras via PostgreSQL native partitioning per dag — dagliga partitions skapas/droppas av Hangfire-jobb i Fas 4. Migrationen `20260508062501_AddAuditLogTable` innehåller stub-kommentar som dokumenterar partition-DDL.

**Implementations-status:** retention-partitionering är **inte aktiverad** i Fas 1 — audit-tabellen växer obegränsat tills Fas 4-jobbet byggs. Spåras som del av TD-16. **Innan Fas 1 prod-deploy** måste antingen (a) retention-jobbet aktiveras eller (b) en manuell ops-procedur dokumenteras i `docs/runbooks/audit-retention.md`.

### Server-genererat correlation-ID

`X-Correlation-Id`-header läses **inte** från klientens request (tidigare design). OWASP ASVS V7.1.4 förordar server-genererat correlation-ID som default — klient-controlled correlation öppnar för audit-spoofing där en angripare kan injicera valfri Guid och korrelera sin angreppsaktivitet med en legitim användares trail.

Om klient-correlation behövs i framtiden (för kund-supportärenden eller distribuerade traces) ska det vara ett separat fält (`client_request_id`), aldrig blandat med `correlation_id` i audit-tabellen.

## Status

**Accepted** för Fas 1. Omvärderas vid Fas 4 (när payload-fältet ska aktiveras + retention-jobbet implementeras + GDPR-radering aktiveras) och Fas 6 (när impersonation och admin-läs-endpoints införs). ADR 0008 kompletteras implicit av denna ADR — pipeline-ordning är nu fem behaviors istället för fyra.
