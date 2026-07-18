"use client";

// "use client": dialogen håller DRAFT-state för tre dimensioner och en
// useTransition runt save-action. De tre väljar-sektionerna (yrken med
// CV-suggest + titel-derive + kaskad/filter, regioner, anställningsformer) är
// extraherade till delade presentations-komponenter (ADR 0077 STEG 5) och delas
// med match-setup-rail-modal — ingen logik dupliceras. Inget av detta går i en
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
import {
  projectOccupationExperience,
  recordFromOccupationExperience,
  toggle,
  type Option,
} from "./match-preferences-shared";
import type { SkillGroup } from "@/lib/dto/skills";
import { OccupationSection } from "./occupation-section";
import { SkillSection } from "./skill-section";
import { ExperienceField } from "./experience-field";
import { FacetSection } from "./facet-section";
import { RegionMunicipalityCascade } from "./region-municipality-cascade";
import type { OrtSelection } from "@/lib/job-ads/ort-selection";

interface MatchPreferencesDialogProps {
  readonly open: boolean;
  readonly onOpenChange: (open: boolean) => void;
  readonly occupationFields: ReadonlyArray<TaxonomyOccupationField>;
  readonly regions: ReadonlyArray<TaxonomyRegion>;
  readonly employmentTypes: ReadonlyArray<TaxonomyOption>;
  /** Den PERSISTERADE mängden (SSOT) — dialogens draft seedas från den vid öppning. */
  readonly persistedOccupationGroups: ReadonlyArray<string>;
  readonly persistedRegions: ReadonlyArray<string>;
  /** Spår 3 PR-D: kommun-axeln (pre-fill för ort-kaskaden). */
  readonly persistedMunicipalities: ReadonlyArray<string>;
  readonly persistedEmploymentTypes: ReadonlyArray<string>;
  /** STEG 3 / ADR 0079: kompetens-axeln + (profil-nivå) erfarenhet (pre-fill).
   *  exp-per-occ (ADR 0079-amendment PR-4): den profil-nivå ExperienceField är
   *  KVAR i dialogen (Klas scopade borttagningen till WIZARDEN). */
  readonly persistedSkills: ReadonlyArray<string>;
  readonly persistedExperienceYears: number | null;
  /** exp-per-occ (ADR 0079-amendment PR-4): per-yrke-erfarenhets-overlay (pre-fill).
   *  Full-replace via dialogens egna PUT (ingen page-wipe). */
  readonly persistedOccupationExperience: ReadonlyArray<{
    readonly conceptId: string;
    readonly years: number | null;
  }>;
  /** STEG 3 / ADR 0079 + #277: GRUPPER för sparade kompetens-concept-id
   *  (chip-render). Ett sparat twin-par renderas som EN chip via gruppens
   *  member-id (BE-resolvad cold-load via ResolveSkillLabels). */
  readonly persistedSkillGroups?: ReadonlyArray<SkillGroup>;
  /**
   * Anropas efter lyckad save med den sparade fulla mängden, så kortet kan
   * anta den lokalt (annars driver kortets klient-state isär från SSOT tills
   * en remount — revalidatePath om-renderar RSC men byter inte useState-värden).
   */
  readonly onSaved: (saved: {
    occupations: ReadonlyArray<string>;
    regions: ReadonlyArray<string>;
    municipalities: ReadonlyArray<string>;
    employment: ReadonlyArray<string>;
    skills: ReadonlyArray<string>;
    experienceYears: number | null;
    // exp-per-occ (ADR 0079-amendment PR-4): den sparade per-yrke-overlayn
    // (scopad till valda yrken), så kortet kan adoptera den lokalt.
    occupationExperience: ReadonlyArray<{
      readonly conceptId: string;
      readonly years: number | null;
    }>;
    /** GRUPPER för de sparade kompetenserna så kortet kan rendera EN chip per
     *  twin-par (skills saknar träd-uppslagning). Spegel av SkillSections
     *  grupp-store (#277). */
    skillGroups: ReadonlyArray<SkillGroup>;
  }) => void;
  /** URL till CV-importflödet (tom-state-länken). */
  readonly importCvHref: string;
  /**
   * #748 (WCAG 2.4.3): forwarded to Radix `DialogContent`. This is a CONTROLLED
   * dialog with no `DialogTrigger`, so Radix's default close-autofocus targets a
   * null `triggerRef` and focus falls to `document.body`. The parent passes a
   * handler that returns focus to the invoking control instead.
   */
  readonly onCloseAutoFocus?: (event: Event) => void;
}

