using System.Text.RegularExpressions;
using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.CompanyWatches;

/// <summary>
/// #311 PR-5 (ADR 0087 D4) — value object for the identity of a <b>curated</b> brand group: a
/// stable slug (e.g. <c>volvo-koncernen</c>) that a <see cref="CompanyWatch"/> targets when its
/// <see cref="CompanyWatchTargetType"/> is <see cref="CompanyWatchTargetType.BrandGroup"/>. The
/// slug is the follow key AND the key the catalogue (<c>IBrandGroupProvider</c>) resolves to a
/// display name + an explicit list of member org.nrs (ADR 0087 D4: manually curated, NO automatic
/// name-matching, ever — the "Volvo×20" trap is the whole reason org.nr is canonical).
///
/// <para>
/// <b>A slug, not a Guid (contrast <see cref="CompanyWatchId"/>).</b> BrandGroup has no table and
/// no surrogate identity — the catalogue is deploy-versioned reference data keyed by a
/// human-authored slug (parity <c>BranschgruppCatalog</c>'s stable-slug id: the FE keys its i18n on
/// this slug, never on a label). A Guid would add indirection over a well-known natural key with no
/// invariant to protect (Evans 2003 — natural identity where a stable natural key exists).
/// </para>
///
/// <para>
/// <b>Format (default-deny, Saltzer/Schroeder 1975):</b> lowercase ASCII alphanumerics in
/// hyphen-separated segments, <c>[a-z0-9]</c> not <c>\w</c> (the #865 house rule — <c>\w</c> admits
/// the whole Unicode letter category), <c>\z</c> not <c>$</c> against newline injection. Max 40
/// chars. This never overlaps the org.nr shape (a slug can never be exactly 10 digits AND satisfy
/// the value-object's role, because the catalogue owns the slug space and the handler 404s any
/// unknown id) — but the two VOs are distinct types so the type system keeps them from being
/// confused regardless.
/// </para>
/// </summary>
public sealed record BrandGroupId
{
    private static readonly Regex Pattern =
        new(@"^[a-z0-9]+(?:-[a-z0-9]+)*\z", RegexOptions.Compiled);

    /// <summary>Max stored slug length; drives the <c>brand_group_id varchar(40)</c> column.</summary>
    public const int MaxLength = 40;

    /// <summary>The stored slug. Never null on a validly-constructed instance.</summary>
    public string Value { get; }

    private BrandGroupId(string value) => Value = value;

    /// <summary>
    /// Validates and constructs. Returns a Validation error (never throws) for null/blank, an
    /// over-length slug, or one not matching the hyphenated-lowercase-alphanumeric shape — the
    /// expected-failure idiom (CLAUDE.md §3 Result idiom).
    /// </summary>
    public static Result<BrandGroupId> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result.Failure<BrandGroupId>(DomainError.Validation(
                "BrandGroupId.Required", "Varumärkesgrupp krävs."));

        if (value.Length > MaxLength)
            return Result.Failure<BrandGroupId>(DomainError.Validation(
                "BrandGroupId.TooLong", $"Varumärkesgrupp får vara högst {MaxLength} tecken."));

        if (!Pattern.IsMatch(value))
            return Result.Failure<BrandGroupId>(DomainError.Validation(
                "BrandGroupId.Invalid",
                "Varumärkesgrupp får bara innehålla gemener a–z, siffror och bindestreck."));

        return Result.Success(new BrandGroupId(value));
    }

    /// <summary>
    /// Reconstructs from an already-validated, persisted value (EF materialisation only) — parity
    /// with the strongly-typed Id <c>HasConversion</c> idiom and <see cref="OrganizationNumber.FromTrusted"/>.
    /// </summary>
    public static BrandGroupId FromTrusted(string value) => new(value);

    public override string ToString() => Value;
}
