using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.JobAds;

// Value Object för (Source, ExternalId)-paret som identifierar en JobAd
// importerad från extern källa. ADR 0032 §4 + CLAUDE.md §5.1 (primitive
// obsession förbjuden) + Evans 2003 (Value Objects).
public sealed record ExternalReference
{
    public JobSource Source { get; }
    public string ExternalId { get; }

    private ExternalReference(JobSource source, string externalId)
    {
        Source = source;
        ExternalId = externalId;
    }

    public static Result<ExternalReference> Create(JobSource source, string? externalId)
    {
        if (source == JobSource.Manual)
            return Result.Failure<ExternalReference>(
                DomainError.Validation(
                    "ExternalReference.ManualNotAllowed",
                    "ExternalReference kräver extern källa, inte Manual."));
        if (string.IsNullOrWhiteSpace(externalId))
            return Result.Failure<ExternalReference>(
                DomainError.Validation(
                    "ExternalReference.IdRequired",
                    "External ID är obligatoriskt."));
        if (externalId.Length > 100)
            return Result.Failure<ExternalReference>(
                DomainError.Validation(
                    "ExternalReference.IdTooLong",
                    "External ID får vara max 100 tecken."));

        return Result.Success(new ExternalReference(source, externalId.Trim()));
    }
}
