import { describe, it, expect } from "vitest";
import {
  cvImprovementDtoSchema,
  proposedChangeKindSchema,
  structuralTransformKindSchema,
  changeProvenanceDtoSchema,
  parsedGapSummarySchema,
  pendingParsedResumeSummarySchema,
  type ParsedGapSummary,
  type ProposedChangeDto,
} from "./parsed-resume";

/**
 * Drift-fail-loud-kontraktet för CV-förbättra-DTO:n (F4-10). De LÅSTA mängderna
 * (ändringstyp/transform/proveniens-kind/profil) är strikta `z.enum` så att ett
 * okänt backend-värde ger `safeParse → success:false` (→ `kind: "error"` i BFF:n,
 * → civil degradering i UI:t) i stället för att tyst rendera fel. Fixturerna
 * speglar backend-kontraktet i `Jobbliggaren.Application.Resumes.Improvement.*`
 * verbatim (enum-namnen korsar wire som .NET-namn).
 */

// En ersättnings-baserad ändring: replacement satt, operation null, KnowledgeBank-
// proveniens. evidence är ETT objekt (inte en array — skillnad mot granska-DTO:n).
const replacementChange: ProposedChangeDto = {
  targetId: "exp-0-line-2",
  kind: "ClicheReplacement",
  category: "Language",
  criterionId: "A7",
  evidence: {
    kind: "TextSpan",
    start: 12,
    length: 18,
    quote: "teamplayer med driv",
    note: "klyscha utan konkret stöd",
    observation: null,
  },
  replacement: { before: "teamplayer med driv", after: "ledde ett team om fyra" },
  operation: null,
  rationale: "Konkretisera klyschan med en mätbar handling.",
  provenance: {
    kind: "KnowledgeBank",
    source: "cliche-bank",
    version: "1.2.0",
    key: "teamplayer",
    transform: null,
  },
};

// En operation-only ändring: operation satt, replacement null, StructuralTransform-
// proveniens. Strukturell evidens (observation, ingen citerbar textrad).
const operationChange: ProposedChangeDto = {
  targetId: "header-personnummer",
  kind: "PersonnummerStrip",
  category: "AtsParsability",
  criterionId: null,
  evidence: {
    kind: "Structural",
    start: null,
    length: null,
    quote: null,
    note: null,
    observation: "Ett personnummer hittades i sidhuvudet.",
  },
  replacement: null,
  operation: { kind: "RemovePersonnummer", target: "header" },
  rationale: "Personnummer behövs inte i ett CV och bör tas bort.",
  provenance: {
    kind: "StructuralTransform",
    source: null,
    version: null,
    key: null,
    transform: "RemovePersonnummer",
  },
};

const fullFixture = {
  clicheListVersion: "cliche-1.2.0",
  verbMappingVersion: "verb-1.1.0",
  rubricVersion: "1.0.0",
  profile: "Ats",
  changes: [replacementChange, operationChange],
};

// Paritet med ProposedChangeKind.cs (alla 9, exakt namn).
const ALL_CHANGE_KINDS = [
  "ClicheReplacement",
  "WeakVerbUpgrade",
  "DateNormalization",
  "SectionReorder",
  "HeadingNormalization",
  "PersonnummerStrip",
  "PhotoStrip",
  "GpaStrip",
  "AtsSanitization",
] as const;

// Paritet med StructuralTransformKind.cs (alla 7, exakt namn).
const ALL_TRANSFORM_KINDS = [
  "ReformatDate",
  "NormalizeHeadingCase",
  "RemovePersonnummer",
  "RemovePhotoReference",
  "RemoveGpa",
  "StripNonStandardChars",
  "ReorderSection",
] as const;

