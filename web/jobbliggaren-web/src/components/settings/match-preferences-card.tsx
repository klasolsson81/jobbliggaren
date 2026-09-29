"use client";

// "use client": the card holds each part as the server last acknowledged it and the chip removals
// still in flight, writes each part through its own queue, and moves focus after a removal and when
// a part's dialog closes. None of that runs in a Server Component.

import dynamic from "next/dynamic";
import {
  type ReactNode,
  type Ref,
  startTransition,
  useId,
  useMemo,
  useRef,
  useState,
} from "react";
import { useLocale, useTranslations } from "next-intl";
import { codedTaxonomyOptions } from "@/lib/i18n/coded-taxonomy";
import { DISTANS_CHIP_ID } from "@/lib/job-ads/ort-selection";
import type {
  TaxonomyOccupationField,
  TaxonomyOption,
  TaxonomyRegion,
} from "@/lib/dto/taxonomy";
import type { SkillGroup } from "@/lib/dto/skills";
import type { ActionResult } from "@/lib/actions/_action-result";
import type { UpdateMatchPreferencesInput } from "@/lib/actions/match-preferences-schemas";
import { updateMatchPreferencesAction } from "@/lib/actions/match-preferences";
import { Button } from "@/components/ui/button";
import {
  flattenOccupationGroups,
  filterOptions,
  groupsForSelected,
  hasValues,
  labelsForSelected,
  toPatch,
  withoutMembers,
  type EmploymentTypesValue,
  type ExperienceValue,
  type LocationsValue,
  type MatchPart,
  type OccupationExperienceEntry,
  type OccupationsValue,
  type Option,
  type PartValue,
  type SkillChip,
  type SkillsValue,
} from "./match-preferences-shared";
import { ChipList } from "./section-helpers";
import { Outcome, type WriteOutcome } from "./write-outcome";

// #748: the dialog's static import chain (dialog + OccupationSection +
// SkillSection + RegionMunicipalityCascade + CV-upload/suggest wiring, ~2.5k
// lines of client code) is code-split out of the /mina-sidor route bundle
// and fetched on first open. `ssr: false` is deliberate: the dialog only ever
// renders client-side after a click (gated behind the first opening), and it
// gives next/dynamic its OWN Suspense boundary (fallback null) so a first-open
// chunk fetch never bubbles up to the route's loading.tsx and flashes a
// whole-page skeleton. The part buttons stay statically rendered → no CLS.
const MatchPreferencesDialogLazy = dynamic(
  () => import("./match-preferences-dialog").then((m) => m.MatchPreferencesDialog),
  { ssr: false }
);

// Pure helpers re-exporteras så befintliga tester/konsumenter (som importerar
// dem härifrån) inte bryts; definitionen bor i match-preferences-shared.
export { flattenOccupationGroups, filterOptions };

/** The parts drawn as chips; Antal års erfarenhet is one value. */
type ListPart = Exclude<MatchPart, "experience">;
const LIST_PARTS: ReadonlyArray<ListPart> = [
  "occupations",
  "skills",
  "locations",
  "employmentTypes",
];

/** Each part as the server last acknowledged it. */
interface Parts {
  readonly occupations: OccupationsValue;
  readonly skills: SkillsValue;
  readonly locations: LocationsValue;
  readonly employmentTypes: EmploymentTypesValue;
  readonly experience: ExperienceValue;
}

/** The chip members whose removal is written or waiting to be, per part. */
type Removals = Readonly<Record<ListPart, ReadonlySet<string>>>;

const NO_REMOVALS: Removals = {
  occupations: new Set(),
  skills: new Set(),
  locations: new Set(),
  employmentTypes: new Set(),
};

const NO_OUTCOMES: Readonly<Record<MatchPart, WriteOutcome | null>> = {
  occupations: null,
  skills: null,
  locations: null,
  employmentTypes: null,
  experience: null,
};

const SETTLED: Promise<unknown> = Promise.resolve();

