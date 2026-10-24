using System.Xml;

namespace Jobbliggaren.Infrastructure.Common.IO;

/// <summary>
/// The one reading of untrusted XML (#272 SEC-1; OWASP XXE Prevention Cheat Sheet). DTDs are prohibited, so a DOCTYPE
/// throws and no entity can be declared or expanded; there is no resolver, so no external entity or DTD is ever
/// fetched. A caller reads only through <see cref="XmlReader.Create(Stream, XmlReaderSettings)"/> with these settings,
/// never through <c>XDocument.Load(Stream)</c> or <c>XmlDocument.Load</c>, which build their own settings.
/// </summary>
internal static class HardenedXmlReaderSettings
{
    /// <summary>
    /// Fresh settings on every call, so no caller can change another's. <paramref name="maxCharactersInDocument"/> is
    /// the caller's own document bound; <see langword="null"/> leaves it to a byte cap the caller wraps around the
    /// stream it reads.
    /// </summary>
    public static XmlReaderSettings Create(long? maxCharactersInDocument) => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = maxCharactersInDocument ?? 0,
        CloseInput = true,
        IgnoreProcessingInstructions = true,
        IgnoreComments = true,
    };
}
