import { describe, it, expect } from "vitest";
import { createTranslator } from "next-intl";
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
import svResumes from "../../../messages/sv/resumes.json";

// Real next-intl translator scoped to the `enums` namespace from the Swedish
// catalog (the source of truth). In production it comes from
// `useTranslations("resumes.enums")`.
const t = createTranslator({
  locale: "sv",
  messages: { resumes: svResumes },
  namespace: "resumes.enums",
});

describe("review-labels", () => {
  it("mappar verdict → svensk etikett + ton (NotAssessed är neutral, aldrig fail)", () => {
    expect(verdictLabel(t, "Pass")).toEqual({ label: "Godkänt", tone: "success" });
    expect(verdictLabel(t, "Warn")).toEqual({ label: "Delvis", tone: "warning" });
    expect(verdictLabel(t, "Fail")).toEqual({ label: "Underkänt", tone: "danger" });
    expect(verdictLabel(t, "NotAssessed")).toEqual({
      label: "Ej bedömt",
      tone: "neutral",
    });
  });

  it("mappar band → svensk etikett + ton", () => {
    expect(bandLabel(t, "NotReady").label).toBe("Ej redo");
    expect(bandLabel(t, "NeedsRework").label).toBe("Behöver omarbetning");
    expect(bandLabel(t, "Competitive").label).toBe("Konkurrenskraftigt");
    expect(bandLabel(t, "TopTier")).toEqual({ label: "Toppskikt", tone: "success" });
  });

  it("mappar kategori → svensk etikett", () => {
    expect(categoryLabel(t, "Content")).toBe("Innehåll");
    expect(categoryLabel(t, "Structure")).toBe("Struktur");
    expect(categoryLabel(t, "Language")).toBe("Språk");
    expect(categoryLabel(t, "AtsParsability")).toBe("ATS-läsbarhet");
    expect(categoryLabel(t, "VisualQuality")).toBe("Visuell kvalitet");
  });

  it("mappar övergripande konfidens → etikett + ton", () => {
    expect(overallConfidenceLabel(t, "Confident").tone).toBe("success");
    expect(overallConfidenceLabel(t, "Degraded").tone).toBe("warning");
    expect(overallConfidenceLabel(t, "Failed").tone).toBe("danger");
  });

  it("mappar sektionsnivå → etikett + ton (NotFound = neutral, ärligt 'hittades inte')", () => {
    expect(sectionLevelLabel(t, "Confident")).toEqual({
      label: "Hittad",
      tone: "success",
    });
    expect(sectionLevelLabel(t, "Degraded").tone).toBe("warning");
    expect(sectionLevelLabel(t, "NotFound")).toEqual({
      label: "Hittades inte",
      tone: "neutral",
    });
  });

  it("mappar kända sektionsnamn till svenska och faller tillbaka till råvärdet", () => {
    expect(sectionKindLabel(t, "Contact")).toBe("Kontakt");
    expect(sectionKindLabel(t, "Experience")).toBe("Erfarenhet");
    expect(sectionKindLabel(t, "Languages")).toBe("Språk");
    // Okänt värde → råvärdet (robust mot framtida sektionstyper).
    expect(sectionKindLabel(t, "Certifications")).toBe("Certifications");
  });

  // --- F4-10 förbättra-etiketter -------------------------------------------

  it("mappar varje ProposedChangeKind → svensk etikett (alla låsta värden täckta)", () => {
    expect(proposedChangeKindLabel(t, "ClicheReplacement")).toBe("Ersätt klyscha");
    expect(proposedChangeKindLabel(t, "WeakVerbUpgrade")).toBe("Starkare verb");
    expect(proposedChangeKindLabel(t, "DateNormalization")).toBe("Normalisera datum");
    expect(proposedChangeKindLabel(t, "SectionReorder")).toBe("Ändra sektionsordning");
    expect(proposedChangeKindLabel(t, "HeadingNormalization")).toBe("Normalisera rubrik");
    expect(proposedChangeKindLabel(t, "PersonnummerStrip")).toBe("Ta bort personnummer");
    expect(proposedChangeKindLabel(t, "PhotoStrip")).toBe("Ta bort foto");
    expect(proposedChangeKindLabel(t, "GpaStrip")).toBe("Ta bort betyg");
    expect(proposedChangeKindLabel(t, "AtsSanitization")).toBe("ATS-sanering");
    // Parity-pin: ingen låst kind saknar en etikett (drift fail-loud i CI).
    for (const kind of proposedChangeKindSchema.options) {
      expect(proposedChangeKindLabel(t, kind)).toBeTruthy();
    }
  });

  it("mappar varje StructuralTransformKind → svensk etikett (alla låsta värden täckta)", () => {
    expect(structuralTransformLabel(t, "ReformatDate")).toBe("Normalisera datum");
    expect(structuralTransformLabel(t, "NormalizeHeadingCase")).toBe("Normalisera rubrik");
    expect(structuralTransformLabel(t, "RemovePersonnummer")).toBe("Ta bort personnummer");
    expect(structuralTransformLabel(t, "RemovePhotoReference")).toBe("Ta bort foto");
    expect(structuralTransformLabel(t, "RemoveGpa")).toBe("Ta bort betyg");
    expect(structuralTransformLabel(t, "StripNonStandardChars")).toBe("Ta bort icke-standardtecken");
    expect(structuralTransformLabel(t, "ReorderSection")).toBe("Ändra sektionsordning");
    for (const kind of structuralTransformKindSchema.options) {
      expect(structuralTransformLabel(t, kind)).toBeTruthy();
    }
  });

  it("härleder den neutrala pill-etiketten ur om förslaget har en textersättning", () => {
    // Texten bär typen (Omformulering/Struktur), aldrig enbart färg (WCAG 1.4.1).
    expect(changeKindPillLabel(t, true)).toBe("Omformulering");
    expect(changeKindPillLabel(t, false)).toBe("Struktur");
  });
});