// The delivered form of a text button (`START_OVER_LINK` in change-email-setting.tsx): underlined
// at rest, and a 40/44 px hit area without a taller box.
const PART_BUTTON =
  "-my-2 h-auto px-0 py-2 text-brand-700 underline underline-offset-2 max-md:-my-2.5 max-md:py-2.5";

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
  /** #551 punkt 4: distans-axeln. */
  readonly initialRemote: boolean;
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
   * vidare till Yrken-dialogen som pre-fill.
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

/**
 * The Matchning card of /mina-sidor (#1918): five parts, each changed on its own through its own
 * dialog, and each write carrying only its own part (ADR 0147).
 */
export function MatchPreferencesCard({
  occupationFields,
  regions,
  employmentTypes,
  initialOccupationGroups,
  initialRegions,
  initialMunicipalities,
  initialRemote,
  initialEmploymentTypes,
  initialSkills,
  initialSkillGroups,
  initialExperienceYears,
  initialOccupationExperience,
  degraded,
}: MatchPreferencesCardProps) {
  const t = useTranslations("settings");
  const tEnum = useTranslations("jobads.enums");
  const locale = useLocale();
  // Stabil identitet: en ny collator per render hade legat i memons deps nedan och
  // gjort den till en garanterad miss.
  const collator = useMemo(() => new Intl.Collator(locale), [locale]);
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
  // Anställningsform är allmänsubstantiv och byter språk med locale:n (#1537); ort och
  // yrkesgrupp ovan är egennamn och passerar oöversatta.
  const employmentOptions = useMemo<ReadonlyArray<Option>>(
    () => codedTaxonomyOptions(tEnum, collator, employmentTypes),
    [employmentTypes, tEnum, collator]
  );

  // The ref is what a queued write reads when it runs; the state is what renders. Both change
  // together, and only when a write succeeds.
  const [parts, setParts] = useState<Parts>(() => ({
    occupations: {
      part: "occupations",
      groups: initialOccupationGroups,
      experience: initialOccupationExperience,
    },
    skills: { part: "skills", skills: initialSkills, skillGroups: initialSkillGroups },
    locations: {
      part: "locations",
      regions: initialRegions,
      municipalities: initialMunicipalities,
      remote: initialRemote,
    },
    employmentTypes: { part: "employmentTypes", types: initialEmploymentTypes },
    experience: { part: "experience", years: initialExperienceYears },
  }));
  const partsRef = useRef(parts);
  // Not useOptimistic: its layer drops only when every pending transition has settled, so one
  // refused removal would stay hidden until an unrelated write finished.
  const [removals, setRemovals] = useState<Removals>(NO_REMOVALS);
  const [outcomes, setOutcomes] = useState(NO_OUTCOMES);
  const queues = useRef<Record<MatchPart, Promise<unknown>>>({
    occupations: SETTLED,
    skills: SETTLED,
    locations: SETTLED,
    employmentTypes: SETTLED,
    experience: SETTLED,
  });
  const partButtons = useRef<Partial<Record<MatchPart, HTMLButtonElement | null>>>({});
  // One dialog instance per opening, so its draft is seeded from the part as shown then.
  const [dialog, setDialog] = useState<{
    readonly seq: number;
    readonly value: PartValue;
    readonly open: boolean;
  } | null>(null);

  function send(patch: UpdateMatchPreferencesInput): Promise<ActionResult> {
    return new Promise((resolve) => {
      startTransition(async () => {
        try {
          resolve(await updateMatchPreferencesAction(patch));
        } catch {
          resolve({ success: false, error: t("matchPrefs.errors.network") });
        }
      });
    });
  }

  /**
   * ADR 0147 D9: a part's writes run one after another, and each binds its payload when it runs,
   * after the part's previous write has an outcome. No write leans on Next
   * dispatching Server Actions one at a time.
   */
  function enqueueWrite(
    part: MatchPart,
    bind: () => { value: PartValue; patch: UpdateMatchPreferencesInput },
    settle?: () => void
  ): Promise<ActionResult> {
    setOutcomes((prev) => ({ ...prev, [part]: null }));
    const run = queues.current[part].then(async () => {
      const { value, patch } = bind();
      const result = await send(patch);
      if (result.success) {
        partsRef.current = withPart(partsRef.current, value);
        setParts(partsRef.current);
      }
      settle?.();
      setOutcomes((prev) => ({
        ...prev,
        [part]: result.success
          ? { ok: true, at: new Date() }
          : { ok: false, error: result.error },
      }));
      return result;
    });
    queues.current[part] = run.then(
      () => undefined,
      () => undefined
    );
    return run;
  }

  /**
   * Hides the chip at once and writes the part without it. A refusal restores this removal's own
   * members, never a snapshot from the click, so the part again shows what the server holds.
   */
  function removeChip(part: ListPart, members: ReadonlyArray<string>) {
    const own = new Set(members);
    setRemovals((prev) => ({ ...prev, [part]: new Set([...prev[part], ...own]) }));
    void enqueueWrite(
      part,
      () => {
        const value = withoutMembers(partsRef.current[part], own);
        // The card renders no years, so it sends none and the server keeps them for the
        // occupations still chosen (ADR 0147 D2).
        const patch =
          value.part === "occupations"
            ? { occupations: { preferredOccupationGroups: [...value.groups] } }
            : toPatch(value);
        return { value, patch };
      },
      () =>
        setRemovals((prev) => ({
          ...prev,
          [part]: new Set([...prev[part]].filter((id) => !own.has(id))),
        }))
    );
  }

  function saveFromDialog(value: PartValue): Promise<ActionResult> {
    return enqueueWrite(value.part, () => ({ value, patch: toPatch(value) }));
  }

  if (degraded) {
    return (
      <section className="jp-card">
        <h2 className="jp-card__title">{t("matchPrefs.title")}</h2>
        <p className="text-body-sm text-text-primary">
          {t("matchPrefs.degraded")}
        </p>
      </section>
    );
  }

  // What the card shows: each part as acknowledged, without the removals still in flight.
  const shown: Parts = {
    occupations: withoutMembers(parts.occupations, removals.occupations),
    skills: withoutMembers(parts.skills, removals.skills),
    locations: withoutMembers(parts.locations, removals.locations),
    employmentTypes: withoutMembers(parts.employmentTypes, removals.employmentTypes),
    experience: parts.experience,
  };

  // Each rendered chip carries its member-id set: most parts are 1:1
  // (`[conceptId]`), but a SKILL chip is a GROUP whose member ids (the ESCO + AF
  // twin) must ALL be dropped on removal (#277).
  const asChips = (options: ReadonlyArray<Option>): ReadonlyArray<SkillChip> =>
    options.map((o) => ({ ...o, memberConceptIds: [o.conceptId] }));

  const chips: Readonly<Record<ListPart, ReadonlyArray<SkillChip>>> = {
    occupations: asChips(labelsForSelected(shown.occupations.groups, occupationOptions)),
    skills: groupsForSelected(shown.skills.skills, shown.skills.skillGroups),
    // Distans först (bredaste ort-valet), sedan valda län, sedan enskilda kommuner.
    locations: asChips([
      ...(shown.locations.remote
        ? [{ conceptId: DISTANS_CHIP_ID, label: t("matchPrefs.cascade.distans") }]
        : []),
      ...labelsForSelected(shown.locations.regions, regionOptions),
      ...labelsForSelected(shown.locations.municipalities, municipalityOptions),
    ]),
    employmentTypes: asChips(labelsForSelected(shown.employmentTypes.types, employmentOptions)),
  };

  const title: Readonly<Record<MatchPart, string>> = {
    occupations: t("matchPrefs.facetOccupations"),
    skills: t("matchPrefs.facetSkills"),
    locations: t("matchPrefs.facetOrter"),
    employmentTypes: t("matchPrefs.facetEmployment"),
    experience: t("matchPrefs.experience.label"),
  };
  const empty: Readonly<Record<ListPart, string>> = {
    occupations: t("matchPrefs.emptyOccupations"),
    skills: t("matchPrefs.emptySkills"),
    locations: t("matchPrefs.emptyOrter"),
    employmentTypes: t("matchPrefs.emptyEmployment"),
  };

  function openDialog(part: MatchPart) {
    setDialog((prev) => ({ seq: (prev?.seq ?? 0) + 1, value: shown[part], open: true }));
  }

  const years = shown.experience.years;

  return (
    <section className="jp-card jp-matchprefs">
      <h2 className="jp-card__title">{t("matchPrefs.title")}</h2>

      {LIST_PARTS.map((part) => (
        <PartGroup
          key={part}
          title={title[part]}
          filled={hasValues(shown[part])}
          outcome={outcomes[part]}
          onEdit={() => openDialog(part)}
          buttonRef={(el) => {
            partButtons.current[part] = el;
          }}
        >
          <ChipList
            className="jp-chiplist"
            items={chips[part]}
            onRemove={(chip) => removeChip(part, chip.memberConceptIds)}
            focusAfterLast={() => partButtons.current[part]}
          />
          {chips[part].length === 0 && (
            <p className="text-body-sm text-text-primary">{empty[part]}</p>
          )}
        </PartGroup>
      ))}

      <PartGroup
        title={title.experience}
        filled={hasValues(shown.experience)}
        outcome={outcomes.experience}
        onEdit={() => openDialog("experience")}
        buttonRef={(el) => {
          partButtons.current.experience = el;
        }}
      >
        <p className="text-body-sm text-text-primary">
          {years === null
            ? t("matchPrefs.experience.reviewEmpty")
            : t("matchPrefs.experience.reviewValue", { years })}
        </p>
      </PartGroup>

      {dialog !== null && (
        <MatchPreferencesDialogLazy
          key={dialog.seq}
          open={dialog.open}
          onOpenChange={(open) =>
            setDialog((current) =>
              current !== null && current.seq === dialog.seq ? { ...current, open } : current
            )
          }
          value={dialog.value}
          occupationFields={occupationFields}
          regions={regions}
          employmentTypes={employmentTypes}
          importCvHref={IMPORT_CV_HREF}
          onSave={saveFromDialog}
          onCloseAutoFocus={(event) => {
            event.preventDefault();
            partButtons.current[dialog.value.part]?.focus();
          }}
        />
      )}
    </section>
  );
}

