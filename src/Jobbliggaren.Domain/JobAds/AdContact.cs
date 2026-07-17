using Jobbliggaren.Domain.Privacy;

namespace Jobbliggaren.Domain.JobAds;

/// <summary>
/// One recruiter contact person on an imported ad (#842 Tier A, ADR 0106 / CTO re-bind R1(a)).
/// The bounded, un-indexed, retention-bounded, surgically erasable carrier that replaces the
/// unbounded free-text one — name, role, work email and work phone as the source published them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Blank is absence.</b> Every field normalises blank → null in construction (the
/// <c>JobAdFacets</c> empty-string lesson: a value that looks present and is functionally absent
/// walks straight past every <c>IS NOT NULL</c> reasoning). A contact carrying neither name, email
/// nor phone is not a contact — <see cref="TryCreate"/> yields null and the caller drops it
/// (a role alone identifies nobody).
/// </para>
/// <para>
/// <b><see cref="ToString"/> is REDACTED on purpose.</b> A record's compiler-generated
/// <c>ToString()</c> prints every member, so a plain <c>{Contact}</c> log placeholder — with no
/// <c>@</c> anywhere — would dump a recruiter's name, email and phone through MEL's default
/// formatting, slipping past both the destructuring guard and every token scan (the
/// <c>JobAdImportItem</c>/<c>JobAdFacets</c> lesson, found by security-auditor there). CLAUDE.md §5:
/// recruiter PII is never logged in plaintext.
/// </para>
/// </remarks>
public sealed record AdContact
{
    public string? Name { get; }
    public string? Role { get; }
    public string? Email { get; }
    public string? Phone { get; }
    public AdContactOrigin Origin { get; }

    private AdContact(string? name, string? role, string? email, string? phone, AdContactOrigin origin)
    {
        Name = name;
        Role = role;
        Email = email;
        Phone = phone;
        Origin = origin;
    }

    /// <summary>
    /// Builds a contact from wire/detector values, or null when nothing identifying survives
    /// normalisation. Wire junk is DROPPED, not thrown on — the ACL feeds whatever the source
    /// published, and an all-null <c>application_contacts</c> element is absence, not corruption.
    /// </summary>
    public static AdContact? TryCreate(
        string? name, string? role, string? email, string? phone, AdContactOrigin origin)
    {
        var normName = Normalize(name);
        var normRole = Normalize(role);
        var normEmail = Normalize(email);
        var normPhone = Normalize(phone);

        return normName is null && normEmail is null && normPhone is null
            ? null
            : new AdContact(normName, normRole, normEmail, normPhone, origin);
    }

    /// <summary>
    /// The canonical email comparison form — delegated to the recogniser's normalizer
    /// (<see cref="RecruiterContactRedactor.NormalizeEmail"/>). One normalizer, one rule (#844).
    /// </summary>
    public string? NormalizedEmail => RecruiterContactRedactor.NormalizeEmail(Email);

    /// <summary>Canonical phone comparison form — see <see cref="NormalizedEmail"/>.</summary>
    public string? NormalizedPhone => RecruiterContactRedactor.NormalizePhone(Phone);

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Redacted — see the type remarks. Only the non-PII discriminator survives.</summary>
    public override string ToString() => $"AdContact(Origin={Origin}, redacted)";
}
