using System.Text.RegularExpressions;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Email;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// #706 / ADR 0050 gate N-1 — binds the edge's query-string scrubbing to the parameter names the
/// mail templates actually render.
///
/// <para>
/// <b>The mechanism this exists for is case-sensitive and silent.</b> Caddy's <c>query</c> log
/// filter matches parameter keys exactly: measured 2026-08-29 on caddy 2.11.4, a request carrying
/// <c>?TOKEN=...</c> passed a filter configured to delete <c>token</c> and was logged verbatim. So
/// the scrubbing in <c>deploy/caddy/Caddyfile</c> holds only while the three link generators keep
/// spelling their parameters the way that file spells them, and nothing in either file can see the
/// other. Rename <c>token</c> to <c>t</c>, add an <c>otp</c> parameter, or upper-case a key, and the
/// edge keeps reporting success while a single-use account-takeover primitive resumes reaching
/// <c>http.log.error</c>.
/// </para>
///
/// <para>
/// <b>What is asserted is the pair, never one side.</b> A test that only read the Caddyfile would
/// pass against a generator that had moved on; one that only read the generators would pass against
/// an edge that scrubs nothing. The facts below therefore derive the parameter names from the real
/// <see cref="EmailTemplates"/> methods and require every one of them to be either scrubbed or
/// named as deliberately kept — so a NEW parameter fails until someone decides which it is, rather
/// than defaulting to exposed.
/// </para>
///
/// <para>
/// <b>On the premise (CLAUDE.md §5 <c>Tests:</c>).</b> The content records below are hand-built,
/// but nothing asserted here rests on their values: the parameter names are literals in the
/// generators and are identical for every input. The state under assertion is one <c>src/</c>
/// produces on every send.
/// </para>
///
/// <para>
/// <b>Referer is pinned in the same place because it carries the same secret.</b> The header
/// transports a token-bearing URL onto requests for OTHER routes, so it is dropped globally rather
/// than filtered per route; the Caddyfile's own comment owns that reasoning. Here it is only held
/// present, so removing the line fails a test rather than a review.
/// </para>
/// </summary>
public class CaddyfileTokenScrubbingPinTests
{
    private const string BaseUrl = "https://jobbliggaren.se";

    // Base64Url alphabet only, as the real tokens are.
    private const string Base64UrlToken = "Q2ZESjhL-nP_ab12CD"; // gitleaks:allow

    /// <summary>
    /// The parameters gate N-1 requires never to reach a stored log post. This list is the gate's,
    /// not this test's: G2 names <c>token</c> and <c>email</c>.
    /// </summary>
    private static readonly string[] ScrubbedParameters = ["token", "email"];

    /// <summary>
    /// Deliberately logged. <c>uid</c> is the only handle left for correlating a failed
    /// confirmation with an account, and it carries no secret.
    /// </summary>
    private static readonly string[] KeptParameters = ["uid"];

    private static readonly Regex TokenLink = new(
        @"https://\S+/(?:bekrafta-epost|bekrafta-konto|aterstall-losenord)\?\S+",
        RegexOptions.Compiled);

