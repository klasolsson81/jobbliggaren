using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Email;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// ADR 0087 D5 (#311 PR-4) + bevakning F4a RF-13=13B (#803, BC-8) — locks the invariants of
/// <see cref="EmailTemplates.FollowedCompanyNotification"/> (Infrastructure-internal, reachable via
/// InternalsVisibleTo). The load-bearing invariants: the settings/unsubscribe link
/// (<c>{baseUrl}/installningar</c>, GDPR Art. 7(3)) is ALWAYS present; the body carries ONLY public
/// ad fields (title + company) and NEVER an org.nr, a grade label, a score, or a recipient address
/// (ADR 0087 D8 / CLAUDE.md §5); civic tone (no exclamation marks, no em-dash).
///
/// <para>
/// <b>F4a adds the filter disclosure.</b> A filtered watch means ads are MISSING from this email, and
/// silent narrowing was rejected on §5-grounds — so an active filter must disclose itself, one line
/// per active axis, positioned where it answers "why might something be missing" (after the list,
/// before the CTA). The disclosure is deliberately NAME-FREE: the summary has ANY-semantics ("at least
/// one contributing watch is filtered"), so any ort-bearing claim would be FALSE as soon as a second
/// watch filters on a different ort — and it would leak preference-PII to a third-party sender for no
/// user benefit. The tests below pin exactly that.
/// </para>
/// </summary>
public class EmailTemplatesFollowedCompanyNotificationTests
{
    private const string BaseUrl = "https://jobbliggaren.se";

    // The two disclosure lines (RF-13=13B). Asserted as SUBSTRINGS of the rendered body so a copy
    // reflow does not break the test, while the CLAIM each line makes stays pinned.
    private const string OnlyMatchedDisclosure = "Du får bara matchande annonser";
    private const string LocationDisclosure = "Du har ortsfilter";
    private const string DisclosureFooter = "Du ser och ändrar filtren under Företag:";
    private const string OpenAdsCta = "Öppna annonserna";

    private static FollowedCompanyAdItem Item(
        string title = "Backend-utvecklare", string company = "Acme AB") => new(title, company);

    private static FollowedCompanyNotificationEmail Content(
        int totalCount, DigestCadence cadence, params FollowedCompanyAdItem[] items) =>
        new(cadence, Items: items, TotalCount: totalCount);

    private static FollowedCompanyNotificationEmail ContentWithSummary(
        FollowedCompanyFilterSummary? summary,
        params FollowedCompanyAdItem[] items) =>
        new(DigestCadence.Weekly, Items: items, TotalCount: items.Length, FilterSummary: summary);

    // A CAPPED digest (TotalCount > Items.Count → the template renders "och N till.") carrying a
    // disclosure — the only combination in which andMore and the disclosure are adjacent.
    private static FollowedCompanyNotificationEmail CappedContentWithSummary(
        int totalCount, FollowedCompanyFilterSummary? summary, params FollowedCompanyAdItem[] items) =>
        new(DigestCadence.Weekly, Items: items, TotalCount: totalCount, FilterSummary: summary);

    // ── Pre-F4a invariants (unchanged) ───────────────────────────────────────────────────────────

    [Fact]
    public void FollowedCompanyNotification_ShouldAlwaysContainSettingsLink()
    {
        var email = EmailTemplates.FollowedCompanyNotification(
            BaseUrl, Content(1, DigestCadence.Weekly, Item()));

        email.PlainTextBody.ShouldContain($"{BaseUrl}/installningar");
    }

    [Fact]
    public void FollowedCompanyNotification_ShouldListEachItemsTitleAndCompany()
    {
        var email = EmailTemplates.FollowedCompanyNotification(
            BaseUrl,
            Content(2, DigestCadence.Daily, Item("Frontend", "Beta AB"), Item("DevOps", "Gamma AB")));

        email.PlainTextBody.ShouldContain("Frontend, Beta AB");
        email.PlainTextBody.ShouldContain("DevOps, Gamma AB");
    }

