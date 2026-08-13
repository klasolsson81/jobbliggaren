using Shouldly;

namespace Jobbliggaren.Migrate.UnitTests;

/// <summary>
/// Decision table for <see cref="SchemaAheadGate"/> (#1236). The gate is a pure function, so
/// every state the runbook names — first boot, in-sync hourly reconcile, forward deploy, pure
/// backwards pin, true divergence — is pinned here without a database; the Testcontainers
/// substrate test proves the same object against a real <c>__EFMigrationsHistory</c>.
/// </summary>
public class SchemaAheadGateTests
{
    private const string A = "20260419145850_InitialCreate";
    private const string B = "20260520212725_F6P4aJobAdTrigramIndexes";
    private const string C = "20990101000000_FromANewerImage";
    private const string D = "20990202000000_AlsoFromANewerImage";

    // --- Proceed states -----------------------------------------------------

    [Fact]
    public void Decide_EmptyDatabase_ProceedsWithFullPendingList()
    {
        // First boot: the history table does not exist yet, so the applied read is empty.
        // This is the state every fresh box (and the Netcup first boot) hits.
        var decision = SchemaAheadGate.Decide([], [A, B], overrideValue: null);

        decision.Verdict.ShouldBe(SchemaAheadVerdict.Proceed);
        decision.Pending.ShouldBe([A, B]);
        decision.Unknown.ShouldBeEmpty();
        decision.OverridePresentButIdle.ShouldBeFalse();
    }

    [Fact]
    public void Decide_InSync_ProceedsWithNothingPending()
    {
        // The hourly reconcile against an unchanged tag: same assembly, same history.
        var decision = SchemaAheadGate.Decide([A, B], [A, B], overrideValue: null);

        decision.Verdict.ShouldBe(SchemaAheadVerdict.Proceed);
        decision.Pending.ShouldBeEmpty();
        decision.Unknown.ShouldBeEmpty();
    }

    [Fact]
    public void Decide_ForwardDeploy_ProceedsWithPendingInAssemblyOrder()
    {
        var decision = SchemaAheadGate.Decide([A], [A, B, C], overrideValue: null);

        decision.Verdict.ShouldBe(SchemaAheadVerdict.Proceed);
        decision.Pending.ShouldBe([B, C]); // assembly order — what MigrateAsync will apply, in order
    }

    [Fact]
    public void Decide_StaleOverrideInSync_ProceedsAndFlagsIdleOverride()
    {
        // The self-expiry half of the override design: a leftover value on a box that is no
        // longer behind must not refuse anything, but it must be nudged out of `.env`.
        var decision = SchemaAheadGate.Decide([A, B], [A, B], overrideValue: C);

        decision.Verdict.ShouldBe(SchemaAheadVerdict.Proceed);
        decision.OverridePresentButIdle.ShouldBeTrue();
    }

    // --- Pure backwards pin -------------------------------------------------

    [Fact]
    public void Decide_PureBackwardsPin_RefusesSchemaAheadNamingTheUnknownRows()
    {
        var decision = SchemaAheadGate.Decide([A, B, C], [A, B], overrideValue: null);

        decision.Verdict.ShouldBe(SchemaAheadVerdict.RefuseSchemaAhead);
        decision.Unknown.ShouldBe([C]);
        decision.Pending.ShouldBeEmpty();
    }

    [Fact]
    public void Decide_UnknownRows_PreserveAppliedOrder()
    {
        var decision = SchemaAheadGate.Decide([A, D, B, C], [A, B], overrideValue: null);

        decision.Unknown.ShouldBe([D, C]); // applied order, not sorted
    }

    [Fact]
    public void Decide_MatchingOverride_SkipsAsOverriddenNoOp()
    {
        var decision = SchemaAheadGate.Decide([A, B, C], [A, B], overrideValue: C);

        decision.Verdict.ShouldBe(SchemaAheadVerdict.OverriddenNoOp);
        decision.Unknown.ShouldBe([C]);
    }

