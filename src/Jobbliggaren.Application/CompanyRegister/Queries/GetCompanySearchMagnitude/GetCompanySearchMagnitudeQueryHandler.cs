using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.CompanyRegister.Queries.GetCompanySearchMagnitude;

/// <summary>
/// <c>Create</c> (the single normalizer) → the port's magnitude count over the SAME predicate
/// authority as the page query (one predicate, two ceilings — the drift defense the sibling
/// criterion port bound in Fork G3). Register behind the port only (DPIA C-D4).
///
/// <para>
/// <b>The rule is ADR 0120</b> ("a rendered count is true, or it is absent"); this is where its
/// clause 3 is applied, and the one place the MEASUREMENT behind it is written down — callers
/// reference this handler rather than restating a number that changes on every SCB sync.
/// </para>
/// </summary>
public sealed class GetCompanySearchMagnitudeQueryHandler(ICompanyRegisterSearchQuery search)
    : IQueryHandler<GetCompanySearchMagnitudeQuery, CompanySearchMagnitudeDto?>
{
    public async ValueTask<CompanySearchMagnitudeDto?> Handle(
        GetCompanySearchMagnitudeQuery query, CancellationToken cancellationToken)
    {
        // Paging is irrelevant to a magnitude; the VO still wants legal values (its caps guard
        // the OFFSET surface the magnitude query never uses). Fixed 1/1 — never user input.
        var criteria = CompanyRegisterSearchCriteria.Create(
            query.SniCodes, query.MunicipalityCodes, query.Name, query.OrganizationNumber,
            page: 1, pageSize: 1);

        if (criteria.IsFailure)
        {
            // Unreachable by construction (the validator runs the SAME Create) — see
            // SearchCompaniesQueryHandler for the drift argument.
            throw new InvalidOperationException(
                "CompanyRegisterSearchCriteria.Create failed post-validation: "
                + criteria.Error.Code);
        }

        // An unfiltered browse-all carries NO number. The honest answer there is the whole active
        // register - 743 654 rows, unchanged from the 2026-07-25 corpus and re-read 2026-08-01 (the
        // table is written by one periodic bulk job, so it does not drift between syncs) - and the
        // product Ceiling can render that only as "10 000+", which understates the register by two
        // orders of magnitude on the one view whose whole job is to say how big it is. Klas ruled
        // (2026-08-01): the exact number if it is free, otherwise NO number, never the saturated one.
        //
        // It is not free. An exact count is an index-only scan over ix_company_register_status:
        // 26 ms with the visibility map set, but 438 ms without it - and nothing sets that map
        // automatically: the table is written by one periodic bulk job and the SCB refresher
        // ANALYZEs but does not VACUUM. Read the map itself (pg_class.relallvisible against
        // relpages), never autovacuum_count - that counter is resettable, and was measured
        // reset on the dev database 2026-08-16, so a zero there cannot be read as "never ran".
        // The cheap case is not the case
        // that persists, so the expensive one would land on every unfiltered page load.
        //
        // The policy lives HERE rather than at the endpoint because the question is asked of the
        // NORMALIZED criteria: a caller re-deriving "no axes" from raw request input would be a
        // second normalizer of the same rule. Returning early also means we do not compute a
        // number nobody renders - which is the product rule the change rests on, and it would be
        // right even if the count were free.
        //
        // It is not quite free, and not for the reason an earlier draft of this comment gave.
        // BuildCountCommand and BuildMagnitudeCommand are the same statement MODULO THE CAP, and
        // the caps differ by 5x - the page count stops at MaxServableRows (2 000 at pageSize 20),
        // the magnitude at Ceiling (10 000). So the page query does NOT already pay for this: the
        // skip saves a connection, a round trip, and a count over five times as many rows. What it
        // does not save is the 438 ms above, which is the EXACT count we never perform.
        if (criteria.Value.IsUnfiltered)
        {
            return null;
        }

        var magnitude = await search.CountMatchingAsync(
            criteria.Value, CompanySearchMagnitudeDto.Ceiling, cancellationToken);

        return new CompanySearchMagnitudeDto(
            magnitude, Saturated: magnitude >= CompanySearchMagnitudeDto.Ceiling);
    }
}