    [Fact]
    public void FollowedCompanyNotification_ShouldRenderAndMore_WhenCapped()
    {
        // TotalCount (5) exceeds the displayed items (2) → "och 3 till."
        var email = EmailTemplates.FollowedCompanyNotification(
            BaseUrl, Content(5, DigestCadence.Weekly, Item(), Item("Frontend", "Beta AB")));

        email.PlainTextBody.ShouldContain("och 3 till");
    }

    [Fact]
    public void FollowedCompanyNotification_ShouldNotRenderAndMore_WhenAllShown()
    {
        var email = EmailTemplates.FollowedCompanyNotification(
            BaseUrl, Content(1, DigestCadence.Weekly, Item()));

        email.PlainTextBody.ShouldNotContain("till.");
    }

    [Fact]
    public void FollowedCompanyNotification_ShouldUseSingularCountPhrase_ForOneAd()
    {
        var email = EmailTemplates.FollowedCompanyNotification(
            BaseUrl, Content(1, DigestCadence.Weekly, Item()));

        email.PlainTextBody.ShouldContain("en ny annons");
    }

    [Fact]
    public void FollowedCompanyNotification_ShouldNotContainExclamationOrEmDash()
    {
        // Civic tone (CLAUDE.md §10 + feedback_no_em_dash_in_ui_copy). Rendered WITH both disclosure
        // lines active, so the F4a copy is inside the assertion's reach — a new line that smuggled in
        // an em-dash or an exclamation mark would otherwise slip past this gate untested.
        var email = EmailTemplates.FollowedCompanyNotification(
            BaseUrl,
            ContentWithSummary(
                new FollowedCompanyFilterSummary(OnlyMatchedActive: true, LocationFilterActive: true),
                Item(), Item("Frontend", "Beta AB")));

        // Guard: if the disclosure stopped rendering, the tone assertions below would pass vacuously.
        email.PlainTextBody.ShouldContain(OnlyMatchedDisclosure);
        email.PlainTextBody.ShouldNotContain("!");
        email.PlainTextBody.ShouldNotContain("—"); // em-dash
        email.Subject.ShouldNotContain("!");
    }

    [Fact]
    public void FollowedCompanyNotification_ShouldNotContainRecipientOrOrgNumber()
    {
        // The content contract carries no recipient and no org.nr; the body must never surface either
        // (ADR 0087 D8 — the personnummer-shaped-org.nr guard; the follow email shows company NAME).
        // Rendered WITH the disclosure so the F4a copy + the /foretag link are covered too.
        var email = EmailTemplates.FollowedCompanyNotification(
            BaseUrl,
            ContentWithSummary(
                new FollowedCompanyFilterSummary(OnlyMatchedActive: true, LocationFilterActive: true),
                Item("Snickare", "Firma Karlsson")));

        email.PlainTextBody.ShouldNotContain("5592804784");
        email.PlainTextBody.ShouldNotContain("@");
    }

    // ── F4a / RF-13=13B — the filter disclosure, all four summary states ─────────────────────────

    [Fact]
    public void FollowedCompanyNotification_NullFilterSummary_RendersNoDisclosureAtAll()
    {
        // The common (unfiltered) path: nothing was narrowed, so there is nothing to disclose. A
        // disclosure here would be a false claim that ads are missing.
        var email = EmailTemplates.FollowedCompanyNotification(
            BaseUrl, ContentWithSummary(summary: null, Item()));

        email.PlainTextBody.ShouldNotContain(OnlyMatchedDisclosure);
        email.PlainTextBody.ShouldNotContain(LocationDisclosure);
        email.PlainTextBody.ShouldNotContain(DisclosureFooter);
        email.PlainTextBody.ShouldNotContain($"{BaseUrl}/foretag");
    }

