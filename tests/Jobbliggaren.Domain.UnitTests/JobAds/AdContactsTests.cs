using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.Privacy;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.JobAds;

/// <summary>
/// The canonical merge/dedup/order VO (#842 Tier A, architect Q3/Q4). The property under test is
/// IDEMPOTENCE: the nightly sync re-ingests every listed ad, so From must be a pure function of
/// its value inputs — same contacts in, sequence-equal value out, regardless of input order or
/// which path (write vs jsonb read-back) produced it.
/// </summary>
public class AdContactsTests
{
    private static AdContact Declared(
        string? name = null, string? role = null, string? email = null, string? phone = null) =>
        AdContact.TryCreate(name, role, email, phone, AdContactOrigin.Declared)!;

    private static ContactSpan EmailSpan(string raw) =>
        new(raw, RecruiterContactRedactor.NormalizeEmail(raw)!, ContactKind.Email);

    private static ContactSpan PhoneSpan(string raw) =>
        new(raw, RecruiterContactRedactor.NormalizePhone(raw)!, ContactKind.Phone);

    [Fact]
    public void TryCreate_drops_an_entry_with_nothing_identifying()
    {
        AdContact.TryCreate("  ", "Rekryterare", " ", null, AdContactOrigin.Declared)
            .ShouldBeNull(); // a role alone identifies nobody

        AdContact.TryCreate(null, null, null, null, AdContactOrigin.Declared).ShouldBeNull();
    }

    [Fact]
    public void TryCreate_normalises_blank_to_null()
    {
        var contact = AdContact.TryCreate(" Anna Karlsson ", "", "anna@acme.se", "  ",
            AdContactOrigin.Declared)!;

        contact.Name.ShouldBe("Anna Karlsson");
        contact.Role.ShouldBeNull();
        contact.Phone.ShouldBeNull();
    }

    [Fact]
    public void A_declared_contact_covers_the_body_hit_of_its_own_email_case_insensitively()
    {
        var declared = Declared(name: "Anna Karlsson", email: "Anna@Acme.se", phone: "070-123 45 67");

        var contacts = AdContacts.From([declared], [EmailSpan("anna@acme.se")]);

        var only = contacts.Contacts.ShouldHaveSingleItem();
        only.Origin.ShouldBe(AdContactOrigin.Declared);
        only.Name.ShouldBe("Anna Karlsson");
    }

    [Fact]
    public void A_declared_contact_covers_the_body_hit_of_its_own_phone_across_formatting()
    {
        var declared = Declared(name: "Anna", phone: "+46 70 123 45 67");

        var contacts = AdContacts.From([declared], [PhoneSpan("070-123 45 67")]);

        contacts.Contacts.ShouldHaveSingleItem().Origin.ShouldBe(AdContactOrigin.Declared);
    }

    [Fact]
    public void An_uncovered_span_is_promoted_without_a_guessed_name()
    {
        var declared = Declared(name: "Anna", email: "anna@acme.se");

        var contacts = AdContacts.From([declared], [EmailSpan("jobb@acme.se")]);

        contacts.Contacts.Count.ShouldBe(2);
        var promoted = contacts.Contacts.Single(c => c.Origin == AdContactOrigin.ExtractedFromBody);
        promoted.Email.ShouldBe("jobb@acme.se");
        promoted.Name.ShouldBeNull(); // no NER, ever (ADR 0106 D5)
        promoted.Role.ShouldBeNull();
    }

    [Fact]
    public void Name_only_contacts_are_distinct_people_and_never_collapse()
    {
        var contacts = AdContacts.From(
            [Declared(name: "Anna Karlsson"), Declared(name: "Magnus Fagerberg")], []);

        contacts.Contacts.Count.ShouldBe(2);
    }

    [Fact]
    public void A_duplicate_declared_contact_that_adds_nothing_is_dropped()
    {
        var full = Declared(name: "Anna", email: "anna@acme.se", phone: "070-123 45 67");
        var subset = Declared(email: "ANNA@ACME.SE");

        var contacts = AdContacts.From([full, subset], []);

        contacts.Contacts.ShouldHaveSingleItem().Name.ShouldBe("Anna");
    }

    [Fact]
    public void From_is_order_insensitive_and_read_back_is_a_fixed_point()
    {
        var a = Declared(name: "Anna", email: "anna@acme.se");
        var b = Declared(name: "Bo", phone: "08-123 456 78");
        var span = EmailSpan("jobb@acme.se");

        var forward = AdContacts.From([a, b], [span]);
        var shuffled = AdContacts.From([b, a], [span]);
        var readBack = AdContacts.FromPersisted(forward.Contacts);

        shuffled.ShouldBe(forward);
        readBack.ShouldBe(forward); // the EF comparer sees no phantom change on reload
    }

    [Fact]
    public void Declared_contacts_survive_the_cap_before_promoted_ones()
    {
        var declared = Enumerable.Range(0, AdContacts.MaxContacts)
            .Select(i => Declared(name: $"Person {i:D2}", email: $"p{i:D2}@acme.se"))
            .ToList();
        var promoted = EmailSpan("overflow@acme.se");

        var contacts = AdContacts.From(declared, [promoted]);

        contacts.Contacts.Count.ShouldBe(AdContacts.MaxContacts);
        contacts.Contacts.ShouldAllBe(c => c.Origin == AdContactOrigin.Declared);
    }

    [Fact]
    public void Empty_inputs_yield_the_Empty_singleton()
        => AdContacts.From([], []).ShouldBeSameAs(AdContacts.Empty);

    [Fact]
    public void ToString_is_redacted_on_both_types()
    {
        var contact = Declared(name: "Anna Karlsson", email: "anna@acme.se", phone: "0701234567");
        var contacts = AdContacts.From([contact], []);

        contact.ToString().ShouldNotContain("Anna");
        contact.ToString().ShouldNotContain("acme.se");
        contact.ToString().ShouldNotContain("0701234567");
        contacts.ToString().ShouldNotContain("acme.se");
    }
}
