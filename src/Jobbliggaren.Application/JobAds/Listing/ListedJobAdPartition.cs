namespace Jobbliggaren.Application.JobAds.Listing;

/// <summary>
/// Which sitemap part an ad belongs to (ADR 0174): part ⌊d·parts/16⌋ + 1, where d is the first hex digit of the id's
/// canonical lower-case form. Membership follows the id and never its position in a list, so two parts fetched hours
/// apart can neither skip nor repeat an ad.
/// </summary>
public static class ListedJobAdPartition
{
    /// <summary>A part count that divides the 16 hex digits evenly.</summary>
    public static bool IsSupported(int parts) => parts is 1 or 2 or 4 or 8 or 16;

    public static int PartOf(Guid id, int parts)
    {
        if (!IsSupported(parts))
            throw new ArgumentOutOfRangeException(nameof(parts), parts, "A part count must divide the 16 hex digits evenly.");

        // Big-endian is the canonical form's byte order, so the high nibble of the first byte is its first digit.
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes, bigEndian: true, out _);
        var firstDigit = bytes[0] >> 4;
        return firstDigit * parts / 16 + 1;
    }
}