    [Fact]
    public void FollowedCompanyNotification_SummaryWithBothFlagsFalse_RendersNoDisclosureAtAll()
    {
        // A PRESENT but inert summary is the same non-event as no summary — it must not produce an
        // empty "filters are active" section that discloses nothing.
        var email = EmailTemplates.FollowedCompanyNotification(
            BaseUrl,
            ContentWithSummary(
                new FollowedCompanyFilterSummary(OnlyMatchedActive: false, LocationFilterActive: false),
                Item()));

        email.PlainTextBody.ShouldNotContain(OnlyMatchedDisclosure);
        email.PlainTextBody.ShouldNotContain(LocationDisclosure);
        email.PlainTextBody.ShouldNotContain(DisclosureFooter);
    }

    [Fact]
    public void FollowedCompanyNotification_OnlyMatchedActive_RendersOnlyThatLine()
    {
        // One line per ACTIVE axis: disclosing an ort filter the user does not have would be a false
        // statement about why ads are missing.
        var email = EmailTemplates.FollowedCompanyNotification(
            BaseUrl,
            ContentWithSummary(
                new FollowedCompanyFilterSummary(OnlyMatchedActive: true, LocationFilterActive: false),
                Item()));

        email.PlainTextBody.ShouldContain(OnlyMatchedDisclosure);
        email.PlainTextBody.ShouldNotContain(LocationDisclosure);
        email.PlainTextBody.ShouldContain(DisclosureFooter);
    }

    [Fact]
    public void FollowedCompanyNotification_LocationFilterActive_RendersOnlyThatLine()
    {
        var email = EmailTemplates.FollowedCompanyNotification(
            BaseUrl,
            ContentWithSummary(
                new FollowedCompanyFilterSummary(OnlyMatchedActive: false, LocationFilterActive: true),
                Item()));

        email.PlainTextBody.ShouldContain(LocationDisclosure);
        email.PlainTextBody.ShouldNotContain(OnlyMatchedDisclosure);
        email.PlainTextBody.ShouldContain(DisclosureFooter);
    }

    [Fact]
    public void FollowedCompanyNotification_BothFiltersActive_RendersBothLines()
    {
        var email = EmailTemplates.FollowedCompanyNotification(
            BaseUrl,
            ContentWithSummary(
                new FollowedCompanyFilterSummary(OnlyMatchedActive: true, LocationFilterActive: true),
                Item()));

        email.PlainTextBody.ShouldContain(OnlyMatchedDisclosure);
        email.PlainTextBody.ShouldContain(LocationDisclosure);
    }

    [Fact]
    public void FollowedCompanyNotification_Disclosure_SitsAfterItemListAndBeforeOpenAdsLink()
    {
        // POSITION is part of the contract: the disclosure answers "why might something be missing
        // from THIS list", so it must follow the list and precede the CTA. Rendered before the list it
        // reads as a header nobody connects to the ads; rendered after the CTA (or down with the
        // settings paragraph, which answers the different question "why am I getting this at all") it
        // is read by nobody at the moment the question arises.
        var email = EmailTemplates.FollowedCompanyNotification(
            BaseUrl,
            ContentWithSummary(
                new FollowedCompanyFilterSummary(OnlyMatchedActive: true, LocationFilterActive: true),
                Item("Frontend", "Beta AB")));

        var body = email.PlainTextBody;
        var lastItemIndex = body.IndexOf("Frontend, Beta AB", StringComparison.Ordinal);
        var disclosureIndex = body.IndexOf(OnlyMatchedDisclosure, StringComparison.Ordinal);
        var locationIndex = body.IndexOf(LocationDisclosure, StringComparison.Ordinal);
        var ctaIndex = body.IndexOf(OpenAdsCta, StringComparison.Ordinal);

        lastItemIndex.ShouldBeGreaterThan(-1);
        ctaIndex.ShouldBeGreaterThan(-1);
        disclosureIndex.ShouldBeGreaterThan(lastItemIndex, "disclosuren ligger EFTER annonslistan");
        locationIndex.ShouldBeGreaterThan(disclosureIndex, "en rad per axel, i ordning");
        ctaIndex.ShouldBeGreaterThan(locationIndex, "disclosuren ligger FÖRE Öppna annonserna-CTA:n");
    }

