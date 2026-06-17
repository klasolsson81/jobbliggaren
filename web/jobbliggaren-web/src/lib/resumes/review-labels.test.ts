import { describe, it, expect } from "vitest";
import {
  verdictLabel,
  bandLabel,
  categoryLabel,
  overallConfidenceLabel,
  sectionLevelLabel,
  sectionKindLabel,
  proposedChangeKindLabel,
  structuralTransformLabel,
  changeKindPillLabel,
} from "./review-labels";
import {
  proposedChangeKindSchema,
  structuralTransformKindSchema,
} from "@/lib/dto/parsed-resume";

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

  // --- F4-10 förbättra-etiketter -------------------------------------------

  it("mappar varje ProposedChangeKind → svensk etikett (alla låsta värden täckta)", () => {
    expect(proposedChangeKindLabel("ClicheReplacement")).toBe("Ersätt klyscha");
    expect(proposedChangeKindLabel("WeakVerbUpgrade")).toBe("Starkare verb");
    expect(proposedChangeKindLabel("DateNormalization")).toBe("Normalisera datum");
    expect(proposedChangeKindLabel("SectionReorder")).toBe("Ändra sektionsordning");
    expect(proposedChangeKindLabel("HeadingNormalization")).toBe("Normalisera rubrik");
    expect(proposedChangeKindLabel("PersonnummerStrip")).toBe("Ta bort personnummer");
    expect(proposedChangeKindLabel("PhotoStrip")).toBe("Ta bort foto");
    expect(proposedChangeKindLabel("GpaStrip")).toBe("Ta bort betyg");
    expect(proposedChangeKindLabel("AtsSanitization")).toBe("ATS-sanering");
    // Parity-pin: ingen låst kind saknar en etikett (drift fail-loud i CI).
    for (const kind of proposedChangeKindSchema.options) {
      expect(proposedChangeKindLabel(kind)).toBeTruthy();
    }
  });

  it("mappar varje StructuralTransformKind → svensk etikett (alla låsta värden täckta)", () => {
    expect(structuralTransformLabel("ReformatDate")).toBe("Normalisera datum");
    expect(structuralTransformLabel("NormalizeHeadingCase")).toBe("Normalisera rubrik");
    expect(structuralTransformLabel("RemovePersonnummer")).toBe("Ta bort personnummer");
    expect(structuralTransformLabel("RemovePhotoReference")).toBe("Ta bort foto");
    expect(structuralTransformLabel("RemoveGpa")).toBe("Ta bort betyg");
    expect(structuralTransformLabel("StripNonStandardChars")).toBe("Ta bort icke-standardtecken");
    expect(structuralTransformLabel("ReorderSection")).toBe("Ändra sektionsordning");
    for (const kind of structuralTransformKindSchema.options) {
      expect(structuralTransformLabel(kind)).toBeTruthy();
    }
  });

  it("härleder den neutrala pill-etiketten ur om förslaget har en textersättning", () => {
    // Texten bär typen (Omformulering/Struktur), aldrig enbart färg (WCAG 1.4.1).
    expect(changeKindPillLabel(true)).toBe("Omformulering");
    expect(changeKindPillLabel(false)).toBe("Struktur");
  });
});
