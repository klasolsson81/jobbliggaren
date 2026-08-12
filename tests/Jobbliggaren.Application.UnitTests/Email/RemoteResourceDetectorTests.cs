using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// Proves <see cref="RemoteResourceDetector"/> can FAIL. An absence assertion over a detector nobody
/// has seen reject anything is fail-open: it cannot tell "there is no remote resource" from "the
/// detector is broken", and the Art. 30 retention entry's ground 2 rests on the difference.
///
/// <para>
/// <b>The first version of this suite was fail-open in a way that no counterfactual could see, and it
/// took an auditor to find it (security-auditor Major 1, 2026-08-12).</b> Every arm had a
/// counterfactual, so every arm was proven able to fire IN ISOLATION — on a document containing
/// nothing else. None was proven to fire across a whole document, and the tag-span regex was
/// <c>&lt;[^&gt;]*&gt;</c>, which ends a "tag" at the first <c>&gt;</c> even inside a quoted attribute
/// value. Six constructed documents fetched and passed. The lesson is written into the shape of this
/// file: <see cref="FindRemoteResources_WhenAnEarlierAttributeContainsAngleBracket_StillReportsIt"/>
/// exists because a probe that never crosses the control it tests measures nothing about the control.
/// </para>
/// </summary>
public class RemoteResourceDetectorTests
{
    private const string BaseUrl = "https://jobbliggaren.se";

    private static string Wrap(string fragment) =>
        $"<!DOCTYPE html><html lang=\"sv\"><body>{fragment}</body></html>";

    [Theory]
    [InlineData("""<p><img src="https://tracker.example/p.gif"></p>""")]
    [InlineData("""<p><script src="https://cdn.example/a.js"></script></p>""")]
    [InlineData("""<link rel="stylesheet" href="https://cdn.example/a.css">""")]
    [InlineData("""<iframe src="https://evil.example/"></iframe>""")]
    [InlineData("""<video poster="https://cdn.example/v.jpg"></video>""")]
    [InlineData("""<img srcset="https://cdn.example/a.png 1x">""")]
    [InlineData("""<td background="https://cdn.example/bg.png">x</td>""")]
    [InlineData("""<div style="background-image:url('https://cdn.example/bg.png')">x</div>""")]
    [InlineData("""<input type="image" src="https://cdn.example/b.png">""")]
    public void FindRemoteResources_WhenTheDocumentFetches_ReportsIt(string fragment)
    {
        FindRemoteResources(fragment).ShouldNotBeEmpty();
    }

    [Fact]
    public void FindRemoteResources_WhenAStyleBlockAppears_ReportsItAsItsOwnCategory()
    {
        // <style> cannot fetch by itself, and is banned anyway: it is @import's only vehicle, and this
        // codebase's email layout may not depend on a style block at all. It is reported as its own
        // category so a future legitimate <style> question is not read as "the Art. 30 entry is
        // false" — a false alarm that pressures the reader to weaken the detector is the failure mode
        // the detector's own doc warns about (dotnet-architect Nice-to-have 3, 2026-08-12).
        //
        // @import deliberately has no arm of its own; this fact is what covers it.
        var findings = FindRemoteResources("""<style>@import url("https://fonts.example/f.css");</style>""");

        findings.ShouldNotBeEmpty();
        findings.ShouldContain(f => f.Contains("cannot fetch by itself", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""<td title="a>b" background="https://evil.example/bg.png">x</td>""")]
    [InlineData("""<div title="a>b" style="background-image:url(https://evil.example/b.png)">x</div>""")]
    [InlineData("""<a title="a>b" href="https://evil.example/x">x</a>""")]
    [InlineData("""<td data-x='a>b' background="https://evil.example/bg.png">x</td>""")]
    public void FindRemoteResources_WhenAnEarlierAttributeContainsAngleBracket_StillReportsIt(
        string fragment)
    {
        // THE case the first version missed. A `>` inside a quoted attribute value used to end the
        // tag span, so everything after it fell out of "live markup" and the fetch went unreported —
        // while every isolated counterfactual stayed green. Nothing here can be produced by our own
        // templates (Encode turns `>` into `&gt;`), which is exactly why it needed an explicit probe
        // rather than trust: the pin must hold against any document, not only against ours.
        FindRemoteResources(fragment).ShouldNotBeEmpty();
    }

    [Fact]
    public void FindRemoteResources_WhenAnAnchorPointsOffHost_ReportsIt()
    {
        // A bare off-host link fetches nothing on its own, and is still a finding: it is how a click
        // tracker or a redirector gets in, and "no absolute URL whose host lies outside BaseUrl" is
        // the form security-auditor's condition 1 names. The protocol-relative form must be caught
        // too, since it is the one that looks like a path.
        FindRemoteResources("""<a href="https://sponsor.example/x">x</a>""").ShouldNotBeEmpty();
        FindRemoteResources("""<a href="//sponsor.example/x">x</a>""").ShouldNotBeEmpty();
    }

    [Fact]
    public void FindRemoteResources_WhenAHostMerelyLooksLikeOurs_ReportsIt()
    {
        // The host comparison is exact, not a suffix or prefix test: `jobbliggaren.se.evil.example`
        // and `evil-jobbliggaren.se` both contain the allowed host as a substring, and a detector
        // built on Contains would wave both through. The third form hides the real host behind
        // userinfo, which is the classic way a URL is read wrong by eye.
        FindRemoteResources("""<a href="https://jobbliggaren.se.evil.example/x">x</a>""")
            .ShouldNotBeEmpty();
        FindRemoteResources("""<a href="https://evil-jobbliggaren.se/x">x</a>""").ShouldNotBeEmpty();
        FindRemoteResources("""<a href="https://jobbliggaren.se@evil.example/x">x</a>""")
            .ShouldNotBeEmpty();
    }

    [Fact]
    public void FindRemoteResources_WhenEverythingIsOnHost_ReportsNothing()
    {
        // The control that keeps every counterfactual above honest: the detector must not be one that
        // rejects all input. Without this, every arm would also "pass" against a detector hard-wired
        // to report a finding.
        FindRemoteResources("""<a href="https://jobbliggaren.se/jobb">Öppna annonserna</a>""")
            .ShouldBeEmpty();
    }

    [Fact]
    public void FindRemoteResources_WhenInertTextMentionsAnOffHostUrl_ReportsNothing()
    {
        // The reason attribute and URL arms are scoped to live markup. Encoded ad text literally
        // contains `src=` and a URL while being unable to fetch anything; reporting it would be a
        // finding about a string no client acts on, and the first honest fixture carrying an injected
        // payload would force whoever met it to weaken a GDPR pin to get green.
        FindRemoteResources("""<p>Acme &lt;img src="https://evil.example/p.gif"&gt; AB</p>""")
            .ShouldBeEmpty();
    }

    [Fact]
    public void FindRemoteResources_WhenEncodingIsLost_ReportsIt()
    {
        // The counterfactual that makes the encoding test in EmailHtmlNoRemoteResourceTests
        // non-vacuous. EmailHtml.Encode is what stands between third-party ad text and live markup in
        // a mail we DKIM-sign; if it were removed, the same company name would arrive as a live <img>.
        // Feeding the detector that un-encoded document proves "the injection test is green" can never
        // mean "the detector cannot see an injection".
        FindRemoteResources("""<p>Acme <img src="https://evil.example/pixel.gif"> AB</p>""")
            .ShouldNotBeEmpty();
    }

    private static IReadOnlyList<string> FindRemoteResources(string fragment) =>
        RemoteResourceDetector.FindRemoteResources(Wrap(fragment), BaseUrl);
}
