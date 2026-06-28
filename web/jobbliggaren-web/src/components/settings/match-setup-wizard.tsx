"use client";

// "use client": wizarden håller klient-only steg-state (vilket steg, draft för
// tre dimensioner), programmatisk fokus-flytt till stegrubriken vid stegbyte
// (WCAG 2.4.3), och en useTransition runt det enda save-anropet. URL ändras
// ALDRIG mid-wizard (ephemer formulär-state, ej deep-linkbar — ADR 0077 Alt D
// förkastad). Inget av detta går i en Server Component.

import { useRef, useState, useTransition } from "react";
import { useTranslations } from "next-intl";
import { Info } from "lucide-react";
import { Dialog, DialogContent, DialogTitle, DialogDescription } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import type {
  TaxonomyOccupationField,
  TaxonomyOption,
  TaxonomyRegion,
} from "@/lib/dto/taxonomy";
import { updateMatchPreferencesAction } from "@/lib/actions/match-preferences";
import {
  flattenOccupationGroups,
  labelsForSelected,
  projectOccupationExperience,
  recordFromOccupationExperience,
  toggle,
  type Option,
} from "./match-preferences-shared";
import { OccupationSection } from "./occupation-section";
import { SkillSection } from "./skill-section";
import { FacetSection } from "./facet-section";
import { RegionMunicipalityCascade } from "./region-municipality-cascade";
import { PreferenceChip } from "./preference-chip";
import type { OrtSelection } from "@/lib/job-ads/ort-selection";

interface MatchSetupWizardProps {
  readonly open: boolean;
  readonly onOpenChange: (open: boolean) => void;
  readonly occupationFields: ReadonlyArray<TaxonomyOccupationField>;
  readonly regions: ReadonlyArray<TaxonomyRegion>;
  readonly employmentTypes: ReadonlyArray<TaxonomyOption>;
  /** Persisterad SSOT — wizardens draft seedas från den vid öppning. */
  readonly persistedOccupationGroups: ReadonlyArray<string>;
  readonly persistedRegions: ReadonlyArray<string>;
  /** Spår 3 PR-D: kommun-axeln (pre-fill för ort-kaskaden). */
  readonly persistedMunicipalities: ReadonlyArray<string>;
  readonly persistedEmploymentTypes: ReadonlyArray<string>;
  /** STEG 3 / ADR 0079: kompetens-axeln (pre-fill för kompetens-steget). */
  readonly persistedSkills: ReadonlyArray<string>;
  /**
   * exp-per-occ (ADR 0079-amendment PR-4): den persisterade per-yrke-
   * erfarenhets-overlayn (en gles delmängd av `persistedOccupationGroups`).
   * Seedar `draftOccupationExperience` vid öppning; ett användar-/persisterat
   * värde vinner över CV-förslagets `approximateYears`. `years` är `null` när
   * angivet men ospecificerat. Ersätter den tidigare profil-nivå-erfarenheten
   * (Klas-beslut: ExperienceField borttaget ur wizarden).
   */
  readonly persistedOccupationExperience: ReadonlyArray<{
    readonly conceptId: string;
    readonly years: number | null;
  }>;
  /**
   * STEG 3 / ADR 0079: labels för redan-sparade kompetens-concept-id (den
   * platta skill-taxonomin skickas aldrig som träd → värden läser tillbaka
   * labels när de finns; saknade faller tillbaka på id-strängen).
   */
  readonly persistedSkillLabels?: ReadonlyArray<Option>;
  /** Anropas efter lyckad save med den sparade fulla mängden. */
  readonly onSaved?: (saved: {
    occupations: ReadonlyArray<string>;
    regions: ReadonlyArray<string>;
    municipalities: ReadonlyArray<string>;
    employment: ReadonlyArray<string>;
    skills: ReadonlyArray<string>;
    // exp-per-occ (ADR 0079-amendment PR-4): den sparade per-yrke-overlayn
    // (scopad till valda yrken). Ersätter `experienceYears` (profil-nivå borttaget).
    occupationExperience: ReadonlyArray<{
      readonly conceptId: string;
      readonly years: number | null;
    }>;
  }) => void;
  /** CV-importflödets route (tom-state-länken i yrkes-steget). */
  readonly importCvHref: string;
  /**
   * Fas 4 onboarding (CTO Variant B): id för det just uppladdade `parsed_resume`:t
   * (welcome-flödet). Vidarebefordras till yrkes-steget så CV-förslaget läses ur
   * staging-artefakten i stället för ur ett ännu-icke-promotat Resume. Utelämnat från
   * `/cv`- och `/installningar`-ingångarna (de har ett promotat Resume → latestRole-vägen).
   */
  readonly parsedResumeId?: string;
  /**
   * STEG 1 / ADR 0079 — welcome-flödet befordrar CV:t FÖRE wizarden, vilket
   * raderar staging-artefakten som bär de rika multi-signal-yrkesförslagen
   * (utbildning+erfarenhet, #145). Welcome-modalen förhämtar därför förslagen
   * innan promote och bär in dem hit. När satt seedas draften med dessa förslag
   * och OccupationSection auto-föreslår INTE (skulle annars dubbel-läsa den
   * svagare latestRole-vägen). Utelämnad → oförändrat beteende.
   */
  readonly proposedOccupationGroups?: ReadonlyArray<string>;
  /**
   * STEG 3 / ADR 0079 — welcome-flödet befordrar CV:t FÖRE wizarden, vilket
   * raderar staging-artefakten som bär CV-kompetens-förslagen. Welcome-modalen
   * förhämtar dem (med labels, {@link Option}) innan promote och bär in dem hit.
   * När satt seedas kompetens-draften med dessa förslag och SkillSection
   * auto-föreslår INTE (staging-artefakten finns inte längre). Utelämnad →
   * oförändrat (inget CV-förslag i kompetens-steget).
   */
  readonly proposedSkills?: ReadonlyArray<Option>;
}

