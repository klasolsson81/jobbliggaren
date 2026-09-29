"use client";

// "use client": each part's dialog holds that part's draft, and
// places focus on open and after a refused save. None of that runs in a Server Component. The
// editors are the shared sections (ADR 0077 STEG 5), the same ones the first-time rail mounts.

import {
  type ReactNode,
  type RefObject,
  useId,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
} from "react";
import { useLocale, useTranslations } from "next-intl";
import { codedTaxonomyOptions } from "@/lib/i18n/coded-taxonomy";
import { Dialog, DialogContent, DialogTitle } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { PendingLabel } from "@/components/forms/pending-label";
import type {
  TaxonomyOccupationField,
  TaxonomyOption,
  TaxonomyRegion,
} from "@/lib/dto/taxonomy";
import type { ActionResult } from "@/lib/actions/_action-result";
import {
  hasValues,
  projectOccupationExperience,
  recordFromOccupationExperience,
  toggle,
  type EmploymentTypesValue,
  type ExperienceValue,
  type LocationsValue,
  type OccupationsValue,
  type PartValue,
  type SkillsValue,
} from "./match-preferences-shared";
import { OccupationSection } from "./occupation-section";
import { SkillSection } from "./skill-section";
import { ExperienceField } from "./experience-field";
import { FacetSection } from "./facet-section";
import { RegionMunicipalityCascade } from "./region-municipality-cascade";

interface MatchPreferencesDialogProps {
  readonly open: boolean;
  readonly onOpenChange: (open: boolean) => void;
  /** The part as the card shows it. The draft is seeded from it at mount; the card mounts one
   *  dialog per opening. */
  readonly value: PartValue;
  readonly occupationFields: ReadonlyArray<TaxonomyOccupationField>;
  readonly regions: ReadonlyArray<TaxonomyRegion>;
  readonly employmentTypes: ReadonlyArray<TaxonomyOption>;
  /** URL till CV-importflödet (tom-state-länken). */
  readonly importCvHref: string;
  /** Writes the part. The card queues it behind the part's earlier writes (ADR 0147 D9). */
  readonly onSave: (value: PartValue) => Promise<ActionResult>;
  /**
   * #748 (WCAG 2.4.3): forwarded to Radix `DialogContent`. This is a CONTROLLED
   * dialog with no `DialogTrigger`, so Radix's default close-autofocus targets a
   * null `triggerRef` and focus falls to `document.body`. The card passes a
   * handler that returns focus to the part's own button instead.
   */
  readonly onCloseAutoFocus: (event: Event) => void;
}

/** One dialog, parameterised by the part it edits (#1918). */
export function MatchPreferencesDialog(props: MatchPreferencesDialogProps) {
  const { value } = props;
  switch (value.part) {
    case "occupations":
      return <OccupationsDialog {...props} value={value} />;
    case "skills":
      return <SkillsDialog {...props} value={value} />;
    case "locations":
      return <LocationsDialog {...props} value={value} />;
    case "employmentTypes":
      return <EmploymentTypesDialog {...props} value={value} />;
    case "experience":
      return <ExperienceDialog {...props} value={value} />;
  }
}

type PartDialogProps<V extends PartValue> = Omit<MatchPreferencesDialogProps, "value"> & {
  readonly value: V;
};

function OccupationsDialog({
  open,
  onOpenChange,
  onCloseAutoFocus,
  value,
  occupationFields,
  importCvHref,
  onSave,
}: PartDialogProps<OccupationsValue>) {
  const t = useTranslations("settings");
  const titleId = useId();
  const [groups, setGroups] = useState<ReadonlyArray<string>>(value.groups);
  // exp-per-occ (ADR 0079-amendment PR-4): per-yrke-erfarenhets-overlay (draft). CV-förslagets år
  // mergas in via onSeedExperience utan att skriva över ett befintligt värde.
  const [years, setYears] = useState<Readonly<Record<string, number | null>>>(() =>
    recordFromOccupationExperience(value.experience)
  );

  return (
    <PartDialog
      open={open}
      onOpenChange={onOpenChange}
      onCloseAutoFocus={onCloseAutoFocus}
      title={t("matchPrefs.facetOccupations")}
      titleId={titleId}
      saveLabel={t("matchPrefs.dialog.saveOccupations")}
      wide
      save={() =>
        onSave({
          part: "occupations",
          groups,
          // Only the occupations still chosen carry years (the subset rule).
          experience: projectOccupationExperience(years, groups),
        })
      }
    >
      <OccupationSection
        occupationFields={occupationFields}
        selected={groups}
        onToggle={(conceptId) => setGroups((prev) => toggle(prev, conceptId))}
        onReplace={setGroups}
        onClear={() => setGroups([])}
        importCvHref={importCvHref}
        idPrefix="match-dialog"
        initialPickerOpen={!hasValues(value)}
        experienceByConceptId={years}
        onExperienceChange={(conceptId, next) =>
          setYears((prev) => ({ ...prev, [conceptId]: next }))
        }
        onSeedExperience={(seed) =>
          setYears((prev) => {
            const next = { ...prev };
            for (const [conceptId, seeded] of Object.entries(seed)) {
              if (!(conceptId in next)) next[conceptId] = seeded;
            }
            return next;
          })
        }
      />
    </PartDialog>
  );
}

