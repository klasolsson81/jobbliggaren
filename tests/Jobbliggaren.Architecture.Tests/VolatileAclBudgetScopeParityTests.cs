using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1739 — every rate budget the application declares has its key family in the volatile Redis ACL, and the ACL
/// names no budget family nothing declares. A budget's key is <c>budget/{scope}/v1/{fingerprint}</c> and the
/// template lists the families one by one, so a scope added without its line is refused by Redis once the ACL is
/// live, and that refusal reads like an outage. <c>RedisAclContractTests</c> exercises a hand-written list of
/// scopes; this reads both sides.
/// <para>
/// It covers the BUDGET families only. The template's other selectors have no declaring side to read here. And
/// one form of scope stays invisible to it: a <see cref="RateBudgetScope"/> constructed inline in a method body,
/// which no static member declares.
/// </para>
/// </summary>
public partial class VolatileAclBudgetScopeParityTests
{
    private const BindingFlags Statics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly Type[] ApplicationTypes =
        typeof(Jobbliggaren.Application.AssemblyMarker).Assembly.GetTypes();

    [GeneratedRegex(@"~jobbliggaren:budget/(?<scope>[a-z0-9-]+)/v1/\*")]
    private static partial Regex BudgetFamily();

    [Fact]
    public void Every_declared_budget_scope_has_its_family_in_the_template_and_no_family_is_orphaned()
    {
        var declared = DeclaredScopeNames();
        var listed = BudgetFamily()
            .Matches(File.ReadAllText(Path.Combine(RepoRoot(), "deploy", "redis", "volatile.acl.template")))
            .Select(match => match.Groups["scope"].Value)
            .Order(StringComparer.Ordinal)
            .ToList();

        declared.ShouldNotBeEmpty();
        listed.ShouldBe(declared);
    }

    [Fact]
    public void The_scan_finds_the_scopes_of_both_forms()
    {
        // A control on the scan itself: a field and a factory that takes its window, one of each.
        var declared = DeclaredScopeNames();

        declared.ShouldContain("login-challenge-codes");
        declared.ShouldContain("login-challenge-cooldown");
    }

    [Fact]
    public void Only_the_two_policy_types_declare_a_budget_scope()
    {
        // Fail-closed on the declaring surface: a scope moved into a handler, or a third policy type, turns this
        // red and is decided on purpose.
        ApplicationTypes
            .Where(type => ScopeMembers(type).Any())
            .Select(type => type.FullName)
            .Order(StringComparer.Ordinal)
            .ShouldBe(new[] { typeof(ChangeEmailPolicy).FullName, typeof(LoginChallengePolicy).FullName }
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_scope_factory_takes_only_windows()
    {
        // The scan fills a TimeSpan parameter and nothing else, so a factory it cannot call must fail here and
        // not be skipped.
        ApplicationTypes
            .SelectMany(type => type.GetMethods(Statics))
            .Where(IsScopeFactory)
            .Where(method => method.GetParameters().Any(parameter => parameter.ParameterType != typeof(TimeSpan)))
            .Select(method => $"{method.DeclaringType!.Name}.{method.Name}")
            .ShouldBeEmpty();
    }

    // Every static RateBudgetScope a type declares: fields, properties of either body form, and factories.
    private static IEnumerable<MemberInfo> ScopeMembers(Type type) =>
        type.GetFields(Statics)
            .Where(field => field.FieldType == typeof(RateBudgetScope) && !field.Name.StartsWith('<'))
            .Cast<MemberInfo>()
            .Concat(type.GetProperties(Statics).Where(property => property.PropertyType == typeof(RateBudgetScope)))
            .Concat(type.GetMethods(Statics).Where(IsScopeFactory));

    private static bool IsScopeFactory(MethodInfo method) =>
        method.ReturnType == typeof(RateBudgetScope) && !method.IsSpecialName;

    private static List<string> DeclaredScopeNames() =>
        ApplicationTypes
            .SelectMany(ScopeMembers)
            .Select(member => member switch
            {
                FieldInfo field => (RateBudgetScope)field.GetValue(null)!,
                PropertyInfo property => (RateBudgetScope)property.GetValue(null)!,
                MethodInfo factory => (RateBudgetScope)factory.Invoke(
                    null, [.. factory.GetParameters().Select(_ => (object)TimeSpan.FromMinutes(1))])!,
                _ => throw new InvalidOperationException($"Unread scope member {member.Name}."),
            })
            .Select(scope => scope.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

    // thisFile = <repo>/tests/Jobbliggaren.Architecture.Tests/<this file> → up two = repo root.
    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
