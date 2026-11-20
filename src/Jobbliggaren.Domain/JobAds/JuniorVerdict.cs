namespace Jobbliggaren.Domain.JobAds;

/// <summary>
/// #2264 — what the junior rules say about an ad in an enabled occupation field. <see cref="Unclear"/> is a
/// verdict of its own, not an absence: the ad carries a junior signal that no automatic rule decides, so it waits
/// for an admin's decision.
/// </summary>
public enum JuniorVerdict
{
    Junior = 1,
    NotJunior = 2,
    Unclear = 3,
}
