"use client";

// "use client": dialogen håller DRAFT-state för tre dimensioner och en
// useTransition runt save-action. De tre väljar-sektionerna (yrken med
// CV-suggest + titel-derive + kaskad/filter, regioner, anställningsformer) är
// extraherade till delade presentations-komponenter (ADR 0077 STEG 5) och delas
// med match-setup-wizard — ingen logik dupliceras. Inget av detta går i en
// Server Component.

import { useState, useTransition } from "react";
import { useTranslations } from "next-intl";
import { Dialog, DialogContent, DialogTitle, DialogDescription } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import type {
  TaxonomyOccupationField,
  TaxonomyOption,
  TaxonomyRegion,
} from "@/lib/dto/taxonomy";
import {
  updateMatchPreferencesAction,
} from "@/lib/actions/match-preferences";
import { toggle, type Option } from "./match-preferences-shared";
import { OccupationSection } from "./occupation-section";
import { FacetSection } from "./facet-section";

interface MatchPreferencesDialogProps {
  readonly open: boolean;
  readonly onOpenChange: (open: boolean) => void;
  readonly occupationFields: ReadonlyArray<TaxonomyOccupationField>;
  readonly regions: ReadonlyArray<TaxonomyRegion>;
  readonly employmentTypes: ReadonlyArray<TaxonomyOption>;
  /** Den PERSISTERADE mängden (SSOT) — dialogens draft seedas från den vid öppning. */
  readonly persistedOccupationGroups: ReadonlyArray<string>;
  readonly persistedRegions: ReadonlyArray<string>;
  readonly persistedEmploymentTypes: ReadonlyArray<string>;
  /**
   * Anropas efter lyckad save med den sparade fulla mängden, så kortet kan
   * anta den lokalt (annars driver kortets klient-state isär från SSOT tills
   * en remount — revalidatePath om-renderar RSC men byter inte useState-värden).
   */
  readonly onSaved: (saved: {
    occupations: ReadonlyArray<string>;
    regions: ReadonlyArray<string>;
    employment: ReadonlyArray<string>;
  }) => void;
  /** URL till CV-importflödet (tom-state-länken). */
  readonly importCvHref: string;
}

export function MatchPreferencesDialog({
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
}: MatchPreferencesDialogProps) {
  const t = useTranslations("settings");
  const regionOptions: ReadonlyArray<Option> = regions.map((r) => ({
    conceptId: r.conceptId,
    label: r.label,
  }));
  const employmentOptions: ReadonlyArray<Option> = employmentTypes.map((e) => ({
    conceptId: e.conceptId,
    label: e.label,
  }));

  // ── DRAFT-state. Seedas från den persisterade mängden VID ÖPPNING via en
  // seed-nyckel — när `open` flippar till true återställs drafterna till SSOT.
  // (Render-tids-derivation, inte setState-i-effect.)
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

  // Save.
  const [isSaving, startSaving] = useTransition();
  const [saveError, setSaveError] = useState<string | null>(null);

  // Seed/återställ drafterna när dialogen öppnas. Render-tids-flagga
  // (react-hooks/set-state-in-effect-fri).
  if (open && !seededFor) {
    setSeededFor(true);
    setDraftOccupations(persistedOccupationGroups);
    setDraftRegions(persistedRegions);
    setDraftEmployment(persistedEmploymentTypes);
    setSaveError(null);
  }
  if (!open && seededFor) {
    setSeededFor(false);
  }

  function toggleOccupation(conceptId: string) {
    setDraftOccupations((prev) => toggle(prev, conceptId));
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
        onSaved({
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

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="jp-matchdialog">
        <div className="jp-matchdialog__head">
          <DialogTitle className="jp-matchdialog__title">
            {t("matchPrefs.dialog.title")}
          </DialogTitle>
          <DialogDescription className="jp-matchdialog__intro">
            {t("matchPrefs.dialog.intro")}
          </DialogDescription>
          {/* Stäng-knappen = shadcn/radix Close inbyggd i DialogContent (civic-
              restylad i globals.css), inte en egen knapp — undviker dubblerad
              "Stäng" för skärmläsare och ärver ESC/fokus-retur. */}
        </div>

        <div className="jp-matchdialog__body">
          <section
            className="jp-matchdialog__section"
            role="group"
            aria-labelledby="match-dialog-occ-head"
          >
            <OccupationSection
              occupationFields={occupationFields}
              selected={draftOccupations}
              onToggle={toggleOccupation}
              onReplace={(next) => setDraftOccupations(next)}
              onClear={() => setDraftOccupations([])}
              importCvHref={importCvHref}
              idPrefix="match-dialog"
              headingId="match-dialog-occ-head"
            />
          </section>

          <section
            className="jp-matchdialog__section"
            role="group"
            aria-labelledby="match-dialog-region-head"
          >
            <FacetSection
              title={t("matchPrefs.facetRegions")}
              options={regionOptions}
              selected={draftRegions}
              onToggle={(id) => setDraftRegions((prev) => toggle(prev, id))}
              onClear={() => setDraftRegions([])}
              pinnedAriaLabel={t("matchPrefs.selectedRegions")}
              headingId="match-dialog-region-head"
            />
          </section>

          <section
            className="jp-matchdialog__section"
            role="group"
            aria-labelledby="match-dialog-employment-head"
          >
            <FacetSection
              title={t("matchPrefs.facetEmployment")}
              options={employmentOptions}
              selected={draftEmployment}
              onToggle={(id) => setDraftEmployment((prev) => toggle(prev, id))}
              onClear={() => setDraftEmployment([])}
              pinnedAriaLabel={t("matchPrefs.selectedEmployment")}
              headingId="match-dialog-employment-head"
            />
          </section>
        </div>

        <div className="jp-matchdialog__foot">
          <Button type="button" onClick={onSave} disabled={isSaving}>
            {isSaving ? t("matchPrefs.dialog.saving") : t("matchPrefs.dialog.save")}
          </Button>
          <Button
            type="button"
            variant="ghost"
            onClick={() => onOpenChange(false)}
            disabled={isSaving}
          >
            {t("matchPrefs.dialog.cancel")}
          </Button>
          {saveError && (
            <p role="alert" className="text-body-sm text-danger-600">
              {saveError}
            </p>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}
