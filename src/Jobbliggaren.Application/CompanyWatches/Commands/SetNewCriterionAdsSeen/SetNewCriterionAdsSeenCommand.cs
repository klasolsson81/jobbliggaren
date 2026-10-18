using FluentValidation;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.CompanyWatches.Commands.SetNewCriterionAdsSeen;

/// <summary>
/// ADR 0175 D6, D7 — acknowledges one Branschbevakning's page of new ads: moves its <c>NewAdsSeenThrough</c> to
/// <paramref name="SeenThrough"/>, the window the server cut. Sent from the page's <c>after()</c> once it rendered a
/// window (ADR 0018 Amendment, #1663). Owner-scoped: a foreign or unknown id is 404 and writes nothing.
/// </summary>
/// <param name="SeenThrough">
/// Required. There is deliberately no "now" fallback: acknowledging up to the clock would swallow hits written after
/// the page was read, the loss #1576 closed. A future value is clamped to now.
/// </param>
public sealed record SetNewCriterionAdsSeenCommand(Guid CriterionId, DateTimeOffset? SeenThrough)
    : ICommand<Result>, IAuthenticatedRequest;

public sealed class SetNewCriterionAdsSeenCommandValidator : AbstractValidator<SetNewCriterionAdsSeenCommand>
{
    public SetNewCriterionAdsSeenCommandValidator()
    {
        RuleFor(c => c.SeenThrough).NotNull();
    }
}