    [Fact]
    public void FollowedCompanyNotification_CappedDigestWithDisclosure_KeepsParagraphsSeparated()
    {
        // The one combination no other test renders: a CAPPED digest (which emits "och N till.") TOGETHER
        // with a disclosure. The two are adjacent in the template, and the blank line between them comes
        // from a single leading AppendLine() in BuildFilterDisclosure — delete it and the disclosure glues
        // itself onto "och 3 till.", while every other test in this file still passes. This pins both the
        // separation (no run-on paragraph) and that nothing double-spaces into a gap (no "\n\n\n").
        var email = EmailTemplates.FollowedCompanyNotification(
            BaseUrl,
            CappedContentWithSummary(
                totalCount: 5,
                new FollowedCompanyFilterSummary(OnlyMatchedActive: true, LocationFilterActive: true),
                Item("Frontend", "Beta AB"),
                Item("DevOps", "Gamma AB")));

        var body = email.PlainTextBody.Replace("\r\n", "\n", StringComparison.Ordinal);

        body.ShouldNotContain("\n\n\n"); // aldrig en dubbel tom rad (slarvig styckesättning)

        var lastItem = body.IndexOf("DevOps, Gamma AB", StringComparison.Ordinal);
        var andMore = body.IndexOf("och 3 till", StringComparison.Ordinal);
        var disclosure = body.IndexOf(OnlyMatchedDisclosure, StringComparison.Ordinal);
        var cta = body.IndexOf(OpenAdsCta, StringComparison.Ordinal);

        andMore.ShouldBeGreaterThan(lastItem, "\"och N till\" hör till listan");
        disclosure.ShouldBeGreaterThan(andMore, "disclosuren kommer EFTER hela listan, inklusive andMore");
        cta.ShouldBeGreaterThan(disclosure, "disclosuren ligger FÖRE CTA:n");

        // The disclosure must start on its own paragraph, not be glued to the andMore line.
        body.ShouldContain("\n\n" + OnlyMatchedDisclosure);
    }

    [Fact]
    public void FollowedCompanyNotification_Disclosure_ContainsCompaniesLink()
    {
        // The disclosure must be ACTIONABLE: it says where the filters are changed (/foretag shows
        // WHICH watches are filtered). A disclosure with no route to the setting is a dead end.
        var email = EmailTemplates.FollowedCompanyNotification(
            BaseUrl,
            ContentWithSummary(
                new FollowedCompanyFilterSummary(OnlyMatchedActive: false, LocationFilterActive: true),
                Item()));

        email.PlainTextBody.ShouldContain($"{BaseUrl}/foretag");
    }

    [Fact]
    public void FollowedCompanyNotification_Disclosure_CarriesNoOrtNameAndNoGradeWord()
    {
        // Name-free by REQUIREMENT, not by simplification. The summary is ANY-semantic ("at least one
        // contributing watch is filtered"), so "this email only shows ads in Göteborg" would be a LIE
        // to a user who also follows a company filtered on Malmö — and it would ship preference-PII to
        // a third-party sender (Art. 5(1)(c)). The D1 seal likewise keeps grade words out of a
        // follow email, which is not scored at all.
        var email = EmailTemplates.FollowedCompanyNotification(
            BaseUrl,
            ContentWithSummary(
                new FollowedCompanyFilterSummary(OnlyMatchedActive: true, LocationFilterActive: true),
                Item("Snickare", "Firma Karlsson")));

        var body = email.PlainTextBody;
        foreach (var ort in new[] { "Göteborg", "Malmö", "Stockholm", "Skåne", "Västra Götaland" })
            body.ShouldNotContain(ort); // namn-fri copy: ett ortsnamn vore falskt under ANY-semantiken
        body.ShouldNotContain("Stark match"); // en follow-hit är inte gradad (D1-förseglingen)
        body.ShouldNotContain("poäng");
    }
}
