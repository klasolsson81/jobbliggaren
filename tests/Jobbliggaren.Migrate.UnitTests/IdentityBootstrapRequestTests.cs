using Shouldly;

namespace Jobbliggaren.Migrate.UnitTests;

public sealed class IdentityBootstrapRequestTests
{
    private const string A = "20260610090100_InitialIdentity";
    private const string B = "20260925172152_NullPasswordHashes";
    private const string C = "20261007074622_AddAccountAccessSuspension";
    private const string NewerImage = "20991231235959_SeededByNewerImage";

    [Fact]
    public void Validate_ShouldReturnApprovedAdditions_WhenHistoryExactlyMatchesPredecessor()
    {
        var request = Parse("--expect-history", A, "--expect-migrations", $"{B},{C}");

        request.Validate([A, B, C], [A]).ShouldBe([B, C]);
    }

    [Fact]
    public void Validate_ShouldReturnNothingPending_WhenCandidateWasCompletelyApplied()
    {
        var request = Parse("--expect-history", $"{A},{B}", "--expect-migrations", C);

        request.Validate([A, B, C], [A, B, C]).ShouldBeEmpty();
    }

    [Fact]
    public void Validate_ShouldAllowExactBoundFirstBoot_WhenPredecessorIsEmpty()
    {
        var request = Parse("--expect-history", "", "--expect-migrations", $"{A},{B},{C}");

        request.Validate([A, B, C], []).ShouldBe([A, B, C]);
        request.Validate([A, B, C], [A, B, C]).ShouldBeEmpty();
    }

    [Fact]
    public void Validate_ShouldReturnCompiledManifest_WhenInitialHistoryIsEmpty()
    {
        var request = Parse("--initial");

        request.Validate([A, B, C], []).ShouldBe([A, B, C]);
    }

    [Theory]
    [MemberData(nameof(MalformedArguments))]
    public void TryParse_ShouldRefuseMissingMalformedOrUnorderedArguments(string[] arguments)
    {
        IdentityBootstrapRequest.TryParse(arguments, out _).ShouldBeFalse();
    }

    public static TheoryData<string[]> MalformedArguments => Cases(
    [
        [],
        ["bootstrap"],
        ["--initial", "extra"],
        ["--initial", "--initial"],
        ["--expect-history"],
        ["--expect-history", A],
        ["--expect-history", A, "--expect-migrations"],
        ["--expect-migrations", B],
        ["--expect-migrations", B, "--expect-history", A],
        ["--expect-history", A, "--expect-history", B],
        ["--expect-history", A, "--expect-migrations", B, "extra"],
        ["--expect-history", A, "--expect-migrations", ""],
        ["--expect-history", $"{A},{A}", "--expect-migrations", B],
        ["--expect-history", $"{B},{A}", "--expect-migrations", C],
        ["--expect-history", A, "--expect-migrations", $"{B},{B}"],
        ["--expect-history", A, "--expect-migrations", $"{C},{B}"],
        ["--expect-history", $" {A}", "--expect-migrations", B],
        ["--expect-history", A, "--expect-migrations", $"{B} "],
        ["--expect-history", $"{A},", "--expect-migrations", B],
        ["--expect-history", A, "--expect-migrations", $",{B}"],
        ["--expect-history", $"{A}\n", "--expect-migrations", B],
        ["--expect-history", A, "--expect-migrations", $"{B}\r\n"],
        ["--expect-history", "not-a-migration", "--expect-migrations", B],
        ["--expect-history", A, "--expect-migrations", "2026100707462_TooShort"],
    ]);

    [Theory]
    [MemberData(nameof(InvalidCompiledManifests))]
    public void Validate_ShouldRefuseInvalidCompiledManifest_BeforeDerivingPending(string[] compiled)
    {
        var initial = Parse("--initial");

        Should.Throw<InvalidOperationException>(() => initial.Validate(compiled, []));
    }

    public static TheoryData<string[]> InvalidCompiledManifests => Cases(
    [
        [],
        [A, A],
        [B, A],
        [A, "not-a-migration"],
        [A, $"{B}\n"],
    ]);

    [Theory]
    [MemberData(nameof(UnapprovedHistories))]
    public void Validate_ShouldRefuseEveryHistoryOtherThanPredecessorOrCompleteCandidate(string[] applied)
    {
        var request = Parse("--expect-history", $"{A},{B}", "--expect-migrations", C);

        Should.Throw<InvalidOperationException>(() => request.Validate([A, B, C], applied));
    }

    public static TheoryData<string[]> UnapprovedHistories => Cases(
    [
        [],
        [A],
        [A, C],
        [A, B, NewerImage],
        [A, B, C, NewerImage],
        [B, A],
        [A, B, B],
    ]);

    [Fact]
    public void Validate_ShouldRefusePartialAddition_WhenAnotherApprovedMigrationRemains()
    {
        var request = Parse("--expect-history", A, "--expect-migrations", $"{B},{C}");

        Should.Throw<InvalidOperationException>(() => request.Validate([A, B, C], [A, B]));
    }

    [Theory]
    [InlineData(A, B)]
    [InlineData(A, C)]
    [InlineData(B, C)]
    [InlineData(A, A)]
    [InlineData(A, NewerImage)]
    public void Validate_ShouldRefuseApprovedSetThatDoesNotExactlyMatchCompiledPrefixAndSuffix(
        string predecessor, string additions)
    {
        var request = Parse("--expect-history", predecessor, "--expect-migrations", additions);

        Should.Throw<InvalidOperationException>(() => request.Validate([A, B, C], [A]));
    }

    [Fact]
    public void Validate_ShouldRefuseInitialIntent_WhenAnyMigrationIsAlreadyApplied()
    {
        var request = Parse("--initial");

        Should.Throw<InvalidOperationException>(() => request.Validate([A, B, C], [A]));
        Should.Throw<InvalidOperationException>(() => request.Validate([A, B, C], [A, B, C]));
    }

    private static IdentityBootstrapRequest Parse(params string[] arguments)
    {
        IdentityBootstrapRequest.TryParse(arguments, out var request).ShouldBeTrue();
        return request.ShouldNotBeNull();
    }

    private static TheoryData<string[]> Cases(string[][] rows)
    {
        var cases = new TheoryData<string[]>();
        foreach (var row in rows)
            cases.Add(row);
        return cases;
    }
}
