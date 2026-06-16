import { z } from "zod";

/**
 * Zod-scheman för CV-importflödet (Fas 4 STEG B, F4-8/F4-9). Anti-corruption-
 * lager mot backend-DTO:erna i `Jobbliggaren.Application.Resumes.*`:
 *
 *  - `ImportResumeResponse`        → {@link importResumeResponseSchema}
 *  - `ParsedResumeDetailDto`       → {@link parsedResumeDetailDtoSchema}
 *  - `CvReviewDto`                 → {@link cvReviewDtoSchema}
 *
 * Backend serialiserar camelCase (verifierat mot `@/lib/dto/resumes`). Enum-
 * fälten korsar wire som sina .NET-namn (`enum.ToString()` i mapper:na) — de
 * LÅSTA mängderna (verdict/band/kategori/profil/evidens-typ/konfidensnivå)
 * modelleras som strikta `z.enum` så att drift fail-loud:ar (DtoParseError →
 * `kind: "error"`) i stället för att tyst rendera fel. De öppna strängfälten
 * (status, fallback, sektion-namn, språk) hålls som `z.string()`.
 *
 * CV-PII (parse-innehåll, råtext) läses ALDRIG i klientbunten — endast i
 * server-only BFF (`@/lib/api/resumes`) bakom Bearer + ägar-scope. Personnummer
 * ytas flag-only (count + kinds, aldrig råvärde — ADR 0074 Invariant 1).
 */

/** Backend-svar efter `POST /api/v1/resumes/import`. Klienten behöver bara
 * `parsedResumeId` (för att navigera till granska-vyn) — övriga fält (confidence,
 * personnummer, yrkesförslag) hämtas på granska-vyn via `GET .../parsed/{id}`.
 * Okända nycklar ignoreras av zod (icke-strict), så schemat är avsiktligt smalt. */
export const importResumeResponseSchema = z.object({
  parsedResumeId: z.string(),
});
export type ImportResumeResponse = z.infer<typeof importResumeResponseSchema>;

/** Övergripande parse-konfidens (OQ5). `Failed` = ingen användbar text → manuell
 * inmatning; `Degraded` = text men ofullständig struktur; `Confident` = pålitlig. */
export const overallConfidenceLevelSchema = z.enum([
  "Confident",
  "Degraded",
  "Failed",
]);
export type OverallConfidenceLevel = z.infer<
  typeof overallConfidenceLevelSchema
>;

/** Per-sektions parse-konfidens. `NotFound` = ärligt "hittades inte" (aldrig
 * hopblandat med tom-men-närvarande sektion). */
export const sectionConfidenceLevelSchema = z.enum([
  "Confident",
  "Degraded",
  "NotFound",
]);
export type SectionConfidenceLevel = z.infer<
  typeof sectionConfidenceLevelSchema
>;

export const sectionConfidenceDtoSchema = z.object({
  /** Sektion-namn (`Contact`/`Profile`/`Experience`/`Education`/`Skills`/`Languages`). */
  section: z.string(),
  level: sectionConfidenceLevelSchema,
  /** Citerad, PII-fri evidens (t.ex. "rubrik hittad", "1 post extraherad"). */
  evidence: z.array(z.string()),
});
export type SectionConfidenceDto = z.infer<typeof sectionConfidenceDtoSchema>;

export const parseConfidenceDtoSchema = z.object({
  overall: overallConfidenceLevelSchema,
  requiresManualReview: z.boolean(),
  /** Fallback-orsak (`None`/`ExtractionFailed`/`NoSectionsDetected`/… ). Öppen sträng. */
  fallback: z.string(),
  sections: z.array(sectionConfidenceDtoSchema),
});
export type ParseConfidenceDto = z.infer<typeof parseConfidenceDtoSchema>;

/** PII-säker personnummer-scan: antal + typer, ALDRIG ett råvärde (ADR 0074
 * Invariant 1). Driver den civila "ta bort"-varningen (IMY flag-only). */
export const personnummerScanDtoSchema = z.object({
  found: z.boolean(),
  count: z.number().int().nonnegative(),
  kinds: z.array(z.string()),
});
export type PersonnummerScanDto = z.infer<typeof personnummerScanDtoSchema>;

export const parsedContactDtoSchema = z.object({
  fullName: z.string().nullable(),
  email: z.string().nullable(),
  phone: z.string().nullable(),
  location: z.string().nullable(),
});
export type ParsedContactDto = z.infer<typeof parsedContactDtoSchema>;

/** En löst tolkad erfarenhetspost. `period` är den RÅA tolkade strängen (t.ex.
 * "2021–2024") — gap-fill-formen (F2) gör den till strukturerade datum; backend
 * gissar aldrig datum på ett PII-fält (DQ3-3a). `rawText` = verbatim posttext. */
export const parsedExperienceDtoSchema = z.object({
  title: z.string().nullable(),
  organization: z.string().nullable(),
  period: z.string().nullable(),
  rawText: z.string(),
});
export type ParsedExperienceDto = z.infer<typeof parsedExperienceDtoSchema>;

