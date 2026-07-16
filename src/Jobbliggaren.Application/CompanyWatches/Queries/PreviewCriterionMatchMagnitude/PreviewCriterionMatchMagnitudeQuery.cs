using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Commands;
using Jobbliggaren.Application.CompanyWatches.Queries.GetCriterionMatchMagnitude;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.CompanyWatches.Queries.PreviewCriterionMatchMagnitude;

/// <summary>
/// The picker's live preview (CTO Fork G3's second consumer): the magnitude of an UNSAVED
/// criterion — "ditt urval matchar 412 företag" while the user is still composing. Takes the same
/// wire input as create, runs the SAME shared validation (C-D12 raw caps + existence; a preview is
/// not a backdoor past them), and the same port method with the same ceiling — so the number the
/// picker previews is BY CONSTRUCTION the number the saved criterion's headline will show.
///
/// <para>
/// <c>Result&lt;T&gt;</c> (not <c>T?</c>): there is no id and therefore no not-found — the failure
/// channel is the Domain spec's Validation errors (parity <c>LookupCompanyQuery</c>, the house
/// rule's other branch: null→404 is reserved for queries whose only error IS not-found).
/// </para>
/// </summary>
public sealed record PreviewCriterionMatchMagnitudeQuery(CompanyWatchCriteriaInput Criteria)
    : IQuery<Result<CriterionMatchMagnitudeDto>>, IAuthenticatedRequest;
