namespace Jobbliggaren.Application.JobAds.Publication;

/// <summary>
/// A list's ads spread over their employers, as <see cref="PublicListFloor.SpreadOf"/> builds it for
/// <see cref="PublicListFloor.Holds"/>. It is built inside the query that evaluates the floor and never leaves it, because
/// its values are counts per employer.
/// </summary>
public sealed class EmployerSpread
{
    /// <summary>The list's ads.</summary>
    public int Ads { get; init; }

    /// <summary>The list's employers, counted as <see cref="PublicListFloor.SpreadOf"/> defines them.</summary>
    public int Employers { get; init; }

    /// <summary>The ads of the employer that has the most, counted as <see cref="PublicListFloor.SpreadOf"/> defines it.</summary>
    public int LargestEmployerAds { get; init; }
}
