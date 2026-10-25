using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml;
using Jobbliggaren.Application.Admin.Email.Dmarc;
using Jobbliggaren.Infrastructure.Common.IO;

namespace Jobbliggaren.Infrastructure.Email.Dmarc.Decoding;

/// <summary>
/// #1981 PR 4a (ADR 0184) — reads one report part, base library only. Anyone can mail the report address, so the part
/// is hostile input: it is decompressed in memory under <see cref="DmarcIngestionLimits.MaxDecompressedXmlBytes"/>,
/// never written to disk, and read by the hardened <see cref="XmlReader"/> alone. The RFC 7489 form (with or without
/// its namespace) and the RFC 9990 form are read by local name at fixed paths; an element in any other namespace, and
/// every element outside those paths, is skipped unread.
/// </summary>
internal sealed class XmlDmarcReportDecoder : IDmarcReportDecoder
{
    private static readonly DmarcDecoding Malformed = new DmarcDecoding.Malformed();

    public DmarcDecoding Decode(byte[] content, DmarcReportPartKind kind)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "A report part kind this release does not know.");

        if (content.Length > DmarcIngestionLimits.MaxReportPartBytes)
            return Malformed;

        try
        {
            var xml = kind switch
            {
                DmarcReportPartKind.Gzip => Gunzip(content),
                DmarcReportPartKind.Zip => Unzip(content),
                _ => new ArraySegment<byte>(content),
            };
            if (xml.Array is not { } buffer)
                return Malformed;

            var candidate = ReportReader.Read(buffer, xml.Offset, xml.Count, SHA256.HashData(xml.AsSpan()));
            return candidate is null ? Malformed : new DmarcDecoding.Decoded(candidate);
        }
        catch (InvalidDataException)
        {
            // The compression or the archive is broken, or a byte cap was crossed.
            return Malformed;
        }
        catch (XmlException)
        {
            // Not well-formed XML, a DTD, or an encoding the reader refuses.
            return Malformed;
        }
    }

    // Concatenated members decode as one stream under one cap.
    private static ArraySegment<byte> Gunzip(byte[] content)
    {
        using var gzip = new GZipStream(new MemoryStream(content, writable: false), CompressionMode.Decompress);
        return ReadCapped(gzip);
    }

    // The declared Length is the archive's own claim, checked before inflating; the capped read bounds what is actually
    // inflated. An entry without an .xml name, a nested archive among them, is refused unread.
    private static ArraySegment<byte> Unzip(byte[] content)
    {
        using var archive = new ZipArchive(new MemoryStream(content, writable: false), ZipArchiveMode.Read);
        if (archive.Entries is not [var entry]
            || !entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
            || entry.IsEncrypted
            || entry.Length is < 0 or > DmarcIngestionLimits.MaxDecompressedXmlBytes)
            return default;

        using var stream = entry.Open();
        return ReadCapped(stream);
    }

    private static ArraySegment<byte> ReadCapped(Stream decompressed)
    {
        const long cap = DmarcIngestionLimits.MaxDecompressedXmlBytes;
        using var capped = new ByteCappedReadStream(decompressed, new ByteBudget(cap), cap);
        var xml = new MemoryStream();
        capped.CopyTo(xml);
        return new ArraySegment<byte>(xml.GetBuffer(), 0, (int)xml.Length);
    }

    /// <summary>
    /// One pass over the document. Every kept field is a required singular element; one that is missing or repeated
    /// makes the document malformed. Records stop at one past the cap, which <see cref="DmarcAggregateReport.Accept"/>
    /// refuses, so the rest of an oversized report is never read.
    /// </summary>
    private sealed class ReportReader
    {
        private const string Rfc7489Namespace = "http://dmarc.org/dmarc-xml/0.1";
        private const string Rfc9990Namespace = "urn:ietf:params:xml:ns:dmarc-2.0";

        private static readonly char[] XmlWhitespace = [' ', '\t', '\r', '\n'];

        private readonly XmlReader _reader;
        private readonly List<DmarcRecordCandidate> _records = [];

        private bool _metadataSeen;
        private bool _policySeen;
        private bool _dateRangeSeen;
        private string? _reporter;
        private string? _reportId;
        private string? _policyDomain;
        private long? _begin;
        private long? _end;

        private ReportReader(XmlReader reader) => _reader = reader;

        private enum Walk
        {
            Continue,
            Stop,
            Malformed,
        }

        public static DmarcReportCandidate? Read(byte[] buffer, int offset, int count, byte[] digest)
        {
            using var reader = XmlReader.Create(
                new MemoryStream(buffer, offset, count, writable: false),
                HardenedXmlReaderSettings.Create(maxCharactersInDocument: null));

            if (reader.MoveToContent() != XmlNodeType.Element || !IsReportElement(reader, "feedback"))
                return null;

            return new ReportReader(reader).ReadFeedback(digest);
        }

        private DmarcReportCandidate? ReadFeedback(byte[] digest)
        {
            var walk = Children(name => name switch
            {
                "report_metadata" => Once(ref _metadataSeen, ReportMetadata),
                "policy_published" => Once(ref _policySeen, PolicyPublished),
                "record" => Record(),
                _ => Skip(),
            });

            if (walk == Walk.Malformed)
                return null;

            // A complete document is read to its end, so a second root or trailing content refuses it.
            if (walk == Walk.Continue)
            {
                while (_reader.Read())
                {
                }
            }

            if (_reporter is null || _reportId is null || _begin is null || _end is null || _policyDomain is null)
                return null;

            return new DmarcReportCandidate(
                _reporter, _reportId, _policyDomain, _begin.Value, _end.Value, _records, digest);
        }

        private Walk ReportMetadata() => Children(name => name switch
        {
            "org_name" => Text(ref _reporter),
            "report_id" => Text(ref _reportId),
            "date_range" => Once(ref _dateRangeSeen, DateRange),
            _ => Skip(),
        });

        private Walk DateRange() => Children(name => name switch
        {
            "begin" => Number(ref _begin),
            "end" => Number(ref _end),
            _ => Skip(),
        });

        private Walk PolicyPublished() => Children(name => name == "domain" ? Text(ref _policyDomain) : Skip());

        private Walk Record()
        {
            var record = new RecordFields();
            var walk = Children(name => name switch
            {
                "row" => Once(ref record.RowSeen, () => Row(record)),
                "identifiers" => Once(ref record.IdentifiersSeen, () => Identifiers(record)),
                _ => Skip(),
            });
            if (walk != Walk.Continue)
                return walk;

            if (record.SourceIp is null || record.Count is null || record.HeaderFrom is null
                || record.Dkim is null || record.Spf is null)
                return Walk.Malformed;

            _records.Add(new DmarcRecordCandidate(
                record.SourceIp, record.Count.Value, record.HeaderFrom, record.Dkim, record.Spf));
            return _records.Count > DmarcIngestionLimits.MaxRecordsPerReport ? Walk.Stop : Walk.Continue;
        }

        private Walk Row(RecordFields record) => Children(name => name switch
        {
            "source_ip" => Text(ref record.SourceIp),
            "count" => Number(ref record.Count),
            "policy_evaluated" => Once(ref record.PolicyEvaluatedSeen, () => PolicyEvaluated(record)),
            _ => Skip(),
        });

        private Walk PolicyEvaluated(RecordFields record) => Children(name => name switch
        {
            "dkim" => Text(ref record.Dkim),
            "spf" => Text(ref record.Spf),
            _ => Skip(),
        });

        private Walk Identifiers(RecordFields record) =>
            Children(name => name == "header_from" ? Text(ref record.HeaderFrom) : Skip());

        /// <summary>
        /// Visits each child element of the element the reader is on, by local name, and leaves the reader past that
        /// element. A child outside the report namespaces is skipped without a visit.
        /// </summary>
        private Walk Children(Func<string, Walk> visit)
        {
            if (_reader.IsEmptyElement)
            {
                _reader.Read();
                return Walk.Continue;
            }

            var depth = _reader.Depth;
            _reader.Read();
            while (true)
            {
                switch (_reader.NodeType)
                {
                    case XmlNodeType.EndElement when _reader.Depth == depth:
                        _reader.Read();
                        return Walk.Continue;
                    case XmlNodeType.Element:
                        var walk = IsReportNamespace(_reader.NamespaceURI) ? visit(_reader.LocalName) : Skip();
                        if (walk != Walk.Continue)
                            return walk;
                        break;
                    case XmlNodeType.None:
                        return Walk.Malformed;
                    default:
                        _reader.Read();
                        break;
                }
            }
        }

        private Walk Skip()
        {
            if (_reader.IsEmptyElement)
            {
                _reader.Read();
                return Walk.Continue;
            }

            var depth = _reader.Depth;
            while (_reader.Read())
            {
                if (_reader.Depth > DmarcIngestionLimits.MaxXmlElementDepth)
                    return Walk.Malformed;
                if (_reader.NodeType == XmlNodeType.EndElement && _reader.Depth == depth)
                {
                    _reader.Read();
                    return Walk.Continue;
                }
            }

            return Walk.Malformed;
        }

        private static Walk Once(ref bool seen, Func<Walk> read)
        {
            if (seen)
                return Walk.Malformed;
            seen = true;
            return read();
        }

        // A leaf with element content throws XmlException, which makes the document malformed.
        private Walk Text(ref string? slot)
        {
            if (slot is not null)
                return Walk.Malformed;
            slot = _reader.ReadElementContentAsString().Trim(XmlWhitespace);
            return Walk.Continue;
        }

        private Walk Number(ref long? slot)
        {
            if (slot is not null)
                return Walk.Malformed;

            var text = _reader.ReadElementContentAsString().Trim(XmlWhitespace);
            if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
                return Walk.Malformed;

            slot = value;
            return Walk.Continue;
        }

        private static bool IsReportElement(XmlReader reader, string localName) =>
            reader.LocalName == localName && IsReportNamespace(reader.NamespaceURI);

        private static bool IsReportNamespace(string namespaceUri) =>
            namespaceUri is "" or Rfc7489Namespace or Rfc9990Namespace;

        private sealed class RecordFields
        {
            public bool RowSeen;
            public bool IdentifiersSeen;
            public bool PolicyEvaluatedSeen;
            public string? SourceIp;
            public long? Count;
            public string? HeaderFrom;
            public string? Dkim;
            public string? Spf;
        }
    }
}
