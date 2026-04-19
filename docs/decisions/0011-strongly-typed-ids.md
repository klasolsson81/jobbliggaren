# ADR 0011 — Strongly-typed IDs som `readonly record struct`

**Datum:** 2026-04-19
**Status:** Accepted
**Kontext:** Fas 0 / Fas 1-gräns, inför första aggregate. Formaliserar löfte i ADR 0001 + CLAUDE.md §2.2.
**Beslutsfattare:** Klas Olsson
**Relaterad:** ADR 0001 (DDD-principer), CLAUDE.md §2.2, BUILD.md Bilaga B

---

## Kontext

ADR 0001 och CLAUDE.md §2.2 specificerar att aggregat refererar varandra **enbart via strongly-typed IDs**, aldrig via direkta objekt. BUILD.md Bilaga B listade `NNNN-strongly-typed-ids.md` som planerad ADR att skriva innan det första aggregatets ID-typ definieras.

Inför Fas 1 och skapandet av det första aggregatet (`JobAd`) formaliseras mönstret. Utan dokumenterat beslut riskeras inkonsekvens: olika aggregat-IDs implementeras på olika sätt av olika agenter eller i olika sessioner.

Det konkreta problemet som strongly-typed IDs löser är kompilator-fångade ID-missmatcher. Med `Guid` direkt kan man av misstag skicka ett `jobSeekerId` till ett fält som förväntar sig `jobAdId` — kompilatorn klagar inte, testerna kanske inte täcker det, och felet syns i runtime. Med separata typer är det ett kompileringsfel.

## Beslut

Varje aggregate root får en egen ID-typ definierad som `readonly record struct` i domänprojektet:

```csharp
public readonly record struct JobAdId(Guid Value);
```

Konventionen är `<AggregateName>Id(Guid Value)` — ett par rader, ingen abstrakt basklass, ingen extern dependency.

## Alternativ övervägda

### Alt A — Plain `Guid` per fält (nuläge utan beslut)
**För:** Inget extra kod, direkt EF Core-stöd utan konfiguration, bekant för alla .NET-utvecklare.
**Emot:** Ingen type safety — `JobAdId` och `JobSeekerId` är utbytbara i kompilatorn. ID-mismatch-buggar är tysta och svåra att hitta. Kodbas med många aggregat bli svårläst (`Guid id` säger inget om vad det är).

### Alt B — `readonly record struct <Aggregate>Id(Guid Value)` (valt)
**För:** Kompilatorn fångar ID-mismatch direkt. `record struct` ger gratis equality, deconstruction och `ToString`. `readonly` förhindrar mutation. Stack-allokerat — ingen GC-overhead. ~3 raders boilerplate per aggregat.
**Emot:** EF Core kräver explicit value conversion i entity configuration. JSON-serialisering kräver custom converter om ID:t exponeras direkt i API-svar (löses i Api-lagret per behov).

### Alt C — Class-baserad ID med custom `Equals`/`GetHashCode`
**För:** Mer flexibel; kan bära extra metadata.
**Emot:** Heap-allokerat (GC-overhead för värden som skapas/förstörs i varje query). Mer boilerplate för equals-logiken. `record struct` ger samma utan nackdelarna.

### Alt D — Vogen library för strongly-typed primitives
**För:** Genererar ID-typer automatiskt, inklusive EF Core-converters och JSON-converters.
**Emot:** Extra dependency (BUILD.md §3.1: nya bibliotek kräver motivering). Ger noll arkitekturellt mervärde utöver vad `readonly record struct` ger gratis. Binding till ett tredjepartsbibliotek för en 3-raders konstrukt är oproportionerligt.

## Konsekvenser

### Positiva
- Kompilatorn fångar ID-mismatch — `JobAdId` kan inte tilldelas ett `JobSeekerId`-fält.
- `record struct` ger gratis value equality: `new JobAdId(id) == new JobAdId(id)` är `true` utan extra kod.
- Koden är självdokumenterande: `JobAdId id` är tydligare än `Guid jobAdId`.
- Stack-allokerat: ingen GC-overhead för den vanligaste operationen i en query-tung applikation.
- Noll externa dependencies — standardkonstrukt i C# 14 (record struct tillgängligt sedan C# 10).

### Negativa
- ~3 raders boilerplate per aggregat (oacceptabelt liten kostnad, men återkommande).
- EF Core kräver explicit value conversion i entity configuration per ID-typ:
  ```csharp
  builder.Property(x => x.Id)
         .HasConversion(id => id.Value, value => new JobAdId(value));
  ```
- JSON-serialisering av ID-typen direkt i API-svar kräver custom `JsonConverter<JobAdId>` (alternativt: mappa alltid till `Guid` i DTO innan det lämnar Application-lagret — rekommenderat).

### Mitigering
- Boilerplate kan genereras av en framtida `add-aggregate`-scaffolding-skill — ett litet investerat steg per aggregat, inte ett pågående kostnadsproblem.
- DTO-mappning till `Guid` i Application-lagret eliminerar JSON-converter-behovet för API-svar.

## Implementation

Mönstret introduceras i Fas 1 med `JobAdId` som första instans. Alla efterföljande aggregat följer samma konvention.

**Påverkar:**
- `JobbPilot.Domain` — innehåller ID-typen, ingen dependency utåt
- `JobbPilot.Infrastructure` — entity configuration lägger till `HasConversion`
- `JobbPilot.Application` — handlers och queries använder typen, DTOs mappar till `Guid`

**Scaffolding (framtida):** `add-aggregate`-skill bör generera ID-typen automatiskt som del av aggregate-scaffolding.

## Referenser

- ADR 0001 — Clean Architecture med DDD (§ aggregat och IDs)
- CLAUDE.md §2.2 — "Aggregates refererar varandra **endast via strongly-typed IDs**, aldrig direkta objekt"
- BUILD.md Bilaga B — planerade ADRs