const TOTAL_STEPS = 5;

/**
 * #251 (A4) — AF-taxonomins "Vanlig anställning"-concept-id (FRUSEN, legally-
 * stable referensnod; klass2-taxonomy.json / ADR 0043-amendment). De flesta
 * annonser ligger under denna breda bucket, så en användare som väljer en
 * smalare anställningstyp (t.ex. Tillsvidareanställning) men INTE Vanlig
 * anställning riskerar att missa träffar. Concept-id (ej label) är den stabila
 * nyckeln: FE får relabela presentations-labeln (klass2-taxonomy.json-noten),
 * men concept-id:t är fruset. Driver den icke-blockerande noten i steg 4.
 */
const VANLIG_ANSTALLNING_CONCEPT_ID = "PFZr_Syz_cUq";

/**
 * Match-setup-wizard (ADR 0077 STEG 5) — en skippbar fyra-stegs wide
 * standardmodal som bygger matchnings-profilen ur CV:t. Skriver ENBART till
 * `MatchPreferences` via det befintliga PUT:et, vid sista stegets "Spara
 * matchning" (propose-and-approve, ADR 0040/0071 — inget skrivs innan dess).
 * Utbildnings-steg är OUT v1 (Klas-fork; `MatchPreferences` saknar dimension).
 */
