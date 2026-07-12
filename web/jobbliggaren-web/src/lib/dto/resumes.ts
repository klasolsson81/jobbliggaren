import { z } from "zod";
import { pagedResult } from "./_helpers";

export const resumeVersionKindSchema = z.enum(["Master", "Tailored"]);
export type ResumeVersionKind = z.infer<typeof resumeVersionKindSchema>;

export const personalInfoDtoSchema = z.object({
  fullName: z.string(),
  email: z.string().nullable(),
  phone: z.string().nullable(),
  location: z.string().nullable(),
});
export type PersonalInfoDto = z.infer<typeof personalInfoDtoSchema>;

export const experienceDtoSchema = z.object({
  company: z.string(),
  role: z.string(),
  /** "yyyy-MM-dd" — DateOnly serialiserad */
  startDate: z.string(),
  /** "yyyy-MM-dd" eller null */
  endDate: z.string().nullable(),
  description: z.string().nullable(),
});
export type ExperienceDto = z.infer<typeof experienceDtoSchema>;

export const educationDtoSchema = z.object({
  institution: z.string(),
  degree: z.string(),
  /** "yyyy-MM-dd" */
  startDate: z.string(),
  /** "yyyy-MM-dd" eller null */
  endDate: z.string().nullable(),
});
export type EducationDto = z.infer<typeof educationDtoSchema>;

export const skillDtoSchema = z.object({
  name: z.string(),
  yearsExperience: z.number().nullable(),
});
export type SkillDto = z.infer<typeof skillDtoSchema>;

/** Talat språk (Fas 4b AppCopy superset, ADR 0095 D-C). `proficiency` bär
 * `LanguageProficiency`-SmartEnumets Name-token (engelska: NotStated/Basic/Good/
 * Fluent/Native). LÅST mängd → strikt `z.enum` så drift fail-loud:ar; backend
 * mappar okänd/utelämnad token till NotStated (aldrig syntetiserad). */
export const spokenLanguageDtoSchema = z.object({
  name: z.string(),
  proficiency: z.enum(["NotStated", "Basic", "Good", "Fluent", "Native"]),
});
export type SpokenLanguageDto = z.infer<typeof spokenLanguageDtoSchema>;

/** En post i en dynamisk yrkesstyrd CV-sektion (Fas 4b superset, ADR 0095 D-B).
 * `lines` är valfria brödrader (STJ passerar undefined när nyckeln utelämnas).
 *
 * `title` är NULLBAR (#815, ADR 0095 D-E-amendering): domänen tillåter en post utan
 * rubrik ("Referenser / Lämnas på begäran."), så API:t kan emittera `"title": null`.
 * Stod schemat kvar som `z.string()` skulle ett enda sådant CV få HELA detaljsidan att
 * falla till felläge — skrivvägen hade kunnat persistera ett tillstånd läsvägen inte kan
 * tolka. Det är exakt den skriv/läs-asymmetri #815 handlar om, speglad. */
export const sectionEntryDtoSchema = z.object({
  title: z.string().nullish(),
  lines: z.array(z.string()).optional(),
});
export type SectionEntryDto = z.infer<typeof sectionEntryDtoSchema>;

/** En dynamisk yrkesstyrd CV-sektion utöver de fyra standard-sektionerna
 * (Fas 4b superset, ADR 0095 D-B). `heading` är fri användartext. */
export const resumeSectionDtoSchema = z.object({
  heading: z.string(),
  entries: z.array(sectionEntryDtoSchema).optional(),
});
export type ResumeSectionDto = z.infer<typeof resumeSectionDtoSchema>;

export const resumeContentDtoSchema = z.object({
  personalInfo: personalInfoDtoSchema,
  experiences: z.array(experienceDtoSchema),
  educations: z.array(educationDtoSchema),
  skills: z.array(skillDtoSchema),
  summary: z.string().nullable(),
  // Fas 4b AppCopy superset (ADR 0095): optional-med-default paritet mot backend
  // `ResumeContentDto` (pre-superset payloads utelämnar dem → parsar rent).
  // `skillGroups` är utanför PR-8.3-scope (CTO Q7(b)) och modelleras inte här;
  // okända nycklar ignoreras av zod (icke-strict).
  languages: z.array(spokenLanguageDtoSchema).optional(),
  sections: z.array(resumeSectionDtoSchema).optional(),
});
export type ResumeContentDto = z.infer<typeof resumeContentDtoSchema>;

export const resumeVersionDtoSchema = z.object({
  id: z.string(),
  kind: resumeVersionKindSchema,
  content: resumeContentDtoSchema,
  createdAt: z.string(),
  updatedAt: z.string(),
});
export type ResumeVersionDto = z.infer<typeof resumeVersionDtoSchema>;

export const resumeLanguageSchema = z.enum(["Sv", "En"]);
export type ResumeLanguage = z.infer<typeof resumeLanguageSchema>;

/** CV:ts ursprung (paritet `ResumeSourceOrigin`, ADR 0096). LÅST mängd → strikt
 * `z.enum` så drift fail-loud:ar. Driver hubb-kortets badge: `Import` →
 * "Importerad", `Template` → "Skapad", `Legacy` → ingen badge (pre-origin-CV). */
export const resumeOriginSchema = z.enum(["Legacy", "Import", "Template"]);
export type ResumeOrigin = z.infer<typeof resumeOriginSchema>;

