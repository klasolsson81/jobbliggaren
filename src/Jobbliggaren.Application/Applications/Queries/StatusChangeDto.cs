namespace Jobbliggaren.Application.Applications.Queries;

// ADR 0092 D4: one status transition on the detail timeline. From/To are the
// ApplicationStatus SmartEnum names (the FE resolves them to Swedish labels via
// next-intl, like Status). Detail-only — NOT on the list ApplicationDto (CQRS
// list != detail; the list already carries the LastStatusChangeAt scalar).
public sealed record StatusChangeDto(
    string From,
    string To,
    DateTimeOffset ChangedAt);
