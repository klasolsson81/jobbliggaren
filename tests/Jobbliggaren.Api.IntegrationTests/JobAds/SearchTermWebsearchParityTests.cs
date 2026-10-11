using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.JobAds.Internal;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #2143 (ADR 0167 D1) — asks PostgreSQL itself whether an excluded term reaches the EXISTS as a
/// negation, rather than restating websearch_to_tsquery's tokenizer: a leading NOT inside the EXISTS
/// would hide the complement of what the word finds. Every printable ASCII punctuation character is
/// tried between two dashes, so an operator the parser does not know about fails here.
/// </summary>
[Collection("Api")]
public class SearchTermWebsearchParityTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    public static TheoryData<string> Raws()
    {
        var raws = new TheoryData<string> { "-\"(-deltid\"", "-\"-deltid\"" };
        foreach (var c in "!\"#$%&'()*+./:;<=>?@[\\]^_`{|}~")
            raws.Add($"-{c}-deltid");
        return raws;
    }

    [Theory]
    [MemberData(nameof(Raws))]
    public async Task AnExcludedTerm_NeverReachesTheExistsAsANegation(string raw)
    {
        var ct = TestContext.Current.CancellationToken;

        foreach (var term in new SearchQueryParser().Parse(raw).ExcludedTerms)
            (await WebsearchAsync(term, ct)).ShouldNotContain("!",
                customMessage: $"the EXISTS for \"{raw}\" would hide the complement of what \"{term}\" finds");
    }

    private async Task<string> WebsearchAsync(string text, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Database
            .SqlQuery<string>($"SELECT websearch_to_tsquery('swedish', {text})::text AS \"Value\"")
            .SingleAsync(ct);
    }
}
