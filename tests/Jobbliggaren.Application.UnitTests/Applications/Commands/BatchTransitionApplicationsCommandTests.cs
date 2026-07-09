using Jobbliggaren.Application.Applications.Commands.BatchTransition;
using Jobbliggaren.Domain.Common;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Applications.Commands;

/// <summary>
/// #630 PR 9 — the command's <see cref="IBatchAuditableCommand{TResponse}"/>
/// surface. AuditBehavior writes one audit row per id returned by
/// ExtractAggregateIds; the Distinct() there is what keeps a silently-deduped
/// double-click (identical (id, target) twice) from writing TWO audit rows for
/// ONE real transition (IBatchAuditableCommand XML doc: the returned ids must
/// not contain duplicates). Nothing else pins that Distinct() — the handler
/// dedups the transition, but the audit contract lives on the command.
/// </summary>
public class BatchTransitionApplicationsCommandTests
{
    private static BatchTransitionApplicationsCommand Command(
        params BatchTransitionItem[] items) => new(items);

    [Fact]
    public void ExtractAggregateIds_WithIdenticalDuplicateItems_ReturnsDistinctIds()
    {
        // CTO bind Q6: identical duplicates are silently deduped to one
        // transition, so they must yield ONE audit row, not two.
        var id = Guid.NewGuid();
        var command = Command(
            new BatchTransitionItem(id, "Submitted"),
            new BatchTransitionItem(id, "Submitted"));

        var ids = command.ExtractAggregateIds(Result.Success());

        ids.ShouldHaveSingleItem().ShouldBe(id);
    }

    [Fact]
    public void ExtractAggregateIds_WithDistinctItems_ReturnsOneIdPerApplication()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var command = Command(
            new BatchTransitionItem(id1, "Submitted"),
            new BatchTransitionItem(id2, "Rejected"));

        var ids = command.ExtractAggregateIds(Result.Success());

        ids.Count.ShouldBe(2);
        ids.ShouldContain(id1);
        ids.ShouldContain(id2);
    }
}
