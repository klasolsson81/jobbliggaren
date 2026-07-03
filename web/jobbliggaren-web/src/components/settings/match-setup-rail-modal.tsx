"use client";

// "use client": epik #526 ("förslag 2a Steglistan") — EN modal med fast storlek
// (1000×648) som ersätter det gamla fem-modal-flödet (välkomst + CV-upload + "CV
// inläst" + wizard-steg). Håller klient-only steg-state (0..6), draft för fem
// dimensioner (yrke/kompetens/ort/anställningsform + per-yrke-erfarenhet), en
// render-tids seed-flagga från persisterad SSOT, programmatisk fokus-flytt till
// stegrubriken vid stegbyte (WCAG 2.4.3), inline CV-upload på Start-steget, en
// live träffräknare (useDraftMatchCount) och en useTransition runt det enda
// save-anropet. URL ändras ALDRIG mid-flöde (ephemer formulär-state, ej deep-
// linkbar). Inget av detta går i en Server Component.
//
// Återanvänder de befintliga sektionerna oförändrade (branch-by-abstraction):
// OccupationSection / SkillSection / RegionMunicipalityCascade / FacetSection
// monteras per-steg (villkorligt) — load-bearing för lazy CV-suggest: CV:t läses
// ur staging-artefakten (parsedResumeId) när sektionen monteras, aldrig eager.
// 2a-looket (vald-chip/yrkecard/förslags-chip) läggs på via scoped additiv CSS i
// globals.css (.jp-wizard--rail ...), aldrig genom att ändra sektionerna.

import { useRef, useState, useTransition, type ReactNode } from "react";
import { useTranslations } from "next-intl";
import { Check, Info } from "lucide-react";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogTitle,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { BrandMarkSvg } from "@/components/brand/brand-mark-svg";
import { CvUploadForm } from "@/components/resumes/cv-upload-form";
import { OccupationSection } from "./occupation-section";
import { SkillSection } from "./skill-section";
import { RegionMunicipalityCascade } from "./region-municipality-cascade";
import { FacetSection } from "./facet-section";
import {
  flattenOccupationGroups,
  groupsForSelected,
  labelsForSelected,
  projectOccupationExperience,
  recordFromOccupationExperience,
  toggle,
  type Option,
  type SkillChip,
} from "./match-preferences-shared";
import { updateMatchPreferencesAction } from "@/lib/actions/match-preferences";
import { useDraftMatchCount } from "@/lib/hooks/use-draft-match-count";
import type {
  TaxonomyOccupationField,
  TaxonomyOption,
  TaxonomyRegion,
} from "@/lib/dto/taxonomy";
import type { SkillGroup } from "@/lib/dto/skills";
import type { OrtSelection } from "@/lib/job-ads/ort-selection";

// Steg-index (0..6). 0 Start, 1 Yrken, 2 Kompetenser, 3 Orter, 4 Anställningsform,
// 5 Granska, 6 Klart-läget (efter Spara). De sex sökbara/synliga stegen (0..5)
// är railens rader; 6 är enbart bekräftelse-läget.
const STEP_START = 0;
const STEP_YRKEN = 1;
const STEP_KOMPETENSER = 2;
const STEP_ORTER = 3;
const STEP_FORMER = 4;
const STEP_GRANSKA = 5;
const STEP_DONE = 6;

/** Railens sex rader (0..5) i ordning. */
const RAIL_STEPS: ReadonlyArray<number> = [
  STEP_START,
  STEP_YRKEN,
  STEP_KOMPETENSER,
  STEP_ORTER,
  STEP_FORMER,
  STEP_GRANSKA,
];

/** Svenskt tal med tunt tusenavgränsande mellanslag (räknaren, tabular-nums). */
const svNumber = new Intl.NumberFormat("sv-SE");

/** Rail-radens visuella tillstånd — härleds ur position, inte innehåll. */
type RailState = "done" | "active" | "upcoming";

