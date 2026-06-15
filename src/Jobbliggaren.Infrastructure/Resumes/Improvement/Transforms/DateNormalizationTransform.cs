using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Improvement.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Infrastructure.Resumes.Review;
using Jobbliggaren.Infrastructure.Resumes.Review.Rules;

namespace Jobbliggaren.Infrastructure.Resumes.Improvement.Transforms;

/// <summary>
/// B6 date format (Fas 4 STEG 10, F4-10): flags an experience period that carries a date (has a
/// digit) but is NOT in a machine-recognised format (<see cref="PeriodParser"/> rejects it,
/// e.g. month names "jan 2022 - juni 2024"), proposing canonical reformatting. A structural
/// FLAG: it cites the offending span and the <c>ReformatDate</c> operation without inventing a
/// concrete reformatted value (the determinism cannot derive month-name dates without
/// synthesising — honest, §5). Already-canonical periods (parsed by <see cref="PeriodParser"/>,
/// incl. "2021–2024" / "01/2022 – 06/2024") are left untouched.
/// </summary>
internal sealed class DateNormalizationTransform : ICvTransform
{
    public ProposedChangeKind Kind => ProposedChangeKind.DateNormalization;

    public IEnumerable<ProposedChange> Propose(CvImprovementContext context)
    {
        var index = 0;
        var entryNumber = 0;
        foreach (var experience in context.Content.Experience)
        {
            entryNumber++;
            var period = experience.Period;
            if (string.IsNullOrWhiteSpace(period)
                || !period.Any(char.IsDigit)
                || PeriodParser.TryParse(period, out _, out _, out _))
            {
                continue;
            }

            var evidence = ReviewText.Span(
                experience.RawText ?? string.Empty, period, "icke-standard datumformat (periodspann)");

            yield return ProposedChange.FromStructuralOp(
                targetId: $"date:{index++}",
                kind: ProposedChangeKind.DateNormalization,
                category: RubricCategory.Structure,
                criterionId: context.CriterionIdFor("B6"),
                evidence: evidence,
                replacement: null,
                operation: new StructuralOperation(
                    StructuralTransformKind.ReformatDate, $"erfarenhetspost {entryNumber}"),
                rationale: "Standardisera datumformatet (t.ex. MM/ÅÅÅÅ) för konsekvens.",
                provenance: new StructuralTransformProvenance(StructuralTransformKind.ReformatDate),
                pureTransform: null);
        }
    }
}