export function MatchPreferencesDialog({
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
  persistedExperienceYears,
  persistedOccupationExperience,
  persistedSkillGroups = [],
  onSaved,
  importCvHref,
  onCloseAutoFocus,
}: MatchPreferencesDialogProps) {
  const t = useTranslations("settings");
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
  const [draftMunicipalities, setDraftMunicipalities] = useState<
    ReadonlyArray<string>
  >(persistedMunicipalities);
  const [draftEmployment, setDraftEmployment] = useState<ReadonlyArray<string>>(
    persistedEmploymentTypes
  );
  const [draftSkills, setDraftSkills] = useState<ReadonlyArray<string>>(
    persistedSkills
  );
  const [draftExperience, setDraftExperience] = useState<number | null>(
    persistedExperienceYears
  );
  // exp-per-occ (ADR 0079-amendment PR-4): per-yrke-erfarenhets-overlay (draft).
  // Seedas vid öppning från den persisterade overlayn; CV-förslagets år mergas
  // in via onSeedExperience (utan att skriva över ett befintligt värde).
  const [draftOccupationExperience, setDraftOccupationExperience] = useState<
    Readonly<Record<string, number | null>>
  >(() => recordFromOccupationExperience(persistedOccupationExperience));
  // Speglar SkillSections grupp-store så onSaved kan bära EN chip per twin-par
  // ut till kortet (#277).
  const [skillGroups, setSkillGroups] = useState<ReadonlyArray<SkillGroup>>(
    persistedSkillGroups
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
    setDraftMunicipalities(persistedMunicipalities);
    setDraftEmployment(persistedEmploymentTypes);
    setDraftSkills(persistedSkills);
    setDraftExperience(persistedExperienceYears);
    setDraftOccupationExperience(
      recordFromOccupationExperience(persistedOccupationExperience)
    );
    setSaveError(null);
  }
  if (!open && seededFor) {
    setSeededFor(false);
  }

  function toggleOccupation(conceptId: string) {
    setDraftOccupations((prev) => toggle(prev, conceptId));
  }

  // exp-per-occ (ADR 0079-amendment PR-4): användaren ändrade ett yrkes år.
  // `null` (tomt fält) lagras explicit (skilt från "ingen nyckel").
  function onOccupationExperienceChange(conceptId: string, years: number | null) {
    setDraftOccupationExperience((prev) => ({ ...prev, [conceptId]: years }));
  }

  // exp-per-occ (ADR 0079-amendment PR-4): CV-förslaget seedar härledda år —
  // mergas in MEN skriver ALDRIG över ett befintligt värde (persisterat eller
  // användar-angivet). `0` och `null` bevaras skilt.
  function seedOccupationExperience(seed: Readonly<Record<string, number | null>>) {
    setDraftOccupationExperience((prev) => {
      const next = { ...prev };
      for (const [conceptId, years] of Object.entries(seed)) {
        if (!(conceptId in next)) next[conceptId] = years;
      }
      return next;
    });
  }

  // Ort-kaskaden emitterar HELA ort-paret (region + kommun) i ett anrop —
  // dialogen speglar det i två draft-states men submittar dem atomiskt (NOTE-1).
  function onOrtChange(next: OrtSelection) {
    setDraftRegions(next.region);
    setDraftMunicipalities(next.municipality);
  }

  function onSave() {
    setSaveError(null);
    // exp-per-occ (ADR 0079-amendment PR-4): projicera overlayn till wire-formen,
    // ENBART för fortfarande valda yrken (subset-regeln) — borttaget yrke tappar
    // sin rad. Full-replace genom dialogens egen PUT (ingen page-wipe).
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
        // STEG 3 / ADR 0079: kompetens + (profil-nivå) erfarenhet i SAMMA PUT
        // (page-wipe-guard). exp-per-occ PR-4: + per-yrke-overlayn.
        preferredSkills: [...draftSkills],
        experienceYears: draftExperience,
        preferredOccupationExperience: [...occupationExperience],
      });
      if (result.success) {
        onSaved({
          occupations: draftOccupations,
          regions: draftRegions,
          municipalities: draftMunicipalities,
          employment: draftEmployment,
          skills: draftSkills,
          experienceYears: draftExperience,
          occupationExperience,
          skillGroups,
        });
        onOpenChange(false);
      } else {
        setSaveError(result.error);
      }
    });
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="jp-matchdialog" onCloseAutoFocus={onCloseAutoFocus}>
        <div className="jp-matchdialog__head">
          <DialogTitle className="jp-matchdialog__title">
            {t("matchPrefs.dialog.title")}
          </DialogTitle>
          <DialogDescription className="jp-matchdialog__intro">
            {t("matchPrefs.dialog.intro")}
          </DialogDescription>
          {/* Stäng-knappen = shadcn/radix Close inbyggd i DialogContent (civic-
              restylad i globals.css), inte en egen knapp — undviker dubblerad
              "Stäng" för skärmläsare och ärver ESC-stängning. Fokus-retur till
              den öppnande kontrollen sköts av onCloseAutoFocus (WCAG 2.4.3) —
              denna trigger-lösa controlled dialog har ingen triggerRef att ärva
              den från (#748). */}
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
              // exp-per-occ (ADR 0079-amendment PR-4): per-yrke-år-fält, konsekvent
              // med wizarden. Full-replace genom dialogens egen PUT (ingen page-wipe).
              experienceByConceptId={draftOccupationExperience}
              onExperienceChange={onOccupationExperienceChange}
              onSeedExperience={seedOccupationExperience}
            />
          </section>

          <section
            className="jp-matchdialog__section"
            role="group"
            aria-labelledby="match-dialog-skill-head"
          >
            <SkillSection
              selected={draftSkills}
              onReplace={(next) => setDraftSkills(next)}
              onClear={() => setDraftSkills([])}
              idPrefix="match-dialog-skill"
              headingId="match-dialog-skill-head"
              initialGroups={persistedSkillGroups}
              onGroupsChange={setSkillGroups}
            />
            {/* Erfarenhet bor i samma sektion som kompetens (samma steg/host). */}
            <div className="mt-4">
              <ExperienceField
                value={draftExperience}
                onChange={setDraftExperience}
                idPrefix="match-dialog-experience"
              />
            </div>
          </section>

          <section
            className="jp-matchdialog__section"
            role="group"
            aria-labelledby="match-dialog-region-head"
          >
            <RegionMunicipalityCascade
              regions={regions}
              selectedRegions={draftRegions}
              selectedMunicipalities={draftMunicipalities}
              onChange={onOrtChange}
              headingId="match-dialog-region-head"
              idPrefix="match-dialog-ort"
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
