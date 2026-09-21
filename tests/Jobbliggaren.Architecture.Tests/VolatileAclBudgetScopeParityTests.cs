using System.Reflection;
using System.Text.RegularExpressions;
using Jobbliggaren.Application.Common.Abstractions;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1739 — every rate budget the application declares has its key family in the volatile Redis ACL, and the ACL
/// names no budget family nothing declares. A budget's key is <c>budget/{scope}/v1/{fingerprint}</c> and the
/// template lists the families one by one, so a scope added without its line is refused by Redis once the ACL is
/// live, and that refusal reads like an outage. <c>RedisAclContractTests</c> exercises a hand-written list of
/// scopes; this reads both sides.
/// </summary>
public partial class VolatileAclBudgetScopeParityTests
{
    private const string Template = "deploy/redis/volatile.acl.template";

    [GeneratedRegex(@"~jobbliggaren:budget/(?<scope>[a-z0-9-]+)/v1/\*")]
    private static partial Regex BudgetFamily();

    [Fact]
    public void Every_declared_budget_scope_has_its_family_in_the_template_and_no_family_is_orphaned()
    {
        var declared = DeclaredScopeNames();
        var listed = BudgetFamily()
            .Matches(File.ReadAllText(Path.Combine(FindRepoRoot(), Template)))
            .Select(match => match.Groups["scope"].Value)
            .Order(StringComparer.Ordinal)
            .ToList();

        declared.ShouldNotBeEmpty();
        listed.ShouldBe(declared, $"budget families in {Template} against the RateBudgetScope members of the Application assembly");
    }

    [Fact]
    public void The_scan_finds_the_scopes_of_both_forms()
    {
        // A control on the scan itself: a field and a factory that takes its window, one of each.
        var declared = DeclaredScopeNames();

        declared.ShouldContain("login-challenge-codes");
        declared.ShouldContain("login-challenge-cooldown");
    }

    // Every static RateBudgetScope the Application assembly exposes: fields, and factories whose only parameter is
    // the window. A scope built any other way is not found here, and would be missing from the template's check.
    private static List<string> DeclaredScopeNames()
    {
        const BindingFlags statics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var types = typeof(Jobbliggaren.Application.AssemblyMarker).Assembly.GetTypes();

        var fromFields = types
            .SelectMany(type => type.GetFields(statics))
            .Where(field => field.FieldType == typeof(RateBudgetScope))
            .Select(field => (RateBudgetScope)field.GetValue(null)!);

        var fromFactories = types
            .SelectMany(type => type.GetMethods(statics))
            .Where(method => method.ReturnType == typeof(RateBudgetScope)
                             && method.GetParameters() is [{ ParameterType: var only }]
                             && only == typeof(TimeSpan))
            .Select(method => (RateBudgetScope)method.Invoke(null, [TimeSpan.FromMinutes(1)])!);

        return fromFields.Concat(fromFactories)
            .Select(scope => scope.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;

        dir.ShouldNotBeNull("could not find the repo root (CLAUDE.md) walking up from the test bin");
        return dir!.FullName;
    }
}