interface MatchSetupRailModalProps {
  readonly open: boolean;
  readonly onOpenChange: (open: boolean) => void;
  readonly occupationFields: ReadonlyArray<TaxonomyOccupationField>;
  readonly regions: ReadonlyArray<TaxonomyRegion>;
  readonly employmentTypes: ReadonlyArray<TaxonomyOption>;
  /** Persisterad SSOT — draften seedas från den vid öppning. */
  readonly persistedOccupationGroups: ReadonlyArray<string>;
  readonly persistedRegions: ReadonlyArray<string>;
  readonly persistedMunicipalities: ReadonlyArray<string>;
  readonly persistedEmploymentTypes: ReadonlyArray<string>;
  readonly persistedSkills: ReadonlyArray<string>;
  /**
   * GRUPPER för redan-sparade kompetens-concept-id (#277): den platta skill-
   * taxonomin skickas aldrig som träd → värden bär in den BE-resolvade gruppmeta-
   * datan så ett sparat twin-par renderas som EN chip. Utelämnade id faller
   * tillbaka på id-strängen.
   */
  readonly persistedSkillGroups?: ReadonlyArray<SkillGroup>;
  /**
   * exp-per-occ (ADR 0079-amendment PR-4): den persisterade per-yrke-erfarenhets-
   * overlayn (gles delmängd av `persistedOccupationGroups`). `years === null` =
   * angivet men ospecificerat.
   */
  readonly persistedOccupationExperience: ReadonlyArray<{
    readonly conceptId: string;
    readonly years: number | null;
  }>;
  /** CV-importflödets route (tom-state-länken i yrkes-sektionen). */
  readonly importCvHref: string;
  /** Anropas efter lyckad save med den sparade fulla mängden. */
  readonly onSaved?: (saved: {
    occupations: ReadonlyArray<string>;
    regions: ReadonlyArray<string>;
    municipalities: ReadonlyArray<string>;
    employment: ReadonlyArray<string>;
    skills: ReadonlyArray<string>;
    occupationExperience: ReadonlyArray<{
      readonly conceptId: string;
      readonly years: number | null;
    }>;
  }) => void;
  /** Starta på ett visst steg (default Start). */
  readonly initialStep?: number;
}

/**
 * Match-setup-rail-modal (epik #526, förslag 2a). EN Radix-Dialog med fast
 * storlek: en steglista (rail) till vänster + huvudyta till höger som byter
 * innehåll per steg utan att skalet ändrar mått. Fri navigering (inget steg är
 * obligatoriskt). Skriver ENBART till `MatchPreferences` via det befintliga
 * full-replace-PUT:et vid "Spara matchning" (propose-and-approve, ADR 0040/0071
 * — inget skrivs innan dess). CV:t läses men promotas ALDRIG i det här flödet.
 */
