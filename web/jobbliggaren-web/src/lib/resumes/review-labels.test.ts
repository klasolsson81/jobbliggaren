import { describe, it, expect } from "vitest";
import {
  verdictLabel,
  bandLabel,
  categoryLabel,
  overallConfidenceLabel,
  sectionLevelLabel,
  sectionKindLabel,
} from "./review-labels";

describe("review-labels", () => {
  it("mappar verdict → svensk etikett + ton (NotAssessed är neutral, aldrig fail)", () => {
    expect(verdictLabel("Pass")).toEqual({ label: "Godkänt", tone: "success" });
    expect(verdictLabel("Warn")).toEqual({ label: "Delvis", tone: "warning" });
    expect(verdictLabel("Fail")).toEqual({ label: "Underkänt", tone: "danger" });
    expect(verdictLabel("NotAssessed")).toEqual({
      label: "Ej bedömt v1",
      tone: "neutral",
    });
  });

  it("mappar band → svensk etikett + ton", () => {
    expect(bandLabel("NotReady").label).toBe("Ej redo");
    expect(bandLabel("NeedsRework").label).toBe("Behöver omarbetning");
    expect(bandLabel("Competitive").label).toBe("Konkurrenskraftigt");
    expect(bandLabel("TopTier")).toEqual({ label: "Toppskikt", tone: "success" });
  });

  it("mappar kategori → svensk etikett", () => {
    expect(categoryLabel("Content")).toBe("Innehåll");
    expect(categoryLabel("Structure")).toBe("Struktur");
    expect(categoryLabel("Language")).toBe("Språk");
    expect(categoryLabel("AtsParsability")).toBe("ATS-läsbarhet");
    expect(categoryLabel("VisualQuality")).toBe("Visuell kvalitet");
  });

  it("mappar övergripande konfidens → etikett + ton", () => {
    expect(overallConfidenceLabel("Confident").tone).toBe("success");
    expect(overallConfidenceLabel("Degraded").tone).toBe("warning");
    expect(overallConfidenceLabel("Failed").tone).toBe("danger");
  });

  it("mappar sektionsnivå → etikett + ton (NotFound = neutral, ärligt 'hittades inte')", () => {
    expect(sectionLevelLabel("Confident")).toEqual({
      label: "Hittad",
      tone: "success",
    });
    expect(sectionLevelLabel("Degraded").tone).toBe("warning");
    expect(sectionLevelLabel("NotFound")).toEqual({
      label: "Hittades inte",
      tone: "neutral",
    });
  });

  it("mappar kända sektionsnamn till svenska och faller tillbaka till råvärdet", () => {
    expect(sectionKindLabel("Contact")).toBe("Kontakt");
    expect(sectionKindLabel("Experience")).toBe("Erfarenhet");
    expect(sectionKindLabel("Languages")).toBe("Språk");
    // Okänt värde → råvärdet (robust mot framtida sektionstyper).
    expect(sectionKindLabel("Certifications")).toBe("Certifications");
  });
});
