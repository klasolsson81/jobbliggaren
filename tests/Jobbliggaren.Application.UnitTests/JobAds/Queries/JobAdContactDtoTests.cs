using Jobbliggaren.Application.JobAds.Queries;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.Privacy;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Queries;

/// <summary>
/// #842 PR4 — <see cref="JobAdContactDto"/> is the ONE sanctioned type through which a recruiter
/// contact crosses the Application boundary (§2.3) and the ONE fail-closed mapper BOTH detail
/// readers share (the live ad's <see cref="JobAdDetailDto"/> and the frozen apply-time copy's
/// <c>AdSnapshotDto</c>). The load-bearing property under test is R1(b):
/// <see cref="JobAdContactDto.IsDerived"/> is <c>Origin != Declared</c> (fail-closed), so a regex
/// hit is never presented as the advertiser's declaration.
/// <para>
/// The <c>ToString()</c> redaction and the "no domain contact type reaches a Mediator response"
/// reachability lock are pinned in <c>RecruiterContactFtsLockTests</c> (architecture) and are
/// deliberately NOT duplicated here.
/// </para>
/// </summary>
public class JobAdContactDtoTests
{
    private static AdContact Declared(
        string? name = null, string? role = null, string? email = null, string? phone = null) =>
        AdContact.TryCreate(name, role, email, phone, AdContactOrigin.Declared)!;

    // A detector span produced by the recogniser's OWN normaliser (the AdContactsTests idiom) —
    // one normaliser, one rule (#844).
    private static ContactSpan EmailSpan(string raw) =>
        new(raw, RecruiterContactRedactor.NormalizeEmail(raw)!, ContactKind.Email);

    // ── FromDomain: the fail-closed IsDerived truth claim (R1(b)) ────────────────────────────────

    [Fact]
    public void FromDomain_WhenContactIsDeclared_IsNotDerivedAndCopiesAllFieldsVerbatim()
    {
        var contact = Declared(
            name: "Anna Karlsson", role: "Rekryterare", email: "anna@acme.se", phone: "070-123 45 67");

        var dto = JobAdContactDto.FromDomain(contact);

        dto.IsDerived.ShouldBeFalse("a declared contact is the advertiser's own declaration.");
        dto.Name.ShouldBe("Anna Karlsson");
        dto.Role.ShouldBe("Rekryterare");
        dto.Email.ShouldBe("anna@acme.se");
        dto.Phone.ShouldBe("070-123 45 67");
    }

    [Fact]
    public void FromDomain_WhenContactIsExtractedFromBody_IsDerivedAndNameStaysNull()
    {
        // The domain NEVER guesses a name for a promoted body hit (no NER, ADR 0106 D5) — construct
        // it the way AdContacts.From does: name null, Origin = ExtractedFromBody.
        var contact = AdContact.TryCreate(
            name: null, role: null, email: "jobb@acme.se", phone: null,
            AdContactOrigin.ExtractedFromBody)!;

        var dto = JobAdContactDto.FromDomain(contact);

        dto.IsDerived.ShouldBeTrue("a body-extracted hit is OUR inference, not her declaration.");
        dto.Name.ShouldBeNull();
        dto.Email.ShouldBe("jobb@acme.se");
    }

    [Fact]
    public void FromDomain_ForEveryNonDeclaredOrigin_IsDerivedIsTrue_FailClosed()
    {
        // FAIL-CLOSED CONTRACT (R1(b); stated in the XML doc on JobAdContactDto). IsDerived is
        // `Origin != Declared`, deliberately NOT `Origin == ExtractedFromBody`. Only the advertiser's
        // OWN declaration may render as her declaration; every other provenance — INCLUDING any
        // origin value added to the enum in the future — must render as our inference (presenting a
        // regex hit as her declaration is the §5 untruth class). Iterating the enum pins that: the
        // day a third origin appears this assertion already covers it, and a `== ExtractedFromBody`
        // mapper would map the new value to IsDerived=false and go RED here. With two values today it
        // also proves BOTH existing cases (Klas-direktiv 2026-07-17: assert on both, comment intent).
        foreach (var origin in Enum.GetValues<AdContactOrigin>())
        {
            var contact = AdContact.TryCreate(
                name: "Kim", role: null, email: "kim@example.com", phone: null, origin)!;

            JobAdContactDto.FromDomain(contact).IsDerived.ShouldBe(
                origin != AdContactOrigin.Declared,
                $"origin {origin}: only Declared is first-party; all else is derived (fail-closed).");
        }
    }

    // ── ListFrom: null and empty BOTH collapse to [], canonical order preserved ───────────────────

    [Fact]
    public void ListFrom_WhenContactsIsNull_ReturnsEmpty()
        => JobAdContactDto.ListFrom(null).ShouldBeEmpty();

    [Fact]
    public void ListFrom_WhenContactsIsEmpty_ReturnsEmpty()
        => JobAdContactDto.ListFrom(AdContacts.Empty).ShouldBeEmpty();

    [Fact]
    public void ListFrom_WhenDeclaredAndPromotedMixed_ProjectsDeclaredFirstWithCorrectIsDerived()
    {
        // A declared contact (email anna@acme.se) plus an UNCOVERED body hit (jobb@acme.se): the VO
        // keeps both, declared first in its canonical order (Origin rank 0 before 1). ListFrom must
        // preserve that order and stamp IsDerived per entry.
        var declared = Declared(name: "Anna Karlsson", role: "Rekryterare", email: "anna@acme.se");
        var contacts = AdContacts.From([declared], [EmailSpan("jobb@acme.se")]);

        var dtos = JobAdContactDto.ListFrom(contacts);

        dtos.Count.ShouldBe(2);

        dtos[0].IsDerived.ShouldBeFalse("declared sorts first in the VO's canonical order.");
        dtos[0].Name.ShouldBe("Anna Karlsson");
        dtos[0].Email.ShouldBe("anna@acme.se");

        dtos[1].IsDerived.ShouldBeTrue("the promoted body hit is derived.");
        dtos[1].Name.ShouldBeNull("no NER — a promoted hit never carries a guessed name.");
        dtos[1].Email.ShouldBe("jobb@acme.se");
    }
}