function SkillsDialog({
  open,
  onOpenChange,
  onCloseAutoFocus,
  value,
  onSave,
}: PartDialogProps<SkillsValue>) {
  const t = useTranslations("settings");
  const titleId = useId();
  const [skills, setSkills] = useState<ReadonlyArray<string>>(value.skills);
  // SkillSection's group store, mirrored so the card names the saved chips (#277).
  const [skillGroups, setSkillGroups] = useState(value.skillGroups);

  return (
    <PartDialog
      open={open}
      onOpenChange={onOpenChange}
      onCloseAutoFocus={onCloseAutoFocus}
      title={t("matchPrefs.facetSkills")}
      titleId={titleId}
      saveLabel={t("matchPrefs.dialog.saveSkills")}
      wide={false}
      save={() => onSave({ part: "skills", skills, skillGroups })}
    >
      <SkillSection
        selected={skills}
        onReplace={setSkills}
        onClear={() => setSkills([])}
        idPrefix="match-dialog-skill"
        initialPickerOpen={!hasValues(value)}
        initialGroups={value.skillGroups}
        onGroupsChange={setSkillGroups}
      />
    </PartDialog>
  );
}

function LocationsDialog({
  open,
  onOpenChange,
  onCloseAutoFocus,
  value,
  regions,
  onSave,
}: PartDialogProps<LocationsValue>) {
  const t = useTranslations("settings");
  const titleId = useId();
  const [ort, setOrt] = useState(value);

  return (
    <PartDialog
      open={open}
      onOpenChange={onOpenChange}
      onCloseAutoFocus={onCloseAutoFocus}
      title={t("matchPrefs.facetOrter")}
      titleId={titleId}
      saveLabel={t("matchPrefs.dialog.saveOrter")}
      wide
      save={() => onSave(ort)}
    >
      {/* The cascade emits the whole ort pair in one call, so region and municipality are
          saved together (NOTE-1). */}
      <RegionMunicipalityCascade
        regions={regions}
        selectedRegions={ort.regions}
        selectedMunicipalities={ort.municipalities}
        remote={ort.remote}
        onChange={(next) =>
          setOrt((prev) => ({
            ...prev,
            regions: next.region,
            municipalities: next.municipality,
            remote: next.remote ?? prev.remote,
          }))
        }
        idPrefix="match-dialog-ort"
        initialPickerOpen={!hasValues(value)}
      />
    </PartDialog>
  );
}

function EmploymentTypesDialog({
  open,
  onOpenChange,
  onCloseAutoFocus,
  value,
  employmentTypes,
  onSave,
}: PartDialogProps<EmploymentTypesValue>) {
  const t = useTranslations("settings");
  const tEnum = useTranslations("jobads.enums");
  const locale = useLocale();
  const titleId = useId();
  // Allmänsubstantiv, alltså locale-copy (#1537).
  const options = useMemo(
    () => codedTaxonomyOptions(tEnum, new Intl.Collator(locale), employmentTypes),
    [tEnum, locale, employmentTypes]
  );
  const [types, setTypes] = useState<ReadonlyArray<string>>(value.types);

  return (
    <PartDialog
      open={open}
      onOpenChange={onOpenChange}
      onCloseAutoFocus={onCloseAutoFocus}
      title={t("matchPrefs.facetEmployment")}
      titleId={titleId}
      saveLabel={t("matchPrefs.dialog.saveEmployment")}
      wide={false}
      save={() => onSave({ part: "employmentTypes", types })}
    >
      <FacetSection
        options={options}
        selected={types}
        onToggle={(conceptId) => setTypes((prev) => toggle(prev, conceptId))}
        onClear={() => setTypes([])}
        pinnedAriaLabel={t("matchPrefs.selectedEmployment")}
      />
    </PartDialog>
  );
}