describe("cvImprovementDtoSchema", () => {
  it("parsar en full fixtur som täcker BÅDE en ersättnings-ändring och en operation-only ändring", () => {
    const result = cvImprovementDtoSchema.safeParse(fullFixture);
    expect(result.success).toBe(true);
    if (result.success) {
      expect(result.data.changes).toHaveLength(2);
    }
  });

  it("accepterar ersättnings-ändring med replacement satt och operation null", () => {
    const result = cvImprovementDtoSchema.safeParse({
      ...fullFixture,
      changes: [replacementChange],
    });
    expect(result.success).toBe(true);
    if (result.success) {
      const change = result.data.changes[0];
      expect(change?.replacement).toEqual({
        before: "teamplayer med driv",
        after: "ledde ett team om fyra",
      });
      expect(change?.operation).toBeNull();
    }
  });

  it("accepterar operation-only ändring med operation satt och replacement null", () => {
    const result = cvImprovementDtoSchema.safeParse({
      ...fullFixture,
      changes: [operationChange],
    });
    expect(result.success).toBe(true);
    if (result.success) {
      const change = result.data.changes[0];
      expect(change?.operation).toEqual({
        kind: "RemovePersonnummer",
        target: "header",
      });
      expect(change?.replacement).toBeNull();
    }
  });

  it("modellerar evidence som ETT objekt (inte en array, till skillnad från granska-DTO:n)", () => {
    // En array-evidens (granska-formen) ska INTE accepteras här.
    const withArrayEvidence = {
      ...fullFixture,
      changes: [{ ...replacementChange, evidence: [replacementChange.evidence] }],
    };
    expect(cvImprovementDtoSchema.safeParse(withArrayEvidence).success).toBe(
      false,
    );
  });

  it("accepterar tom changes-array (inga förslag för profilen)", () => {
    const result = cvImprovementDtoSchema.safeParse({
      ...fullFixture,
      changes: [],
    });
    expect(result.success).toBe(true);
  });

  it("avvisar okänt ProposedChangeKind-värde (drift fail-loud)", () => {
    const drifted = {
      ...fullFixture,
      changes: [{ ...replacementChange, kind: "TeleportSection" }],
    };
    expect(cvImprovementDtoSchema.safeParse(drifted).success).toBe(false);
  });

  it("avvisar okänt RubricCategory-värde (drift fail-loud)", () => {
    const drifted = {
      ...fullFixture,
      changes: [{ ...replacementChange, category: "Charisma" }],
    };
    expect(cvImprovementDtoSchema.safeParse(drifted).success).toBe(false);
  });

  it("avvisar okänt profil-värde (case-sensitive Ats|Visual)", () => {
    expect(
      cvImprovementDtoSchema.safeParse({ ...fullFixture, profile: "ats" }).success,
    ).toBe(false);
  });

  it("avvisar saknat versions-fält (clicheListVersion)", () => {
    const { clicheListVersion: _omit, ...rest } = fullFixture;
    void _omit;
    expect(cvImprovementDtoSchema.safeParse(rest).success).toBe(false);
  });

  it("avvisar okänt StructuralOperation.kind-värde (drift fail-loud)", () => {
    const drifted = {
      ...fullFixture,
      changes: [
        {
          ...operationChange,
          operation: { kind: "BeamUpSection", target: "header" },
        },
      ],
    };
    expect(cvImprovementDtoSchema.safeParse(drifted).success).toBe(false);
  });

  it("avvisar okänd provenance.kind (varken KnowledgeBank eller StructuralTransform)", () => {
    const drifted = {
      ...fullFixture,
      changes: [
        {
          ...replacementChange,
          provenance: { ...replacementChange.provenance, kind: "OuijaBoard" },
        },
      ],
    };
    expect(cvImprovementDtoSchema.safeParse(drifted).success).toBe(false);
  });
});

describe("proposedChangeKindSchema (LÅST mängd, paritet ProposedChangeKind.cs)", () => {
  it.each(ALL_CHANGE_KINDS)("accepterar känd ändringstyp %s", (kind) => {
    expect(proposedChangeKindSchema.safeParse(kind).success).toBe(true);
  });

  it("täcker exakt 9 värden (ingen drift mot backend-enumet)", () => {
    expect(proposedChangeKindSchema.options).toHaveLength(9);
  });

  it("avvisar okänt värde", () => {
    expect(proposedChangeKindSchema.safeParse("ClicheRemoval").success).toBe(
      false,
    );
  });
});

