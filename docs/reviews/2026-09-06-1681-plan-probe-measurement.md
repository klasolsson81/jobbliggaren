# 2026-09-06 — #1681 plan probe: batching the criterion ad count

**Session:** CC, worktree `c:/tmp/jbl-1681`, branch `feat/1681-smart-watches-on-overview`.
**Status:** measurement, not a verdict. Taken BEFORE any production code, to test a design premise.

## The premise under test

To put per-criterion ad counts on `/oversikt`, the obvious move is to batch the N register
queries into one statement — one round trip instead of N. The candidate shape was a constant
statement taking the per-criterion arrays as DATA (`jsonb_to_recordset(@criteria) AS v(i int,
sni text[], kommun text[])`), because `CompanyWatchBrowseQuery.cs` forbids building a batch by
substituting parameter names into the WHERE text: *"a WHERE text that reads correctly only after
substitution is the shape that makes the EXPLAIN pin stop covering production."*

## Instrument

Throwaway `postgres:18.4` container (never the shared dev DB, §6.5). Schema mirrors the delivered
shapes: `company_register` (PK `organization_number`, GIN `array_ops` on `sni_codes`, btree on
`sate_kommun_code` and `status`), `job_ads` with a btree on `organization_number`. 200 000 register
rows, every one `Active` and in kommun `0180` so only the SNI axis discriminates — the seeding
rationale `CompanyRegisterSearchQueryPlanTests` states. One active ad per company. `ANALYZE` on
both. `EXPLAIN (ANALYZE, BUFFERS, COSTS OFF)`, plan CHOICE, no GUC.

Regenerate: the probe SQL is reproduced in the PR body; it is not committed, because a committed
copy would decay against the real schema.

## Result

| Form | Buffers / criterion | Plan |
|---|---|---|
| Today's `AdCountSql`, array parameters, one criterion | 543 cold, **47 warm** | Nested Loop → Bitmap Index Scan on `ix_company_register_sni_codes_gin` → Index Scan using the `job_ads` org.nr index |
| Batched lateral (`jsonb_to_recordset`), subquery per row | **~2 372** | GIN index STILL used (`Index Cond: sni_codes && v.sni`), but `Hash Join` with **`Seq Scan on job_ads`, 200 000 rows, once per criterion** |
| Same lateral with `EXISTS` instead of the join | ~1 881 | `Hash Right Semi Join` + `Seq Scan on job_ads` |

## Reading

The GIN index survives the lateral — array overlap against a lateral column is still index-servable,
which was the design-critical unknown. What does not survive is the **join** strategy: inside a
lateral over a function scan the planner has no statistics for `v.sni` / `v.kommun`, so it stops
choosing the nested-loop index lookup into `job_ads` and hash-joins against a full scan of it.

Batching is therefore **40–50x worse**, not better. It is also exactly the vacuous-guarantee class
this repo names twice (#805-3, #842): index used, semantics correct, every test green, and the cost
silently scaling with `job_ads`.

`dotnet-architect` confirmed the mechanism independently and added one this probe did not measure:
the arrays sit inside a `jsonb` parameter the planner cannot look into, so every execution behaves
like a *generic plan* on the SNI axis — permanently, by construction, without anyone enabling
`Max Auto Prepare`. The hazard `GenericPlan_DoesNotUseTheNameIndex_SoMaxAutoPrepareWouldKillIt`
pins as a future risk would become the present state.

## Consequence

The batch was dropped before a line of it was written. Whatever form the counts take, they use the
EXISTING `CountActiveAdsAsync` / `ListActiveAdIdsAsync` — the statements the EXPLAIN pin already
covers — so there is no new SQL, no duplicated predicate text, no new pin and no drift surface.

This measurement settles HOW to ask. It does not settle WHETHER `/oversikt` can afford to ask;
that is the latency finding in the architect report, and `senior-cto-advisor` owns the routing.
