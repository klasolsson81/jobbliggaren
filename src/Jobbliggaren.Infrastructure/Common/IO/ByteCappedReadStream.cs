namespace Jobbliggaren.Infrastructure.Common.IO;

/// <summary>
/// #272 SEC-1: a read-only wrapper that hard-caps the decompressed bytes read from the stream beneath it and charges
/// them to a <see cref="ByteBudget"/>. It throws once either is crossed, so a "lying" archive that declares a small size
/// is bounded during inflation, not after materialisation. The message never carries input content.
/// </summary>
internal sealed class ByteCappedReadStream(Stream inner, ByteBudget budget, long cap = long.MaxValue) : Stream
{
    private long _read;

    private int Account(int n)
    {
        _read += n;
        budget.Charge(n);
        if (_read > cap)
            throw new InvalidDataException("Decompressed input exceeded the allowed byte ceiling.");
        return n;
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Account(inner.Read(buffer, offset, count));

    public override int Read(Span<byte> buffer) =>
        Account(inner.Read(buffer));

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            inner.Dispose();
        base.Dispose(disposing);
    }
}
