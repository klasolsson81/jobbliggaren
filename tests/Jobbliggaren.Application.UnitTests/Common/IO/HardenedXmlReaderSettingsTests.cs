using System.Text;
using System.Xml;
using Jobbliggaren.Infrastructure.Common.IO;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Common.IO;

// The one reading of untrusted XML. The CV extractor and the DMARC report decoder both take their settings from here,
// so a DOCTYPE is refused and no entity is expanded for either; each caller's own reader tests cover its use.
public sealed class HardenedXmlReaderSettingsTests
{
    [Fact]
    public void Create_ReturnsFreshSettings_OnEveryCall()
    {
        var first = HardenedXmlReaderSettings.Create(maxCharactersInDocument: null);
        var second = HardenedXmlReaderSettings.Create(maxCharactersInDocument: null);

        first.ShouldNotBeSameAs(second);
    }

    [Fact]
    public void Create_ProhibitsDtds_AndIgnoresCommentsAndProcessingInstructions()
    {
        var settings = HardenedXmlReaderSettings.Create(maxCharactersInDocument: null);

        settings.DtdProcessing.ShouldBe(DtdProcessing.Prohibit);
        settings.IgnoreComments.ShouldBeTrue();
        settings.IgnoreProcessingInstructions.ShouldBeTrue();
    }

    [Fact]
    public void Create_ADocumentWithAnInternalEntity_ThrowsBeforeExpandingIt()
    {
        const string xml = "<!DOCTYPE x [ <!ENTITY e \"EXPANDED\"> ]><x>&e;</x>";

        Should.Throw<XmlException>(() => ReadToEnd(xml, HardenedXmlReaderSettings.Create(maxCharactersInDocument: null)));
    }

    [Fact]
    public void Create_ADocumentNamingAnExternalDtd_ThrowsWithoutFetchingIt()
    {
        const string xml = "<!DOCTYPE x SYSTEM \"http://127.0.0.1:9/never.dtd\"><x/>";

        Should.Throw<XmlException>(() => ReadToEnd(xml, HardenedXmlReaderSettings.Create(maxCharactersInDocument: null)));
    }

    [Fact]
    public void Create_WithACharacterCeiling_RefusesALongerDocument()
    {
        var xml = "<x>" + new string('a', 200) + "</x>";

        Should.Throw<XmlException>(() => ReadToEnd(xml, HardenedXmlReaderSettings.Create(maxCharactersInDocument: 100)));
    }

    [Fact]
    public void Create_WithoutACharacterCeiling_LeavesTheBoundToTheCaller()
    {
        HardenedXmlReaderSettings.Create(maxCharactersInDocument: null).MaxCharactersInDocument.ShouldBe(0);
    }

    private static void ReadToEnd(string xml, XmlReaderSettings settings)
    {
        using var reader = XmlReader.Create(new MemoryStream(Encoding.UTF8.GetBytes(xml)), settings);
        while (reader.Read())
        {
        }
    }
}
