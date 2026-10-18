namespace Jobbliggaren.Application.JobAds.Listing;

/// <summary>
/// The bounds of the listed-ad snapshot behind the job sitemap and the public catalogue (ADR 0174, ADR 0176). Constants, not options: a setting could be
/// made to disagree with the rule it serves (ADR 0159 D2), and the age bound is part of the legal record (ADR 0166
/// D10). <c>ListedJobAdSnapshotLimitsTests</c> pins how they relate.
/// </summary>
public static class ListedJobAdSnapshotLimits
{
    /// <summary>How often the API reads the listed set.</summary>
    public static readonly TimeSpan SampleInterval = TimeSpan.FromMinutes(5);

    /// <summary>The oldest snapshot the API answers from. An older one, or none, is a 503.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(15);

    /// <summary>How far ahead of the clock a snapshot may be stamped and still count, for a clock that stepped back.</summary>
    public static readonly TimeSpan FutureTolerance = TimeSpan.FromSeconds(5);

    /// <summary>How many sitemap parts the set is split into. The web's part routes follow it (an architecture test).</summary>
    public const int PartCount = 2;

    /// <summary>
    /// The most ids one read may return: about twice the peak listed count measured on 2026-10-11 (41 548, ADR 0166).
    /// At the ceiling each part holds about 40 000, below the 50 000 a sitemap file allows, so the API's refusal is the
    /// warning that comes first.
    /// </summary>
    public const int Ceiling = 80_000;
}
