using System.Globalization;
using System.Text.RegularExpressions;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Commands.ConsumeLoginLink;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1738 — joins the numbers the login pages MIRROR from the backend to the backend's own.
///
/// <para>
/// <b>The contract.</b> The web client cannot read a C# constant, so it re-types five: how long the
/// code and consent cookies live, how long "Skicka ny kod" stays closed, how many digits a code has,
/// and how long a link token may be. No test compared the two sides. The drift is not cosmetic: a
/// longer server cooldown than the client's lets a resend through that answers with a record-less
/// challenge id, and a longer code makes the client refuse every code before it is sent.
/// </para>
///
/// <para>
/// <b>Direction</b> is <see cref="AuthErrorCodeWireContractTests"/>'s: the likely accident is
/// backend-first, and a backend change runs <c>dotnet test</c>.
/// </para>
///
/// <para>
/// <b>On the premise (AGENTS.md §5 <c>Tests:</c>).</b> The client's numbers are read out of the
/// shipped source files, and the backend's out of the real constants, the options type and the real
/// validator. The cooldown joins the options' DEFAULT: a value set in configuration is not visible
/// from here.
/// </para>
/// </summary>
public class LoginFlowMirrorWireContractTests
{
    private const string FlowModule = "web/jobbliggaren-web/src/lib/auth/login-flow.ts";
    private const string SchemaModule = "web/jobbliggaren-web/src/lib/auth/challenge-schemas.ts";

    [Fact]
    public void TheCodeCookieLivesAsLongAsTheChallenge() =>
        ReadSeconds("CODE_PHASE_MAX_AGE_SECONDS")
            .ShouldBe((int)LoginChallengePolicy.ChallengeTtl.TotalSeconds, Hint(FlowModule));

    [Fact]
    public void TheConsentCookieLivesAsLongAsTheGrant() =>
        ReadSeconds("CONSENT_PHASE_MAX_AGE_SECONDS")
            .ShouldBe((int)LoginChallengePolicy.GrantTtl.TotalSeconds, Hint(FlowModule));

    [Fact]
    public void TheResendCooldownIsTheDefaultServerWindow() =>
        ReadSeconds("RESEND_COOLDOWN_SECONDS")
            .ShouldBe(new AuthEmailCooldownOptions().LoginChallengeWindowSeconds, Hint(FlowModule));

    [Fact]
    public void TheCodeFieldAsksForAsManyDigitsAsTheBackendMints()
    {
        var digits = Capture(SchemaModule, @"\bcodeInputSchema\b[^;]*?\[0-9\]\{(\d+)\}");

        digits.ShouldBe(LoginChallengePolicy.CodeLength, Hint(SchemaModule));
    }

    [Fact]
    public void TheLinkTokenBoundIsTheValidatorsOwn()
    {
        var bound = Capture(SchemaModule, @"\blinkTokenInputSchema\b[^;]*?\.max\((\d+)\)");
        var validator = new ConsumeLoginLinkCommandValidator();

        validator.Validate(new ConsumeLoginLinkCommand(new string('x', bound))).IsValid
            .ShouldBeTrue($"the client sends a token of {bound} characters. " + Hint(SchemaModule));
        validator.Validate(new ConsumeLoginLinkCommand(new string('x', bound + 1))).IsValid
            .ShouldBeFalse($"the client refuses a token of {bound + 1} characters. " + Hint(SchemaModule));
    }

    private static string Hint(string module) =>
        $"{module} re-types this number from the backend. Change both sides in the same PR.";

    /// <summary>An exported constant written as an integer or as a product of integers.</summary>
    private static int ReadSeconds(string name)
    {
        var expression = Regex
            .Match(Read(FlowModule), $@"\bexport\s+const\s+{name}\s*=\s*([^;]+);")
            .Groups[1];

        if (!expression.Success)
            throw Unreadable(name, FlowModule);

        var product = 1;
        foreach (var factor in expression.Value.Split('*'))
        {
            if (!int.TryParse(factor.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                throw Unreadable(name, FlowModule);
            product *= value;
        }

        return product;
    }

    private static int Capture(string module, string pattern)
    {
        var group = Regex.Match(Read(module), pattern, RegexOptions.Singleline).Groups[1];
        return group.Success
            ? int.Parse(group.Value, CultureInfo.InvariantCulture)
            : throw Unreadable(pattern, module);
    }

    /// <summary>Comments go first: they name these numbers in prose.</summary>
    private static string Read(string module)
    {
        var source = File.ReadAllText(
            Path.Combine(FindRepoRoot(), module.Replace('/', Path.DirectorySeparatorChar)));
        source = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return Regex.Replace(source, @"//[^\n]*", string.Empty);
    }

    private static InvalidOperationException Unreadable(string what, string module) =>
        new($"Could not read '{what}' out of {module}. It was renamed or reshaped - re-make this "
            + "join deliberately, do not delete it.");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;

        dir.ShouldNotBeNull(
            "could not find the repo root (CLAUDE.md) walking up from the test bin - this class "
            + "needs the source tree for its cross-language source-text scan");
        return dir!.FullName;
    }
}
