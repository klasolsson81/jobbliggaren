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
 * Ren mappning av CV-granska-/parse-enumvärdena (som korsar wire som sina .NET-
 * namn) till svensk UI-copy + StatusPill-toner. Hårdkodad svenska (next-intl ej
 * aktiv, Klas-direktiv); civic ton (ingen emoji/utropstecken, "du"). Inga
 * trösklar/siffror — bara etiketter. En `?? fallback` håller renderingen robust
 * om backend någonsin skickar ett okänt värde (zod fail-loud:ar dock redan på de
 * låsta mängderna).
 */

export interface LabelWithTone {
  readonly label: string;
  readonly tone: PillTone;
}

const VERDICT: Record<CriterionVerdict, LabelWithTone> = {
  Pass: { label: "Godkänt", tone: "success" },
  Warn: { label: "Delvis", tone: "warning" },
  Fail: { label: "Underkänt", tone: "danger" },
  NotAssessed: { label: "Ej bedömt", tone: "neutral" },
};

const BAND: Record<ScoreBandLabel, LabelWithTone> = {
  NotReady: { label: "Ej redo", tone: "danger" },
  NeedsRework: { label: "Behöver omarbetning", tone: "warning" },
  Competitive: { label: "Konkurrenskraftigt", tone: "info" },
  TopTier: { label: "Toppskikt", tone: "success" },
};

const CATEGORY: Record<RubricCategory, string> = {
  Content: "Innehåll",
  Structure: "Struktur",
  Language: "Språk",
  AtsParsability: "ATS-läsbarhet",
  VisualQuality: "Visuell kvalitet",
};

const OVERALL: Record<OverallConfidenceLevel, LabelWithTone> = {
  Confident: { label: "Tolkningen ser pålitlig ut", tone: "success" },
  Degraded: { label: "Tolkningen är ofullständig", tone: "warning" },
  Failed: { label: "Ingen text kunde läsas", tone: "danger" },
};

const SECTION_LEVEL: Record<SectionConfidenceLevel, LabelWithTone> = {
  Confident: { label: "Hittad", tone: "success" },
  Degraded: { label: "Delvis", tone: "warning" },
  NotFound: { label: "Hittades inte", tone: "neutral" },
};

const SECTION_KIND: Record<string, string> = {
  Contact: "Kontakt",
  Profile: "Profil",
  Experience: "Erfarenhet",
  Education: "Utbildning",
  Skills: "Kompetenser",
  Languages: "Språk",
};

/** Förbättringsförslagets typ (F4-10) → svensk etikett. Används i den strukturella
 * observations-meningen ("Föreslagen ändring: {etikett} på {fält}"). Den korta
 * pill-etiketten (Omformulering/Struktur) härleds separat ur om förslaget har en
 * textersättning eller är en ren strukturell operation (se `changeKindPillLabel`). */
const PROPOSED_CHANGE_KIND: Record<ProposedChangeKind, string> = {
  ClicheReplacement: "Ersätt klyscha",
  WeakVerbUpgrade: "Starkare verb",
  DateNormalization: "Normalisera datum",
  SectionReorder: "Ändra sektionsordning",
  HeadingNormalization: "Normalisera rubrik",
  PersonnummerStrip: "Ta bort personnummer",
  PhotoStrip: "Ta bort foto",
  GpaStrip: "Ta bort betyg",
  AtsSanitization: "ATS-sanering",
};

/** Strukturell transform-typ (F4-10) → svensk etikett. Bär provenance-foten för
 * strukturella regler ("Källa: strukturell regel ({etikett})"). Skild från
 * ProposedChangeKind (`provenance.transform` är den faktiska regeln som kördes). */
const STRUCTURAL_TRANSFORM: Record<StructuralTransformKind, string> = {
  ReformatDate: "Normalisera datum",
  NormalizeHeadingCase: "Normalisera rubrik",
  RemovePersonnummer: "Ta bort personnummer",
  RemovePhotoReference: "Ta bort foto",
  RemoveGpa: "Ta bort betyg",
  StripNonStandardChars: "Ta bort icke-standardtecken",
  ReorderSection: "Ändra sektionsordning",
};

export function verdictLabel(verdict: CriterionVerdict): LabelWithTone {
  return VERDICT[verdict];
}

export function bandLabel(band: ScoreBandLabel): LabelWithTone {
  return BAND[band];
}

export function categoryLabel(category: RubricCategory): string {
  return CATEGORY[category];
}

export function overallConfidenceLabel(
  overall: OverallConfidenceLevel
): LabelWithTone {
  return OVERALL[overall];
}

export function sectionLevelLabel(
  level: SectionConfidenceLevel
): LabelWithTone {
  return SECTION_LEVEL[level];
}

/** Sektion-namn är en öppen sträng på wire → fall tillbaka till råvärdet. */
export function sectionKindLabel(kind: string): string {
  return SECTION_KIND[kind] ?? kind;
}

/** Förbättringsförslagets typ → svensk etikett (för strukturella förslag). */
export function proposedChangeKindLabel(kind: ProposedChangeKind): string {
  return PROPOSED_CHANGE_KIND[kind];
}

/** Strukturell transform-typ → svensk etikett (för provenance-foten). */
export function structuralTransformLabel(kind: StructuralTransformKind): string {
  return STRUCTURAL_TRANSFORM[kind];
}

/** Den neutrala pill-etiketten för ett förslag: textbärande förslag visar
 * "Omformulering", rena strukturella operationer visar "Struktur". Alltid neutral
 * ton — typen bärs av texten, aldrig enbart av färg (WCAG 1.4.1). */
export function changeKindPillLabel(hasReplacement: boolean): string {
  return hasReplacement ? "Omformulering" : "Struktur";
}
