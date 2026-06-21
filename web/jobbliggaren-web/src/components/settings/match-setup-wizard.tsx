"use client";

// "use client": wizarden håller klient-only steg-state (vilket steg, draft för
// tre dimensioner), programmatisk fokus-flytt till stegrubriken vid stegbyte
// (WCAG 2.4.3), och en useTransition runt det enda save-anropet. URL ändras
// ALDRIG mid-wizard (ephemer formulär-state, ej deep-linkbar — ADR 0077 Alt D
// förkastad). Inget av detta går i en Server Component.

import { useRef, useState, useTransition } from "react";
import { useTranslations } from "next-intl";
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
  toggle,
  type Option,
} from "./match-preferences-shared";
import { OccupationSection } from "./occupation-section";
import { FacetSection } from "./facet-section";
import { PreferenceChip } from "./preference-chip";

interface MatchSetupWizardProps {
  readonly open: boolean;
  readonly onOpenChange: (open: boolean) => void;
  readonly occupationFields: ReadonlyArray<TaxonomyOccupationField>;
  readonly regions: ReadonlyArray<TaxonomyRegion>;
  readonly employmentTypes: ReadonlyArray<TaxonomyOption>;
  /** Persisterad SSOT — wizardens draft seedas från den vid öppning. */
  readonly persistedOccupationGroups: ReadonlyArray<string>;
  readonly persistedRegions: ReadonlyArray<string>;
  readonly persistedEmploymentTypes: ReadonlyArray<string>;
  /** Anropas efter lyckad save med den sparade fulla mängden. */
  readonly onSaved?: (saved: {
    occupations: ReadonlyArray<string>;
    regions: ReadonlyArray<string>;
    employment: ReadonlyArray<string>;
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
}

const TOTAL_STEPS = 4;

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
  persistedEmploymentTypes,
  onSaved,
  importCvHref,
  parsedResumeId,
}: MatchSetupWizardProps) {
  const t = useTranslations("settings");
  // Stegens konkreta substantiv-rubriker (design-bind A2.i), via katalogen.
  const stepTitles: ReadonlyArray<string> = [
    t("matchPrefs.wizard.stepOccupations"),
    t("matchPrefs.wizard.stepRegions"),
    t("matchPrefs.wizard.stepEmployment"),
    t("matchPrefs.wizard.stepDone"),
  ];
  const regionOptions: ReadonlyArray<Option> = regions.map((r) => ({
    conceptId: r.conceptId,
    label: r.label,
  }));
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
  const [draftEmployment, setDraftEmployment] = useState<ReadonlyArray<string>>(
    persistedEmploymentTypes
  );

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
    setDraftOccupations(persistedOccupationGroups);
    setDraftRegions(persistedRegions);
    setDraftEmployment(persistedEmploymentTypes);
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

  function onSave() {
    setSaveError(null);
    startSaving(async () => {
      const result = await updateMatchPreferencesAction({
        preferredOccupationGroups: [...draftOccupations],
        preferredRegions: [...draftRegions],
        preferredEmploymentTypes: [...draftEmployment],
      });
      if (result.success) {
        onSaved?.({
          occupations: draftOccupations,
          regions: draftRegions,
          employment: draftEmployment,
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

        {step === 4 ? (
          <ReviewStep
            labels={{
              occupationsTitle: t("matchPrefs.facetOccupations"),
              regionsTitle: t("matchPrefs.facetRegions"),
              employmentTitle: t("matchPrefs.facetEmployment"),
              occupationsEmpty: t("matchPrefs.emptyOccupations"),
              regionsEmpty: t("matchPrefs.emptyRegions"),
              employmentEmpty: t("matchPrefs.emptyEmployment"),
              occupationsAria: t("matchPrefs.selectedOccupations"),
              regionsAria: t("matchPrefs.selectedRegions"),
              employmentAria: t("matchPrefs.selectedEmployment"),
            }}
            occupations={labelsForSelected(draftOccupations, flattenOccupationGroups(occupationFields))}
            regions={labelsForSelected(draftRegions, regionOptions)}
            employment={labelsForSelected(draftEmployment, employmentOptions)}
            onRemoveOccupation={(id) =>
              setDraftOccupations((prev) => toggle(prev, id))
            }
            onRemoveRegion={(id) => setDraftRegions((prev) => toggle(prev, id))}
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
                  autoSuggestFromCv
                  parsedResumeId={parsedResumeId}
                />
              )}
              {step === 2 && (
                <FacetSection
                  title={t("matchPrefs.facetRegions")}
                  options={regionOptions}
                  selected={draftRegions}
                  onToggle={(id) => setDraftRegions((prev) => toggle(prev, id))}
                  onClear={() => setDraftRegions([])}
                  pinnedAriaLabel={t("matchPrefs.selectedRegions")}
                  showHeading={false}
                />
              )}
              {step === 3 && (
                <FacetSection
                  title={t("matchPrefs.facetEmployment")}
                  options={employmentOptions}
                  selected={draftEmployment}
                  onToggle={(id) => setDraftEmployment((prev) => toggle(prev, id))}
                  onClear={() => setDraftEmployment([])}
                  pinnedAriaLabel={t("matchPrefs.selectedEmployment")}
                  showHeading={false}
                />
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
      return t("matchPrefs.wizard.introRegions");
    case 3:
      return t("matchPrefs.wizard.introEmployment");
    default:
      return t("matchPrefs.wizard.introReview");
  }
}

/**
 * Klart-/bekräfta-steget (steg 4): granska de valda chipsen per dimension.
 * Borttagbara (PreferenceChip) — sista chansen att rensa innan save. Tom
 * dimension visar en ärlig "inget valt"-rad (ej fejkat).
 */
interface ReviewLabels {
  readonly occupationsTitle: string;
  readonly regionsTitle: string;
  readonly employmentTitle: string;
  readonly occupationsEmpty: string;
  readonly regionsEmpty: string;
  readonly employmentEmpty: string;
  readonly occupationsAria: string;
  readonly regionsAria: string;
  readonly employmentAria: string;
}

function ReviewStep({
  labels,
  occupations,
  regions,
  employment,
  onRemoveOccupation,
  onRemoveRegion,
  onRemoveEmployment,
}: {
  readonly labels: ReviewLabels;
  readonly occupations: ReadonlyArray<Option>;
  readonly regions: ReadonlyArray<Option>;
  readonly employment: ReadonlyArray<Option>;
  readonly onRemoveOccupation: (conceptId: string) => void;
  readonly onRemoveRegion: (conceptId: string) => void;
  readonly onRemoveEmployment: (conceptId: string) => void;
}) {
  return (
    <div className="jp-wizard__step">
      <ReviewFacet
        title={labels.occupationsTitle}
        empty={labels.occupationsEmpty}
        chips={occupations}
        onRemove={onRemoveOccupation}
        ariaLabel={labels.occupationsAria}
      />
      <ReviewFacet
        title={labels.regionsTitle}
        empty={labels.regionsEmpty}
        chips={regions}
        onRemove={onRemoveRegion}
        ariaLabel={labels.regionsAria}
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
