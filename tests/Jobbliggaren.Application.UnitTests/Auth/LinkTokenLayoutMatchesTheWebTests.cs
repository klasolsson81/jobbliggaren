using System.Text.RegularExpressions;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Infrastructure.Auth.LoginChallenges;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// #2094 (dotnet-architect, Viktigt): the web's <c>src/lib/auth/link-token.ts</c> reads the layout of a login link token
/// that <see cref="RedisLoginChallengeStore"/> writes, the challenge id's bytes and then the secret's, so that a link
/// lands on the return path of its own login. Nothing else joins the two: were the store's layout to change, every
/// link would land on /oversikt with every suite green. Same move as <c>ContactAddressMatchesPublishedContactTests</c>:
/// a C# test reads the web app's file.
/// </summary>
public class LinkTokenLayoutMatchesTheWebTests
{
    private const string WebFile = "web/jobbliggaren-web/src/lib/auth/link-token.ts";

    [Fact]
    public void TheWebReadsTheChallengeIdAndTokenLengthsTheStoreWrites()
    {
        var source = ReadLinkTokenSource();

        IntConstant(source, "CHALLENGE_ID_BYTES").ShouldBe(
            ChallengeId.ByteLength, $"{WebFile} reads the challenge id's bytes; change both sides together.");
        IntConstant(source, "TOKEN_BYTES").ShouldBe(
            ChallengeId.ByteLength + RedisLoginChallengeStore.LinkSecretLength,
            $"{WebFile} reads the whole token's bytes; change both sides together.");
    }

    [Fact]
    public void TheWebsTokenPatternHasTheBase64UrlLengthOfThoseBytes()
    {
        var match = Regex.Match(ReadLinkTokenSource(), @"\[A-Za-z0-9_-\]\{(?<length>\d+)\}");
        match.Success.ShouldBeTrue($"{WebFile} no longer carries the token pattern this test reads.");

        var bytes = ChallengeId.ByteLength + RedisLoginChallengeStore.LinkSecretLength;
        int.Parse(match.Groups["length"].Value, System.Globalization.CultureInfo.InvariantCulture)
            .ShouldBe((bytes * 4 + 2) / 3, $"{WebFile}'s pattern must admit exactly an unpadded Base64Url of {bytes} bytes.");
    }

    private static int IntConstant(string source, string name)
    {
        var match = Regex.Match(source, $@"const {name} = (?<value>\d+);");
        match.Success.ShouldBeTrue($"{WebFile} no longer declares {name}.");
        return int.Parse(match.Groups["value"].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string ReadLinkTokenSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, WebFile);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"Could not find {WebFile} by walking up from {AppContext.BaseDirectory}. "
            + "This test reads the web app's file and needs the repo checkout to be present.");
    }
}