export function MatchSetupRailModal({
  open,
  onOpenChange,
  occupationFields,
  regions,
  employmentTypes,
  persistedOccupationGroups,
  persistedRegions,
  persistedMunicipalities,
  persistedEmploymentTypes,
  persistedSkills,
  persistedSkillGroups = [],
  persistedOccupationExperience,
  importCvHref,
  onSaved,
  initialStep = STEP_START,
}: MatchSetupRailModalProps) {
  const t = useTranslations("matchsetup");

  // Platta taxonomi-listor för chip-labels + granska-läget.
  const occupationOptions = flattenOccupationGroups(occupationFields);
  const regionOptions: ReadonlyArray<Option> = regions.map((r) => ({
    conceptId: r.conceptId,
    label: r.label,
  }));
  const municipalityOptions: ReadonlyArray<Option> = regions.flatMap((r) =>
    r.municipalities.map((m) => ({ conceptId: m.conceptId, label: m.label }))
  );
  const employmentOptions: ReadonlyArray<Option> = employmentTypes.map((e) => ({
    conceptId: e.conceptId,
    label: e.label,
  }));

  const [step, setStep] = useState(initialStep);

  // Draft-state per dimension, seedat från SSOT vid öppning (render-tids-flagga).
  const [seededFor, setSeededFor] = useState(false);
  const [draftOccupations, setDraftOccupations] =
    useState<ReadonlyArray<string>>(persistedOccupationGroups);
  const [draftRegions, setDraftRegions] =
    useState<ReadonlyArray<string>>(persistedRegions);
  const [draftMunicipalities, setDraftMunicipalities] =
    useState<ReadonlyArray<string>>(persistedMunicipalities);
  const [draftEmployment, setDraftEmployment] =
    useState<ReadonlyArray<string>>(persistedEmploymentTypes);
  const [draftSkills, setDraftSkills] =
    useState<ReadonlyArray<string>>(persistedSkills);
  const [draftOccupationExperience, setDraftOccupationExperience] = useState<
    Readonly<Record<string, number | null>>
  >(() => recordFromOccupationExperience(persistedOccupationExperience));

  // #253/#277: SkillSection speglar HELA sin grupp-store (seed ∪ sök ∪ CV) hit,
  // så granska-läget kan slå upp manuellt tillagda kompetenser som grupper.
  const [discoveredSkillGroups, setDiscoveredSkillGroups] = useState<
    ReadonlyArray<SkillGroup>
  >([]);
  const allSkillGroups: ReadonlyArray<SkillGroup> = [
    ...persistedSkillGroups,
    ...discoveredSkillGroups,
  ];

  // Start-steget: id för det just inline-uppladdade parsed_resume:t (CV:t
  // promotas INTE). Bär till yrkes-/kompetens-sektionerna så de auto-föreslår
  // live ur staging-artefakten. `null` = inget CV uppladdat ("Fortsätt utan CV").
  const [uploadedParsedId, setUploadedParsedId] = useState<string | null>(null);
  // Filnamnet för "CV inläst: {filnamn}"-plattan. CvUploadForm.onUploaded ger i
  // dag bara parsedResumeId (se rapport: en rekommenderad section-utökning), så
  // detta är tills vidare alltid null → plattan faller tillbaka på "CV inläst".
  const [uploadedFileName, setUploadedFileName] = useState<string | null>(null);

  // Kompetens-steget: en remount-nyckel som "Återställ förslagen" bumpar för att
  // montera om SkillSection → dess en-gångs auto-suggest kör igen (pre-addar CV-
  // kompetenserna på nytt). Bara meningsfull när ett CV är uppladdat.
  const [skillRemountKey, setSkillRemountKey] = useState(0);

  const [saved, setSaved] = useState(false);
  const [isSaving, startSaving] = useTransition();
  const [saveError, setSaveError] = useState<string | null>(null);

  // Fokus-flytt till stegrubriken vid stegbyte (WCAG 2.4.3).
  const titleRef = useRef<HTMLHeadingElement | null>(null);

  // Seed/återställ vid öppning; nollställ vid stängning (render-tids-flagga,
  // set-state-in-effect-fri — samma mönster som match-setup-wizard).
  if (open && !seededFor) {
    setSeededFor(true);
    setStep(initialStep);
    setDraftOccupations(persistedOccupationGroups);
    setDraftRegions(persistedRegions);
    setDraftMunicipalities(persistedMunicipalities);
    setDraftEmployment(persistedEmploymentTypes);
    setDraftSkills(persistedSkills);
    setDraftOccupationExperience(
      recordFromOccupationExperience(persistedOccupationExperience)
    );
    setDiscoveredSkillGroups([]);
    setUploadedParsedId(null);
    setUploadedFileName(null);
    setSkillRemountKey(0);
    setSaved(false);
    setSaveError(null);
  }
  if (!open && seededFor) {
    setSeededFor(false);
  }

  // Live träffräknare: räknar aktiva annonser som matchar de fyra sökbara
  // dimensionerna (kompetenser gallrar aldrig counten — kvalitet, ej filter).
  // `count === null` medan förfrågan pågår / vid degradering → neutral platshållare.
  const { count } = useDraftMatchCount(
    {
      occupationGroups: draftOccupations,
      regions: draftRegions,
      municipalities: draftMunicipalities,
      employmentTypes: draftEmployment,
    },
    // Bara när modalen är öppen — /cv monterar den stängd (open=false); ingen
    // bakgrunds-poll mot den rate-limitade endpointen då.
    open,
  );

  // Härledda chips + räknare (rena projektioner).
  const occupationChips = labelsForSelected(draftOccupations, occupationOptions);
  const skillChips: ReadonlyArray<SkillChip> = groupsForSelected(
    draftSkills,
    allSkillGroups
  );
  const ortChips: ReadonlyArray<Option> = [
    ...labelsForSelected(draftRegions, regionOptions),
    ...labelsForSelected(draftMunicipalities, municipalityOptions),
  ];
  const employmentChips = labelsForSelected(draftEmployment, employmentOptions);

  const yrkenCount = draftOccupations.length;
  const kompCount = skillChips.length;
  const orterCount = ortChips.length;
  const formerCount = draftEmployment.length;

  function focusTitle() {
    // Flytta fokus till stegrubriken EFTER commit (WCAG 2.4.3). queueMicrotask
    // kör efter React-commit; DOM-fallback om wrappern inte vidarebefordrar ref.
    queueMicrotask(() => {
      const node =
        titleRef.current ??
        document.querySelector<HTMLElement>('[data-slot="dialog-title"]');
      node?.focus();
    });
  }

  function goToStep(next: number) {
    setStep(next);
    focusTitle();
  }

  // Ort-kaskaden emitterar HELA ort-paret (region + kommun) i ett anrop.
  function onOrtChange(next: OrtSelection) {
    setDraftRegions(next.region);
    setDraftMunicipalities(next.municipality);
  }

  // exp-per-occ: användaren ändrade ett yrkes år (null = tomt fält).
  function onOccupationExperienceChange(conceptId: string, years: number | null) {
    setDraftOccupationExperience((prev) => ({ ...prev, [conceptId]: years }));
  }

  // exp-per-occ: CV-förslaget seedar härledda år — mergas in MEN skriver ALDRIG
  // över ett befintligt (persisterat/användar-angivet) värde.
  function seedOccupationExperience(
    seed: Readonly<Record<string, number | null>>
  ) {
    setDraftOccupationExperience((prev) => {
      const nextRecord = { ...prev };
      for (const [conceptId, years] of Object.entries(seed)) {
        if (!(conceptId in nextRecord)) nextRecord[conceptId] = years;
      }
      return nextRecord;
    });
  }

  function restoreSkillSuggestions() {
    setSkillRemountKey((k) => k + 1);
  }

  function handleCvUploaded(parsedResumeId: string, fileName?: string) {
    setUploadedParsedId(parsedResumeId);
    setUploadedFileName(fileName ?? null);
  }

  function onSave() {
    setSaveError(null);
    const occupationExperience = projectOccupationExperience(
      draftOccupationExperience,
      draftOccupations
    );
    startSaving(async () => {
      const result = await updateMatchPreferencesAction({
        preferredOccupationGroups: [...draftOccupations],
        preferredRegions: [...draftRegions],
        preferredMunicipalities: [...draftMunicipalities],
        preferredEmploymentTypes: [...draftEmployment],
        preferredSkills: [...draftSkills],
        preferredOccupationExperience: [...occupationExperience],
      });
      if (result.success) {
        onSaved?.({
          occupations: draftOccupations,
          regions: draftRegions,
          municipalities: draftMunicipalities,
          employment: draftEmployment,
          skills: draftSkills,
          occupationExperience,
        });
        setSaved(true);
        goToStep(STEP_DONE);
      } else {
        setSaveError(result.error);
      }
    });
  }

  function onPrimary() {
    if (step === STEP_DONE) {
      onOpenChange(false);
      return;
    }
    if (step === STEP_GRANSKA) {
      onSave();
      return;
    }
    goToStep(step + 1);
  }

  function onBack() {
    if (step > STEP_START && step <= STEP_GRANSKA) goToStep(step - 1);
  }

  // "Hoppa över det här steget" (steg 1-4) går framåt utan att röra draften.
  // På Start stänger "Gör det senare" utan att spara (skip är aldrig destruktivt).
  function onSkip() {
    if (step === STEP_START) {
      onOpenChange(false);
      return;
    }
    if (step <= STEP_FORMER) goToStep(step + 1);
  }

  // Railens rad-label (rail.step.*) — delas av rad-listan + mobil-stegräknaren.
  function railLabel(index: number): string {
    switch (index) {
      case STEP_START:
        return t("rail.step.start");
      case STEP_YRKEN:
        return t("rail.step.yrken");
      case STEP_KOMPETENSER:
        return t("rail.step.kompetenser");
      case STEP_ORTER:
        return t("rail.step.orter");
      case STEP_FORMER:
        return t("rail.step.former");
      case STEP_GRANSKA:
        return t("rail.step.granska");
      default:
        return "";
    }
  }

  // Huvudytans rubrik (H2). Start + Granska bär egna rubriker; övriga = rad-label.
  function currentTitle(): string {
    switch (step) {
      case STEP_START:
        return t("start.title");
      case STEP_GRANSKA:
        return t("granska.title");
      default:
        return railLabel(step);
    }
  }

  function currentIntro(): string {
    // Yrken/Kompetenser: utan uppladdat CV finns inga CV-förslag → copy som inte
    // lovar dem (design-review Major). Med CV behålls "vi föreslår ur ditt CV".
    const hasCv = uploadedParsedId !== null;
    switch (step) {
      case STEP_START:
        return t("start.intro");
      case STEP_YRKEN:
        return hasCv ? t("yrken.intro") : t("yrken.introNoCv");
      case STEP_KOMPETENSER:
        return hasCv ? t("komp.intro") : t("komp.introNoCv");
      case STEP_ORTER:
        return t("orter.intro");
      case STEP_FORMER:
        return t("former.intro");
      default:
        return t("granska.intro");
    }
  }

  function stateFor(index: number): RailState {
    if (index === step) return "active";
    if (index < step) return "done";
    return "upcoming";
  }

  // Railens meta-text: innehålls-baserad. Ett tomt steg som passerats visar sin
  // konsekvens ("Hela landet"), ett ännu-ej-nått tomt steg visar "Ej ifyllt"
  // (WCAG 1.4.1 — klar-status bärs av text, inte bara färg/ikon).
  function metaFor(index: number): string {
    const passed = index < step;
    switch (index) {
      case STEP_START:
        return uploadedParsedId !== null
          ? t("rail.meta.cvInlast")
          : t("rail.meta.frivilligtCv");
      case STEP_YRKEN:
        return yrkenCount > 0
          ? t("rail.meta.valda", { count: yrkenCount })
          : passed
            ? t("rail.meta.allaYrken")
            : t("rail.meta.ejIfyllt");
      case STEP_KOMPETENSER:
        return kompCount > 0
          ? t("rail.meta.valda", { count: kompCount })
          : passed
            ? t("rail.meta.ingaValda")
            : t("rail.meta.ejIfyllt");
      case STEP_ORTER:
        return orterCount > 0
          ? t("rail.meta.valda", { count: orterCount })
          : passed
            ? t("rail.meta.helaLandet")
            : t("rail.meta.ejIfyllt");
      case STEP_FORMER:
        return formerCount > 0
          ? t("rail.meta.valda", { count: formerCount })
          : passed
            ? t("rail.meta.allaFormer")
            : t("rail.meta.ejIfyllt");
      case STEP_GRANSKA:
        return saved ? t("rail.meta.sparad") : t("rail.meta.aterstar");
      default:
        return "";
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="jp-stdmodal jp-wizard--rail">
        {/* Mobil topp-rad (dold på desktop): sigill + titel. Kryss = radix Close. */}
        <div className="jp-wizard__mobilebar">
          <BrandMarkSvg
            width={26}
            height={26}
            primaryFill="var(--jp-mark-primary)"
            accentFill="var(--jp-mark-accent)"
            paperFill="var(--jp-mark-paper)"
            ariaHidden
          />
          <span className="jp-wizard__mobilebar-title">{t("aria.dialog")}</span>
        </div>

        {/* Railen (272px): brand-rad + sex steg-knappar + räknarkort. */}
        <nav className="jp-wizard__rail" aria-label={t("aria.railNav")}>
          <div className="jp-wizard__railbrand">
            <BrandMarkSvg
              className="jp-wizard__railbrand-mark"
              width={30}
              height={30}
              primaryFill="var(--jp-mark-primary)"
              accentFill="var(--jp-mark-accent)"
              paperFill="var(--jp-mark-paper)"
              ariaHidden
            />
            <span className="jp-wizard__railbrand-text">
              <span className="jp-wizard__railbrand-title">{t("rail.title")}</span>
              <span className="jp-wizard__railbrand-sub">{t("rail.subtitle")}</span>
            </span>
          </div>

          <ul className="jp-wizard__raillist">
            {RAIL_STEPS.map((index) => {
              const state = stateFor(index);
              return (
                <li key={index}>
                  <button
                    type="button"
                    className="jp-wizard__railitem"
                    data-state={state}
                    aria-current={state === "active" ? "step" : undefined}
                    onClick={() => goToStep(index)}
                  >
                    <span
                      className="jp-wizard__railind"
                      data-state={state}
                      aria-hidden="true"
                    >
                      {state === "done" ? (
                        <Check size={13} strokeWidth={3.4} />
                      ) : (
                        <span className="jp-wizard__railnum">{index + 1}</span>
                      )}
                    </span>
                    <span className="jp-wizard__railitem-text">
                      <span className="jp-wizard__railitem-label">
                        {railLabel(index)}
                      </span>
                      <span className="jp-wizard__railmeta">{metaFor(index)}</span>
                    </span>
                  </button>
                </li>
              );
            })}
          </ul>

          <div className="jp-wizard__countcard">
            <div className="jp-wizard__countcard-head">
              <span className="jp-wizard__countcard-dot" aria-hidden="true" />
              <span className="jp-wizard__countcard-label">{t("counter.label")}</span>
            </div>
            <p
              className="jp-wizard__countcard-num"
              role="status"
              aria-live="polite"
            >
              {count === null ? (
                <span aria-hidden="true">{"–"}</span>
              ) : (
                svNumber.format(count)
              )}
            </p>
            <p className="jp-wizard__countcard-unit">{t("counter.unit")}</p>
          </div>
        </nav>

        {/* Huvudytan: head (stegräknare + H2 + intro) · body (scroll) · footer. */}
        <div className="jp-wizard__main">
          {step === STEP_DONE ? (
            <>
              <div className="jp-wizard__mainhead">
                <span className="jp-wizard__stepcounter">{t("stepcounterDone")}</span>
              </div>
              <div className="jp-wizard__body jp-wizard__body--center">
                <div className="jp-wizard__done">
                  <span className="jp-wizard__done-badge" aria-hidden="true">
                    <Check size={28} strokeWidth={2.5} />
                  </span>
                  <DialogTitle
                    ref={titleRef}
                    tabIndex={-1}
                    className="jp-wizard__done-title"
                  >
                    {t("done.title")}
                  </DialogTitle>
                  <DialogDescription className="jp-wizard__done-body">
                    {t("done.body")}
                  </DialogDescription>
                </div>
              </div>
              <div className="jp-wizard__foot">
                <span className="jp-wizard__foot-spacer" />
                <Button type="button" onClick={() => onOpenChange(false)}>
                  {t("nav.stang")}
                </Button>
              </div>
            </>
          ) : (
            <>
              <div className="jp-wizard__mainhead">
                <span className="jp-wizard__stepcounter">
                  {t("stepcounter", { step: step + 1 })}
                  <span className="jp-wizard__stepcounter-name">
                    {" · "}
                    {railLabel(step)}
                  </span>
                </span>
                <div className="jp-wizard__mobileprog" aria-hidden="true">
                  {RAIL_STEPS.map((index) => (
                    <span
                      key={index}
                      className={
                        index <= step
                          ? "jp-wizard__mobileseg jp-wizard__mobileseg--on"
                          : "jp-wizard__mobileseg"
                      }
                    />
                  ))}
                </div>
                <DialogTitle
                  ref={titleRef}
                  tabIndex={-1}
                  className="jp-wizard__h2"
                >
                  {currentTitle()}
                </DialogTitle>
                <DialogDescription className="jp-wizard__intro">
                  {currentIntro()}
                </DialogDescription>
              </div>

              <div className="jp-wizard__body">
                <section role="group" aria-label={currentTitle()}>
                  {step === STEP_START && (
                    <div className="jp-wizard__start">
                      <div className="jp-wizard__pitch">
                        <BrandMarkSvg
                          className="jp-wizard__pitch-mark"
                          width={60}
                          height={60}
                          primaryFill="var(--jp-mark-primary)"
                          accentFill="var(--jp-mark-accent)"
                          paperFill="var(--jp-mark-paper)"
                          ariaHidden
                        />
                        <div className="jp-wizard__pitch-text">
                          <h3 className="jp-wizard__pitch-title">
                            {t("start.pitchTitle")}
                          </h3>
                          <p className="jp-wizard__pitch-body">
                            {t("start.pitchBody")}
                          </p>
                        </div>
                      </div>

                      {uploadedParsedId === null ? (
                        <div className="jp-wizard__upload">
                          <div className="jp-wizard__upload-head">
                            <h4 className="jp-wizard__upload-title">
                              {t("start.uploadTitle")}
                            </h4>
                            <p className="jp-wizard__upload-body">
                              {t("start.uploadBody")}
                            </p>
                          </div>
                          <CvUploadForm onUploaded={handleCvUploaded} />
                          <p className="jp-wizard__nocv">
                            {t.rich("start.noCv", {
                              b: (chunks) => (
                                <button
                                  type="button"
                                  className="jp-wizard__startlink"
                                  onClick={() => goToStep(STEP_YRKEN)}
                                >
                                  {chunks}
                                </button>
                              ),
                            })}
                          </p>
                        </div>
                      ) : (
                        <div className="jp-wizard__cvdone">
                          <span
                            className="jp-wizard__cvdone-badge"
                            aria-hidden="true"
                          >
                            <Check size={13} strokeWidth={3} />
                          </span>
                          <div className="jp-wizard__cvdone-text">
                            <p className="jp-wizard__cvdone-title">
                              {uploadedFileName
                                ? t("start.cvDoneTitle", {
                                    filename: uploadedFileName,
                                  })
                                : t("rail.meta.cvInlast")}
                            </p>
                            <p className="jp-wizard__cvdone-body">
                              {t("start.cvDoneBody")}
                            </p>
                          </div>
                        </div>
                      )}
                    </div>
                  )}

                  {step === STEP_YRKEN && (
                    <>
                      {yrkenCount > 0 ? (
                        <div className="jp-wizard__groupcaption-row">
                          <span className="jp-wizard__groupcaption">
                            {t("yrken.selected", { count: yrkenCount })}
                          </span>
                        </div>
                      ) : (
                        <InfoNote>
                          {t.rich(
                            uploadedParsedId !== null
                              ? "yrken.empty"
                              : "yrken.emptyNoCv",
                            { b: (c) => <b>{c}</b> },
                          )}
                        </InfoNote>
                      )}
                      <OccupationSection
                        occupationFields={occupationFields}
                        selected={draftOccupations}
                        onToggle={(id) =>
                          setDraftOccupations((prev) => toggle(prev, id))
                        }
                        onReplace={(next) => setDraftOccupations(next)}
                        onClear={() => setDraftOccupations([])}
                        importCvHref={importCvHref}
                        idPrefix="match-rail-occ"
                        showHeading={false}
                        experienceByConceptId={draftOccupationExperience}
                        onExperienceChange={onOccupationExperienceChange}
                        onSeedExperience={seedOccupationExperience}
                        // Alltid på (paritet gamla wizarden): med en inline-uppladdad
                        // parsedResumeId föreslås ur staging-artefakten, annars ur
                        // ett ev. befordrat CV (latestRole) — /cv-vägen förlorar
                        // aldrig sitt CV-förslag.
                        autoSuggestFromCv
                        parsedResumeId={uploadedParsedId ?? undefined}
                      />
                    </>
                  )}

                  {step === STEP_KOMPETENSER && (
                    <>
                      {kompCount > 0 ? (
                        <div className="jp-wizard__groupcaption-row">
                          <span className="jp-wizard__groupcaption">
                            {t("komp.selected", { count: kompCount })}
                          </span>
                          <button
                            type="button"
                            className="jp-clearlink"
                            onClick={() => setDraftSkills([])}
                          >
                            {t("komp.clear")}
                          </button>
                        </div>
                      ) : (
                        <div className="jp-wizard__emptywrap">
                          <InfoNote>
                            {t.rich("komp.empty", { b: (c) => <b>{c}</b> })}
                          </InfoNote>
                          {uploadedParsedId !== null && (
                            <Button
                              type="button"
                              variant="secondary"
                              size="sm"
                              onClick={restoreSkillSuggestions}
                            >
                              {t("komp.restore")}
                            </Button>
                          )}
                        </div>
                      )}
                      <SkillSection
                        key={`match-rail-skill-${skillRemountKey}`}
                        selected={draftSkills}
                        onReplace={(next) => setDraftSkills(next)}
                        onClear={() => setDraftSkills([])}
                        idPrefix="match-rail-skill"
                        showHeading={false}
                        initialGroups={allSkillGroups}
                        onGroupsChange={setDiscoveredSkillGroups}
                        autoSuggestFromCv
                        parsedResumeId={uploadedParsedId ?? undefined}
                      />
                    </>
                  )}

                  {step === STEP_ORTER && (
                    <>
                      {orterCount > 0 ? (
                        <div className="jp-wizard__groupcaption-row">
                          <span className="jp-wizard__groupcaption">
                            {t("orter.selected", { count: orterCount })}
                          </span>
                        </div>
                      ) : (
                        <InfoNote>
                          {t.rich("orter.empty", { b: (c) => <b>{c}</b> })}
                        </InfoNote>
                      )}
                      <RegionMunicipalityCascade
                        regions={regions}
                        selectedRegions={draftRegions}
                        selectedMunicipalities={draftMunicipalities}
                        onChange={onOrtChange}
                        showHeading={false}
                        idPrefix="match-rail-ort"
                      />
                    </>
                  )}

                  {step === STEP_FORMER && (
                    <div className="jp-wizard__formercheck">
                      <FacetSection
                        title={t("rail.step.former")}
                        options={employmentOptions}
                        selected={draftEmployment}
                        onToggle={(id) =>
                          setDraftEmployment((prev) => toggle(prev, id))
                        }
                        onClear={() => setDraftEmployment([])}
                        pinnedAriaLabel={t("rail.step.former")}
                        showHeading={false}
                      />
                      <InfoNote>
                        {t.rich("former.note", { b: (c) => <b>{c}</b> })}
                      </InfoNote>
                    </div>
                  )}

                  {step === STEP_GRANSKA && (
                    <div className="jp-wizard__review">
                      <ReviewGroup
                        idSuffix="yrken"
                        caption={t("granska.captionYrken")}
                        editLabel={t("granska.edit")}
                        onEdit={() => goToStep(STEP_YRKEN)}
                        chips={occupationChips}
                        empty={t("granska.emptyYrken")}
                      />
                      <ReviewGroup
                        idSuffix="komp"
                        caption={t("granska.captionKompetenser")}
                        editLabel={t("granska.edit")}
                        onEdit={() => goToStep(STEP_KOMPETENSER)}
                        chips={skillChips}
                        empty={t("granska.emptyKomp")}
                      />
                      <ReviewGroup
                        idSuffix="orter"
                        caption={t("granska.captionOrter")}
                        editLabel={t("granska.edit")}
                        onEdit={() => goToStep(STEP_ORTER)}
                        chips={ortChips}
                        empty={t("granska.emptyOrter")}
                      />
                      <ReviewGroup
                        idSuffix="former"
                        caption={t("granska.captionFormer")}
                        editLabel={t("granska.edit")}
                        onEdit={() => goToStep(STEP_FORMER)}
                        chips={employmentChips}
                        empty={t("granska.emptyFormer")}
                      />
                    </div>
                  )}
                </section>
              </div>

              <div className="jp-wizard__foot">
                {step >= STEP_YRKEN && step <= STEP_GRANSKA && (
                  <Button
                    type="button"
                    variant="ghost"
                    onClick={onBack}
                    disabled={isSaving}
                  >
                    {t("nav.tillbaka")}
                  </Button>
                )}
                <span className="jp-wizard__foot-spacer" />
                {/* Mobil-räknaren är den polite live-regionen på mobil (rail-
                    kortet är display:none där). display:none på desktop → inert,
                    så bara en live-region per layout. */}
                <span
                  className="jp-wizard__mobilecount"
                  role="status"
                  aria-live="polite"
                >
                  <span
                    className="jp-wizard__mobilecount-dot"
                    aria-hidden="true"
                  />
                  {count === null ? (
                    <span aria-hidden="true">{"–"}</span>
                  ) : (
                    svNumber.format(count)
                  )}
                </span>
                {step <= STEP_FORMER && (
                  <button
                    type="button"
                    className="jp-wizard__skip"
                    onClick={onSkip}
                    disabled={isSaving}
                  >
                    {step === STEP_START
                      ? t("nav.gorSenare")
                      : t("nav.hoppaOver")}
                  </button>
                )}
                <Button type="button" onClick={onPrimary} disabled={isSaving}>
                  {step === STEP_GRANSKA
                    ? t("nav.spara")
                    : step === STEP_START
                      ? t("nav.fortsatt")
                      : t("nav.nasta")}
                </Button>
                {saveError && (
                  <p
                    role="alert"
                    className="jp-wizard__error text-body-sm text-danger-600"
                  >
                    {saveError}
                  </p>
                )}
              </div>
            </>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}

/** Info-notis (status-nivå): info-bg + info-ikon + text med fet inledning. Samma
 * roll som den befintliga `.jp-wizard__note` (återanvänd klass). */
function InfoNote({ children }: { readonly children: ReactNode }) {
  return (
    <p className="jp-wizard__note">
      <Info className="jp-wizard__note-icon" size={16} aria-hidden="true" />
      <span>{children}</span>
    </p>
  );
}

/** En granska-grupp: mono-caption + högerställd "Ändra"-länk (hoppar till steget)
 * + read-only-chips. Tom grupp skrivs ut som EN konsekvens-chip ("Hela landet"). */
function ReviewGroup({
  idSuffix,
  caption,
  editLabel,
  onEdit,
  chips,
  empty,
}: {
  readonly idSuffix: string;
  readonly caption: string;
  readonly editLabel: string;
  readonly onEdit: () => void;
  readonly chips: ReadonlyArray<{ readonly conceptId: string; readonly label: string }>;
  readonly empty: string;
}) {
  const headId = `match-rail-review-${idSuffix}`;
  return (
    <section className="jp-wizard__reviewgroup" aria-labelledby={headId}>
      <div className="jp-wizard__reviewhead">
        <span id={headId} className="jp-wizard__groupcaption">
          {caption}
        </span>
        <button type="button" className="jp-wizard__editlink" onClick={onEdit}>
          {editLabel}
        </button>
      </div>
      {chips.length === 0 ? (
        <span className="jp-chip">{empty}</span>
      ) : (
        <ul className="jp-chiplist">
          {chips.map((chip) => (
            <li key={chip.conceptId}>
              <span className="jp-chip">{chip.label}</span>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
