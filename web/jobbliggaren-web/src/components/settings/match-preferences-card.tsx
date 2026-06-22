"use client";

// "use client": kortet håller lokal vald-mängd-state (tre dimensioner),
// optimistisk chip-borttagning med useTransition runt save-action +
// revert-vid-fel, tangentbords-borttagning med fokus-flytt till grannen, samt
// en dialog-öppna-affordans. Inget av detta går i en Server Component.

import { useMemo, useRef, useState, useTransition } from "react";
import { useTranslations } from "next-intl";
import type {
  TaxonomyOccupationField,
  TaxonomyOption,
  TaxonomyRegion,
} from "@/lib/dto/taxonomy";
import { updateMatchPreferencesAction } from "@/lib/actions/match-preferences";
import { Button } from "@/components/ui/button";
import {
  flattenOccupationGroups,
  filterOptions,
  labelsForSelected,
  type Option,
} from "./match-preferences-shared";
import { PreferenceChip } from "./preference-chip";
import { MatchPreferencesDialog } from "./match-preferences-dialog";

// Pure helpers re-exporteras så befintliga tester/konsumenter (som importerar
// dem härifrån) inte bryts; definitionen bor i match-preferences-shared.
export { flattenOccupationGroups, filterOptions };

/** Yrken/Orter/Anställningsformer — de tre facetterna kortet renderar. "orter"
 * är EN dimension i två granulariteter (län + kommun, Spår 3 PR-D). */
type Facet = "occupations" | "orter" | "employment";

interface MatchPreferencesCardProps {
  /** Yrkesområden (med underordnade yrkesgrupper) → kortet plattar själv. */
  readonly occupationFields: ReadonlyArray<TaxonomyOccupationField>;
  /** Län (med underordnade kommuner) → kortet plattar kommun-labels själv. */
  readonly regions: ReadonlyArray<TaxonomyRegion>;
  /** Anställningsform-options (råa JobTech-labels, "honest 8"). */
  readonly employmentTypes: ReadonlyArray<TaxonomyOption>;
  /** Sparade val att initiera från (concept-id-listor från profilen). */
  readonly initialOccupationGroups: ReadonlyArray<string>;
  readonly initialRegions: ReadonlyArray<string>;
  /** Spår 3 PR-D: kommun-axeln (sparade kommun-concept-id från profilen). */
  readonly initialMunicipalities: ReadonlyArray<string>;
  readonly initialEmploymentTypes: ReadonlyArray<string>;
  /**
   * Civil degradering: när taxonomin inte kunde läsas in passar föräldern
   * `false` för optionerna och sätter `degraded` så kortet visar en lugn
   * "kunde inte läsas in just nu"-text i stället för väljarna.
   */
  readonly degraded: boolean;
}

/** CV-importflödets route (verifierad on-disk: app/(app)/cv/importera). */
const IMPORT_CV_HREF = "/cv/importera";

