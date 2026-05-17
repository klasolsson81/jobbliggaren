# Retroaktiv CTO-triage — Resume.SoftDelete idempotens-guard (commit 62c9dc7)

**Datum:** 2026-05-17
**Agent:** senior-cto-advisor (agentId a08e5bea1cf9f90eb) — decision-maker per CLAUDE.md §9.6
**Anledning:** Commit `62c9dc7` buntade av misstag CC B:s Resume.SoftDelete-idempotens-arbete via delat git-index. Den CTO-triage CC B:s uppgift ("Resume.SoftDelete idempotens-guard — CTO-triage") skulle fått före commit utfördes inte. Denna triage körs retroaktivt.

## Beslut

**Disposition (a): BENIGN consistency-alignment. CTO-clearable — kräver INGET Klas-beslut (kod-mässigt), endast medveten retroaktiv process-kvittens. Ingen domän-uppföljning. Ingen TD.**

## Motivering (empirisk konventionskälla)

| Aggregat | `SoftDelete` root-guard | Källa |
|---|---|---|
| `Application` | `if (DeletedAt.HasValue) return;` (rad 131) | Redan på main före 62c9dc7 |
| `JobSeeker` | `if (DeletedAt.HasValue) return;` (rad 79) | Redan på main |
| `SavedSearch` | `if (DeletedAt.HasValue) return;` (rad 106) | Redan på main |
| `Resume` | `if (DeletedAt.HasValue) return;` (rad 165) | **Tillagd av 62c9dc7** |

3/4 aggregate-roots hade redan exakt guarden med identisk struktur (guard → `DeletedAt` → child-cascade → `RaiseDomainEvent`). Resume var avvikaren. 62c9dc7 tog bort avvikelsen.

- **DDD (Evans 2003 kap. 5-6):** soft-delete är en terminal state-transition; dubbel-exekvering + dubbelt `ResumeDeletedDomainEvent` påstår ett falskt historiskt faktum ("events är sanningen", CLAUDE.md §2.2). Guarden upprätthåller en invariant som alltid borde funnits — den ändrar inte kontraktet.
- **SRP/OCP (Martin 2017 kap. 7-8):** publik signatur `void SoftDelete(IDateTimeProvider)` oförändrad; enkel-anropare ser noll skillnad; endast den defekta dubbel-anropsvägen ändras.
- **Fowler 2018 kap. 1:** bug-fix mot oskriven men codebase-etablerad invariant, inte novel design-ändring.

## Disposition (operativ)

1. **Leave** — koden stannar oförändrad på main (korrekt + konventionsenlig).
2. **Forward-attribution-commit** — attribuerar Resume.SoftDelete till parallell CC + refererar denna trail + 62c9dc7. Återställer §6.3-mekanism 3 (agent-invocation) post-hoc.
3. **Retroaktiv review-trail** — denna fil + security-auditor-filen.

De +2 ResumeTests-testerna behålls (korrekt regressionsskydd för den nu-upprätthållna invarianten).

## Sidoobservation — explicit triagerad till ICKE-TD

Child-guard-konsistens ojämn på main, **oberoende av denna commit**: `ResumeVersion.SoftDelete` har guard; `ApplicationNote.SoftDelete` (rad 37) + `FollowUp.SoftDelete` (rad 57) saknar. Bedöms benign + icke-TD: child-`SoftDelete` är `internal`/anropas endast via parent vars root-guard nu finns på alla fyra → child-dubbelanrop oåtkomligt (defense-in-depth, ej korrekthetsinvariant). Att lyfta som TD vore §9.6-anti-mönster. Opportunistisk alignment vid nästa naturliga Applications-child-touch — inte nu, inte som TD.

## Behöver Klas-beslut?

**Nej, kod-mässigt.** Entydigt motiverat mot principer. Klas ska medvetet **kvittera process-incidenten** (parallell-CC delade git-index → CTO-gate hoppades över) via manuell diff-granskning av forward-attribution-commiten (§6.3-mekanism 4) — inte besluta koden. Om Klas vill härda parallell-CC-isolering är det en separat process-fråga som inte blockerar denna disposition.
