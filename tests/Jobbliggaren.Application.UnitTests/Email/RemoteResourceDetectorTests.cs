using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// Proves <see cref="RemoteResourceDetector"/> can FAIL. An absence assertion over a detector nobody
/// has seen reject anything is fail-open: it cannot tell "there is no remote resource" from "the
/// detector is broken", and the Art. 30 retention entry's ground 2 rests on the difference.
///
/// <para>
/// <b>Coverage, and it is STRUCTURAL rather than asserted.</b> The element and attribute theories are
/// driven from <see cref="RemoteResourceDetector"/>'s own arrays through <c>MemberData</c>, so a
/// literal added there gets a probe or fails the build — the claim cannot go quiet the way a
/// transcribed list can. The remaining arms (the forbidden element, the CSS construct, the off-host
/// URL) have one probe each and are single-literal today. The attribute and CSS probes use ON-HOST URLs and assert the FINDING STRING, because an
/// off-host URL in the same fixture would let the URL arm satisfy the assertion on its own and the
/// arm under test would never be measured. That was true of the previous version of this file, whose
/// doc nonetheless claimed a counterfactual per arm — code-reviewer Major 3 measured six element
/// literals with no probe at all, and every attribute probe carrying an off-host URL. The claim was
/// broader than the measurement carrying it, which is the same defect this suite exists to prevent.
/// </para>
///
/// <para>
/// <b>Two earlier failures worth keeping in view.</b> The tag-span regex was once
/// <c>&lt;[^&gt;]*&gt;</c>, which ends a "tag" at the first <c>&gt;</c> even inside a quoted attribute
/// value; six constructed documents fetched and passed while every isolated probe stayed green
/// (security-auditor Major 1). Nothing crossed the tag boundary, so
/// <see cref="FindRemoteResources_WhenAnEarlierAttributeContainsAngleBracket_StillReportsIt"/> exists:
/// a probe that never crosses the control it tests measures nothing about the control. And
/// <see cref="FindRemoteResources_WithOddQuoteParity_IsKnownNotToReport"/> pins the remaining blind
/// spot as a DECLARED limit rather than leaving it to be discovered.
/// </para>
/// </summary>
public class RemoteResourceDetectorTests
{
    private const string BaseUrl = "https://jobbliggaren.se";
    private const string OnHost = "https://jobbliggaren.se/asset.png";

    private static string Wrap(string fragment) =>
        $"<!DOCTYPE html><html lang=\"sv\"><body>{fragment}</body></html>";

    private static IReadOnlyList<string> FindRemoteResources(string fragment) =>
        RemoteResourceDetector.FindRemoteResources(Wrap(fragment), BaseUrl);

    // ---------- the element arm: one probe per literal ----------

    public static TheoryData<string> FetchingElements()
    {
        // Driven FROM the detector's own array, never transcribed. A hand-written probe list makes
        // "every literal has a probe" a claim that goes quiet the moment a literal is added — the
        // same growth-blindness the template guard and the palette guard both had, and the third
        // instance of it in this PR (code-reviewer, 2026-08-12).
        var data = new TheoryData<string>();
        foreach (var element in RemoteResourceDetector.FetchingElements)
            data.Add(element);
        return data;
    }

    [Theory]
    [MemberData(nameof(FetchingElements))]
    public void FindRemoteResources_ForEveryFetchingElement_ReportsThatElement(string element)
    {
        // Bare tag, no URL and no source attribute, so ONLY the element arm can produce a finding and
        // the assertion cannot be satisfied by another arm. Six of these had no probe at all before
        // (code-reviewer Major 3).
        var name = element.TrimStart('<');

        FindRemoteResources($"<{name}></{name}>")
            .ShouldContain($"fetching element: {element}");
    }

    [Fact]
    public void FindRemoteResources_WhenAStyleBlockAppears_ReportsItAsItsOwnCategory()
    {
        // <style> cannot fetch by itself, and is banned anyway: it is @import's only vehicle, and this
        // codebase's email layout may not depend on a style block at all. It is reported as its own
        // category so a future legitimate <style> question is not read as "the Art. 30 entry is
        // false" — a false alarm that pressures the reader to weaken the detector is the failure mode
        // the detector's own doc warns about.
        //
        // @import deliberately has no arm of its own; this probe is what covers it.
        FindRemoteResources("""<style>@import url("https://fonts.example/f.css");</style>""")
            .ShouldContain("forbidden element (cannot fetch by itself): <style");
    }

    // ---------- the attribute and CSS arms, isolated with ON-HOST URLs ----------