    [Fact]
    public void Decide_MatchingOverride_IsOrderInsensitiveAndTrimmed()
    {
        var decision = SchemaAheadGate.Decide(
            [A, B, C, D], [A, B], overrideValue: $" {D} , {C} ");

        decision.Verdict.ShouldBe(SchemaAheadVerdict.OverriddenNoOp);
    }

    // The named non-goal: a boolean-shaped override must never bless anything. A leftover
    // `=1` months later is exactly the incident class the gate exists for.
    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("yes")]
    public void Decide_BooleanShapedOverride_RefusesSchemaAhead(string overrideValue)
    {
        var decision = SchemaAheadGate.Decide([A, B, C], [A, B], overrideValue);

        decision.Verdict.ShouldBe(SchemaAheadVerdict.RefuseSchemaAhead);
    }

    [Fact]
    public void Decide_PartialOverride_RefusesSchemaAhead()
    {
        var decision = SchemaAheadGate.Decide([A, B, C, D], [A, B], overrideValue: C);

        decision.Verdict.ShouldBe(SchemaAheadVerdict.RefuseSchemaAhead);
        decision.Unknown.ShouldBe([C, D]);
    }

    [Fact]
    public void Decide_SupersetOverride_RefusesSchemaAhead()
    {
        // Exact set equality, not subset: blessing MORE than the database shows is a value
        // written for a different state — the self-expiry rule cuts both directions.
        var decision = SchemaAheadGate.Decide([A, B, C], [A, B], overrideValue: $"{C},{D}");

        decision.Verdict.ShouldBe(SchemaAheadVerdict.RefuseSchemaAhead);
    }

    [Fact]
    public void Decide_OverrideCaseMismatch_RefusesSchemaAhead()
    {
        // Ordinal comparison — the same comparison EF uses for migration IDs.
        var decision = SchemaAheadGate.Decide(
            [A, B, C], [A, B], overrideValue: C.ToLowerInvariant());

        decision.Verdict.ShouldBe(SchemaAheadVerdict.RefuseSchemaAhead);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",")]
    public void Decide_AbsentOrEmptyOverride_IsNoOverride(string? overrideValue)
    {
        // Compose renders an unset `${MIGRATE_ALLOW_SCHEMA_AHEAD:-}` as an EMPTY string and
        // sets the variable anyway — the gate, as the one normalizer, must read that as unset.
        var decision = SchemaAheadGate.Decide([A, B, C], [A, B], overrideValue);

        decision.Verdict.ShouldBe(SchemaAheadVerdict.RefuseSchemaAhead);
    }

    // --- True divergence ----------------------------------------------------

    [Fact]
    public void Decide_Divergence_RefusesUnconditionally()
    {
        var decision = SchemaAheadGate.Decide([A, C], [A, B], overrideValue: null);

        decision.Verdict.ShouldBe(SchemaAheadVerdict.RefuseDivergence);
        decision.Unknown.ShouldBe([C]);
        decision.Pending.ShouldBe([B]);
    }

    [Fact]
    public void Decide_DivergenceWithMatchingOverride_StillRefusesDivergence()
    {
        // The override blesses old-code-on-newer-schema. It never blesses an APPLY into a
        // history that forked — no automatic migration is safe there.
        var decision = SchemaAheadGate.Decide([A, C], [A, B], overrideValue: C);

        decision.Verdict.ShouldBe(SchemaAheadVerdict.RefuseDivergence);
    }

    // --- The exit-code contract the runbook and the journal cite ------------

    [Fact]
    public void ExitCodes_AvoidTheReconcileUnitsVocabulary()
    {
        // 0 = success, 1 = migrate's existing crash path, 2 = the reconcile wrapper's
        // "cannot answer" in the same journal. The gate's codes must collide with none of them.
        SchemaAheadGate.ExitRefusedSchemaAhead.ShouldBe(3);
        SchemaAheadGate.ExitRefusedDivergence.ShouldBe(4);
    }

    [Fact]
    public void OverrideVariableName_MatchesTheComposeContract()
    {
        SchemaAheadGate.OverrideVariableName.ShouldBe("MIGRATE_ALLOW_SCHEMA_AHEAD");
    }
}
