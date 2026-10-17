using System.Reflection;
using Jobbliggaren.Application.Auth.Activity;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #2161 (ADR 0172 D3) — the activity record means what its two writers make it mean: "last login" is a session the
/// grant issued, "last active" a request the session handler accepted. A third caller would change both meanings
/// without touching the table, so the port's consumers are pinned here, across every assembly that can resolve it.
/// </summary>
public class AccountActivityRecorderConsumerTests
{
    private const BindingFlags Declared =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
        | BindingFlags.DeclaredOnly;

    private static readonly Assembly[] OwnedAssemblies =
    [
        typeof(Jobbliggaren.Application.AssemblyMarker).Assembly,
        typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly,
        typeof(Jobbliggaren.Api.Endpoints.AdminJobAdsEndpoints).Assembly,
        typeof(Jobbliggaren.Worker.Auditing.WorkerSystemUser).Assembly,
        typeof(Jobbliggaren.Migrate.ConnectionStringFactory).Assembly,
    ];

    [Fact]
    public void Only_the_session_grant_and_the_request_middleware_take_the_activity_recorder()
    {
        var consumers = OwnedAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.GetConstructors(Declared).Cast<MethodBase>().Concat(type.GetMethods(Declared))
                .Any(member => member.GetParameters()
                    .Any(parameter => parameter.ParameterType == typeof(IAccountActivityRecorder))))
            .Select(type => type.FullName!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        consumers.ShouldBe(
            new[]
            {
                typeof(PasswordlessSessionGrant).FullName!,
                "Jobbliggaren.Api.Activity.AccountActivityMiddleware",
            }.OrderBy(name => name, StringComparer.Ordinal).ToList(),
            "A new caller of the activity recorder changes what 'last login' or 'last active' means; ADR 0172 D1 "
            + "defines both. Found: " + string.Join(", ", consumers));
    }
}