export function MatchPreferencesCard({
  occupationFields,
  regions,
  employmentTypes,
  initialOccupationGroups,
  initialRegions,
  initialMunicipalities,
  initialEmploymentTypes,
  degraded,
}: MatchPreferencesCardProps) {
  const t = useTranslations("settings");
  // Facet-rubriker och tom-state-texter per dimension (svenska via katalogen).
  const facetLabel: Record<Facet, string> = {
    occupations: t("matchPrefs.facetOccupations"),
    orter: t("matchPrefs.facetOrter"),
    employment: t("matchPrefs.facetEmployment"),
  };
  const facetEmpty: Record<Facet, string> = {
    occupations: t("matchPrefs.emptyOccupations"),
    orter: t("matchPrefs.emptyOrter"),
    employment: t("matchPrefs.emptyEmployment"),
  };
  const occupationOptions = useMemo(
    () => flattenOccupationGroups(occupationFields),
    [occupationFields]
  );
  const regionOptions = useMemo<ReadonlyArray<Option>>(
    () => regions.map((r) => ({ conceptId: r.conceptId, label: r.label })),
    [regions]
  );
  // Kommun-options (flatten av länens kommuner) för ort-facettens chip-labels.
  const municipalityOptions = useMemo<ReadonlyArray<Option>>(
    () =>
      regions.flatMap((r) =>
        r.municipalities.map((m) => ({ conceptId: m.conceptId, label: m.label }))
      ),
    [regions]
  );
  const employmentOptions = useMemo<ReadonlyArray<Option>>(
    () =>
      employmentTypes.map((e) => ({ conceptId: e.conceptId, label: e.label })),
    [employmentTypes]
  );

  const [occupationGroups, setOccupationGroups] = useState<
    ReadonlyArray<string>
  >(initialOccupationGroups);
  const [selectedRegions, setSelectedRegions] =
    useState<ReadonlyArray<string>>(initialRegions);
  const [selectedMunicipalities, setSelectedMunicipalities] =
    useState<ReadonlyArray<string>>(initialMunicipalities);
  const [selectedEmployment, setSelectedEmployment] = useState<
    ReadonlyArray<string>
  >(initialEmploymentTypes);

  const [dialogOpen, setDialogOpen] = useState(false);
  const [isSaving, startSaving] = useTransition();
  const [savedAt, setSavedAt] = useState<Date | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);

  const addButtonRef = useRef<HTMLButtonElement>(null);
  // Refs till varje chips ⨯-knapp, nyckel "facet:conceptId". Populeras ENBART
  // via ref-callback (commit-tid) — aldrig läst/skriven under render (WCAG
  // 2.4.3 fokus-flytt sker i en queueMicrotask EFTER commit).
  const removeRefs = useRef(new Map<string, HTMLButtonElement | null>());
  const refKey = (facet: Facet, conceptId: string) => `${facet}:${conceptId}`;

  interface PrefSets {
    occupations: ReadonlyArray<string>;
    regions: ReadonlyArray<string>;
    municipalities: ReadonlyArray<string>;
    employment: ReadonlyArray<string>;
  }

  const currentSets = (): PrefSets => ({
    occupations: occupationGroups,
    regions: selectedRegions,
    municipalities: selectedMunicipalities,
    employment: selectedEmployment,
  });

  /** Persisterar HELA mängden (full-replace) med revert-vid-fel. Region + kommun
   *  skickas atomiskt i samma PUT (NOTE-1). */
  function persist(next: PrefSets, revert: () => void) {
    setSaveError(null);
    startSaving(async () => {
      const result = await updateMatchPreferencesAction({
        preferredOccupationGroups: [...next.occupations],
        preferredRegions: [...next.regions],
        preferredMunicipalities: [...next.municipalities],
        preferredEmploymentTypes: [...next.employment],
      });
      if (result.success) {
        setSavedAt(new Date());
      } else {
        revert();
        setSaveError(result.error);
      }
    });
  }

  /**
   * Ort-facetten ritar två axlar (län + kommun): hitta vilken ett id bor i.
   * Invariant: region- och kommun-concept-id ligger i DISJUNKTA namnrymder i
   * JobTech-taxonomin, så "finns i selectedRegions" entydigt skiljer axlarna.
   * Skulle ett framtida taxonomi-id kollidera mellan axlarna måste detta byta
   * till en id→axel-karta byggd ur trädet.
   */
  function ortAxisOf(conceptId: string): "regions" | "municipalities" {
    return selectedRegions.includes(conceptId) ? "regions" : "municipalities";
  }

  /**
   * Optimistisk borttagning av en chip: ta bort lokalt direkt (ingen spinner),
   * persistera hela nya mängden, revert vid fel. `keyboard` → flytta fokus till
   * grannen (eller "Lägg till" om facetten blev tom), aldrig till body.
   *
   * Ort-facetten kombinerar två axlar — `axisFor` pekar ut vilken state-lista
   * id:t faktiskt bor i (län ELLER kommun), så grannskaps-/revert-logiken
   * fortsätter att vara axel-exakt.
   */
  function removeChip(facet: Facet, conceptId: string, keyboard: boolean) {
    const prev = currentSets();
    const axisFor: keyof PrefSets =
      facet === "occupations"
        ? "occupations"
        : facet === "employment"
          ? "employment"
          : ortAxisOf(conceptId);
    const list = prev[axisFor];
    const removedIndex = list.indexOf(conceptId);
    const nextList = list.filter((v) => v !== conceptId);
    const next: PrefSets = { ...prev, [axisFor]: nextList };

    // Bestäm grannen att flytta fokus till (conceptId, inte index): nästa
    // kvarvarande chip, annars föregående, annars "Lägg till". Beräknas FÖRE
    // borttagningen mot den gamla listan.
    const neighbourConceptId =
      list[removedIndex + 1] ?? list[removedIndex - 1] ?? null;

    applyAxis(axisFor, nextList);

    if (keyboard) {
      queueMicrotask(() => {
        const target =
          neighbourConceptId !== null
            ? removeRefs.current.get(refKey(facet, neighbourConceptId))
            : null;
        if (target) target.focus();
        else addButtonRef.current?.focus();
      });
    }

    persist(next, () => applyAxis(axisFor, prev[axisFor]));
  }

  /** Skriver EN axel till rätt state-setter. */
  function applyAxis(axis: keyof PrefSets, value: ReadonlyArray<string>) {
    if (axis === "occupations") setOccupationGroups(value);
    else if (axis === "regions") setSelectedRegions(value);
    else if (axis === "municipalities") setSelectedMunicipalities(value);
    else setSelectedEmployment(value);
  }

  function onDialogSaved(saved: {
    occupations: ReadonlyArray<string>;
    regions: ReadonlyArray<string>;
    municipalities: ReadonlyArray<string>;
    employment: ReadonlyArray<string>;
  }) {
    // Dialogen skrev den fulla mängden (SSOT). Anta den lokalt så kortets chips
    // är koherenta direkt, och visa status-raden. (revalidatePath om-renderar
    // RSC men byter inte kortets useState-värden — därför adopterar vi här.)
    setOccupationGroups(saved.occupations);
    setSelectedRegions(saved.regions);
    setSelectedMunicipalities(saved.municipalities);
    setSelectedEmployment(saved.employment);
    setSavedAt(new Date());
  }

  if (degraded) {
    return (
      <section className="jp-card" id="matchning">
        <h2 className="jp-card__title">{t("matchPrefs.title")}</h2>
        <p className="text-body-sm text-text-secondary">
          {t("matchPrefs.degraded")}
        </p>
      </section>
    );
  }

  const facetData: Record<Facet, ReadonlyArray<Option>> = {
    occupations: labelsForSelected(occupationGroups, occupationOptions),
    // Ort-facetten: valda län FÖRST (helläns-axeln), sedan enskilda kommuner.
    orter: [
      ...labelsForSelected(selectedRegions, regionOptions),
      ...labelsForSelected(selectedMunicipalities, municipalityOptions),
    ],
    employment: labelsForSelected(selectedEmployment, employmentOptions),
  };

  return (
    <section className="jp-card jp-matchprefs" id="matchning">
      <h2 className="jp-card__title">{t("matchPrefs.title")}</h2>
      <p className="text-body-sm text-text-secondary">{t("matchPrefs.intro")}</p>

      <div className="jp-matchprefs__facets mt-5">
        {(["occupations", "orter", "employment"] as const).map((facet) => {
          const chips = facetData[facet];
          const headId = `match-facet-${facet}`;
          return (
            <section
              key={facet}
              className="jp-matchprefs__facet"
              role="group"
              aria-labelledby={headId}
            >
              <p
                id={headId}
                className="jp-popover__title jp-matchprefs__facethead"
              >
                {facetLabel[facet]}
              </p>
              {chips.length === 0 ? (
                <p className="jp-matchprefs__empty text-body-sm text-text-secondary">
                  {facetEmpty[facet]}
                </p>
              ) : (
                <ul className="jp-chiplist">
                  {chips.map((chip) => (
                    <li key={chip.conceptId}>
                      <PreferenceChip
                        ref={(el) => {
                          removeRefs.current.set(
                            refKey(facet, chip.conceptId),
                            el
                          );
                        }}
                        label={chip.label}
                        onRemove={() =>
                          removeChip(facet, chip.conceptId, false)
                        }
                        onRemoveKey={() =>
                          removeChip(facet, chip.conceptId, true)
                        }
                      />
                    </li>
                  ))}
                </ul>
              )}
            </section>
          );
        })}
      </div>

      <div className="jp-matchprefs__addrow">
        <Button
          ref={addButtonRef}
          type="button"
          variant="secondary"
          aria-haspopup="dialog"
          onClick={() => setDialogOpen(true)}
        >
          {t("matchPrefs.add")}
        </Button>
        {/* Ömsesidigt uteslutande live-regioner: fel = assertiv alert (cause +
            action), annars artig status-kvittens "Sparat HH:mm". Aldrig en
            alert nästlad i en status-region (inkonsekvent SR-annonsering). */}
        {saveError ? (
          <p role="alert" className="text-body-sm text-danger-600">
            {t("matchPrefs.saveError")}
          </p>
        ) : (
          <p
            role="status"
            aria-live="polite"
            className="text-body-sm text-text-secondary"
          >
            {!isSaving && savedAt
              ? t("matchPrefs.savedAt", {
                  time: savedAt.toLocaleTimeString("sv-SE", {
                    hour: "2-digit",
                    minute: "2-digit",
                  }),
                })
              : ""}
          </p>
        )}
      </div>

      <MatchPreferencesDialog
        open={dialogOpen}
        onOpenChange={setDialogOpen}
        occupationFields={occupationFields}
        regions={regions}
        employmentTypes={employmentTypes}
        persistedOccupationGroups={occupationGroups}
        persistedRegions={selectedRegions}
        persistedMunicipalities={selectedMunicipalities}
        persistedEmploymentTypes={selectedEmployment}
        onSaved={onDialogSaved}
        importCvHref={IMPORT_CV_HREF}
      />
    </section>
  );
}