    /// <summary>
    /// Every query parameter the three token-bearing links render, as the generators spell it.
    /// </summary>
    private static List<string> RenderedParameterNames()
    {
        var bodies = new[]
        {
            EmailTemplates.EmailChangeConfirmation(
                BaseUrl,
                new EmailChangeConfirmationEmail(
                    Guid.NewGuid(), "ny.adress@example.se", Base64UrlToken)).PlainTextBody,
            EmailTemplates.EmailConfirmation(
                BaseUrl,
                new EmailConfirmationEmail(Guid.NewGuid(), Base64UrlToken)).PlainTextBody,
            EmailTemplates.PasswordReset(
                BaseUrl,
                new PasswordResetEmail(Guid.NewGuid(), Base64UrlToken)).PlainTextBody,
        };

        return bodies
            .Select(body => TokenLink.Match(body))
            .Where(match => match.Success)
            .Select(match => match.Value[(match.Value.IndexOf('?') + 1)..])
            .SelectMany(query => query.Split('&'))
            .Select(pair => pair.Split('=')[0])
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    [Fact]
    public void EveryRenderedQueryParameter_IsEitherScrubbedAtTheEdge_OrNamedAsDeliberatelyKept()
    {
        var scrubbed = CaddyfileScrubbedParameters();

        foreach (var name in RenderedParameterNames())
        {
            var handled = scrubbed.Contains(name, StringComparer.Ordinal)
                || KeptParameters.Contains(name, StringComparer.Ordinal);

            handled.ShouldBeTrue(
                $"the mail templates render a query parameter '{name}' that "
                + "deploy/caddy/Caddyfile neither scrubs nor is documented here as kept. "
                + "Decide which it is: add it to the Caddyfile's query filter, or to "
                + $"{nameof(KeptParameters)} with the reason. ADR 0050 gate N-1 / #706.");
        }
    }

    [Fact]
    public void TheCaddyfile_ScrubsEveryParameterTheGateNames_SpelledAsTheGeneratorsSpellIt()
    {
        var scrubbed = CaddyfileScrubbedParameters();
        var rendered = RenderedParameterNames();

        foreach (var name in ScrubbedParameters)
        {
            // Byte-exact, because Caddy's query filter is. A Caddyfile saying `delete Token`
            // against a generator rendering `token` scrubs nothing and reports nothing.
            scrubbed.ShouldContain(
                name,
                $"deploy/caddy/Caddyfile does not delete '{name}' from the logged query string.");

            // And the gate's name must be one something actually renders — otherwise the filter
            // protects a parameter that no longer exists while the real one flows past it.
            rendered.ShouldContain(
                name,
                $"no token-bearing link renders '{name}' any more, so the Caddyfile's filter for "
                + "it is dead. Re-derive the gate's list from the generators.");
        }
    }

    [Fact]
    public void TheCaddyfile_DropsTheRefererHeader()
    {
        // A same-origin navigation from a token-bearing page puts the whole URL on requests for
        // other routes, where no route-scoped filter reaches it.
        CaddyfileLines()
            .Select(line => line.Trim())
            .ShouldContain("request>headers>Referer delete");
    }

    [Fact]
    public void TheOracle_ActuallyReadsThreeLinksAndANonEmptyFilter()
    {
        // Guards the shape the facts above assume. Without this, a regex that stopped matching, or
        // a Caddyfile whose filter block was renamed, would make both of them pass over empty
        // sets — green because nothing was found rather than because everything was right.
        var rendered = RenderedParameterNames();

        rendered.ShouldContain("uid");
        rendered.Count.ShouldBeGreaterThanOrEqualTo(3);
        CaddyfileScrubbedParameters().ShouldNotBeEmpty();
    }

    /// <summary>
    /// The parameter names deleted inside the Caddyfile's <c>request&gt;uri query</c> filter, read
    /// as source text. Line-based on purpose: the file is the artefact that ships, and a parser
    /// clever enough to normalise it could hide the very spelling this class exists to compare.
    /// </summary>
    private static List<string> CaddyfileScrubbedParameters()
    {
        var names = new List<string>();
        var inQueryFilter = false;

        foreach (var raw in CaddyfileLines())
        {
            var line = raw.Trim();

            if (line.StartsWith("request>uri query", StringComparison.Ordinal))
            {
                inQueryFilter = true;
                continue;
            }

            if (!inQueryFilter) continue;
            if (line == "}") break;

            if (line.StartsWith("delete ", StringComparison.Ordinal))
            {
                names.Add(line["delete ".Length..].Trim());
            }
        }

        return names;
    }

    private static string[] CaddyfileLines() => File.ReadAllLines(CaddyfilePath());

    /// <summary>
    /// Walks up from the test binary until the repo root is found, so the test project's own depth
    /// is not hardcoded. Fails loud and names the path it looked for.
    /// </summary>
    private static string CaddyfilePath()
    {
        var relative = Path.Combine("deploy", "caddy", "Caddyfile");

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"Could not find {relative} by walking up from {AppContext.BaseDirectory}. "
            + "The edge scrubbing pin needs the repo checkout to be present.");
    }
}
