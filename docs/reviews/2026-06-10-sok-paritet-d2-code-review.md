# code-reviewer — Platsbanken sök-paritet Fas D2 (ISearchQueryParser)

**Datum:** 2026-06-10
**Status (initial):** ⚠ Changes requested (1 Major)
**Status (efter in-block-fix):** ✓ Major åtgärdad — se Resolution nedan.
**Scope:** Backend — Application + Domain (port/DTO/impl + handler-wiring + DRY-const + tester). Granskar efterlevnad mot redan-fattade decision-domar (Variant A+A, Application-only, reconciliation=notat).

## Blockers
Inga.

## Major

**1. Surrogat-split vid trunkering → lone surrogate → nedströms Npgsql-krasch (bryter "kastar ALDRIG"-garantin).**
`SearchQueryParser.cs` (trunkeringen): `sb.ToString(0, QMaxLength)` arbetar på UTF-16 code units. Om tecken nr 100 är ett surrogatpar som straddlar gränsen → output slutar på lone high surrogate → ogiltig UTF-16 → kan ej UTF-8-enkodas → runtime-exception när ResidualQ når Postgres via JobAdFilterCriteria.Q. Reachable via rå user-input (≥100-tecken emoji-tung sträng). Bryter skriven invariant (XML-doc "kastar ALDRIG / 2-100 tecken"). Per §9.6 nuvarande fas → fix in-block, ej TD.
Föreslagen åtgärd: rune-säker trunkering (backa till sista hela rune-gränsen) + surrogat-gränstest.

### Resolution (in-block, 2026-06-10)
Trunkeringen backar nu till rune-gränsen: `if (char.IsHighSurrogate(sb[cut - 1])) cut--;` innan `sb.ToString(0, cut)`. Två nya tester: `Parse_OverQMaxLengthEndingInSurrogatePair_DoesNotSplitSurrogate` (99 'x' + 👨 → ingen lone surrogate, längd ≤ QMaxLength, alla runes dekodbara) + `Parse_OverQMaxLengthWithSurrogatePairInsideCut_KeepsCompletePair`. Application-sviten 728 grön, format-verify exit 0. Major stängd.

## Minor (per §9.6)

**1. Binär git-blob för SearchQueryParserTests.cs** (literala NUL/BEL/ZWSP/RTL som testdata). Dom: **acceptera, lyft inte** — legitim testdata som testar exakt det parsern härdar mot; escape-omskrivning flyttar verifieringen från faktiskt byte-beteende. (Valfri `.gitattributes`-touch ej merge-krav; ej utförd för att undvika mangling av null-byte-fil.)
**2. `Action act = () => result = _sut.Parse(...)` med `default!`-init** — korrekt/läsbart i Shouldly-kontext, ingen åtgärd.

## Granskade punkter

| Punkt | Utfall |
|---|---|
| Clean Arch / Dependency Rule (§2.1) | ✓ Port+DTO i Abstractions, impl internal i Internal, ren CPU (System.Globalization/Text = BCL, tillåtet) |
| CQRS-adapter + SPOT (§2.3) | ✓ Tunn adapter; en SPOT (JobAdFilterCriteria.Q). RunSavedSearch parsar EJ om persisterat redan-validerat Q — scope-korrekt, ej SPOT-brott |
| Kraschsäkerhet topologi | ✓ Q → enbart OR-gren, dimensioner separata AND-listor (verifierat). Värde-nivå-hålet (surrogat) åtgärdat |
| Domain-ändring (§2.2) | ✓ private→public const behavior-preserving, inga invarianter rörda |
| Unicode-normalisering | ✓ EnumerateRunes + IsWhiteSpace FÖRE Cc/Cf-strip korrekt; trunkering nu rune-säker |
| §5-antipatterns/namngivning | ✓ Ingen magic string (Domain-const-referens), DI singleton korrekt, DI samma commit, InternalsVisibleTo motiverad |
| Test-täckning (§2.4) | ✓ 32 parser-fall + 5 handler-fall + 4 Testcontainers-integ (EJ InMemory). 3 integ-filer korrekt uppdaterade för 2-arg ctorn |

## Bra gjort
- internal sealed + InternalsVisibleTo scopad till exakt två testprojekt — minimal ytläcka
- Integ-tester mot riktig Postgres låser kraschvägen InMemory döljer
- Whitespace-FÖRE-Cc/Cf-ordningen korrekt + motiverad i kommentar
- RunSavedSearch-icke-parsning medvetet dokumenterat SPOT-beslut
- DRY-konsolidering 2/100 → Domain-const

**Slutdom:** Mergeklar efter in-block-fix av Major.
