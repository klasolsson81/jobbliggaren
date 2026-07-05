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