    public static TheoryData<string> FetchingAttributes()
    {
        var data = new TheoryData<string>();
        foreach (var attribute in RemoteResourceDetector.FetchingAttributes)
            data.Add(attribute);
        return data;
    }

    [Theory]
    [MemberData(nameof(FetchingAttributes))]
    public void FindRemoteResources_ForEveryFetchingAttribute_ReportsThatAttribute(string attribute)
    {
        // On a <td>, which is in no element list, and with an ON-HOST URL, so neither the element arm
        // nor the off-host URL arm can fire. The finding string is asserted rather than mere
        // non-emptiness: without both, this probe would measure the URL arm and report success about
        // an attribute arm it never exercised.
        var findings = FindRemoteResources($"""<td {attribute}"{OnHost}">x</td>""");

        findings.ShouldContain($"fetching attribute: {attribute}");
    }

    [Fact]
    public void FindRemoteResources_ForFetchingCss_ReportsIt()
    {
        // Same isolation: on-host URL, non-listed element, finding string asserted.
        FindRemoteResources($"""<td style="background-image:url({OnHost})">x</td>""")
            .ShouldContain("fetching CSS: url(");
    }

    // ---------- the off-host URL arm ----------

    [Fact]
    public void FindRemoteResources_WhenAnAnchorPointsOffHost_ReportsIt()
    {
        // A bare off-host link fetches nothing on its own, and is still a finding: it is how a click
        // tracker or a redirector gets in, and "no absolute URL whose host lies outside BaseUrl" is
        // the form security-auditor's condition 1 names. The protocol-relative form must be caught
        // too, since it is the one that looks like a path. <a> is in no element list, so the URL arm
        // is the only one that can fire here.
        FindRemoteResources("""<a href="https://sponsor.example/x">x</a>""")
            .ShouldContain(f => f.StartsWith("absolute URL outside", StringComparison.Ordinal));
        FindRemoteResources("""<a href="//sponsor.example/x">x</a>""")
            .ShouldContain(f => f.StartsWith("absolute URL outside", StringComparison.Ordinal));
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

    // ---------- the tag boundary: the case an isolated probe cannot reach ----------

    [Theory]
    [InlineData("""<td title="a>b" background="https://evil.example/bg.png">x</td>""")]
    [InlineData("""<div title="a>b" style="background-image:url(https://evil.example/b.png)">x</div>""")]
    [InlineData("""<a title="a>b" href="https://evil.example/x">x</a>""")]
    [InlineData("""<td data-x='a>b' background="https://evil.example/bg.png">x</td>""")]
    public void FindRemoteResources_WhenAnEarlierAttributeContainsAngleBracket_StillReportsIt(
        string fragment)
    {
        // THE case the first version missed. A `>` inside a quoted attribute value used to end the
        // tag span, so everything after it fell out of "live markup" and the fetch went unreported
        // while every isolated counterfactual stayed green. Nothing here can be produced by our own
        // templates (Encode turns `>` into `&gt;`), which is exactly why it needed an explicit probe
        // rather than trust: the pin must hold against any document, not only against ours.
        FindRemoteResources(fragment).ShouldNotBeEmpty();
    }

    [Fact]
    public void FindRemoteResources_WithOddQuoteParity_IsKnownNotToReport()
    {
        // A DECLARED limit, pinned so it cannot be rediscovered as a surprise (security-auditor
        // Minor, 2026-08-12). The HTML5 tokenizer tolerates a stray quote inside an UNQUOTED
        // attribute value and keeps parsing attributes; this detector pairs quotes positionally, so a
        // tag with odd quote parity never reaches a `>` outside a quoted run and falls out of live
        // markup entirely. A browser fetches; the attribute and CSS arms do not see it.
        //
        // ALL THREE live-markup arms go blind on such a tag — attribute, CSS, and the off-host URL arm,
        // which reads the same liveMarkup string. So an off-host URL can hide there too, which is
        // register ground 2's SECOND sentence, and the fixture below is itself an example of one.
        // The first version of this comment named only two arms and bounded the risk with "the
        // tracking pixel is covered", which is too broad: a pixel is delivered just as well by
        // background= or background-image:url(...), both in the blinded set (code-reviewer).
        //
        // What IS covered, stated at exactly its width and asserted below rather than claimed: the
        // ELEMENT-BORNE fetch, because that arm scans the whole document.
        // And nothing in this repo can produce the shape at all: Encode turns " into &quot;.
        // It is declared rather than chased because a regex never becomes a tokenizer, and trading one
        // undeclared residual for another is the round-multiplying move.
        FindRemoteResources("""<td x=a" background="https://evil.example/bg.png">x</td>""")
            .ShouldBeEmpty();

        // The bound, measured rather than asserted in prose: the same evasion against a fetching
        // ELEMENT is still caught, because that arm does not depend on tag spans at all.
        FindRemoteResources("""<img x=a" src="https://evil.example/p.gif">""")
            .ShouldContain("fetching element: <img");
    }

    [Fact]
    public void FindRemoteResources_ForTheOnHostFetchesThatEscapedTheFirstVersion_ReportsThem()
    {
        // The three constructs security-auditor measured passing on 2026-08-12, all ON-HOST, all
        // issuing a request with no user action. They are why the register's ground 2 is now written
        // as two sentences: "no construction fetches without a user action, REGARDLESS OF HOST" and
        // "no absolute URL names a host outside BaseUrl". A host-based wording could never close
        // these, because the property they violate is not host-dependent — and our own <a href>
        // links survive the first sentence precisely because a link is not a fetch.
        FindRemoteResources($"""<svg><image href="{OnHost}"/></svg>""")
            .ShouldContain("fetching element: <svg");
        FindRemoteResources($"""<svg><use href="{OnHost}#i"/></svg>""")
            .ShouldContain("fetching element: <svg");
        FindRemoteResources($"""<meta http-equiv="refresh" content="0;url={OnHost}">""")
            .ShouldContain("fetching attribute: http-equiv=");
    }

    [Fact]
    public void FindRemoteResources_WhenAnAttributeIsSpacedFromItsValue_IsKnownNotToReport()
    {
        // DECLARED limit 2, pinned in the same form as limit 1 (code-reviewer, 2026-08-12: the
        // detector's doc said "both are pinned" while only the first one was — a clause wider than
        // its measurement, in the paragraph written to close exactly that).
        //
        // The attribute arm is NAME-based where the element arm is documented as SHAPE-based: it
        // matches the literal `http-equiv=`, and HTML5 permits spaces around the equals sign. All
        // five attribute literals carry the `=`, so all five have this shape.
        //
        // Bounded the same way limit 1 is: nothing in this repo can emit it (our own shell writes
        // `charset` and `name`, never http-equiv, and no template builds attributes from data), and
        // the element arm is unaffected — asserted below rather than claimed.
        FindRemoteResources($"""<meta http-equiv = "refresh" content="0;url={OnHost}">""")
            .ShouldBeEmpty();

        // The bound, measured: the same spacing against a fetching ELEMENT is still caught.
        FindRemoteResources($"""<img src = "{OnHost}">""").ShouldContain("fetching element: <img");
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("file:///etc/passwd")]
    [InlineData("/relativ-lank")]
    public void FindRemoteResources_WhenAnHrefUsesAnUnlistedScheme_ReportsIt(string href)
    {
        // href cannot be a forbidden attribute — every mail carries anchors — so the SCHEME is the
        // checkable boundary. mailto: was the first non-https scheme to reach LinkParagraph, and
        // nothing in the repo would have caught these arriving the same way (security-auditor Minor,
        // 2026-08-12). The relative form is included deliberately: it is harmless in a browser and
        // meaningless in a mail, where there is no document base to resolve against.
        FindRemoteResources($"""<a href="{href}">x</a>""")
            .ShouldContain(f => f.StartsWith("href with a scheme", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("https://jobbliggaren.se/jobb")]
    [InlineData("mailto:kontakt@jobbliggaren.se")]
    public void FindRemoteResources_ForTheTwoAllowedSchemes_ReportsNothing(string href)
    {
        // The control for the arm above: it must not be one that rejects every href. Both live forms
        // are here, so the allow-list is proven to allow as well as to reject.
        FindRemoteResources($"""<a href="{href}">x</a>""").ShouldBeEmpty();
    }

    // ---------- controls: the detector must not reject everything ----------

    [Fact]
    public void FindRemoteResources_WhenEverythingIsOnHost_ReportsNothing()
    {
        // The control that keeps every probe above honest: without it, every arm would also "pass"
        // against a detector hard-wired to report a finding.
        FindRemoteResources("""<a href="https://jobbliggaren.se/jobb">Öppna annonserna</a>""")
            .ShouldBeEmpty();
    }

    [Fact]
    public void FindRemoteResources_WhenInertTextMentionsAnOffHostUrl_ReportsNothing()
    {
        // The reason attribute, CSS and URL arms are scoped to live markup. Encoded ad text literally
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
        FindRemoteResources("""<p>Acme <img src="https://evil.example/pixel.gif"> AB</p>""")
            .ShouldNotBeEmpty();
    }
}
