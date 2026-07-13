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

/** En post i en fri sektion (#815). `title` är null när posten saknar rubrik — parsern
 *  hittar aldrig på en. */
export const parsedSectionEntryDtoSchema = z.object({
  title: z.string().nullable(),
  lines: z.array(z.string()),
});

/** En sektion CV:t har som inte är någon av de sex typade ("Projekt", "Referenser").
 *  `heading` är användarens egen rad, ordagrant — innehåll, inte en diskriminator. */
export const parsedSectionDtoSchema = z.object({
  heading: z.string(),
  entries: z.array(parsedSectionEntryDtoSchema),
});
export type ParsedSectionDto = z.infer<typeof parsedSectionDtoSchema>;

export const parsedContentDtoSchema = z.object({
  contact: parsedContactDtoSchema,
  profile: z.string().nullable(),
  experiences: z.array(parsedExperienceDtoSchema),
  educations: z.array(parsedEducationDtoSchema),
  skills: z.array(z.string()),
  languages: z.array(z.string()),
  // Deploy-skew-tolerant (samma mönster som `gaps`/`isIgnorable`): en äldre backend
  // utelämnar nyckeln, och ett parse-artefakt skrivet före #815 saknar den i sin
  // krypterade JSON. Bägge landar som [] — aldrig ett kraschat schema.
  sections: z.array(parsedSectionDtoSchema).nullish().transform((v) => v ?? []),
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

/**
 * Onboarding-frikoppling (DEL 1, CTO-bind pending-card): den NON-PII-summeringen
 * av användarens senaste PendingReview-parsade CV
 * (`GET /api/v1/resumes/parsed/latest-pending`). Bär bara id + filnamn + tidpunkt
 * — INTE parse-innehållet (ingen CV-PII), så den får läsas av /cv-listvyn (RSC) för
 * att yta ett "slutför ditt CV"-kort. `uploadedAt` valideras som `z.string()` på
 * wire-nivå (datum-formatering är UI-ansvar, ADR 0020).
 */
/**
 * Icke-PII närvaro-flaggor för de nio bekräfta-uppgifterna (Fas 4b PR-8,
 * CTO-bind Q5, paritet Domain `ParsedGapSummary`). Denormaliserade vid import
 * (ADR 0059) — hubb-listvyn dekrypterar aldrig CV-PII. Detta är den DELADE
 * uppgiftsdefinitionen bakom BÅDE åtgärdskortets "X av Y uppgifter klara"-mätare
 * OCH Slutför-guidens steg-gate; de får aldrig vara oense om vad en uppgift är.
 * `null` på pre-PR-8-import (se `pendingParsedResumeSummarySchema.gaps`).
 */
export const parsedGapSummarySchema = z.object({
  hasFullName: z.boolean(),
  hasEmail: z.boolean(),
  hasPhone: z.boolean(),
  hasLocation: z.boolean(),
  hasProfile: z.boolean(),
  hasExperience: z.boolean(),
  hasEducation: z.boolean(),
  hasSkills: z.boolean(),
  hasLanguages: z.boolean(),
});
export type ParsedGapSummary = z.infer<typeof parsedGapSummarySchema>;

export const pendingParsedResumeSummarySchema = z.object({
  id: z.string(),
  sourceFileName: z.string(),
  uploadedAt: z.string(),
  /** De nio bekräfta-uppgifternas närvaro (Fas 4b PR-8, CTO-bind Q5). `null` för
   * en pre-PR-8-import: ett ärligt "inte beräknat" — kortet renderar UTAN mätare
   * i stället för att gissa (§5). Icke-PII (rena närvaro-boolar). `nullish`, inte
   * bara `nullable`: en äldre backend (deploy-skew) UTELÄMNAR nyckeln helt, och
   * ett hårt parse-fel skulle fälla HELA åtgärdskortet — saknad nyckel är samma
   * ärliga "inte beräknat" som null (öppet additivt fält; de nio boolarna förblir
   * strikta när nyckeln finns). Transformen normaliserar till `null` så
   * konsumenterna slipper `undefined`-grenen. */
  gaps: parsedGapSummarySchema
    .nullish()
    .transform((v) => v ?? null),
});
export type PendingParsedResumeSummary = z.infer<
  typeof pendingParsedResumeSummarySchema
>;

/** Wire-svaret är ANTINGEN summeringen ELLER literal `null` (HTTP 200 i båda fall
 * — inget pending CV ger `null`, inte 404). Schemat är därför nullable. */
export const pendingParsedResumeResponseSchema =
  pendingParsedResumeSummarySchema.nullable();

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
  /** Mänsklig svensk rubrik-rubrik (t.ex. "Mätbara resultat"), single source of
   * truth backend-sidan. Bär rad-rubriken i UI; `criterionId` ("A1") demoteras
   * till en dämpad sekundär referens (B.3). */
  name: z.string(),
  category: rubricCategorySchema,
  verdict: criterionVerdictSchema,
  evidence: z.array(citedEvidenceDtoSchema),
  notAssessedReason: z.string().nullable(),
  /** Den bevarade användarbeslut-overlayen på den KANONISKA granskningen (Fas 4b
   * PR-4, D2(e)): status-namnet ("Open"/"Resolved"/"Ignored") som användaren
   * registrerade, plus stale-stämpeln (ISO) när CV:t ändrats under ett Resolved-
   * beslut vars anmärkning fortfarande finns kvar ("markerad åtgärdad, finns kvar").
   * `null` på staging-granskningen och när inget (kvarvarande) beslut finns. Öppen
   * sträng (status-mängden speglas inte som en låst `z.enum` här — okänt värde
   * renderas neutralt i stället för att fälla hela granskningen). `nullish` +
   * transform: en äldre/staging-backend utelämnar nyckeln → normaliseras till null. */
  userStatus: z
    .string()
    .nullish()
    .transform((v) => v ?? null),
  userStatusStaleAt: z
    .string()
    .nullish()
    .transform((v) => v ?? null),
  /** Speglar kriteriets versionerade StyleOnly-rubrikflagga (Fas 4b PR-8.4, CTO-bind
   * Q1) så att granska-UI:t ärligt kan gate:a "Ignorera regeln (stilfråga)"-kontrollen
   * till enbart stilkriterier — samma mängd som backend upprätthåller (400
   * `FindingNotIgnorable` på övriga). `false` som default (fail-closed) när nyckeln
   * saknas: en äldre backend (deploy-skew) döljer hellre Ignorera-knappen än visar
   * den optimistiskt (§5 — aldrig ett erbjudande servern nekar). */
  isIgnorable: z
    .boolean()
    .nullish()
    .transform((v) => v ?? false),
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

// --- CV-förbättra (F4-10, propose-and-approve) ------------------------------

/** Den LÅSTA mängden ändringstyper (paritet `ProposedChangeKind`). Strikt `z.enum`
 * → drift fail-loud:ar (DtoParseError → `kind: "error"`). `SectionReorder`/`PhotoStrip`
 * är "ej bedömt v1" (motorn emitterar dem ev. inte), men är legala wire-värden →
 * måste finnas med för forward-compat. */
export const proposedChangeKindSchema = z.enum([
  "ClicheReplacement",
  "WeakVerbUpgrade",
  "DateNormalization",
  "SectionReorder",
  "HeadingNormalization",
  "PersonnummerStrip",
  "PhotoStrip",
  "GpaStrip",
  "AtsSanitization",
]);
export type ProposedChangeKind = z.infer<typeof proposedChangeKindSchema>;

/** Den LÅSTA mängden strukturella transform-typer (paritet `StructuralTransformKind`).
 * Strikt `z.enum` (samma drift-fail-loud-motiv). Bär både `operation.kind` och
 * `provenance.transform`. */
export const structuralTransformKindSchema = z.enum([
  "ReformatDate",
  "NormalizeHeadingCase",
  "RemovePersonnummer",
  "RemovePhotoReference",
  "RemoveGpa",
  "StripNonStandardChars",
  "ReorderSection",
]);
export type StructuralTransformKind = z.infer<
  typeof structuralTransformKindSchema
>;

/** En före→efter-textändring (`null` när ändringen är en ren strukturell borttagning).
 * Öppna strängar (motorns redigerade CV-text — redan pnr-redigerad vid choke point). */
export const proposedReplacementDtoSchema = z.object({
  before: z.string(),
  after: z.string(),
});
export type ProposedReplacementDto = z.infer<
  typeof proposedReplacementDtoSchema
>;

/** En strukturell operation (`null` när ändringen är en ren textersättning). `target`
 * är ett öppet fält (sektion-/fält-referens). */
export const structuralOperationDtoSchema = z.object({
  kind: structuralTransformKindSchema,
  target: z.string(),
});
export type StructuralOperationDto = z.infer<
  typeof structuralOperationDtoSchema
>;

/** Taggad transportform av proveniensen: `KnowledgeBank` (kunskapsbank-källa +
 * version + key) eller `StructuralTransform` (deterministisk regel + transform-namn).
 * `kind` är låst; övriga fält är öppna strängar och nullable (bara delmängden för
 * respektive `kind` är satt). Detta är förklarbarhets-kontraktet. */
export const changeProvenanceDtoSchema = z.object({
  kind: z.enum(["KnowledgeBank", "StructuralTransform"]),
  source: z.string().nullable(),
  version: z.string().nullable(),
  key: z.string().nullable(),
  transform: structuralTransformKindSchema.nullable(),
});
export type ChangeProvenanceDto = z.infer<typeof changeProvenanceDtoSchema>;

/** Ett föreslaget förbättringsförslag med citerad evidens + proveniens. `evidence`
 * är ETT objekt (inte en array, till skillnad från granska-DTO:n). En ändring är
 * ANTINGEN ersättnings-baserad (`replacement` satt, `operation` null) ELLER
 * strukturell (`operation` satt, `replacement` null). Återanvänder
 * `citedEvidenceDtoSchema` + `rubricCategorySchema` verbatim. */
export const proposedChangeDtoSchema = z.object({
  targetId: z.string(),
  kind: proposedChangeKindSchema,
  category: rubricCategorySchema,
  criterionId: z.string().nullable(),
  evidence: citedEvidenceDtoSchema,
  replacement: proposedReplacementDtoSchema.nullable(),
  operation: structuralOperationDtoSchema.nullable(),
  rationale: z.string(),
  provenance: changeProvenanceDtoSchema,
});
export type ProposedChangeDto = z.infer<typeof proposedChangeDtoSchema>;

/** Resultatet av ett CV-förbättringspass (F4-10). Display-only i v1: varje förslag
 * visas read-only som vägledning — inget apply-endpoint, ingen tyst omskrivning
 * (CLAUDE.md §5). Ingen opak totalsumma (Goodhart) — bara per-kategori-räkning som
 * skanninfo. Versions-fälten bär determinismens proveniens. */
export const cvImprovementDtoSchema = z.object({
  clicheListVersion: z.string(),
  verbMappingVersion: z.string(),
  rubricVersion: z.string(),
  profile: renderProfileSchema,
  changes: z.array(proposedChangeDtoSchema),
});
export type CvImprovementDto = z.infer<typeof cvImprovementDtoSchema>;
