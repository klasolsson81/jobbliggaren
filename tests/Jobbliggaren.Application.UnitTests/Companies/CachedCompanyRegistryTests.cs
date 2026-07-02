using System.Text;
using Jobbliggaren.Application.Companies.Abstractions;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Infrastructure.CompanyRegistry;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Companies;

/// <summary>
/// #454 (ADR 0088 D6) — the read-through cache decorator. Load-bearing invariants: the CACHE-LAYER
/// personnummer fail-closed gate (security-auditor MUST — no pnr-shaped key may ever touch Redis,
/// independent of the handler's refusal), positive-only caching (Found only; NotFound/Unavailable
/// never cached in v1), org.nr→NAME-only payload, and never-500 degradation (Redis fault/garbage →
/// miss; failed write → lookup still succeeds).
/// </summary>
public class CachedCompanyRegistryTests
{
    private const string LegalEntityOrgNr = "5592804784"; // third digit 9 → legal entity
    private const string PnrShapedOrgNr = "1901012384"; // third digit 0 → personnummer-shaped
    private const string ExpectedKey = "company-registry:v1:" + LegalEntityOrgNr;

    private readonly ICompanyRegistry _inner = Substitute.For<ICompanyRegistry>();
    private readonly IDistributedCache _cache = Substitute.For<IDistributedCache>();

    private CachedCompanyRegistry Sut(int ttlDays = 30) => new(
        _inner, _cache,
        Options.Create(new CompanyRegistryOptions { PositiveCacheTtlDays = ttlDays }));

    private static OrganizationNumber OrgNr(string value) => OrganizationNumber.Create(value).Value;

    private void StubInner(string orgNr, CompanyRegistryLookup lookup) =>
        _inner.LookupAsync(Arg.Is<OrganizationNumber>(o => o.Value == orgNr), Arg.Any<CancellationToken>())
            .Returns(lookup);

