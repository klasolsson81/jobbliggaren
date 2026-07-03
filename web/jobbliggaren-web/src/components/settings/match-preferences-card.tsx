"use client";

// "use client": kortet håller lokal vald-mängd-state (tre dimensioner),
// optimistisk chip-borttagning med useTransition runt save-action +
// revert-vid-fel, tangentbords-borttagning med fokus-flytt till grannen, samt
// en dialog-öppna-affordans. Inget av detta går i en Server Component.

import { useMemo, useRef, useState, useTransition } from "react";
import { useFormatter, useTranslations } from "next-intl";
import { formatTime } from "@/lib/i18n/format";
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
  groupsForSelected,
  labelsForSelected,
  projectOccupationExperience,
  recordFromOccupationExperience,
  type Option,
  type OccupationExperienceEntry,
  type SkillChip,
} from "./match-preferences-shared";
import type { SkillGroup } from "@/lib/dto/skills";
import { PreferenceChip } from "./preference-chip";
import { MatchPreferencesDialog } from "./match-preferences-dialog";

// Pure helpers re-exporteras så befintliga tester/konsumenter (som importerar
// dem härifrån) inte bryts; definitionen bor i match-preferences-shared.
export { flattenOccupationGroups, filterOptions };

/** Yrken/Kompetenser/Orter/Anställningsformer — facetterna kortet renderar.
 * "orter" är EN dimension i två granulariteter (län + kommun, Spår 3 PR-D);
 * "skills" är CV-seedade kompetenser (STEG 3 / ADR 0079). */
type Facet = "occupations" | "skills" | "orter" | "employment";

/** A rendered chip + the FULL set of ids it stands for. Most facets are 1:1
 *  (`memberConceptIds === [conceptId]`); a skill chip is a GROUP whose member
 *  ids (the ESCO + AF twin) are all dropped on removal (#277). Structurally a
 *  `SkillChip` — reused so the uniform chip render carries member ids. */