function ExperienceDialog({
  open,
  onOpenChange,
  onCloseAutoFocus,
  value,
  onSave,
}: PartDialogProps<ExperienceValue>) {
  const t = useTranslations("settings");
  const titleId = useId();
  const [years, setYears] = useState(value.years);
  const fieldRef = useRef<HTMLInputElement | null>(null);

  return (
    <PartDialog
      open={open}
      onOpenChange={onOpenChange}
      onCloseAutoFocus={onCloseAutoFocus}
      title={t("matchPrefs.experience.label")}
      titleId={titleId}
      saveLabel={t("matchPrefs.dialog.saveExperience")}
      wide={false}
      focusOnOpen={fieldRef}
      save={() => onSave({ part: "experience", years })}
    >
      <ExperienceField
        value={years}
        onChange={setYears}
        labelledBy={titleId}
        inputRef={fieldRef}
      />
    </PartDialog>
  );
}

/**
 * The frame every part shares: the part's name as the title, the editor as the whole body (no
 * subheading, no lede: DESIGN.md §8 rules 1–2), and the save named by the part. A refused save
 * keeps the dialog and its draft, and says why in the foot.
 */
function PartDialog({
  open,
  onOpenChange,
  onCloseAutoFocus,
  title,
  titleId,
  saveLabel,
  wide,
  save,
  focusOnOpen,
  children,
}: {
  readonly open: boolean;
  readonly onOpenChange: (open: boolean) => void;
  readonly onCloseAutoFocus: (event: Event) => void;
  readonly title: string;
  /** Names the dialog, and the experience field through `aria-labelledby`. */
  readonly titleId: string;
  readonly saveLabel: string;
  /** Yrken and Orter carry a two-column cascade. */
  readonly wide: boolean;
  readonly save: () => Promise<ActionResult>;
  /** Focused on open. The title when absent. */
  readonly focusOnOpen?: RefObject<HTMLElement | null>;
  readonly children: ReactNode;
}) {
  const t = useTranslations("settings");
  const titleRef = useRef<HTMLHeadingElement | null>(null);
  const saveRef = useRef<HTMLButtonElement | null>(null);
  const errorId = useId();
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);

  // A pending save disables its button, and Chromium then drops focus to <body>. A refused save
  // hands it back once the button is enabled again (WCAG 2.4.3).
  const refocusSave = useRef(false);
  useLayoutEffect(() => {
    if (saving || !refocusSave.current) return;
    refocusSave.current = false;
    saveRef.current?.focus();
  }, [saving]);

  function onSave() {
    setSaveError(null);
    setSaving(true);
    void save().then((result) => {
      setSaving(false);
      if (result.success) {
        onOpenChange(false);
      } else {
        refocusSave.current = true;
        setSaveError(result.error);
      }
    });
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next && saving) return;
        onOpenChange(next);
      }}
    >
      <DialogContent
        className={wide ? "jp-matchdialog" : "jp-matchdialog jp-matchdialog--narrow"}
        aria-labelledby={titleId}
        onOpenAutoFocus={(event) => {
          event.preventDefault();
          (focusOnOpen?.current ?? titleRef.current)?.focus();
        }}
        onCloseAutoFocus={onCloseAutoFocus}
      >
        <div className="jp-matchdialog__head">
          {/* Stäng-knappen = shadcn/radix Close inbyggd i DialogContent, inte en egen
              knapp — undviker dubblerad "Stäng" för skärmläsare och ärver ESC-stängning. */}
          <DialogTitle
            ref={titleRef}
            id={titleId}
            tabIndex={-1}
            className="jp-matchdialog__title"
          >
            {title}
          </DialogTitle>
        </div>

        <div className="jp-matchdialog__body">{children}</div>

        <div className="jp-matchdialog__foot">
          <Button
            ref={saveRef}
            type="button"
            onClick={onSave}
            disabled={saving}
            aria-describedby={saveError ? errorId : undefined}
          >
            <PendingLabel
              pending={saving}
              idle={saveLabel}
              busy={t("matchPrefs.dialog.saving")}
            />
          </Button>
          <Button
            type="button"
            variant="ghost"
            onClick={() => onOpenChange(false)}
            disabled={saving}
          >
            {t("matchPrefs.dialog.cancel")}
          </Button>
          <p role="status" aria-live="polite" className="sr-only">
            {saving ? t("matchPrefs.dialog.saving") : ""}
          </p>
          {saveError && (
            <p id={errorId} role="alert" className="text-body-sm text-danger-600">
              {saveError}
            </p>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}