    [Fact]
    public async Task Lookup_CacheHit_ReturnsFoundWithoutInnerCall()
    {
        var ct = TestContext.Current.CancellationToken;
        _cache.GetAsync(ExpectedKey, Arg.Any<CancellationToken>())
            .Returns(Encoding.UTF8.GetBytes("""{"name":"Testbolaget AB"}"""));

        var result = await Sut().LookupAsync(OrgNr(LegalEntityOrgNr), ct);

        result.Status.ShouldBe(CompanyRegistryStatus.Found);
        result.Entry!.Name.ShouldBe("Testbolaget AB");
        result.Entry.OrganizationNumber.ShouldBe(LegalEntityOrgNr);
        await _inner.DidNotReceiveWithAnyArgs()
            .LookupAsync(Arg.Any<OrganizationNumber>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Lookup_CacheMissThenFound_WritesNameOnlyPayloadWithTtl()
    {
        var ct = TestContext.Current.CancellationToken;
        _cache.GetAsync(ExpectedKey, Arg.Any<CancellationToken>()).Returns((byte[]?)null);
        StubInner(LegalEntityOrgNr, CompanyRegistryLookup.Found(
            new CompanyRegistryEntry(LegalEntityOrgNr, "Testbolaget AB")));

        byte[]? written = null;
        DistributedCacheEntryOptions? writtenOptions = null;
        await _cache.SetAsync(
            ExpectedKey,
            Arg.Do<byte[]>(b => written = b),
            Arg.Do<DistributedCacheEntryOptions>(o => writtenOptions = o),
            Arg.Any<CancellationToken>());

        var result = await Sut(ttlDays: 30).LookupAsync(OrgNr(LegalEntityOrgNr), ct);

        result.Status.ShouldBe(CompanyRegistryStatus.Found);
        written.ShouldNotBeNull();
        // ONLY the name — never owner/person fields, never the org.nr duplicated into the value
        // (the key already carries it; the payload stays the minimal D8(a) public-data shape).
        Encoding.UTF8.GetString(written).ShouldBe("""{"name":"Testbolaget AB"}""");
        writtenOptions.ShouldNotBeNull();
        writtenOptions.AbsoluteExpirationRelativeToNow.ShouldBe(TimeSpan.FromDays(30));
    }

    [Fact]
    public async Task Lookup_NotFound_IsNeverCached_V1()
    {
        var ct = TestContext.Current.CancellationToken;
        _cache.GetAsync(ExpectedKey, Arg.Any<CancellationToken>()).Returns((byte[]?)null);
        StubInner(LegalEntityOrgNr, CompanyRegistryLookup.NotFound);

        var result = await Sut().LookupAsync(OrgNr(LegalEntityOrgNr), ct);

        result.Status.ShouldBe(CompanyRegistryStatus.NotFound);
        await _cache.DidNotReceiveWithAnyArgs().SetAsync(
            Arg.Any<string>(), Arg.Any<byte[]>(),
            Arg.Any<DistributedCacheEntryOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Lookup_Unavailable_IsNeverCached()
    {
        var ct = TestContext.Current.CancellationToken;
        _cache.GetAsync(ExpectedKey, Arg.Any<CancellationToken>()).Returns((byte[]?)null);
        StubInner(LegalEntityOrgNr, CompanyRegistryLookup.Unavailable);

        var result = await Sut().LookupAsync(OrgNr(LegalEntityOrgNr), ct);

        result.Status.ShouldBe(CompanyRegistryStatus.Unavailable);
        await _cache.DidNotReceiveWithAnyArgs().SetAsync(
            Arg.Any<string>(), Arg.Any<byte[]>(),
            Arg.Any<DistributedCacheEntryOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Lookup_PnrShaped_BypassesCacheEntirely_FailClosed()
    {
        // Security-auditor MUST (cache-layer gate): a personnummer-shaped org.nr must never touch
        // the Redis keyspace — read OR write — regardless of what the inner provider answers. The
        // gate is INDEPENDENT of the handler's D4 refusal (defense-in-depth against future paths).
        var ct = TestContext.Current.CancellationToken;
        StubInner(PnrShapedOrgNr, CompanyRegistryLookup.Found(
            new CompanyRegistryEntry(PnrShapedOrgNr, "Enskild firma (fixture)")));

        var result = await Sut().LookupAsync(OrgNr(PnrShapedOrgNr), ct);

        result.Status.ShouldBe(CompanyRegistryStatus.Found);
        await _cache.DidNotReceiveWithAnyArgs().GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _cache.DidNotReceiveWithAnyArgs().SetAsync(
            Arg.Any<string>(), Arg.Any<byte[]>(),
            Arg.Any<DistributedCacheEntryOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Lookup_RedisReadFault_DegradesToMissAndCallsInner()
    {
        var ct = TestContext.Current.CancellationToken;
        _cache.GetAsync(ExpectedKey, Arg.Any<CancellationToken>())
            .Returns<byte[]?>(_ => throw new InvalidOperationException("redis down"));
        StubInner(LegalEntityOrgNr, CompanyRegistryLookup.Found(
            new CompanyRegistryEntry(LegalEntityOrgNr, "Testbolaget AB")));

        var result = await Sut().LookupAsync(OrgNr(LegalEntityOrgNr), ct);

        result.Status.ShouldBe(CompanyRegistryStatus.Found);
        result.Entry!.Name.ShouldBe("Testbolaget AB");
    }

    [Fact]
    public async Task Lookup_GarbageCachedBytes_DegradesToMissAndCallsInner()
    {
        var ct = TestContext.Current.CancellationToken;
        _cache.GetAsync(ExpectedKey, Arg.Any<CancellationToken>())
            .Returns(Encoding.UTF8.GetBytes("not json"));
        StubInner(LegalEntityOrgNr, CompanyRegistryLookup.Found(
            new CompanyRegistryEntry(LegalEntityOrgNr, "Testbolaget AB")));

        var result = await Sut().LookupAsync(OrgNr(LegalEntityOrgNr), ct);

        result.Status.ShouldBe(CompanyRegistryStatus.Found);
        result.Entry!.Name.ShouldBe("Testbolaget AB");
    }

    [Fact]
    public async Task Lookup_RedisWriteFault_DoesNotFailTheLookup()
    {
        var ct = TestContext.Current.CancellationToken;
        _cache.GetAsync(ExpectedKey, Arg.Any<CancellationToken>()).Returns((byte[]?)null);
        _cache.SetAsync(
                Arg.Any<string>(), Arg.Any<byte[]>(),
                Arg.Any<DistributedCacheEntryOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("redis down"));
        StubInner(LegalEntityOrgNr, CompanyRegistryLookup.Found(
            new CompanyRegistryEntry(LegalEntityOrgNr, "Testbolaget AB")));

        var result = await Sut().LookupAsync(OrgNr(LegalEntityOrgNr), ct);

        result.Status.ShouldBe(CompanyRegistryStatus.Found);
    }
}
