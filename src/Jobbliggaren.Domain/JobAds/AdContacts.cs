using Jobbliggaren.Domain.Privacy;

namespace Jobbliggaren.Domain.JobAds;

/// <summary>
/// The recruiter contacts of a single imported <see cref="JobAd"/> (#842 Tier A) — an immutable,
/// normalized value object owning the promote → merge → dedup → order step in ONE place (the
/// <see cref="ExtractedTerms"/> shape). <see cref="From"/> is the single normalization point: the
/// ingest funnel and the jsonb read path both go through it, so the persisted form is canonical
/// and a re-ingest of the same ad yields a sequence-equal value (idempotence is the core
/// requirement — the nightly sync rewrites every listed ad).
/// </summary>
/// <remarks>
/// <para>
/// <b>Null vs empty (the <c>ExtractedTerms</c> semantics, deliberately shared).</b> A null
/// <c>JobAd.Contacts</c> means "never populated" (pre-Tier-A rows before backfill) or "retention
/// cleared" (non-Active ads hold no contact — b1 §4). <see cref="Empty"/> means the funnel ran and
/// the source declared nothing and the detector found nothing. The retention fitness test
/// (<c>contacts IS NOT NULL</c> on non-Active rows == 0) rests on that distinction.
/// </para>
/// <para>
/// <b>Declared wins (re-bind R1(b)).</b> A detector hit whose normalized email/phone is already
/// covered by a declared contact is the SAME contact, not a second one — the advertiser's
/// declaration outranks our inference. Coverage is value-level, not tuple-level: a declared
/// contact carrying both an email and a phone covers a body hit of either.
/// </para>
/// <para>
/// <b><see cref="MaxContacts"/> is a DoS bound, not a coverage claim</b> (no-silent-cap
/// discipline): the wire array is unbounded and jsonb persists whatever it is given; a real ad
/// carries a handful. Declared contacts survive the cap before promoted ones (the sort
/// guarantees it).
/// </para>
/// </remarks>
public sealed class AdContacts : IEquatable<AdContacts>
{
    public const int MaxContacts = 16;

    public static AdContacts Empty { get; } = new([]);

    public IReadOnlyList<AdContact> Contacts { get; }

    public bool IsEmpty => Contacts.Count == 0;

    private AdContacts(IReadOnlyList<AdContact> contacts) => Contacts = contacts;

    /// <summary>
    /// Builds the canonical value object from declared contacts and detector spans:
    /// drops null/empty entries, promotes uncovered spans as
    /// <see cref="AdContactOrigin.ExtractedFromBody"/> (never guessing a name), deduplicates
    /// value-level with declared-wins, orders deterministically and caps at
    /// <see cref="MaxContacts"/>.
    /// </summary>
    public static AdContacts From(
        IEnumerable<AdContact?> declared, IEnumerable<ContactSpan> detectedSpans)
    {
        ArgumentNullException.ThrowIfNull(declared);
        ArgumentNullException.ThrowIfNull(detectedSpans);

        // Declared first: deterministic order, then value-level dedup among themselves.
        var declaredKept = new List<AdContact>();
        var seenEmails = new HashSet<string>(StringComparer.Ordinal);
        var seenPhones = new HashSet<string>(StringComparer.Ordinal);
        var seenNameOnly = new HashSet<string>(StringComparer.Ordinal);

        foreach (var contact in declared.OfType<AdContact>().OrderBy(SortKey, StringComparer.Ordinal))
        {
            var email = contact.NormalizedEmail;
            var phone = contact.NormalizedPhone;

            if (email is null && phone is null)
            {
                // Name-only contact: identity is the name (never collapse two different
                // name-only persons into one).
                if (!seenNameOnly.Add(contact.Name!.ToLowerInvariant()))
                    continue;
                declaredKept.Add(contact);
                continue;
            }

            // A duplicate is a contact ALL of whose reachable values are already covered —
            // dropping a contact that adds a new email or a new phone would lose data.
            var addsEmail = email is not null && !seenEmails.Contains(email);
            var addsPhone = phone is not null && !seenPhones.Contains(phone);
            if (!addsEmail && !addsPhone)
                continue;

            if (email is not null)
                seenEmails.Add(email);
            if (phone is not null)
                seenPhones.Add(phone);
            declaredKept.Add(contact);
        }

        // Promote uncovered detector hits (Origin=ExtractedFromBody, Name=null — no NER, D5).
        // Comparison uses ContactSpan.Normalized as computed BY THE RECOGNISER — never a second
        // normalizer (#844).
        var promoted = new List<AdContact>();
        foreach (var span in detectedSpans)
        {
            var covered = span.Kind == ContactKind.Email
                ? !seenEmails.Add(span.Normalized)
                : !seenPhones.Add(span.Normalized);
            if (covered)
                continue;

            var contact = span.Kind == ContactKind.Email
                ? AdContact.TryCreate(name: null, role: null, email: span.Raw, phone: null,
                    AdContactOrigin.ExtractedFromBody)
                : AdContact.TryCreate(name: null, role: null, email: null, phone: span.Raw,
                    AdContactOrigin.ExtractedFromBody);
            if (contact is not null)
                promoted.Add(contact);
        }

        if (declaredKept.Count == 0 && promoted.Count == 0)
            return Empty;

        // ONE canonical order for write path and read path alike: origin rank (declared survive
        // the cap first) then the value key. If the write path ordered groups separately and the
        // read path re-sorted globally, every reload would "reorder" the value and the EF
        // comparer would see a phantom change on ads whose contacts never moved.
        var ordered = declaredKept
            .Concat(promoted)
            .OrderBy(c => c.Origin == AdContactOrigin.Declared ? 0 : 1)
            .ThenBy(SortKey, StringComparer.Ordinal)
            .Take(MaxContacts)
            .ToList();

        return new AdContacts(ordered);
    }

    /// <summary>Reads a persisted list back into canonical form (the jsonb read path).</summary>
    public static AdContacts FromPersisted(IEnumerable<AdContact?> contacts)
        => From(contacts, []);

    private static string SortKey(AdContact c)
        => $"{c.NormalizedEmail}{c.NormalizedPhone}{c.Name?.ToLowerInvariant()}{c.Role?.ToLowerInvariant()}";

    public bool Equals(AdContacts? other)
        => other is not null && Contacts.SequenceEqual(other.Contacts);

    public override bool Equals(object? obj) => Equals(obj as AdContacts);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var contact in Contacts)
            hash.Add(contact);
        return hash.ToHashCode();
    }

    /// <summary>Redacted — the count is the only non-PII fact (see <see cref="AdContact"/>).</summary>
    public override string ToString() => $"AdContacts(Count={Contacts.Count}, redacted)";
}
