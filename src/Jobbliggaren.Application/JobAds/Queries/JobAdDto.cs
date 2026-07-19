namespace Jobbliggaren.Application.JobAds.Queries;

// #293/#306 (ADR 0042 Beslut E-amendment 2026-06-28) — den tidigare tidsbaserade
// `IsNew`-flaggan (PublishedAt >= Since) är BORTTAGEN. "Ny" = OLÄST avgörs nu på
// FE ur `CreatedAt` mot den per-användar oläst-watermarken (`LastSeenJobsAt`,
// `GET /me/jobs/watermark`) — DTO:n bär inget presentations-fält längre.
//
// #745 (epik #737, perf-finding `d1-list-dto-ships-full-description`) — den list-rad-
// formen bär MEDVETET INGEN `Description`. De tre list-ytorna (JobAdSearchComposition
// .ToDto() → ListJobAds/RunSavedSearch/per-användar-match-sort) renderar aldrig annons-
// texten (korten läser den inte; detaljmodalen/-sidan hämtar den separat via GetJobAd).
// Att projicera den untruncerade `Description` per rad (PageSize upp till 100) de-TOAST:ade
// en bred kolumn Postgres→API→BFF för en payload ingen läser. `Description` lever kvar på
// annons-aggregatet (`JobAd`) och på detalj-tråden (<see cref="JobAdDetailDto"/>, som
// deklarerar den självständigt) — typerna divergerar nu avsiktligt även på `Description`,
// inte bara `Contacts`. Pinnas av JobAdListDtoShapeTests (kontrafaktum: list saknar,
// detalj har).
public sealed record JobAdDto(
    Guid Id,
    string Title,
    string CompanyName,
    string Url,
    string Source,
    string Status,
    DateTimeOffset PublishedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt
);