describe("structuralTransformKindSchema (LÅST mängd, paritet StructuralTransformKind.cs)", () => {
  it.each(ALL_TRANSFORM_KINDS)("accepterar känd transform %s", (kind) => {
    expect(structuralTransformKindSchema.safeParse(kind).success).toBe(true);
  });

  it("täcker exakt 7 värden (ingen drift mot backend-enumet)", () => {
    expect(structuralTransformKindSchema.options).toHaveLength(7);
  });

  it("avvisar okänt värde", () => {
    expect(structuralTransformKindSchema.safeParse("RemoveEverything").success).toBe(
      false,
    );
  });
});

// Fas 4b PR-8 (CTO-bind Q5): de nio icke-PII närvaro-boolarna + hubb-kortets
// summering. `gaps: null` = ärligt "inte beräknat" (pre-PR-8-import) → kortet
// renderar utan mätare i stället för att gissa (§5).
const ALL_NINE_GAPS: ParsedGapSummary = {
  hasFullName: true,
  hasEmail: false,
  hasPhone: true,
  hasLocation: false,
  hasProfile: true,
  hasExperience: true,
  hasEducation: false,
  hasSkills: true,
  hasLanguages: false,
};

describe("parsedGapSummarySchema (nio närvaro-boolar)", () => {
  it("accepterar exakt de nio boolarna", () => {
    expect(parsedGapSummarySchema.safeParse(ALL_NINE_GAPS).success).toBe(true);
  });

  it("avvisar när en av de nio boolarna saknas", () => {
    const { hasLanguages: _omit, ...missingOne } = ALL_NINE_GAPS;
    void _omit;
    expect(parsedGapSummarySchema.safeParse(missingOne).success).toBe(false);
  });

  it("avvisar en icke-boolean-flagga", () => {
    expect(
      parsedGapSummarySchema.safeParse({ ...ALL_NINE_GAPS, hasSkills: "ja" })
        .success,
    ).toBe(false);
  });
});

describe("pendingParsedResumeSummarySchema", () => {
  const base = {
    id: "11111111-1111-1111-1111-111111111111",
    sourceFileName: "cv.pdf",
    uploadedAt: "2026-07-10T08:00:00Z",
  };

  it("accepterar gaps: null (pre-PR-8-import, ärligt 'inte beräknat')", () => {
    expect(
      pendingParsedResumeSummarySchema.safeParse({ ...base, gaps: null }).success,
    ).toBe(true);
  });

  it("accepterar gaps med alla nio boolarna", () => {
    expect(
      pendingParsedResumeSummarySchema.safeParse({ ...base, gaps: ALL_NINE_GAPS })
        .success,
    ).toBe(true);
  });

  it("avvisar gaps som saknar en boolean", () => {
    const { hasEducation: _omit, ...missingOne } = ALL_NINE_GAPS;
    void _omit;
    expect(
      pendingParsedResumeSummarySchema.safeParse({ ...base, gaps: missingOne })
        .success,
    ).toBe(false);
  });
});

describe("changeProvenanceDtoSchema", () => {
  it("accepterar KnowledgeBank-proveniens (source/version/key satt, transform null)", () => {
    expect(
      changeProvenanceDtoSchema.safeParse({
        kind: "KnowledgeBank",
        source: "cliche-bank",
        version: "1.2.0",
        key: "teamplayer",
        transform: null,
      }).success,
    ).toBe(true);
  });

  it("accepterar StructuralTransform-proveniens (transform satt, övriga null)", () => {
    expect(
      changeProvenanceDtoSchema.safeParse({
        kind: "StructuralTransform",
        source: null,
        version: null,
        key: null,
        transform: "RemovePersonnummer",
      }).success,
    ).toBe(true);
  });

  it("avvisar StructuralTransform-proveniens med okänt transform-värde", () => {
    expect(
      changeProvenanceDtoSchema.safeParse({
        kind: "StructuralTransform",
        source: null,
        version: null,
        key: null,
        transform: "Disintegrate",
      }).success,
    ).toBe(false);
  });
});
