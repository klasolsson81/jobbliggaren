namespace JobbPilot.Application.JobAds.Queries.ListJobAds;

// Whitelistad sort-enum per CTO-beslut F2-P7 (CLAUDE.md §5.1 "Magic strings förbjudet",
// OCP via enum-extension). Direction-suffix bakad i value:t för att hålla query-record
// minimal (en enum istället för fält + bool).
public enum JobAdSortBy
{
    PublishedAtDesc = 0,
    PublishedAtAsc = 1,
    ExpiresAtDesc = 2,
    ExpiresAtAsc = 3,
}