export function MatchSetupWizard({
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
  persistedOccupationExperience,
  persistedSkillLabels = [],
  onSaved,
  importCvHref,
  parsedResumeId,
  proposedOccupationGroups,
  proposedSkills,
}: MatchSetupWizardProps) {
  const t = useTranslations("settings");
  // Stegens konkreta substantiv-rubriker (design-bind A2.i), via katalogen.
  const stepTitles: ReadonlyArray<string> = [
    t("matchPrefs.wizard.stepOccupations"),
    t("matchPrefs.wizard.stepSkills"),
    t("matchPrefs.wizard.stepOrter"),
    t("matchPrefs.wizard.stepEmployment"),
    t("matchPrefs.wizard.stepDone"),
  ];
  const regionOptions: ReadonlyArray<Option> = regions.map((r) => ({
    conceptId: r.conceptId,
    label: r.label,
  }));
  // Kommun-options (flatten av länens kommuner) för review-stegets chip-labels.
  const municipalityOptions: ReadonlyArray<Option> = regions.flatMap((r) =>
    r.municipalities.map((m) => ({ conceptId: m.conceptId, label: m.label }))
  );
  const employmentOptions: ReadonlyArray<Option> = employmentTypes.map((e) => ({
    conceptId: e.conceptId,
    label: e.label,
  }));

  // 1-indexerat steg (1..TOTAL_STEPS). Klient-only — ingen route per steg.
  const [step, setStep] = useState(1);

  // Draft-state per dimension, seedat från SSOT vid öppning.
  const [seededFor, setSeededFor] = useState(false);
  const [draftOccupations, setDraftOccupations] = useState<ReadonlyArray<string>>(
    persistedOccupationGroups
  );
  const [draftRegions, setDraftRegions] = useState<ReadonlyArray<string>>(
    persistedRegions
  );
  const [draftMunicipalities, setDraftMunicipalities] = useState<
    ReadonlyArray<string>
  >(persistedMunicipalities);
  const [draftEmployment, setDraftEmployment] = useState<ReadonlyArray<string>>(
    persistedEmploymentTypes
  );
  const [draftSkills, setDraftSkills] = useState<ReadonlyArray<string>>(
    persistedSkills
  );
  // exp-per-occ (ADR 0079-amendment PR-4): per-yrke-erfarenhets-overlay (draft),
  // keyad på yrkesgrupp-concept-id. `null` = angiven men ospecificerad / tomt
  // fält; `0` = noll år (skilda). Seedas vid öppning från persisterad overlay
  // UNION CV-förslagets år (persisterat vinner). Ersätter den tidigare profil-
  // nivå-erfarenheten (Klas-beslut: ExperienceField borttaget ur wizarden).
  const [draftOccupationExperience, setDraftOccupationExperience] = useState<
    Readonly<Record<string, number | null>>
  >(() => recordFromOccupationExperience(persistedOccupationExperience));

  // Labels för kompetens-chips: persisterade labels UNION welcome-förslagens
  // labels (den platta skill-taxonomin skickas aldrig som träd, så SkillSection
  // saknar annars uppslagning för en seedad chip).
  const skillSeedLabels: ReadonlyArray<Option> = [
    ...persistedSkillLabels,
    ...(proposedSkills ?? []),
  ];

  const [isSaving, startSaving] = useTransition();
  const [saveError, setSaveError] = useState<string | null>(null);

  // Fokus-flytt till stegrubriken vid stegbyte (WCAG 2.4.3). Sätts via callback-
  // ref och flyttas i en queueMicrotask EFTER commit — aldrig under render.
  const titleRef = useRef<HTMLHeadingElement | null>(null);

  // Seed/återställ vid öppning; nollställ vid stängning (render-tids-flagga,
  // set-state-in-effect-fri).
  if (open && !seededFor) {
    setSeededFor(true);
    setStep(1);
    // STEG 1 / ADR 0079: seeda med persisterad SSOT UNION welcome-flödets
    // förhämtade CV-förslag (de bärs in när staging-artefakten redan promotats).
    setDraftOccupations([
      ...new Set([
        ...persistedOccupationGroups,
        ...(proposedOccupationGroups ?? []),
      ]),
    ]);
    setDraftRegions(persistedRegions);
    setDraftMunicipalities(persistedMunicipalities);
    setDraftEmployment(persistedEmploymentTypes);
    // STEG 3 / ADR 0079: seed med persisterad SSOT UNION welcome-flödets
    // förhämtade CV-kompetens-förslag (de bärs in när staging-artefakten redan
    // promotats). Erfarenhet seedas direkt (ingen förslags-väg).
    setDraftSkills([
      ...new Set([
        ...persistedSkills,
        ...(proposedSkills ?? []).map((s) => s.conceptId),
      ]),
    ]);
    // exp-per-occ (ADR 0079-amendment PR-4): seed from the persisted overlay.
    // The CV-derived years arrive asynchronously through OccupationSection's
    // `onSeedExperience` (the parsed-resume / latestRole reads) and are merged
    // in WITHOUT overwriting a persisted/user value (`seedOccupationExperience`
    // below). So a user/persisted value wins over the CV-derived one.
    setDraftOccupationExperience(recordFromOccupationExperience(persistedOccupationExperience));
    setSaveError(null);
  }
  if (!open && seededFor) {
    setSeededFor(false);
  }

  function focusTitle() {
    // Flytta fokus till stegrubriken EFTER commit (WCAG 2.4.3). Ref via den
    // shadcn-wrappade DialogTitle (React 19 ref-as-prop → vidare till radix
    // Title); DOM-fallback om wrappern inte vidarebefordrar ref.
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

  // Ort-kaskaden emitterar HELA ort-paret (region + kommun) i ett anrop —
  // föräldern speglar det i två draft-states, men de submittas atomiskt (NOTE-1).
  function onOrtChange(next: OrtSelection) {
    setDraftRegions(next.region);
    setDraftMunicipalities(next.municipality);
  }

  // exp-per-occ (ADR 0079-amendment PR-4): användaren ändrade ett yrkes år.
  // `null` (tomt fält) lagras explicit (skilt från "ingen nyckel"). Yrket
  // antas redan vara i draften (fältet renderas bara för pinnade yrken).
  function onOccupationExperienceChange(conceptId: string, years: number | null) {
    setDraftOccupationExperience((prev) => ({ ...prev, [conceptId]: years }));
  }

  // exp-per-occ (ADR 0079-amendment PR-4): CV-förslaget seedar härledda år.
  // Mergas in MEN skriver ALDRIG över ett befintligt värde (persisterat ELLER
  // tidigare användar-angivet) — ett användar-/persisterat värde vinner över
  // det CV-härledda. `0` och `null` bevaras skilt (CV-deriverad delårsroll vs
  // ej angivet). Endast yrken utan befintlig nyckel tar emot seed-värdet.
  function seedOccupationExperience(seed: Readonly<Record<string, number | null>>) {
    setDraftOccupationExperience((prev) => {
      const next = { ...prev };
      for (const [conceptId, years] of Object.entries(seed)) {
        if (!(conceptId in next)) next[conceptId] = years;
      }
      return next;
    });
  }

  function onSave() {
    setSaveError(null);
    // exp-per-occ (ADR 0079-amendment PR-4): projicera overlayn till
    // `{conceptId, years}[]` ENBART för yrken som fortfarande är valda. Att ta
    // bort ett yrke tar därmed bort dess overlay-rad (respekterar subset-regeln:
    // backend avvisar en rad vars conceptId inte finns i preferredOccupationGroups).
    const occupationExperience = projectOccupationExperience(
      draftOccupationExperience,
      draftOccupations
    );
    startSaving(async () => {
      const result = await updateMatchPreferencesAction({
        preferredOccupationGroups: [...draftOccupations],
        // Region + kommun submittas atomiskt i samma full-replace-PUT (NOTE-1).
        preferredRegions: [...draftRegions],
        preferredMunicipalities: [...draftMunicipalities],
        preferredEmploymentTypes: [...draftEmployment],
        // STEG 3 / ADR 0079: kompetens i SAMMA full-replace-PUT — annars skulle
        // ett spar av en annan dimension nolla det (page-wipe).
        preferredSkills: [...draftSkills],
        // exp-per-occ PR-4: per-yrke-overlayn (full-replace, scopad till valda
        // yrken). Profil-nivå `experienceYears` skickas INTE längre från wizarden
        // (Klas-beslut: ExperienceField borttaget ur wizarden).
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
        onOpenChange(false);
      } else {
        setSaveError(result.error);
      }
    });
  }

  const isLastStep = step === TOTAL_STEPS;
  const isFirstStep = step === 1;

  function onPrimary() {
    if (isLastStep) {
      onSave();
    } else {
      goToStep(step + 1);
    }
  }

  function onBack() {
    if (!isFirstStep) goToStep(step - 1);
  }

  // "Hoppa över det här steget": gå framåt utan att röra draften (tomt = ärligt
  // "ej angivet", ADR 0076). På sista steget finns ingen skip (det ÄR klart-
  // steget); skip ≠ nästa, så den primära knappen bär aldrig skip-semantiken.
  function onSkip() {
    if (!isLastStep) goToStep(step + 1);
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="jp-stdmodal jp-stdmodal--wide">
        <div className="jp-wizard__head">
          <span className="jp-wizard__counter">
            {t("matchPrefs.wizard.counter", { step, total: TOTAL_STEPS })}
          </span>
          <DialogTitle
            ref={titleRef}
            tabIndex={-1}
            id={`match-wizard-step-${step}-head`}
            className="jp-wizard__title"
          >
            {stepTitles[step - 1]}
          </DialogTitle>
          <DialogDescription className="jp-wizard__intro">
            {stepIntro(t, step)}
          </DialogDescription>
          {/* Stäng = inbyggd radix Close (civic-restylad). Esc stänger hela
              wizarden utan bekräftelse — inget skrivs förrän "Spara matchning". */}
        </div>

        {step === 5 ? (
          <ReviewStep
            labels={{
              occupationsTitle: t("matchPrefs.facetOccupations"),
              skillsTitle: t("matchPrefs.facetSkills"),
              orterTitle: t("matchPrefs.facetOrter"),
              employmentTitle: t("matchPrefs.facetEmployment"),
              occupationsEmpty: t("matchPrefs.emptyOccupations"),
              skillsEmpty: t("matchPrefs.emptySkills"),
              orterEmpty: t("matchPrefs.emptyOrter"),
              employmentEmpty: t("matchPrefs.emptyEmployment"),
              occupationsAria: t("matchPrefs.selectedOccupations"),
              skillsAria: t("matchPrefs.selectedSkills"),
              orterAria: t("matchPrefs.selectedOrter"),
              employmentAria: t("matchPrefs.selectedEmployment"),
              // exp-per-occ (ADR 0079-amendment PR-4): per-yrke-år på review-raden.
              occupationYearsEmpty: t("matchPrefs.occupation.reviewYearsEmpty"),
            }}
            occupations={labelsForSelected(draftOccupations, flattenOccupationGroups(occupationFields))}
            occupationExperience={draftOccupationExperience}
            occupationYearsValue={(years) =>
              t("matchPrefs.occupation.reviewYearsValue", { years })
            }
            skills={labelsForSelected(draftSkills, skillSeedLabels)}
            orter={[
              ...labelsForSelected(draftRegions, regionOptions),
              ...labelsForSelected(draftMunicipalities, municipalityOptions),
            ]}
            employment={labelsForSelected(draftEmployment, employmentOptions)}
            onRemoveOccupation={(id) =>
              setDraftOccupations((prev) => toggle(prev, id))
            }
            onRemoveSkill={(id) => setDraftSkills((prev) => toggle(prev, id))}
            // En ort-chip kan vara län ELLER kommun — ta bort ur rätt axel.
            onRemoveOrt={(id) => {
              setDraftRegions((prev) => prev.filter((r) => r !== id));
              setDraftMunicipalities((prev) => prev.filter((m) => m !== id));
            }}
            onRemoveEmployment={(id) =>
              setDraftEmployment((prev) => toggle(prev, id))
            }
          />
        ) : (
          <div className="jp-wizard__step">
            <section
              role="group"
              aria-labelledby={`match-wizard-step-${step}-head`}
            >
              {step === 1 && (
                <OccupationSection
                  occupationFields={occupationFields}
                  selected={draftOccupations}
                  onToggle={(id) => setDraftOccupations((prev) => toggle(prev, id))}
                  onReplace={(next) => setDraftOccupations(next)}
                  onClear={() => setDraftOccupations([])}
                  importCvHref={importCvHref}
                  idPrefix="match-wizard-occ"
                  showHeading={false}
                  // exp-per-occ (ADR 0079-amendment PR-4): per-yrke-år bor i
                  // draft-overlayn. CV-förslagets `approximateYears` seedas in via
                  // onSeedExperience (utan att skriva över ett befintligt värde).
                  experienceByConceptId={draftOccupationExperience}
                  onExperienceChange={onOccupationExperienceChange}
                  onSeedExperience={seedOccupationExperience}
                  // Förhämtade förslag (welcome-flödet, STEG 1) → draften är redan
                  // seedad, så auto-suggest skulle bara dubbel-läsa den svagare
                  // latestRole-vägen. Annars oförändrat (auto-suggest på).
                  autoSuggestFromCv={proposedOccupationGroups === undefined}
                  parsedResumeId={
                    proposedOccupationGroups === undefined
                      ? parsedResumeId
                      : undefined
                  }
                />
              )}
              {step === 2 && (
                // exp-per-occ (ADR 0079-amendment PR-4, Klas-beslut): den
                // profil-nivå ExperienceField är BORTTAGEN ur steg 2 — erfarenhet
                // anges nu per yrke (på yrkes-chippen i steg 1).
                <SkillSection
                  selected={draftSkills}
                  onToggle={(id) => setDraftSkills((prev) => toggle(prev, id))}
                  onReplace={(next) => setDraftSkills(next)}
                  onClear={() => setDraftSkills([])}
                  idPrefix="match-wizard-skill"
                  showHeading={false}
                  initialLabels={skillSeedLabels}
                  // Förhämtade förslag (welcome-flödet) → draften är redan
                  // seedad, staging-artefakten finns inte längre → ingen
                  // auto-suggest. Annars läser parsed-vägen (just uppladdat CV).
                  autoSuggestFromCv={proposedSkills === undefined}
                  parsedResumeId={
                    proposedSkills === undefined ? parsedResumeId : undefined
                  }
                />
              )}
              {step === 3 && (
                <RegionMunicipalityCascade
                  regions={regions}
                  selectedRegions={draftRegions}
                  selectedMunicipalities={draftMunicipalities}
                  onChange={onOrtChange}
                  showHeading={false}
                  idPrefix="match-wizard-ort"
                />
              )}
              {step === 4 && (
                <>
                  <FacetSection
                    title={t("matchPrefs.facetEmployment")}
                    options={employmentOptions}
                    selected={draftEmployment}
                    onToggle={(id) => setDraftEmployment((prev) => toggle(prev, id))}
                    onClear={() => setDraftEmployment([])}
                    pinnedAriaLabel={t("matchPrefs.selectedEmployment")}
                    showHeading={false}
                  />
                  {/* #251 (A4) — icke-blockerande not när användaren smalnat av
                      valet men utelämnat Vanlig anställning (där de flesta jobb
                      ligger). Tomt val = ärligt "alla" (ADR 0076) → ingen not.
                      Live-region-sloten är ALLTID monterad så en skärmläsare
                      annonserar noten när den dyker upp (polite). Påverkar aldrig
                      Nästa/Spara (propose-and-approve: inget skrivs förrän PUT). */}
                  <div
                    className="jp-wizard__note-slot"
                    role="status"
                    aria-live="polite"
                  >
                    {draftEmployment.length > 0 &&
                      !draftEmployment.includes(VANLIG_ANSTALLNING_CONCEPT_ID) && (
                        <p className="jp-wizard__note">
                          <Info
                            className="jp-wizard__note-icon"
                            size={16}
                            aria-hidden="true"
                          />
                          <span>{t("matchPrefs.wizard.employmentVanligNote")}</span>
                        </p>
                      )}
                  </div>
                </>
              )}
            </section>
          </div>
        )}

        <div className="jp-wizard__foot">
          {!isFirstStep && (
            <Button
              type="button"
              variant="ghost"
              onClick={onBack}
              disabled={isSaving}
            >
              {t("matchPrefs.wizard.back")}
            </Button>
          )}
          <span className="jp-wizard__foot-spacer" />
          {!isLastStep && (
            <button
              type="button"
              className="jp-wizard__skip"
              onClick={onSkip}
              disabled={isSaving}
            >
              {t("matchPrefs.wizard.skipStep")}
            </button>
          )}
          <Button type="button" onClick={onPrimary} disabled={isSaving}>
            {isLastStep
              ? isSaving
                ? t("matchPrefs.wizard.saving")
                : t("matchPrefs.wizard.save")
              : t("matchPrefs.wizard.next")}
          </Button>
          {saveError && (
            <p role="alert" className="text-body-sm text-danger-600 jp-wizard__error">
              {saveError}
            </p>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}

/** next-intl-translatorn för "settings"-namespacet (synkron i klient/sync RSC). */
type SettingsTranslator = ReturnType<typeof useTranslations<"settings">>;

/** Stegets hjälptext (bär instruktionen — aldrig placeholder-exempel). */
function stepIntro(t: SettingsTranslator, step: number): string {
  switch (step) {
    case 1:
      return t("matchPrefs.wizard.introOccupations");
    case 2:
      return t("matchPrefs.wizard.introSkills");
    case 3:
      return t("matchPrefs.wizard.introOrter");
    case 4:
      return t("matchPrefs.wizard.introEmployment");
    default:
      return t("matchPrefs.wizard.introReview");
  }
}

/**
 * Klart-/bekräfta-steget (steg 5): granska de valda chipsen per dimension.
 * Borttagbara (PreferenceChip) — sista chansen att rensa innan save. Tom
 * dimension visar en ärlig "inget valt"-rad (ej fejkat).
 *
 * exp-per-occ (ADR 0079-amendment PR-4): den separata erfarenhets-review-
 * sektionen är BORTTAGEN — i stället visar yrkes-facetten varje yrkes år PÅ
 * dess rad (ärlig "år ej angivna" när null/ej angivet).
 */
interface ReviewLabels {
  readonly occupationsTitle: string;
  readonly skillsTitle: string;
  readonly orterTitle: string;
  readonly employmentTitle: string;
  readonly occupationsEmpty: string;
  readonly skillsEmpty: string;
  readonly orterEmpty: string;
  readonly employmentEmpty: string;
  readonly occupationsAria: string;
  readonly skillsAria: string;
  readonly orterAria: string;
  readonly employmentAria: string;
  /** exp-per-occ PR-4: "år ej angivna"-raden när ett yrke saknar år. */
  readonly occupationYearsEmpty: string;
}

function ReviewStep({
  labels,
  occupations,
  occupationExperience,
  occupationYearsValue,
  skills,
  orter,
  employment,
  onRemoveOccupation,
  onRemoveSkill,
  onRemoveOrt,
  onRemoveEmployment,
}: {
  readonly labels: ReviewLabels;
  readonly occupations: ReadonlyArray<Option>;
  /** exp-per-occ PR-4: per-yrke-overlay (draft) keyad på concept-id. */
  readonly occupationExperience: Readonly<Record<string, number | null>>;
  /** exp-per-occ PR-4: "{years} år"-formatteraren (i18n, decimal-fri int). */
  readonly occupationYearsValue: (years: number) => string;
  readonly skills: ReadonlyArray<Option>;
  readonly orter: ReadonlyArray<Option>;
  readonly employment: ReadonlyArray<Option>;
  readonly onRemoveOccupation: (conceptId: string) => void;
  readonly onRemoveSkill: (conceptId: string) => void;
  readonly onRemoveOrt: (conceptId: string) => void;
  readonly onRemoveEmployment: (conceptId: string) => void;
}) {
  return (
    <div className="jp-wizard__step">
      {/* exp-per-occ PR-4: yrkes-facetten visar varje yrkes år på sin rad. */}
      <ReviewOccupationFacet
        title={labels.occupationsTitle}
        empty={labels.occupationsEmpty}
        chips={occupations}
        experienceByConceptId={occupationExperience}
        yearsValue={occupationYearsValue}
        yearsEmpty={labels.occupationYearsEmpty}
        onRemove={onRemoveOccupation}
        ariaLabel={labels.occupationsAria}
      />
      <ReviewFacet
        title={labels.skillsTitle}
        empty={labels.skillsEmpty}
        chips={skills}
        onRemove={onRemoveSkill}
        ariaLabel={labels.skillsAria}
      />
      <ReviewFacet
        title={labels.orterTitle}
        empty={labels.orterEmpty}
        chips={orter}
        onRemove={onRemoveOrt}
        ariaLabel={labels.orterAria}
      />
      <ReviewFacet
        title={labels.employmentTitle}
        empty={labels.employmentEmpty}
        chips={employment}
        onRemove={onRemoveEmployment}
        ariaLabel={labels.employmentAria}
      />
    </div>
  );
}

/**
 * exp-per-occ (ADR 0079-amendment PR-4): yrkes-facetten på review-steget — som
 * ReviewFacet men varje rad bär yrkes-chippen + dess ungefärliga år ("{years}
 * år" eller ärlig "år ej angivna" när null/ej angivet). Ingen magnitud-viz —
 * bara text (det är en preferens, aldrig en poäng). `0` renderar "0 år" (skilt
 * från ej angivet).
 */
function ReviewOccupationFacet({
  title,
  empty,
  chips,
  experienceByConceptId,
  yearsValue,
  yearsEmpty,
  onRemove,
  ariaLabel,
}: {
  readonly title: string;
  readonly empty: string;
  readonly chips: ReadonlyArray<Option>;
  readonly experienceByConceptId: Readonly<Record<string, number | null>>;
  readonly yearsValue: (years: number) => string;
  readonly yearsEmpty: string;
  readonly onRemove: (conceptId: string) => void;
  readonly ariaLabel: string;
}) {
  const headId = `match-wizard-review-${ariaLabel.replace(/\s+/g, "-")}`;
  return (
    <section
      className="jp-matchdialog__section"
      role="group"
      aria-labelledby={headId}
    >
      <div className="jp-matchdialog__sectionhead">
        <span id={headId} className="jp-popover__title">
          {title}
        </span>
      </div>
      {chips.length === 0 ? (
        <p className="text-body-sm text-text-secondary">{empty}</p>
      ) : (
        <ul className="jp-occexp" aria-label={ariaLabel}>
          {chips.map((chip) => {
            const years = experienceByConceptId[chip.conceptId];
            // `0` är ett giltigt skilt värde → visa "0 år"; null/undefined → ärlig
            // "år ej angivna". (`years ?? null` normaliserar undefined→null.)
            const hasYears = years !== null && years !== undefined;
            return (
              <li key={chip.conceptId} className="jp-occexp__row">
                <span className="jp-occexp__chip">
                  <PreferenceChip
                    label={chip.label}
                    onRemove={() => onRemove(chip.conceptId)}
                  />
                </span>
                <span className="jp-occexp__years-label">
                  {hasYears ? yearsValue(years) : yearsEmpty}
                </span>
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}

function ReviewFacet({
  title,
  empty,
  chips,
  onRemove,
  ariaLabel,
}: {
  readonly title: string;
  readonly empty: string;
  readonly chips: ReadonlyArray<Option>;
  readonly onRemove: (conceptId: string) => void;
  readonly ariaLabel: string;
}) {
  const headId = `match-wizard-review-${ariaLabel.replace(/\s+/g, "-")}`;
  return (
    <section
      className="jp-matchdialog__section"
      role="group"
      aria-labelledby={headId}
    >
      <div className="jp-matchdialog__sectionhead">
        <span id={headId} className="jp-popover__title">
          {title}
        </span>
      </div>
      {chips.length === 0 ? (
        <p className="text-body-sm text-text-secondary">{empty}</p>
      ) : (
        <ul className="jp-chiplist" aria-label={ariaLabel}>
          {chips.map((chip) => (
            <li key={chip.conceptId}>
              <PreferenceChip
                label={chip.label}
                onRemove={() => onRemove(chip.conceptId)}
              />
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
