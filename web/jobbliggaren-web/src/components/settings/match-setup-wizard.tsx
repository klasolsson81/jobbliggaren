"use client";

// "use client": wizarden håller klient-only steg-state (vilket steg, draft för
// tre dimensioner), programmatisk fokus-flytt till stegrubriken vid stegbyte
// (WCAG 2.4.3), och en useTransition runt det enda save-anropet. URL ändras
// ALDRIG mid-wizard (ephemer formulär-state, ej deep-linkbar — ADR 0077 Alt D
// förkastad). Inget av detta går i en Server Component.

import { useRef, useState, useTransition } from "react";
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
  /** Anropas efter lyckad save med den sparade fulla mängden. */
  readonly onSaved?: (saved: {
    occupations: ReadonlyArray<string>;
    regions: ReadonlyArray<string>;
    municipalities: ReadonlyArray<string>;
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

/** Stegens konkreta substantiv-rubriker (design-bind A2.i). */
const STEP_TITLES: ReadonlyArray<string> = [
  "Yrken",
  "Orter",
  "Anställningsformer",
  "Klart",
];

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
  onSaved,
  importCvHref,
  parsedResumeId,
}: MatchSetupWizardProps) {
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
    setDraftMunicipalities(persistedMunicipalities);
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

  // Ort-kaskaden emitterar HELA ort-paret (region + kommun) i ett anrop —
  // föräldern speglar det i två draft-states, men de submittas atomiskt (NOTE-1).
  function onOrtChange(next: OrtSelection) {
    setDraftRegions(next.region);
    setDraftMunicipalities(next.municipality);
  }

  function onSave() {
    setSaveError(null);
    startSaving(async () => {
      const result = await updateMatchPreferencesAction({
        preferredOccupationGroups: [...draftOccupations],
        // Region + kommun submittas atomiskt i samma full-replace-PUT (NOTE-1).
        preferredRegions: [...draftRegions],
        preferredMunicipalities: [...draftMunicipalities],
        preferredEmploymentTypes: [...draftEmployment],
      });
      if (result.success) {
        onSaved?.({
          occupations: draftOccupations,
          regions: draftRegions,
          municipalities: draftMunicipalities,
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
            Steg {step} av {TOTAL_STEPS}
          </span>
          <DialogTitle
            ref={titleRef}
            tabIndex={-1}
            id={`match-wizard-step-${step}-head`}
            className="jp-wizard__title"
          >
            {STEP_TITLES[step - 1]}
          </DialogTitle>
          <DialogDescription className="jp-wizard__intro">
            {stepIntro(step)}
          </DialogDescription>
          {/* Stäng = inbyggd radix Close (civic-restylad). Esc stänger hela
              wizarden utan bekräftelse — inget skrivs förrän "Spara matchning". */}
        </div>

        {step === 4 ? (
          <ReviewStep
            occupations={labelsForSelected(draftOccupations, flattenOccupationGroups(occupationFields))}
            orter={[
              ...labelsForSelected(draftRegions, regionOptions),
              ...labelsForSelected(draftMunicipalities, municipalityOptions),
            ]}
            employment={labelsForSelected(draftEmployment, employmentOptions)}
            onRemoveOccupation={(id) =>
              setDraftOccupations((prev) => toggle(prev, id))
            }
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
                  autoSuggestFromCv
                  parsedResumeId={parsedResumeId}
                />
              )}
              {step === 2 && (
                <RegionMunicipalityCascade
                  regions={regions}
                  selectedRegions={draftRegions}
                  selectedMunicipalities={draftMunicipalities}
                  onChange={onOrtChange}
                  showHeading={false}
                  idPrefix="match-wizard-ort"
                />
              )}
              {step === 3 && (
                <FacetSection
                  title="Anställningsformer"
                  options={employmentOptions}
                  selected={draftEmployment}
                  onToggle={(id) => setDraftEmployment((prev) => toggle(prev, id))}
                  onClear={() => setDraftEmployment([])}
                  pinnedAriaLabel="Valda anställningsformer"
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
              Tillbaka
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
              Hoppa över det här steget
            </button>
          )}
          <Button type="button" onClick={onPrimary} disabled={isSaving}>
            {isLastStep ? (isSaving ? "Sparar…" : "Spara matchning") : "Nästa"}
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

/** Stegets hjälptext (bär instruktionen — aldrig placeholder-exempel). */
function stepIntro(step: number): string {
  switch (step) {
    case 1:
      return "Välj de yrken du söker. Vi föreslår utifrån ditt CV. Ändra om det inte stämmer; du väljer själv vilka som tas med.";
    case 2:
      return "Välj de län eller kommuner du vill arbeta i. Lämna tomt för hela landet.";
    case 3:
      return "Välj de anställningsformer du är öppen för. Lämna tomt för alla.";
    default:
      return "Granska dina val. Du kan ta bort något här, eller gå tillbaka för att ändra.";
  }
}

/**
 * Klart-/bekräfta-steget (steg 4): granska de valda chipsen per dimension.
 * Borttagbara (PreferenceChip) — sista chansen att rensa innan save. Tom
 * dimension visar en ärlig "inget valt"-rad (ej fejkat).
 */
function ReviewStep({
  occupations,
  orter,
  employment,
  onRemoveOccupation,
  onRemoveOrt,
  onRemoveEmployment,
}: {
  readonly occupations: ReadonlyArray<Option>;
  readonly orter: ReadonlyArray<Option>;
  readonly employment: ReadonlyArray<Option>;
  readonly onRemoveOccupation: (conceptId: string) => void;
  readonly onRemoveOrt: (conceptId: string) => void;
  readonly onRemoveEmployment: (conceptId: string) => void;
}) {
  return (
    <div className="jp-wizard__step">
      <ReviewFacet
        title="Yrken"
        empty="Alla yrken (inget valt)"
        chips={occupations}
        onRemove={onRemoveOccupation}
        ariaLabel="Valda yrken"
      />
      <ReviewFacet
        title="Orter"
        empty="Hela landet (ingen ort vald)"
        chips={orter}
        onRemove={onRemoveOrt}
        ariaLabel="Valda orter"
      />
      <ReviewFacet
        title="Anställningsformer"
        empty="Alla anställningsformer (inget valt)"
        chips={employment}
        onRemove={onRemoveEmployment}
        ariaLabel="Valda anställningsformer"
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