type RenderChip = SkillChip;

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
  /** STEG 3 / ADR 0079: kompetens-axeln + erfarenhet (sparade från profilen). */
  readonly initialSkills: ReadonlyArray<string>;
  /**
   * STEG 3 / ADR 0079 + ADR 0047 + #277: pre-resolverade GRUPPER för de sparade
   * kompetens-concept-id (server-side reverse-lookup i settings-sidan). Seedar
   * grupp-storen så en återvändande användare ser NAMN och EN chip per twin-par
   * vid kall laddning, utan att öppna dialogen. Den platta skill-taxonomin
   * skickas aldrig som träd → utan denna seed renderas råa concept-id (rå
   * token på läs-yta, ADR 0047). Okänt/borttaget id faller fortfarande
   * tillbaka på id-strängen (graceful, backend droppar okända).
   */
  readonly initialSkillGroups: ReadonlyArray<SkillGroup>;
  readonly initialExperienceYears: number | null;
  /**
   * exp-per-occ (ADR 0079-amendment PR-4): den persisterade per-yrke-
   * erfarenhets-overlayn (gles delmängd av `initialOccupationGroups`). Förs
   * vidare till dialogen som pre-fill och adopteras lokalt efter save så
   * kortets läs-rader är koherenta utan remount.
   */
  readonly initialOccupationExperience: ReadonlyArray<OccupationExperienceEntry>;
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
  initialSkills,
  initialSkillGroups,
  initialExperienceYears,
  initialOccupationExperience,
  degraded,
}: MatchPreferencesCardProps) {
  const t = useTranslations("settings");
  const format = useFormatter();
  // Facet-rubriker och tom-state-texter per dimension (svenska via katalogen).
  const facetLabel: Record<Facet, string> = {
    occupations: t("matchPrefs.facetOccupations"),
    skills: t("matchPrefs.facetSkills"),
    orter: t("matchPrefs.facetOrter"),
    employment: t("matchPrefs.facetEmployment"),
  };
  const facetEmpty: Record<Facet, string> = {
    occupations: t("matchPrefs.emptyOccupations"),
    skills: t("matchPrefs.emptySkills"),
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
  const [selectedSkills, setSelectedSkills] =
    useState<ReadonlyArray<string>>(initialSkills);
  const [experienceYears, setExperienceYears] = useState<number | null>(
    initialExperienceYears
  );
  // exp-per-occ (ADR 0079-amendment PR-4): per-yrke-erfarenhets-overlay. Förs
  // till dialogen som pre-fill och adopteras efter save (annars driver kortets
  // läs-rader isär från SSOT tills remount). En map för O(1)-uppslag per chip.
  const [occupationExperience, setOccupationExperience] = useState<
    Readonly<Record<string, number | null>>
  >(() => recordFromOccupationExperience(initialOccupationExperience));
  // Skill group-store (canonical conceptId → SkillGroup). The flat skill
  // taxonomy is never shipped to the FE as a tree, so a saved skill has no tree
  // lookup — the card adopts the groups the dialog surfaced (post-save) and
  // otherwise falls back to the id (groupsForSelected). Seeded server-side from
  // the persisted skills' grouped reverse-lookup (ADR 0047 + #277) so a
  // returning user sees NAMES and ONE chip per twin-par on a cold load without
  // opening the dialog; the on-save adoption (onDialogSaved) refreshes it after.
  const [skillGroups, setSkillGroups] =
    useState<ReadonlyArray<SkillGroup>>(initialSkillGroups);

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
    skills: ReadonlyArray<string>;
  }

  const currentSets = (): PrefSets => ({
    occupations: occupationGroups,
    regions: selectedRegions,
    municipalities: selectedMunicipalities,
    employment: selectedEmployment,
    skills: selectedSkills,
  });

  /** Persisterar HELA mängden (full-replace) med revert-vid-fel. Region + kommun
   *  skickas atomiskt i samma PUT (NOTE-1). STEG 3 / ADR 0079: kompetens +
   *  erfarenhet skickas i SAMMA PUT så en chip-borttagning i en annan dimension
   *  aldrig nollar dem (page-wipe-guard). exp-per-occ (ADR 0079-amendment PR-4):
   *  per-yrke-overlayn skickas också med, scopad till de NYA yrkena — så en
   *  chip-borttagning i en annan dimension aldrig nollar overlayn, OCH ett
   *  borttaget yrke tappar sin overlay-rad (subset-regeln). */
  function persist(next: PrefSets, revert: () => void) {
    setSaveError(null);
    const occupationExperiencePayload = projectOccupationExperience(
      occupationExperience,
      next.occupations
    );
    startSaving(async () => {
      const result = await updateMatchPreferencesAction({
        preferredOccupationGroups: [...next.occupations],
        preferredRegions: [...next.regions],
        preferredMunicipalities: [...next.municipalities],
        preferredEmploymentTypes: [...next.employment],
        preferredSkills: [...next.skills],
        experienceYears,
        preferredOccupationExperience: [...occupationExperiencePayload],
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
  // #277: `memberConceptIds` carries the FULL set of ids a chip stands for.
  // For most facets that is just `[conceptId]`, but a skill chip is a GROUP — its
  // member ids (the ESCO + AF twin) must ALL be dropped in one removal, otherwise
  // a saved twin-pair would leave a dangling member id behind.
  function removeChip(
    facet: Facet,
    conceptId: string,
    keyboard: boolean,
    memberConceptIds: ReadonlyArray<string> = [conceptId]
  ) {
    const prev = currentSets();
    const axisFor: keyof PrefSets =
      facet === "occupations"
        ? "occupations"
        : facet === "skills"
          ? "skills"
          : facet === "employment"
            ? "employment"
            : ortAxisOf(conceptId);
    const list = prev[axisFor];
    // Drop EVERY member id of the chip's group (difference), not just the
    // canonical — so a twin chip removes both twin ids in one action.
    const drop = new Set(memberConceptIds);
    const nextList = list.filter((v) => !drop.has(v));
    const next: PrefSets = { ...prev, [axisFor]: nextList };

    // Bestäm grannen att flytta fokus till (CHIP-ordning, inte rå member-lista):
    // nästa kvarvarande chip, annars föregående, annars "Lägg till". Beräknas FÖRE
    // borttagningen mot den renderade chip-listan — så ett twin-grupp-chip (vars
    // member-id ej är egna chips) får en korrekt chip-granne med en ref. (Skills
    // = canonical-keyade grupp-chips; övriga facetter = 1:1, samma beteende.)
    const chipIds = facetData[facet].map((c) => c.conceptId);
    const chipIndex = chipIds.indexOf(conceptId);
    const neighbourConceptId =
      chipIds[chipIndex + 1] ?? chipIds[chipIndex - 1] ?? null;

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
    else if (axis === "skills") setSelectedSkills(value);
    else setSelectedEmployment(value);
  }

  function onDialogSaved(saved: {
    occupations: ReadonlyArray<string>;
    regions: ReadonlyArray<string>;
    municipalities: ReadonlyArray<string>;
    employment: ReadonlyArray<string>;
    skills: ReadonlyArray<string>;
    experienceYears: number | null;
    occupationExperience: ReadonlyArray<OccupationExperienceEntry>;
    skillGroups: ReadonlyArray<SkillGroup>;
  }) {
    // Dialogen skrev den fulla mängden (SSOT). Anta den lokalt så kortets chips
    // är koherenta direkt, och visa status-raden. (revalidatePath om-renderar
    // RSC men byter inte kortets useState-värden — därför adopterar vi här.)
    setOccupationGroups(saved.occupations);
    setSelectedRegions(saved.regions);
    setSelectedMunicipalities(saved.municipalities);
    setSelectedEmployment(saved.employment);
    setSelectedSkills(saved.skills);
    setExperienceYears(saved.experienceYears);
    // exp-per-occ (ADR 0079-amendment PR-4): adoptera den sparade per-yrke-overlayn.
    setOccupationExperience(
      recordFromOccupationExperience(saved.occupationExperience)
    );
    // Adoptera grupperna dialogen löst upp (sök/CV-förslag) så kortets
    // kompetens-chips renderar EN chip per twin-par med namn (#277).
    setSkillGroups(saved.skillGroups);
    setSavedAt(new Date());
  }

  if (degraded) {
    return (
      <section className="jp-card" id="matchning">
        <h2 className="jp-card__title">{t("matchPrefs.title")}</h2>
        <p className="text-body-sm text-text-primary">
          {t("matchPrefs.degraded")}
        </p>
      </section>
    );
  }

  // Each rendered chip carries its member-id set: most facets are 1:1
  // (`[conceptId]`), but a SKILL chip is a GROUP whose member ids (the ESCO + AF
  // twin) must ALL be dropped on removal (#277). `asChips` lifts a plain
  // {conceptId,label} Option to that shape; skills use `groupsForSelected`
  // directly so a saved twin-pair collapses to ONE chip.
  const asChips = (
    options: ReadonlyArray<Option>
  ): ReadonlyArray<RenderChip> =>
    options.map((o) => ({ ...o, memberConceptIds: [o.conceptId] }));

  const facetData: Record<Facet, ReadonlyArray<RenderChip>> = {
    occupations: asChips(labelsForSelected(occupationGroups, occupationOptions)),
    // Kompetens-chips: EN chip per grupp (twin-par) ur den adopterade grupp-
    // storen; saknade faller tillbaka på id (ingen träd-uppslagning för skills).
    skills: groupsForSelected(selectedSkills, skillGroups),
    // Ort-facetten: valda län FÖRST (helläns-axeln), sedan enskilda kommuner.
    orter: asChips([
      ...labelsForSelected(selectedRegions, regionOptions),
      ...labelsForSelected(selectedMunicipalities, municipalityOptions),
    ]),
    employment: asChips(labelsForSelected(selectedEmployment, employmentOptions)),
  };

  return (
    <section className="jp-card jp-matchprefs" id="matchning">
      <h2 className="jp-card__title">{t("matchPrefs.title")}</h2>
      <p className="text-body-sm text-text-primary">{t("matchPrefs.intro")}</p>

      <div className="jp-matchprefs__facets mt-5">
        {(["occupations", "skills", "orter", "employment"] as const).map((facet) => {
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
                <p className="jp-matchprefs__empty text-body-sm text-text-primary">
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
                          removeChip(
                            facet,
                            chip.conceptId,
                            false,
                            chip.memberConceptIds
                          )
                        }
                        onRemoveKey={() =>
                          removeChip(
                            facet,
                            chip.conceptId,
                            true,
                            chip.memberConceptIds
                          )
                        }
                      />
                    </li>
                  ))}
                </ul>
              )}
            </section>
          );
        })}
        {/* Erfarenhet: en enkel läs-rad (en siffra, inte chips). Ärlig tom-rad
            när inget angetts. Redigeras i dialogen. */}
        <section
          className="jp-matchprefs__facet"
          role="group"
          aria-labelledby="match-facet-experience"
        >
          <p
            id="match-facet-experience"
            className="jp-popover__title jp-matchprefs__facethead"
          >
            {t("matchPrefs.experience.label")}
          </p>
          <p className="jp-matchprefs__empty text-body-sm text-text-primary">
            {experienceYears === null
              ? t("matchPrefs.experience.reviewEmpty")
              : t("matchPrefs.experience.reviewValue", { years: experienceYears })}
          </p>
        </section>
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
                  time: formatTime(format, savedAt),
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
        persistedSkills={selectedSkills}
        persistedExperienceYears={experienceYears}
        // exp-per-occ (ADR 0079-amendment PR-4): pre-fill dialogen med overlayn
        // scopad till de fortfarande valda yrkena (subset-regeln).
        persistedOccupationExperience={projectOccupationExperience(
          occupationExperience,
          occupationGroups
        )}
        persistedSkillGroups={skillGroups}
        onSaved={onDialogSaved}
        importCvHref={IMPORT_CV_HREF}
      />
    </section>
  );
}
