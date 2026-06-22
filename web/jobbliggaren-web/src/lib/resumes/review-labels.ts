import type { PillTone } from "@/components/ui/status-pill";
import type {
  CriterionVerdict,
  RubricCategory,
  ScoreBandLabel,
  OverallConfidenceLevel,
  SectionConfidenceLevel,
  ProposedChangeKind,
  StructuralTransformKind,
} from "@/lib/dto/parsed-resume";

/**
 * Mappning av CV-granska-/parse-enumvärdena (som korsar wire som sina .NET-namn)
 * till svensk UI-copy + StatusPill-toner. Etiketterna resolvas genom next-intl:
 * samma `t`-anropssignatur fungerar i både klient (`useTranslations("resumes.enums")`)
 * och server (`useTranslations` i en RSC) — anropare skaffar `t` scopat till
 * `"resumes.enums"`-namespacet och skickar in det. De svenska värdena bor i
 * `messages/sv/resumes.json` (källa, typad via AppConfig). Tonerna är ren UI-logik
 * (inte översättningsbara) och stannar här. Civic ton (ingen emoji/utropstecken,
 * "du"); inga trösklar/siffror, bara etiketter.
 */

export interface LabelWithTone {
  readonly label: string;
  readonly tone: PillTone;
}

const VERDICT_TONE: Record<CriterionVerdict, PillTone> = {
  Pass: "success",
  Warn: "warning",
  Fail: "danger",
  NotAssessed: "neutral",
};

const BAND_TONE: Record<ScoreBandLabel, PillTone> = {
  NotReady: "danger",
  NeedsRework: "warning",
  Competitive: "info",
  TopTier: "success",
};

const OVERALL_TONE: Record<OverallConfidenceLevel, PillTone> = {
  Confident: "success",
  Degraded: "warning",
  Failed: "danger",
};

const SECTION_LEVEL_TONE: Record<SectionConfidenceLevel, PillTone> = {
  Confident: "success",
  Degraded: "warning",
  NotFound: "neutral",
};

const SECTION_KINDS = [
  "Contact",
  "Profile",
  "Experience",
  "Education",
  "Skills",
  "Languages",
] as const;
type SectionKind = (typeof SECTION_KINDS)[number];

export function verdictLabel(
  t: (key: `verdict.${CriterionVerdict}`) => string,
  verdict: CriterionVerdict,
): LabelWithTone {
  return { label: t(`verdict.${verdict}`), tone: VERDICT_TONE[verdict] };
}

export function bandLabel(
  t: (key: `band.${ScoreBandLabel}`) => string,
  band: ScoreBandLabel,
): LabelWithTone {
  return { label: t(`band.${band}`), tone: BAND_TONE[band] };
}

export function categoryLabel(
  t: (key: `category.${RubricCategory}`) => string,
  category: RubricCategory,
): string {
  return t(`category.${category}`);
}

export function overallConfidenceLabel(
  t: (key: `overall.${OverallConfidenceLevel}`) => string,
  overall: OverallConfidenceLevel,
): LabelWithTone {
  return { label: t(`overall.${overall}`), tone: OVERALL_TONE[overall] };
}

export function sectionLevelLabel(
  t: (key: `sectionLevel.${SectionConfidenceLevel}`) => string,
  level: SectionConfidenceLevel,
): LabelWithTone {
  return { label: t(`sectionLevel.${level}`), tone: SECTION_LEVEL_TONE[level] };
}

/** Sektion-namn är en öppen sträng på wire → fall tillbaka till råvärdet om det
 * inte är en av de kända nycklarna (zod fail-loud:ar dock redan på mängden). */
export function sectionKindLabel(
  t: (key: `sectionKind.${SectionKind}`) => string,
  kind: string,
): string {
  return (SECTION_KINDS as readonly string[]).includes(kind)
    ? t(`sectionKind.${kind as SectionKind}`)
    : kind;
}

/** Förbättringsförslagets typ → svensk etikett (för strukturella förslag). */
export function proposedChangeKindLabel(
  t: (key: `proposedChangeKind.${ProposedChangeKind}`) => string,
  kind: ProposedChangeKind,
): string {
  return t(`proposedChangeKind.${kind}`);
}

/** Strukturell transform-typ → svensk etikett (för provenance-foten). */
export function structuralTransformLabel(
  t: (key: `structuralTransform.${StructuralTransformKind}`) => string,
  kind: StructuralTransformKind,
): string {
  return t(`structuralTransform.${kind}`);
}

/** Den neutrala pill-etiketten för ett förslag: textbärande förslag visar
 * "Omformulering", rena strukturella operationer visar "Struktur". Alltid neutral
 * ton — typen bärs av texten, aldrig enbart av färg (WCAG 1.4.1). */
export function changeKindPillLabel(
  t: (key: "changeKindPill.replacement" | "changeKindPill.structural") => string,
  hasReplacement: boolean,
): string {
  return hasReplacement
    ? t("changeKindPill.replacement")
    : t("changeKindPill.structural");
}
