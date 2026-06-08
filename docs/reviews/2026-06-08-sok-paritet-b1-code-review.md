# Code-review — Platsbanken sök-paritet Fas B1 (data-layer, Klass 1)

**Datum:** 2026-06-08
**Agent:** code-reviewer (agentId `aef2328aba967a6ac`) — veto-auktoritet, sista kvalitetsgrind före commit.
**Status:** ✓ **Approved — GO för commit.** 0 Blockers, 0 Majors, 3 Minors (alla behåll-som-är).
**security-auditor:** krävs INTE för B1 (motivering nedan).

## Scope
Endast data-layer (ADR 0067 Beslut 2 + ADR 0043-amendment). Två STORED generated columns på job_ads (municipality + occupation_group/ssyk-level-4) + EF-config + migration + taxonomi-snapshot-utökning + seeder MapRows + Kind-enum. Domain & Application medvetet orörda.

## Verifierade granskningspunkter (7/7)

1. **AppDbContextModelSnapshot WaitlistEntry-konsolidering — BENIGN, behåll.** HEAD deklarerade WaitlistEntry tre gånger (entitetskropp + redundant `OwnsOne Acceptance` + kanonisk relations-`OwnsOne`). EF-regenereringen tog bort exakt det redundanta blocket. Working tree behåller entitetskropp + ETT kanoniskt AcceptanceSnapshot owned-type (alla kolumner) + Navigation. Testcontainers migration-apply grönt bevisar att snapshot matchar modellen. Att isolera vore att handredigera genererad fil för att återinföra dubblett — strikt sämre. Behåll.
2. **occupation_group TOP-LEVEL path — KORREKT.** Matchar POCO (top-level, ej nested). Path-förväxlings-spärr bevisar mot riktig Postgres.
3. **STORED ADD = full table rewrite/ACCESS EXCLUSIVE — KORREKT dokumenterat.** Backfill från befintlig raw_payload (34843/33935), ingen re-ingest. Sanitizer-allowlist substantierad.
4. **generate.mjs (Variant C) — UTMÄRKT.** Additiv (2323 occupations bevarade), deterministisk (Ordinal-sort), fail-loud (multi-parent + orphan), ingen npm-dep. 0 duplicate concept-ids över hierarkin.
5. **B1/C1-gräns — INGEN läcka.** LoadAsync + Application orörda. Exakt CTO Variant A.
6. **SqlQueryRaw snake_case-fix — KORREKT.** Råa kolumnnamn matchar EF:s namnkonvention.
7. **security-auditor BEHÖVS INTE.** Generated columns extraherar publika JobTech-taxonomi-referenskoder (icke-PII). Ingen ny endpoint, ingen auth-ändring, ingen ny loggyta (seeder loggar rowCount+version). Sanitizer-allowlist (default-deny) strippar rekryterar-PII före raw_payload. ADR 0067 placerar security-auditor i Fas C2 (VO-cap) + Fas D — ej B1.

## Sex granskningsområden
- **Clean Architecture (§2.1):** Intakt — Domain orörd, generated columns shadow-props i Infrastructure (ACL).
- **DDD (§2.2):** Korrekt — taxonomi Infrastructure-intern, TaxonomyConceptKind internal.
- **CQRS (§2.3):** Ej i scope (ingen handler i B1) — korrekt avgränsat.
- **Tester (§2.4):** Stark — architect-TDD-ordning uppfylld (Testcontainers-migration inkl. path-spärr, MapRows parent-relationer, unique-id, seeder-idempotens, null-nested bakåtkompat). InMemory undviket.
- **Konventioner (§3):** File-scoped namespaces, nullable, `?? []`, Async-suffix, IReadOnlyList.
- **Anti-patterns (§5.1):** Inga.

## Minor (FYI — behåll)
1. Hårdkodade 290/400 i round-trip-test — parat med drift-robust relations-assertion; medveten paritets-baseline-ratchet (project_platsbanken_parity_baseline).
2. LF→CRLF-varningar — kosmetiskt, dotnet format/.gitattributes hanterar.
3. generate.mjs saknar CI-koppling — by design (off-build, Variant C).

## Testresultat (alla gröna mot Testcontainers, ej InMemory)
Unit 630, JobAdGeneratedColumns 5, TaxonomyReadModel-integration 10, Architecture 78, ListJobAdsFilter 13 (regression).

**Verdikt: GO för commit. security-auditor krävs ej för B1.** Vid C1/C2 gör code-reviewer ny holistisk pass (security-auditor relevant för VO-cap i C2).
