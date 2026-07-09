using Jobbliggaren.Application.Applications.Commands.BatchTransition;
using Jobbliggaren.Domain.Applications;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Applications.Commands;

public class BatchTransitionApplicationsCommandValidatorTests
{
    private readonly BatchTransitionApplicationsCommandValidator _validator = new();

    private static BatchTransitionApplicationsCommand Command(
        params BatchTransitionItem[] items) => new(items);

    private static BatchTransitionItem Item(string target = "Submitted") =>
        new(Guid.NewGuid(), target);

    [Fact]
    public void Validate_ValidCommand_Passes()
    {
        var result = _validator.Validate(Command(Item(), Item("Rejected")));

        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(AllStatusNames))]
    public void Validate_EveryKnownStatusName_Passes(string statusName)
    {
        // ADR 0092 D3 parity with the single validator: all ten statuses are
        // valid targets — including manual Ghosted and terminal reopens.
        var result = _validator.Validate(Command(Item(statusName)));

        result.IsValid.ShouldBeTrue();
    }

    public static TheoryData<string> AllStatusNames()
    {
        var data = new TheoryData<string>();
        foreach (var status in ApplicationStatus.List)
            data.Add(status.Name);
        return data;
    }

    [Fact]
    public void Validate_NullItems_Fails()
    {
        var result = _validator.Validate(new BatchTransitionApplicationsCommand(null!));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage == "Minst en ansökan måste anges.");
    }

    [Fact]
    public void Validate_EmptyItems_Fails()
    {
        var result = _validator.Validate(Command());

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage == "Minst en ansökan måste anges.");
    }

    [Fact]
    public void Validate_ExactlyMaxItems_Passes()
    {
        var items = Enumerable.Range(0, BatchTransitionApplicationsCommandValidator.MaxItemsPerCall)
            .Select(_ => Item())
            .ToArray();

        var result = _validator.Validate(Command(items));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_OneOverMaxItems_Fails()
    {
        var items = Enumerable.Range(0, BatchTransitionApplicationsCommandValidator.MaxItemsPerCall + 1)
            .Select(_ => Item())
            .ToArray();

        var result = _validator.Validate(Command(items));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage.StartsWith("Max "));
    }

    [Fact]
    public void Validate_OneOverMaxViaIdenticalDuplicates_Fails()
    {
        // CTO bind Q4: the cap counts the RAW pre-dedup list — dedup is a
        // handler courtesy, not a cap loophole.
        var shared = Item();
        var items = Enumerable
            .Range(0, BatchTransitionApplicationsCommandValidator.MaxItemsPerCall + 1)
            .Select(_ => shared)
            .ToArray();

        var result = _validator.Validate(Command(items));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_EmptyApplicationId_Fails()
    {
        var result = _validator.Validate(Command(new BatchTransitionItem(Guid.Empty, "Submitted")));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage == "ApplicationId är obligatoriskt.");
    }

    [Fact]
    public void Validate_EmptyTargetStatus_Fails()
    {
        var result = _validator.Validate(Command(Item(string.Empty)));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage == "TargetStatus är obligatoriskt.");
    }

    [Fact]
    public void Validate_UnknownTargetStatus_Fails()
    {
        var result = _validator.Validate(Command(Item("Skickad")));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage == "Okänd status.");
    }

    [Fact]
    public void Validate_IdenticalDuplicates_Passes()
    {
        // CTO bind Q6: a resent double-click (same id, same target) is
        // tolerated — the handler dedups to one transition.
        var item = Item();

        var result = _validator.Validate(Command(item, item));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_ConflictingDuplicates_Fails()
    {
        // CTO bind Q6: the same application with two DIFFERENT targets is a
        // contradictory request — reject rather than guess an ordering.
        var id = Guid.NewGuid();

        var result = _validator.Validate(Command(
            new BatchTransitionItem(id, "Submitted"),
            new BatchTransitionItem(id, "Rejected")));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(
            e => e.ErrorMessage == "Samma ansökan förekommer med olika målstatus.");
    }
}
