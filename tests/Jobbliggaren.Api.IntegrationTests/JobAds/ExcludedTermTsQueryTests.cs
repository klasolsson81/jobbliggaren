using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.JobAds.Internal;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

[Collection("Api")]
public class ExcludedTermTsQueryTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    public static TheoryData<string> Raws()
    {
        var raws = new TheoryData<string>
        {
            "-\"(-deltid\"", "-\"-deltid\"",
            "-i!-deltid", "-i&-deltid", "-i|-deltid", "-i(-deltid", "-i)-deltid", "-i<-deltid",
            "-x(or(-deltid", "-x\"-deltid", "-x\"-deltid\"", "-x(-deltid", @"-x\(-deltid",
        };
        foreach (var c in "!\"#$%&'()*+./:;<=>?@[\\]^_`{|}~")
            raws.Add($"-{c}-deltid");
        return raws;
    }

    [Theory]
    [MemberData(nameof(Raws))]
    public async Task AnExcludedTermsTsQuery_NeitherNegatesNorDisjoins(string raw)
    {
        var ct = TestContext.Current.CancellationToken;

        foreach (var term in new SearchQueryParser().Parse(raw).ExcludedTerms)
        {
            var tsQuery = await PlainToTsQueryAsync(term, ct);
            tsQuery.ShouldNotContain("!", customMessage: $"\"{raw}\" → \"{term}\" → {tsQuery}");
            tsQuery.ShouldNotContain("|", customMessage: $"\"{raw}\" → \"{term}\" → {tsQuery}");
        }
    }

    private async Task<string> PlainToTsQueryAsync(string text, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Database
            .SqlQuery<string>($"SELECT plainto_tsquery('swedish', {text})::text AS \"Value\"")
            .SingleAsync(ct);
    }
}
