namespace Jobbliggaren.Application.JobAds.Queries;

// #293/#306 (ADR 0042 Beslut E-amendment 2026-06-28) — den tidigare tidsbaserade
// `IsNew`-flaggan (PublishedAt >= Since) är BORTTAGEN. "Ny" = OLÄST avgörs nu på
// FE ur `CreatedAt` mot den per-användar oläst-watermarken (`LastSeenJobsAt`,
// `GET /me/jobs/watermark`) — DTO:n bär inget presentations-fält längre.
public sealed record JobAdDto(
    Guid Id,
    string Title,
    string CompanyName,
    string Description,
    string Url,
    string Source,
    string Status,
    DateTimeOffset PublishedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt
);
