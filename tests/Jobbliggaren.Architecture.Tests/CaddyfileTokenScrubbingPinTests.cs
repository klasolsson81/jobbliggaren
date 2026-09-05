using System.Text.RegularExpressions;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Email;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #706 / ADR 0050 gate N-1 — binds the edge's query-string scrubbing to the parameter names the
/// mail templates actually render, and to the block the scrubbing has to live in.
///
/// <para>
/// <b>The mechanism this exists for is case-sensitive and silent.</b> Caddy's <c>query</c> log
/// filter matches parameter keys exactly: measured 2026-08-29 on caddy 2.11.4, a request carrying
/// <c>?TOKEN=...</c> passed a filter configured to delete <c>token</c> and was logged verbatim. So
/// the scrubbing in <c>deploy/caddy/Caddyfile</c> holds only while the three link generators keep
/// spelling their parameters the way that file spells them, and nothing in either file can see the
/// other.
/// </para>
///
/// <para>
/// <b>Placement is load-bearing, not incidental.</b> A <c>log</c> block in the GLOBAL options
/// configures the default logger — the one that writes <c>http.log.error</c>. The same lines
/// moved inside the site block configure a separate access logger instead, leaving the default
/// logger unconfigured and the request line unscrubbed again, while logging every request rather
/// than only 5xx. Both were raised independently by <c>code-reviewer</c> and
/// <c>security-auditor</c> against an earlier, position-blind version of this class. Every fact
/// below therefore reads <see cref="GlobalOptionsLines"/> rather than the whole file, and
/// <see cref="TheCaddyfile_CarriesExactlyOneLogDirective"/> closes the other half: a SECOND
/// <c>log</c> block would not be seen by a parser that stops at the first.
/// </para>
///
/// <para>
/// <b>What is asserted is the pair, never one side.</b> A test that only read the Caddyfile would
/// pass against a generator that had moved on; one that only read the generators would pass against
/// an edge that scrubs nothing. The facts below derive the parameter names from the real
/// <see cref="EmailTemplates"/> methods and require every one of them to be either filtered or
/// named as deliberately kept — so a NEW parameter fails until someone decides which it is, rather
/// than defaulting to exposed.
/// </para>
///
/// <para>
/// <b>The mechanism is deliberately not pinned.</b> G2 says "mekanismen är i övrigt fri; kravet är
/// resultatet" and names <c>delete</c>, <c>replace</c> and <c>hash</c>. Accepting all three keeps
/// this class measuring the requirement rather than one legal spelling of it.
/// </para>
///
/// <para>
/// <b>On the premise (CLAUDE.md §5 <c>Tests:</c>).</b> The content records below are hand-built,
/// but nothing asserted here rests on their values: the parameter names are literals in the
/// generators and are identical for every input. The state under assertion is one <c>src/</c>
/// produces on every send.
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
    /// Parameters allowed to reach a log post unfiltered. Deliberately EMPTY: every parameter the
    /// three links render is filtered at the edge. A future parameter goes here only with a written
    /// reason, and adding one is the decision this array exists to make visible.
    /// </summary>
    private static readonly string[] KeptParameters = [];

    /// <summary>
    /// Parameters the APP SURFACE renders that must not reach a stored log post either. Kept apart
    /// from <see cref="ScrubbedParameters"/> on purpose: that list is derived from the mail
    /// generators and every entry must be one they render, while nothing here is a mail parameter
    /// at all. Merging them would make one of the two facts below unsatisfiable.
    /// <para>
    /// <c>employer</c> (#1547): Översikt's summary links carry the user's WHOLE set of watched
    /// org.nr in one URL, and a recent-search replay (#1471) carries the employers one captured
    /// search filtered on. A single org.nr is public-register data
    /// any visitor can type; the set is "whom this user watches" — ADR 0087 D8(b) personal data
    /// about the user, protected there by owner-scoped access and an Art. 17 cascade, neither of
    /// which reaches an edge log.
    /// </para>
    /// <para>
    /// <c>q</c>: the user's free search text, and the only key on this surface that no gate
    /// constrains — <c>parseQParam</c> gates arity and <c>clampSubMinimumQ</c> gates length,
    /// neither gates content. A retention purpose therefore cannot be written for it, which is
    /// Art. 5(1)(c), the same ground <c>uid</c> was deleted on in ADR 0050's
    /// Amendment 2026-08-29 — and, where the term is health-adjacent, Art. 9(1) with no 9(2)
    /// exception available to an edge log. Note the asymmetry it closes: <c>employer</c> is format-gated to ten
    /// digits and was already scrubbed, while the ungated field — the one that can actually carry
    /// a personnummer — was not.
    /// </para>
    /// <para>
    /// <c>userId</c>: <b>not</b> covered by <c>uid</c> above. The filter matches keys exactly and
    /// case-sensitively — the measurement this class opens with — so the mail parameter's entry
    /// protects nothing here. <c>/admin/granskning</c>'s pagination links carry it, and it is a
    /// DIRECT identifier of a natural person (Art. 4(1)); the URL additionally discloses WHOSE
    /// audit records were read, which is more than <c>employer</c> says about anyone.
    /// </para>
    /// <para>
    /// <c>namn</c>: the free-text company-name field on <c>/foretag/sok</c>. Same unbounded-content
    /// class as <c>q</c>, with one addition: <c>proxy.ts</c> washes an org.nr-shaped value out of
    /// it, which is the app stating that it EXPECTS org.nr there — and for an enskild firma the
    /// org.nr IS the holder's personnummer (#841). The wash also fires only on org.nr-shaped
    /// values; every other free-text string passes through untouched.
    /// </para>
    /// <para>
    /// <c>eventType</c> / <c>aggregateType</c>: they read like closed enums and are not. Both are
    /// <c>&lt;input type="text" maxLength={100}&gt;</c> in a native GET form, and
    /// <c>GetAuditLogEntriesQueryValidator</c>'s own comment calls them fri-text-fält while
    /// bounding LENGTH only. A length cap is not a content cap — the same distinction <c>q</c> is
    /// deleted on.
    /// </para>
    /// <para>
    /// <c>prefix</c>: the <c>/jobb</c> search box's LIVE keystrokes, one request per ~300 ms to
    /// <c>/api/jobb/suggest</c>. The same user input as <c>q</c>, which is already deleted here;
    /// streaming it makes the exposure larger, not smaller. Its surface owes an inventory file
    /// still, and that debt is named in the app-surface coverage fact rather than left silent.
    /// </para>
    /// <para>
    /// <b>What decides whether a name belongs here.</b> Scrub when the value's content is
    /// UNBOUNDED, or when the value IS an identifier of a natural person. Everything else draws
    /// from a closed, published or enumerated value space and has a stated purpose — it selects
    /// which server-side query path ran — and is kept.
    /// (senior-cto-advisor, 2026-08-30.) The rule lives here rather than in any one route's test
    /// file because it governs every app surface, and the next surface's author opens this array.
    /// </para>
    /// <para>
    /// <b>This list is one half of a pair, and C# cannot derive the other.</b> The mail inventory
    /// above comes from the real <see cref="EmailTemplates"/> methods. The <c>/jobb</c> inventory
    /// exists only by CALLING that route's TypeScript URL builders, so it is derived — and every
    /// key OF THAT ROUTE judged scrubbed or kept — in
    /// <c>web/jobbliggaren-web/src/lib/job-ads/axis-edge-log-inventory.test.ts</c>, which reads
    /// this array and fails in both directions. A name added here from ANOTHER surface owes its
    /// own inventory file, and until it has one the debt is named in
    /// <c>web/jobbliggaren-web/src/lib/edge-log/app-surface-coverage.test.ts</c>, which requires
    /// every name on this array to be either judged by some surface or listed there with a
    /// reason.
    /// That file deliberately does not parse the Caddyfile — the placement sensitivity above is
    /// owned here and nowhere else.
    /// </para>
    /// </summary>
    private static readonly string[] AppSurfaceScrubbedParameters =
        ["employer", "q", "userId", "namn", "eventType", "aggregateType", "prefix"];

    private static readonly Regex TokenLink = new(
        @"https://\S+/(?:bekrafta-epost|bekrafta-konto|aterstall-losenord)\?\S+",
        RegexOptions.Compiled);

    /// <summary>A line opening a <c>log</c> directive, at any indentation.</summary>
    private static readonly Regex LogDirective = new(@"^log\b", RegexOptions.Compiled);

    /// <summary>
    /// The three token-bearing links, as the real generators render them into the plain-text part.
    /// </summary>
    private static List<string> RenderedLinks()
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
            .Select(match => match.Value)
            .ToList();
    }

    /// <summary>
    /// Every query parameter those links render, as the generators spell it.
    /// </summary>
    private static List<string> RenderedParameterNames()
        => RenderedLinks()
            .Select(link => link[(link.IndexOf('?') + 1)..])
            .SelectMany(query => query.Split('&'))
            .Select(pair => pair.Split('=')[0])
            .Distinct(StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void EveryRenderedQueryParameter_IsEitherFilteredAtTheEdge_OrNamedAsDeliberatelyKept()
    {
        var filtered = CaddyfileFilteredParameters();

        foreach (var name in RenderedParameterNames())
        {
            var handled = filtered.Contains(name, StringComparer.Ordinal)
                || KeptParameters.Contains(name, StringComparer.Ordinal);

            handled.ShouldBeTrue(
                $"the mail templates render a query parameter '{name}' that "
                + "deploy/caddy/Caddyfile neither filters nor is documented here as kept. "
                + "Decide which it is: add it to the Caddyfile's query filter, or to "
                + $"{nameof(KeptParameters)} with the reason. ADR 0050 gate N-1 / #706.");
        }
    }

    [Fact]
    public void TheCaddyfile_FiltersEveryParameterTheGateNames_SpelledAsTheGeneratorsSpellIt()
    {
        var filtered = CaddyfileFilteredParameters();
        var rendered = RenderedParameterNames();

        foreach (var name in ScrubbedParameters)
        {
            // Byte-exact, because Caddy's query filter is. A Caddyfile saying `delete Token`
            // against a generator rendering `token` scrubs nothing and reports nothing.
            filtered.ShouldContain(
                name,
                $"deploy/caddy/Caddyfile does not filter '{name}' out of the logged query string.");

            // And the gate's name must be one something actually renders — otherwise the filter
            // protects a parameter that no longer exists while the real one flows past it.
            rendered.ShouldContain(
                name,
                $"no token-bearing link renders '{name}' any more, so the Caddyfile's filter for "
                + "it is dead. Re-derive the gate's list from the generators.");
        }
    }

    [Fact]
    public void TheCaddyfile_FiltersEveryAppSurfaceParameterThatCarriesPersonalData()
    {
        var filtered = CaddyfileFilteredParameters();

        foreach (var name in AppSurfaceScrubbedParameters)
        {
            // Byte-exact for the same reason as the mail parameters: Caddy's query filter is.
            filtered.ShouldContain(
                name,
                $"deploy/caddy/Caddyfile does not filter '{name}' out of the logged query string. "
                + "The mail-template derivation cannot see this parameter — it is rendered by the "
                + "app, not by a generator — so this list is the only thing holding it. "
                + "ADR 0087 D8(b) / #1547.");
        }
    }

    [Fact]
    public void TheCaddyfile_DropsTheWholeRequestHeaderMap()
    {
        // Not a list of header names. `Referer` carries the token-bearing URL onto requests for
        // OTHER routes, and Caddy's own credential redaction — itself a deny-list — was measured
        // leaving it in cleartext beside a REDACTED `Authorization`. A deny-list cannot satisfy a
        // requirement written as a result.
        GlobalOptionsLines()
            .Select(line => line.Trim())
            .ShouldContain("request>headers delete");
    }

    [Fact]
    public void TheCaddyfile_CarriesExactlyOneLogDirective()
    {
        // A second `log` block beside the global one is valid Caddy and would be invisible to a
        // parser that stops at the first. The challenge snippets are imported INTO the site block,
        // so they can carry one too.
        var everyLine = CaddyfileLines()
            .Concat(ChallengeFiles().SelectMany(File.ReadAllLines));

        everyLine.Count(line => LogDirective.IsMatch(line.Trim())).ShouldBe(
            1,
            "deploy/caddy/ must carry exactly one `log` directive. A second one is a separate "
            + "logger that inherits none of the global block's filters, and gate N-1 reopens.");
    }

    [Fact]
    public void TheOracle_ReadsAllThreeLinks_AndANonEmptyFilterInsideGlobalOptions()
    {
        // Guards the shape the facts above assume. `RenderedParameterNames` de-duplicates, and
        // /bekrafta-epost alone renders the whole union {uid, email, token} — so counting NAMES
        // cannot tell three matched links from one. Count the links.
        RenderedLinks().Count.ShouldBe(
            3,
            "a token-bearing link stopped matching TokenLink, so the pin silently reads fewer "
            + "generators than it claims.");

        CaddyfileFilteredParameters().ShouldNotBeEmpty();
        GlobalOptionsLines().Length.ShouldBeLessThan(CaddyfileLines().Length);

        // Emptying this list would let its own fact pass over zero iterations and the pin would
        // disappear without a red run. `ScrubbedParameters` is backstopped by the
        // generator-derived fact above; nothing derives this one, so the shape guard is here.
        AppSurfaceScrubbedParameters.ShouldNotBeEmpty();
    }

    /// <summary>
    /// The parameter names the Caddyfile's <c>request&gt;uri query</c> filter acts on, read as
    /// source text. Line-based on purpose: the file is the artefact that ships, and a parser clever
    /// enough to normalise it could hide the very spelling this class exists to compare.
    /// </summary>
    private static List<string> CaddyfileFilteredParameters()
    {
        var names = new List<string>();
        var inQueryFilter = false;

        foreach (var raw in GlobalOptionsLines())
        {
            var line = raw.Trim();

            if (line.StartsWith("request>uri query", StringComparison.Ordinal))
            {
                inQueryFilter = true;
                continue;
            }

            if (!inQueryFilter) continue;
            if (line == "}") break;

            // G2 leaves the mechanism free and names all three of these.
            foreach (var action in new[] { "delete ", "replace ", "hash " })
            {
                if (!line.StartsWith(action, StringComparison.Ordinal)) continue;

                names.Add(line[action.Length..].Split(' ')[0].Trim());
                break;
            }
        }

        return names;
    }

    /// <summary>
    /// The Caddyfile's global options block — everything before the site block opens. A filter
    /// found below that line configures a different logger and does not close gate N-1.
    /// </summary>
    private static string[] GlobalOptionsLines()
        => CaddyfileLines()
            .TakeWhile(line => !line.TrimStart().StartsWith("{$SITE_HOST}", StringComparison.Ordinal))
            .ToArray();

    private static string[] CaddyfileLines() => File.ReadAllLines(CaddyfilePath());

    private static string[] ChallengeFiles()
        => Directory.GetFiles(
            Path.Combine(Path.GetDirectoryName(CaddyfilePath())!, "challenge"), "*.caddy");

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
