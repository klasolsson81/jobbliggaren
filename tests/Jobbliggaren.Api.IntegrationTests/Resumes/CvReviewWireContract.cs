using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Resumes;

/// <summary>
/// #2083 PR 2 — the two review-ledger fields as they reach the client, read from the real JSON of
/// both review endpoints (canonical <c>/{id}/review</c> and staging <c>/parsed/{id}/review</c>), so
/// the meaning of a field cannot vary by path. Expected values come from the rubric the running host
/// serves (its <see cref="IRubricProvider"/>), never from a copy in the test.
/// </summary>
internal static class CvReviewWireContract
{
    private static readonly string[] KindNames = Enum.GetNames<NotAssessedKind>();

    internal static Rubric ServedRubric(ApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IRubricProvider>().GetRubric();
    }

    internal static JsonElement VerdictById(JsonElement review, string criterionId) =>
        review.GetProperty("verdicts").EnumerateArray()
            .Single(v => v.GetProperty("criterionId").GetString() == criterionId);

    /// <summary>The property must be on the wire (both fields are declared without a default);
    /// a JSON null reads as null.</summary>
    internal static string? NullableString(JsonElement verdict, string property)
    {
        var value = verdict.GetProperty(property);
        return value.ValueKind == JsonValueKind.Null ? null : value.GetString();
    }

    /// <summary>
    /// Every verdict in the review: a NotAssessed verdict names one of the four kinds and carries no
    /// pass criterion; an assessed verdict carries no kind; a Fail or Warn verdict carries the rubric's
    /// "Godkänns när" clause (null where the rubric authors none, as for B5), and a Pass none (CTO F6).
    /// Fails if no row showed a clause, so the sweep cannot pass on a review without findings.
    /// </summary>
    internal static void ShouldCarryKindAndPassCriterionOnEveryVerdict(JsonElement review, Rubric rubric)
    {
        var clauseById = rubric.Criteria.ToDictionary(c => c.Id, c => c.PassCriterion, StringComparer.Ordinal);
        var rowsWithAClause = 0;

        foreach (var row in review.GetProperty("verdicts").EnumerateArray())
        {
            var id = row.GetProperty("criterionId").GetString()!;
            var verdict = row.GetProperty("verdict").GetString();
            var kind = NullableString(row, "notAssessedKind");
            var clause = NullableString(row, "passCriterion");

            if (verdict == "NotAssessed")
            {
                kind.ShouldNotBeNull($"{id}: ett Ej bedömt-verdikt bär sin skälgrupp.");
                KindNames.ShouldContain(kind, $"{id}: okänd skälgrupp '{kind}'.");
                clause.ShouldBeNull($"{id}: Godkänns när visas aldrig på en Ej bedömt-rad.");
                continue;
            }

            kind.ShouldBeNull($"{id}: ett bedömt verdikt ({verdict}) har ingen skälgrupp.");
            if (verdict is "Fail" or "Warn")
            {
                clause.ShouldBe(clauseById[id], $"{id}: {verdict} bär rubrikens Godkänns när-villkor.");
                if (clause is not null)
                {
                    rowsWithAClause++;
                }
            }
            else
            {
                clause.ShouldBeNull($"{id}: ett Pass-verdikt bär inget Godkänns när-villkor.");
            }
        }

        rowsWithAClause.ShouldBeGreaterThan(0, "granskningen ska ha minst ett fynd som visar sitt villkor.");
    }
}
