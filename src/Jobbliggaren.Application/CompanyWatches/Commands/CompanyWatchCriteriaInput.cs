using FluentValidation;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Domain.CompanyWatches;

namespace Jobbliggaren.Application.CompanyWatches.Commands;

/// <summary>
/// #560 PR-3 — the criterion predicate as it travels on the wire: two raw code lists, LEAVES ONLY
/// (Fork B1/G2: the picker expands a section/huvudgrupp selection to its 5-digit leaves FE-side;
/// the write path never accepts a group code — a dual "group-or-leaf" input mode is the §5
/// primitive-obsession surface). Shared by create, PATCH-update and the picker's live
/// magnitude-preview so all three carry the SAME validation (one
/// <see cref="CompanyWatchCriteriaInputValidator"/>).
/// </summary>
public sealed record CompanyWatchCriteriaInput(
    IReadOnlyList<string>? SniCodes,
    IReadOnlyList<string>? MunicipalityCodes);

/// <summary>
/// The shared predicate-input validator. Rule ORDER is load-bearing (CTO Fork G2 sub-decision 2,
/// realising C-D12):
///
/// <list type="number">
/// <item><b>RAW length cap FIRST</b> — <c>Count</c> is O(1) and runs before anything walks,
/// trims, sorts or allocates. The Domain cap alone protects the STORAGE, not request CPU:
/// <c>CompanyWatchCriteriaSpec.Create</c>'s <c>NormalizeList</c> trims/sorts/dedupes the whole
/// list BEFORE its own cap check, so an attacker's ten-million-code payload must die here, on
/// arithmetic, not there, after an allocation storm.</item>
/// <item><b>Existence per axis SECOND</b> (the A1 bind): an unknown-but-well-formed code
/// ("99999") would pass Domain FORMAT validation, be stored, and then silently match nothing in
/// the register forever — this product's cardinal sin. Unknown codes are echoed back (public SCB
/// reference data, no PII; capped echo so the 400 body stays bounded) with SEPARATE error codes
/// per axis, so the picker can point at the axis the user actually got wrong.</item>
/// <item><b>Format/normalized-cap LAST, in Domain</b> — <c>CompanyWatchCriteriaSpec.Create</c>
/// stays the invariant owner; this validator never re-states the format rule (a malformed code is
/// simply unknown to the catalog and fails the existence rule).</item>
/// </list>
///
/// Blank/whitespace elements are skipped by the existence walk — the Domain's
/// <c>NormalizeList</c> drops them, so they are not codes, and failing them as "unknown" would
/// reject a request the Domain accepts.
/// </summary>
public sealed class CompanyWatchCriteriaInputValidator : AbstractValidator<CompanyWatchCriteriaInput>
{
    /// <summary>Bounded unknown-code echo — the 400 body must not mirror a 1000-element payload.</summary>
    private const int MaxEchoedCodes = 10;

    public CompanyWatchCriteriaInputValidator(ICriterionReferenceProvider reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        RuleFor(i => i.SniCodes)
            .Cascade(CascadeMode.Stop)
            .NotNull()
                .WithMessage("Minst en bransch (SNI-kod) krävs för en bevakning.")
            .Must(static l => l!.Count <= CompanyWatchCriteriaSpec.MaxSniCodes)
                .WithMessage($"Max {CompanyWatchCriteriaSpec.MaxSniCodes} branscher per bevakning.")
            .Custom((codes, ctx) =>
            {
                var unknown = UnknownCodes(codes!, reference.Sni.LeafExists);
                if (unknown.Count > 0)
                {
                    ctx.AddFailure(new FluentValidation.Results.ValidationFailure(
                        ctx.PropertyPath,
                        $"Okända SNI-koder: {Echo(unknown)}.")
                    { ErrorCode = "CompanyWatchCriterion.UnknownSniCodes" });
                }
            });

        RuleFor(i => i.MunicipalityCodes)
            .Cascade(CascadeMode.Stop)
            .NotNull()
                .WithMessage("Minst en kommun krävs för en bevakning.")
            .Must(static l => l!.Count <= CompanyWatchCriteriaSpec.MaxMunicipalityCodes)
                .WithMessage($"Max {CompanyWatchCriteriaSpec.MaxMunicipalityCodes} kommuner per bevakning.")
            .Custom((codes, ctx) =>
            {
                var unknown = UnknownCodes(codes!, reference.Kommuner.Exists);
                if (unknown.Count > 0)
                {
                    ctx.AddFailure(new FluentValidation.Results.ValidationFailure(
                        ctx.PropertyPath,
                        $"Okända kommunkoder: {Echo(unknown)}.")
                    { ErrorCode = "CompanyWatchCriterion.UnknownMunicipalityCodes" });
                }
            });
    }

    // Trim-then-check mirrors the Domain's NormalizeList (trim → drop blank), so "exists" is asked
    // about exactly the value that would be stored. Distinct so a repeated bad code is echoed once.
    private static List<string> UnknownCodes(
        IReadOnlyList<string> codes, Func<string, bool> exists) =>
        codes
            .Where(static c => !string.IsNullOrWhiteSpace(c))
            .Select(static c => c.Trim())
            .Distinct(StringComparer.Ordinal)
            .Where(c => !exists(c))
            .ToList();

    private static string Echo(List<string> unknown) =>
        unknown.Count <= MaxEchoedCodes
            ? string.Join(", ", unknown)
            : $"{string.Join(", ", unknown.Take(MaxEchoedCodes))} och {unknown.Count - MaxEchoedCodes} till";
}