export const parsedEducationDtoSchema = z.object({
  institution: z.string().nullable(),
  degree: z.string().nullable(),
  period: z.string().nullable(),
  rawText: z.string(),
});
export type ParsedEducationDto = z.infer<typeof parsedEducationDtoSchema>;

export const parsedContentDtoSchema = z.object({
  contact: parsedContactDtoSchema,
  profile: z.string().nullable(),
  experiences: z.array(parsedExperienceDtoSchema),
  educations: z.array(parsedEducationDtoSchema),
  skills: z.array(z.string()),
  languages: z.array(z.string()),
});
export type ParsedContentDto = z.infer<typeof parsedContentDtoSchema>;

/** Ett obekräftat SSYK-yrkesgruppsförslag (ADR 0040 Beslut 4 — användaren
 * bekräftar nedströms, aldrig auto-valt). Icke-PII (taxonomi-id + etiketter).
 * Visas display-only i F1; derive/confirm-flödet är STEG B-2. */
export const occupationProposalDtoSchema = z.object({
  conceptId: z.string(),
  label: z.string(),
  matchedOn: z.string(),
});
export type OccupationProposalDto = z.infer<typeof occupationProposalDtoSchema>;

export const parsedResumeDetailDtoSchema = z.object({
  id: z.string(),
  /** `PendingReview` (enda hämtbara), `Promoted`/`Discarded` osynliga via global filter. */
  status: z.string(),
  detectedLanguage: z.string(),
  sourceFileName: z.string(),
  confidence: parseConfidenceDtoSchema,
  personnummer: personnummerScanDtoSchema,
  content: parsedContentDtoSchema,
  occupationProposals: z.array(occupationProposalDtoSchema),
  createdAt: z.string(),
  updatedAt: z.string(),
});
export type ParsedResumeDetailDto = z.infer<typeof parsedResumeDetailDtoSchema>;

// --- CV-granska (F4-9) ------------------------------------------------------

/** Renderingsprofil. Backend-validatorn är case-sensitive (`Ats`|`Visual`) — dessa
 * exakta värden går i `?profile=`-searchParam:n på granska-vyn. */
export const renderProfileSchema = z.enum(["Ats", "Visual"]);
export type RenderProfile = z.infer<typeof renderProfileSchema>;

/** Det LÅSTA fyra-värdes-verdiktet (paritet `MatchDimensionVerdict`). `NotAssessed`
 * = determinismen kan inte bedöma i v1 — aldrig ett påhittat Pass/Fail (§5-ärlighet). */
export const criterionVerdictSchema = z.enum([
  "Pass",
  "Warn",
  "Fail",
  "NotAssessed",
]);
export type CriterionVerdict = z.infer<typeof criterionVerdictSchema>;

export const rubricCategorySchema = z.enum([
  "Content",
  "Structure",
  "Language",
  "AtsParsability",
  "VisualQuality",
]);
export type RubricCategory = z.infer<typeof rubricCategorySchema>;

/** Sekundär, data-härledd bandetikett (ej en opak siffra — Goodhart-skydd). */
export const scoreBandLabelSchema = z.enum([
  "NotReady",
  "NeedsRework",
  "Competitive",
  "TopTier",
]);
export type ScoreBandLabel = z.infer<typeof scoreBandLabelSchema>;

/** Taggad transportform av evidensen: `TextSpan` (citerat span ur CV-texten,
 * redan pnr-redigerat vid motorns choke point) eller `Structural` (observation
 * om frånvaro/struktur). */
export const citedEvidenceDtoSchema = z.object({
  kind: z.enum(["TextSpan", "Structural"]),
  start: z.number().int().nullable(),
  length: z.number().int().nullable(),
  quote: z.string().nullable(),
  note: z.string().nullable(),
  observation: z.string().nullable(),
});
export type CitedEvidenceDto = z.infer<typeof citedEvidenceDtoSchema>;

export const cvCriterionVerdictDtoSchema = z.object({
  criterionId: z.string(),
  category: rubricCategorySchema,
  verdict: criterionVerdictSchema,
  evidence: z.array(citedEvidenceDtoSchema),
  notAssessedReason: z.string().nullable(),
});
export type CvCriterionVerdictDto = z.infer<typeof cvCriterionVerdictDtoSchema>;

export const cvReviewCategoryDtoSchema = z.object({
  category: rubricCategorySchema,
  passCount: z.number().int().nonnegative(),
  warnCount: z.number().int().nonnegative(),
  failCount: z.number().int().nonnegative(),
  notAssessedCount: z.number().int().nonnegative(),
  band: scoreBandLabelSchema,
});
export type CvReviewCategoryDto = z.infer<typeof cvReviewCategoryDtoSchema>;

export const cvReviewDtoSchema = z.object({
  rubricVersion: z.string(),
  profile: renderProfileSchema,
  categories: z.array(cvReviewCategoryDtoSchema),
  verdicts: z.array(cvCriterionVerdictDtoSchema),
  criticalFails: z.array(cvCriterionVerdictDtoSchema),
  assessedCount: z.number().int().nonnegative(),
  totalCount: z.number().int().nonnegative(),
});
export type CvReviewDto = z.infer<typeof cvReviewDtoSchema>;
