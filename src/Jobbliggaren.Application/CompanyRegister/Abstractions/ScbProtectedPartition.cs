namespace Jobbliggaren.Application.CompanyRegister.Abstractions;

/// <summary>
/// #640 (ADR 0091) — a (SätesKommun, 5-digit SNI) partition whose unfetched tail must be EXCLUDED from
/// the deregister vanish-sweep. A dense-metro cell can exceed SCB's 2000-row fetch cap even at the
/// deepest 5-digit <c>Bransch</c> rung (e.g. Stockholm 0180 × Juridisk form 49 × SNI 70100 = 2809): the
/// client fetches the first cap rows, so the rest are un-observed this run. Rather than latch the WHOLE
/// run truncated (which disables the sweep everywhere while such a tail exists — dead companies then
/// accumulate), the client records this key and the sweep protects just its key-space, sweeping the
/// clean 99%+ (the partition-scoped sweep, #640).
///
/// <para>
/// A value object (CLAUDE.md §5, no primitive obsession) whose equality de-duplicates keys the client
/// may record more than once. Only <see cref="SeatMunicipalityCode"/> + a single 5-digit
/// <see cref="SniCode"/> are carried — never an org.nr (CLAUDE.md §5).
/// </para>
/// </summary>
public readonly record struct ScbProtectedPartition(string SeatMunicipalityCode, string SniCode);

/// <summary>
/// #717 (ADR 0091) — the accumulated over-cap facts for ONE <see cref="ScbProtectedPartition"/> key,
/// carried so the run can size its unfetched tail for FREE (the <c>raknaforetag</c> count was already
/// taken — no extra SCB call). The instrumentation feeds #641's facet decision (the dense-metro tails
/// are what leaves the register short of SCB's ~1.17M).
///
/// <para>
/// <b>Why a PAIR, not a single count:</b> the partition ladder splits by <c>Juridisk form</c> ABOVE
/// the 5-digit SNI, and <c>TryExtractProtectedKey</c> drops the form dimension — so the SAME
/// (kommun, SNI) key can be recorded by SEVERAL over-cap leaves (one per over-cap legal form), each of
/// which fetched only its own first <c>cap</c> rows. This cell's unfetched tail is therefore
/// <c>Σ per-leaf (count − cap) = <see cref="OverCapCount"/> − cap × <see cref="LeafCount"/></c>, NOT
/// <c>OverCapCount − cap</c> (over-counts) nor a single leaf's count (under-counts). (A register-wide sum
/// over cells is only an UPPER BOUND — a multi-SNI entity spans several cells, the #628 double-count
/// caveat; the aggregation and that caveat live in the Infrastructure tail summarizer.) The subtraction
/// of the SCB fetch cap lives in Infrastructure (the cap is an SCB concept) — this Application type
/// carries only the raw, cap-agnostic tallies. Counts only, never an org.nr (CLAUDE.md §5).
/// </para>
/// </summary>
/// <param name="OverCapCount">Sum of the over-cap <c>raknaforetag</c> counts across every over-cap leaf
/// that mapped to this (kommun, SNI) key.</param>
/// <param name="LeafCount">How many over-cap leaves mapped to this key (each fetched its own first cap
/// rows), so the fetched portion is <c>cap × LeafCount</c>.</param>
public readonly record struct ScbProtectedPartitionSize(int OverCapCount, int LeafCount);
