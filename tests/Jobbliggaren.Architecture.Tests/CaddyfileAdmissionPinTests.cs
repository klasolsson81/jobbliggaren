using System.Text.RegularExpressions;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1768 / ADR 0154 — the edge's admission gate stands unless <c>SITE_ADMISSION</c> is exactly
/// <c>open</c>, and an environment value never selects a file.
///
/// <para>
/// <c>deploy/caddy/edge-modes.test.sh</c> proves the behaviour on a built image. What it cannot see
/// is a regression it was not written for: the matcher is correct only in its exact text. Spliced in
/// at parse time (<c>{$SITE_ADMISSION}</c>) the value becomes expression source, and a non-boolean
/// expression makes Caddy report "no match" and skip the gate. A file import selected by a value is
/// a glob, and <c>o*</c> was measured opening the site that way (2026-10-06, caddy 2.11.4).
/// </para>
/// </summary>
public class CaddyfileAdmissionPinTests
{
    private const string GateMatcher = "@gated `{env.SITE_ADMISSION} != \"open\"`";

    private static string[] Lines() =>
        File.ReadAllLines(CaddyfileTokenScrubbingPinTests.CaddyfilePath())
            .Select(line => line.Trim())
            .Where(line => !line.StartsWith('#'))
            .ToArray();

    [Fact]
    public void TheGate_IsTheOnlyBasicAuth_AndCarriesTheMatcher()
    {
        var lines = Lines();

        lines.Count(line => line.StartsWith("basic_auth", StringComparison.Ordinal)).ShouldBe(1);
        lines.ShouldContain("basic_auth @gated {");
        lines.Count(line => line.StartsWith("@gated", StringComparison.Ordinal)).ShouldBe(1);
        lines.ShouldContain(
            GateMatcher,
            "the gate's matcher must read the environment at request time and compare it to the "
            + "exact string `open`. Any other text is a different gate.");
    }

    [Fact]
    public void TheAdmissionValue_NeverReachesTheParsedConfiguration()
        => File.ReadAllText(CaddyfileTokenScrubbingPinTests.CaddyfilePath())
            .ShouldNotContain(
                "{$SITE_ADMISSION",
                customMessage: "a parse-time substitution puts the value into the configuration as "
                + "source. Read it with `{env.SITE_ADMISSION}`.");

    [Fact]
    public void EveryImport_NamesASnippetThisFileDefines()
    {
        var lines = Lines();
        var snippets = lines
            .Select(line => SnippetDefinition.Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);
        var targets = lines
            .Where(line => line.StartsWith("import ", StringComparison.Ordinal))
            .Select(line => line["import ".Length..].Trim())
            .ToList();

        targets.ShouldContain(target => target.Contains("{$"));
        foreach (var target in targets)
        {
            // An environment-selected name resolves by its default when the variable is unset.
            snippets.ShouldContain(
                EnvironmentDefault.Replace(target, match => match.Groups["default"].Value),
                $"`import {target}` must name a snippet defined in deploy/caddy/Caddyfile. A path is "
                + "a glob, so an environment value selects whatever file it matches, and a file outside "
                + "this one escapes every pin that reads it.");
        }
    }

    private static readonly Regex SnippetDefinition = new(@"^\((?<name>[a-z0-9_-]+)\) \{$");

    private static readonly Regex EnvironmentDefault = new(@"\{\$[A-Z_]+:(?<default>[a-z0-9_]+)\}");
}
