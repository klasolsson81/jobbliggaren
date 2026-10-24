namespace Jobbliggaren.Application.Admin.Email.Dmarc;

/// <summary>
/// #1981 PR 4a (ADR 0184) — reads one report part into a candidate. Infrastructure implements it with the base library
/// only: bounded decompression, a hardened XML reader, both the RFC 7489 and the RFC 9990 form. Syntax only; whether
/// the report is accepted is <see cref="DmarcAggregateReport.Accept"/>'s. Never throws for anything a part can contain.
/// </summary>
public interface IDmarcReportDecoder
{
    DmarcDecoding Decode(byte[] content, DmarcReportPartKind kind);
}

/// <summary>What decoding ends in: a closed set. A malformed part carries no detail, so nothing of it is logged.</summary>
public abstract record DmarcDecoding
{
    private DmarcDecoding() { }

    public sealed record Decoded(DmarcReportCandidate Candidate) : DmarcDecoding;

    public sealed record Malformed : DmarcDecoding;
}
