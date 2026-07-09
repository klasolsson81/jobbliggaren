using FluentValidation;
using Jobbliggaren.Domain.Applications;

namespace Jobbliggaren.Application.Applications.Commands.BatchTransition;

public sealed class BatchTransitionApplicationsCommandValidator
    : AbstractValidator<BatchTransitionApplicationsCommand>
{
    // Value parity with the four batch-read caps (ADR 0063,
    // MaxJobAdIdsPerCall = 100): the Tabell view pages at 50/page, so a
    // select-all is 50 items with 2x headroom. Counts the RAW pre-dedup list
    // (CTO bind Q4) — dedup is a handler courtesy, not a cap loophole.
    public const int MaxItemsPerCall = 100;

    public BatchTransitionApplicationsCommandValidator()
    {
        RuleFor(c => c.Items)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage("Minst en ansökan måste anges.")
            .NotEmpty()
            .WithMessage("Minst en ansökan måste anges.")
            .Must(items => items.Count <= MaxItemsPerCall)
            .WithMessage($"Max {MaxItemsPerCall} ansökningar per anrop.")
            // Identical duplicates are tolerated (handler dedups — a resent
            // double-click must not double-transition), but the same
            // application with TWO DIFFERENT targets is a contradictory
            // request no ordering guess should resolve (CTO bind Q6).
            .Must(HaveNoConflictingDuplicates)
            .WithMessage("Samma ansökan förekommer med olika målstatus.");

        // Explicit null-element reject (review fix): FluentValidation's
        // RuleForEach/ChildRules silently SKIPS null elements, so without this
        // rule a null item would pass validation and NRE in the handler (500
        // instead of 400). Unreachable via HTTP (the endpoint coerces null wire
        // items to (Guid.Empty, "")), but the command is a public
        // Application-layer surface — fail loud as a validation error.
        RuleForEach(c => c.Items)
            .NotNull()
            .WithMessage("Ogiltig post i listan.");

        RuleForEach(c => c.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ApplicationId)
                .NotEmpty()
                .WithMessage("ApplicationId är obligatoriskt.");

            // Same input rule as the single transition (ADR 0092 D3): any of
            // the ten statuses is a valid target — the aggregate enforces the
            // remaining invariants. PR 10 only SENDS Ghosted/Rejected; that is
            // an FE affair, not a contract restriction (CTO bind flag).
            item.RuleFor(i => i.TargetStatus)
                .NotEmpty()
                .WithMessage("TargetStatus är obligatoriskt.")
                .Must(s => ApplicationStatus.TryFromName(s, out _))
                .WithMessage("Okänd status.");
        });
    }

    private static bool HaveNoConflictingDuplicates(
        IReadOnlyList<BatchTransitionItem> items) =>
        items
            .Where(i => i is not null)
            .GroupBy(i => i.ApplicationId)
            .All(g => g.Select(i => i.TargetStatus).Distinct().Count() == 1);
}
