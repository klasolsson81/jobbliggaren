using Jobbliggaren.Application.Companies.Abstractions;
using Jobbliggaren.Domain.CompanyWatches;

namespace Jobbliggaren.Infrastructure.CompanyRegistry;

/// <summary>
/// #454 (ADR 0088 D3) — the prod-dark provider: always
/// <see cref="CompanyRegistryStatus.Unavailable"/> until the real SCB adapter (follow-up, Sept-2026
/// API) is activated. Deliberately fail-CIVIC rather than fail-loud (contrast Resend's missing-key
/// startup throw): the lookup endpoint must DEGRADE (200-with-status → the FE renders/hides the
/// civic state), never crash, and prod legitimately runs with no registry source for months. The
/// FE additionally hides the whole search section behind <c>COMPANY_REGISTRY_ENABLED=false</c>
/// (F1(a)), so this is the backend backstop if the flag is ever flipped early.
/// </summary>
internal sealed class NullCompanyRegistry : ICompanyRegistry
{
    public ValueTask<CompanyRegistryLookup> LookupAsync(
        OrganizationNumber organizationNumber, CancellationToken cancellationToken)
        => ValueTask.FromResult(CompanyRegistryLookup.Unavailable);
}
