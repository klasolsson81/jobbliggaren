namespace Jobbliggaren.Infrastructure.Common.IO;

/// <summary>
/// What every stream read from one untrusted input may inflate together (#1810). A read that fails is still charged.
/// </summary>
internal sealed class ByteBudget(long bytes)
{
    private long _remaining = bytes;

    public void Charge(int n)
    {
        _remaining -= n;
        if (_remaining < 0)
            throw new InvalidDataException("Decompressed input exceeded its byte budget.");
    }
}