function withPart(parts: Parts, value: PartValue): Parts {
  switch (value.part) {
    case "occupations":
      return { ...parts, occupations: value };
    case "skills":
      return { ...parts, skills: value };
    case "locations":
      return { ...parts, locations: value };
    case "employmentTypes":
      return { ...parts, employmentTypes: value };
    case "experience":
      return { ...parts, experience: value };
  }
}

/**
 * One part: its name as an h3 with its button to the right (below it on a narrow card), what it
 * holds, and the outcome of its latest write (#1391's form).
 */
function PartGroup({
  title,
  filled,
  outcome,
  onEdit,
  buttonRef,
  children,
}: {
  readonly title: string;
  /** The part holds something: "Ändra", else "Lägg till". */
  readonly filled: boolean;
  readonly outcome: WriteOutcome | null;
  readonly onEdit: () => void;
  readonly buttonRef: Ref<HTMLButtonElement>;
  readonly children: ReactNode;
}) {
  const t = useTranslations("settings");
  const titleId = useId();
  const buttonId = useId();
  const errorId = useId();
  return (
    <section className="jp-settings-group" aria-labelledby={titleId}>
      <div className="jp-matchprefs__parthead">
        <h3 id={titleId} className="jp-settings-group__title">
          {title}
        </h3>
        {/* One element whatever its text, so focus that lands on it survives the switch. */}
        <Button
          ref={buttonRef}
          id={buttonId}
          type="button"
          variant="link"
          size="sm"
          className={PART_BUTTON}
          aria-labelledby={`${buttonId} ${titleId}`}
          aria-haspopup="dialog"
          aria-describedby={outcome?.ok === false ? errorId : undefined}
          onClick={onEdit}
        >
          {filled ? t("matchPrefs.change") : t("matchPrefs.add")}
        </Button>
      </div>
      {children}
      <Outcome id={errorId} outcome={outcome} />
    </section>
  );
}
