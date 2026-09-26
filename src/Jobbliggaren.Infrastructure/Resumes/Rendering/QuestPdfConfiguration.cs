using QuestPDF.Infrastructure;

namespace Jobbliggaren.Infrastructure.Resumes.Rendering;

internal static class QuestPdfConfiguration
{
    public static void Apply()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.UseSystemFonts = true;
        QuestPDF.Settings.ThrowOnMissingFontFamilies = false;
        QuestPDF.Settings.ThrowOnMissingTextGlyphs = false;
        QuestPDF.Settings.EnableDetailedLayoutErrors = false;
    }
}