export const resumeListItemDtoSchema = z.object({
  id: z.string(),
  name: z.string(),
  versionCount: z.number().int().nonnegative(),
  createdAt: z.string(),
  updatedAt: z.string(),
  isPrimary: z.boolean(),
  language: resumeLanguageSchema,
  latestRole: z.string().nullable(),
  sectionCount: z.number().int().min(0).max(4),
  topSkills: z.array(z.string()).max(5),
  /** Öppna åtgärder ur den DEK-fria finding-status-ledgern (Fas 4b PR-8, CTO-bind
   * Q1). `null` = INTE granskad vid den nuvarande rubrikversionen → UI:t renderar
   * "Granska", ALDRIG noll (§5-ärlighet: "0" får bara betyda granskad-och-ren).
   * `0` = granskad, inga åtgärder. `N` = N att åtgärda. */
  openFindingCount: z.number().int().nonnegative().nullable(),
  origin: resumeOriginSchema,
  /** Mallnamn (icke-PII root-metadata, ADR 0096). Öppen sträng — nya mallar ska
   * inte fail-loud:a listvyn; visas bara som kort-metadata för Skapad-CV. */
  template: z.string(),
});
export type ResumeListItemDto = z.infer<typeof resumeListItemDtoSchema>;

/** Kanonisk ATS-textvy (`GET /api/v1/resumes/{id}/ats-text`, Fas 4b PR-8.2).
 * `source` är en diskriminator (`Linearized` idag; RawText-syskonet är en
 * framtidsflagga) — öppen sträng så ett framtida värde inte fail-loud:ar den
 * read-only vyn. `text` = linjäriserad, pnr-redigerad CV-text (redan ren vid
 * motorns choke point; BFF:n läser bara vad backend redan garanterat). */
export const atsTextResponseSchema = z.object({
  source: z.string(),
  text: z.string(),
});
export type AtsTextResponse = z.infer<typeof atsTextResponseSchema>;

/** De persisterade malloptionerna för ett CV (Fas 4b PR-8b 8b.3, ADR 0096) —
 * paritet med backend `CvTemplateOptionsDto`. De sex icke-PII medlemsnamnen
 * (SmartEnum Name-tokens) + `effectiveAtsSafe`, domänens SAMMANSATTA ATS-verdikt
 * (`Template.AtsSafe && !PhotoEnabled`) — INTE mall-halvan ensam. `template`/
 * `accentColor`/`fontPair`/`density`/`photoShape` är öppna strängar så en ny
 * mall/accent/form inte fail-loud:ar detaljvyn (nya värden är kort-metadata, inte
 * ett brott). Hydratiserar mallbyggarens nuvarande val + den persisterade
 * ATS-etiketten. */
export const cvTemplateOptionsDtoSchema = z.object({
  template: z.string(),
  accentColor: z.string(),
  fontPair: z.string(),
  density: z.string(),
  photoEnabled: z.boolean(),
  photoShape: z.string(),
  effectiveAtsSafe: z.boolean(),
});
export type CvTemplateOptionsDto = z.infer<typeof cvTemplateOptionsDtoSchema>;

export const resumeDetailDtoSchema = z.object({
  id: z.string(),
  name: z.string(),
  createdAt: z.string(),
  updatedAt: z.string(),
  versions: z.array(resumeVersionDtoSchema),
  // De persisterade malloptionerna (Fas 4b PR-8b 8b.3, ADR 0096). Backend
  // emitterar ALLTID fältet (`ToDetailDto` mappar `r.TemplateOptions`) → required,
  // inte optional. Hydratiserar mallbyggarens val + den persisterade ATS-etiketten.
  templateOptions: cvTemplateOptionsDtoSchema,
});
export type ResumeDetailDto = z.infer<typeof resumeDetailDtoSchema>;

/** Den slutna, icke-PII-katalogen av malloptioner mallbyggaren konsumerar (Fas 4b
 * PR-8b 8b.3, CTO-bind Q2) — paritet med backend `CvTemplateCatalogDto`. Varje
 * option bär BARA sitt stabila medlemsnamn (FE resolvar svensk etikett via
 * next-intl, ingen etikett i payloaden) plus de två fakta FE ALDRIG får härleda
 * själv: en malls `atsSafe` (domänregeln `CvTemplate.AtsSafe`, P5 "ytor får aldrig
 * motsäga varandra") och en accents `hex`-swatch (den WCAG-vaktade `CvPalette`, så
 * en swatch aldrig kan visa en färg pdf:en inte har). Statisk referensdata, samma
 * för alla användare. Öppna namn-strängar så en ny mall/accent/täthet flödar till
 * pickern med bara en ny i18n-etikett, utan att fail-loud:a. */
export const templateCatalogDtoSchema = z.object({
  templates: z.array(z.object({ name: z.string(), atsSafe: z.boolean() })),
  accents: z.array(z.object({ name: z.string(), hex: z.string() })),
  fontPairs: z.array(z.object({ name: z.string() })),
  densities: z.array(z.object({ name: z.string() })),
});
export type TemplateCatalogDto = z.infer<typeof templateCatalogDtoSchema>;
export type TemplateOption = TemplateCatalogDto["templates"][number];
export type AccentOption = TemplateCatalogDto["accents"][number];
export type DensityOption = TemplateCatalogDto["densities"][number];

export const getResumesResultSchema = pagedResult(resumeListItemDtoSchema);
export type GetResumesResult = z.infer<typeof getResumesResultSchema>;
