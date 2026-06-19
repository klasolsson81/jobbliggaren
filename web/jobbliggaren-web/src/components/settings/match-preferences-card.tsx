"use client";

// "use client": kortet håller lokal vald-mängd-state (tre dimensioner),
// kryssrute-toggle via tangentbord, useTransition runt save-action samt en
// härled-förslag-affordans med pending/error-state. Inget av detta går att
// göra i en Server Component.

import { useMemo, useState, useTransition } from "react";
import { Check } from "lucide-react";
import type {
  TaxonomyOccupationField,
  TaxonomyOption,
  TaxonomyRegion,
} from "@/lib/dto/taxonomy";
import {
  deriveOccupationsAction,
  updateMatchPreferencesAction,
} from "@/lib/actions/match-preferences";
import type { OccupationCandidate } from "@/lib/dto/match-preferences";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

/** Platt taxonomi-val (concept-id → svenskt namn). */
interface Option {
  readonly conceptId: string;
  readonly label: string;
}

interface MatchPreferencesCardProps {
  /** Yrkesområden (med underordnade yrkesgrupper) → kortet plattar själv. */
  readonly occupationFields: ReadonlyArray<TaxonomyOccupationField>;
  /** Län-options (concept-id + namn). */
  readonly regions: ReadonlyArray<TaxonomyRegion>;
  /** Anställningsform-options (råa JobTech-labels, "honest 8"). */
  readonly employmentTypes: ReadonlyArray<TaxonomyOption>;
  /** Sparade val att initiera från (concept-id-listor från profilen). */
  readonly initialOccupationGroups: ReadonlyArray<string>;
  readonly initialRegions: ReadonlyArray<string>;
  readonly initialEmploymentTypes: ReadonlyArray<string>;
  /**
   * Civil degradering: när taxonomin inte kunde läsas in passar föräldern
   * `false` för optionerna och sätter `degraded` så kortet visar en lugn
   * "kunde inte läsas in just nu"-text i stället för väljarna.
   */
  readonly degraded: boolean;
}

/**
 * Plattar `occupationFields[].occupationGroups[]` till en enkel
 * `{conceptId,label}`-lista. Ren funktion (testbar utan rendering).
 */
export function flattenOccupationGroups(
  fields: ReadonlyArray<TaxonomyOccupationField>
): ReadonlyArray<Option> {
  return fields.flatMap((field) =>
    field.occupationGroups.map((group) => ({
      conceptId: group.conceptId,
      label: group.label,
    }))
  );
}

/**
 * Substring-filtrerar options på label (case-insensitive, locale "sv").
 * Tom/blank query → hela listan. Ren funktion (testbar utan rendering).
 */
export function filterOptions(
  options: ReadonlyArray<Option>,
  query: string
): ReadonlyArray<Option> {
  const q = query.trim().toLocaleLowerCase("sv");
  if (q.length === 0) return options;
  return options.filter((o) => o.label.toLocaleLowerCase("sv").includes(q));
}

function toggle(
  selected: ReadonlyArray<string>,
  conceptId: string
): string[] {
  return selected.includes(conceptId)
    ? selected.filter((v) => v !== conceptId)
    : [...selected, conceptId];
}

/** En kryssrute-rad. Speglar `.jp-checkitem`-mönstret från jobb-klass2-panel. */
function CheckItem({
  label,
  checked,
  onToggle,
}: {
  label: string;
  checked: boolean;
  onToggle: () => void;
}) {
  return (
    <div
      className="jp-checkitem"
      role="checkbox"
      aria-checked={checked}
      tabIndex={0}
      onClick={onToggle}
      onKeyDown={(e) => {
        if (e.key === " " || e.key === "Enter") {
          e.preventDefault();
          onToggle();
        }
      }}
    >
      <span className="jp-checkitem__box">
        {checked && <Check size={14} aria-hidden="true" />}
      </span>
      {label}
    </div>
  );
}

export function MatchPreferencesCard({
  occupationFields,
  regions,
  employmentTypes,
  initialOccupationGroups,
  initialRegions,
  initialEmploymentTypes,
  degraded,
}: MatchPreferencesCardProps) {
  const occupationOptions = useMemo(
    () => flattenOccupationGroups(occupationFields),
    [occupationFields]
  );
  const regionOptions = useMemo<ReadonlyArray<Option>>(
    () =>
      regions.map((r) => ({ conceptId: r.conceptId, label: r.label })),
    [regions]
  );
  const employmentOptions = useMemo<ReadonlyArray<Option>>(
    () =>
      employmentTypes.map((e) => ({
        conceptId: e.conceptId,
        label: e.label,
      })),
    [employmentTypes]
  );

  const [occupationGroups, setOccupationGroups] = useState<
    ReadonlyArray<string>
  >(initialOccupationGroups);
  const [selectedRegions, setSelectedRegions] = useState<ReadonlyArray<string>>(
    initialRegions
  );
  const [selectedEmployment, setSelectedEmployment] = useState<
    ReadonlyArray<string>
  >(initialEmploymentTypes);

  // Yrkesgrupp-filter (substring).
  const [occupationFilter, setOccupationFilter] = useState("");
  const filteredOccupations = useMemo(
    () => filterOptions(occupationOptions, occupationFilter),
    [occupationOptions, occupationFilter]
  );

  // Härled-förslag (propose-and-approve).
  const [deriveTitle, setDeriveTitle] = useState("");
  const [candidates, setCandidates] = useState<
    ReadonlyArray<OccupationCandidate>
  >([]);
  const [deriveSubmitted, setDeriveSubmitted] = useState(false);
  const [deriveError, setDeriveError] = useState<string | null>(null);
  const [isDeriving, startDeriving] = useTransition();

  // Save.
  const [isSaving, startSaving] = useTransition();
  const [savedAt, setSavedAt] = useState<Date | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);

  function onDerive(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    const title = deriveTitle.trim();
    if (title.length === 0) return;
    setDeriveError(null);
    startDeriving(async () => {
      const result = await deriveOccupationsAction(title);
      setDeriveSubmitted(true);
      if (result.success) {
        setCandidates(result.candidates);
      } else {
        setCandidates([]);
        setDeriveError(result.error);
      }
    });
  }

  function onSave() {
    setSaveError(null);
    startSaving(async () => {
      const result = await updateMatchPreferencesAction({
        preferredOccupationGroups: [...occupationGroups],
        preferredRegions: [...selectedRegions],
        preferredEmploymentTypes: [...selectedEmployment],
      });
      if (result.success) {
        setSavedAt(new Date());
      } else {
        setSaveError(result.error);
      }
    });
  }

  if (degraded) {
    return (
      <section className="jp-card" id="matchning">
        <h2 className="jp-card__title">Matchning</h2>
        <p className="text-body-sm text-text-secondary">
          Dina matchningsval kunde inte läsas in just nu. Försök ladda om sidan
          om en stund.
        </p>
      </section>
    );
  }

  return (
    <section className="jp-card jp-matchprefs" id="matchning">
      <h2 className="jp-card__title">Matchning</h2>
      <p className="text-body-sm text-text-secondary">
        Ange vilka yrken, regioner och anställningsformer du söker. Vi använder
        det för att visa hur väl varje annons matchar din profil. Alla fält är
        frivilliga.
      </p>

      <div className="flex flex-col gap-7 mt-6">
        {/* ── Yrkesgrupper ───────────────────────────── */}
        <div
          role="group"
          aria-label="Yrkesgrupper"
          aria-describedby="match-occupation-help"
        >
          <div className="jp-panel__sectionhead">
            <span className="jp-popover__title">Yrkesgrupper</span>
            {occupationGroups.length > 0 && (
              <button
                type="button"
                className="jp-clearlink"
                onClick={() => setOccupationGroups([])}
              >
                Rensa
              </button>
            )}
          </div>

          {/* Härled-förslag ur en yrkestitel (förslag → bekräfta, skriver
              aldrig direkt). Egen form så Enter triggar Föreslå, inte Spara. */}
          <form onSubmit={onDerive} className="flex flex-col gap-1.5 mb-4">
            <Label htmlFor="match-derive-title">
              Föreslå utifrån en yrkestitel
            </Label>
            <div className="flex gap-2">
              <Input
                id="match-derive-title"
                type="text"
                value={deriveTitle}
                onChange={(e) => setDeriveTitle(e.target.value)}
                maxLength={120}
                disabled={isDeriving}
                aria-describedby="match-derive-help"
              />
              <Button
                type="submit"
                variant="secondary"
                disabled={isDeriving || deriveTitle.trim().length === 0}
              >
                {isDeriving ? "Söker…" : "Föreslå"}
              </Button>
            </div>
            <p
              id="match-derive-help"
              className="text-body-sm text-text-secondary"
            >
              Skriv en yrkestitel så föreslår vi yrkesgrupper att lägga till. Du
              väljer själv vilka som tas med.
            </p>
          </form>

          {deriveError && (
            <p role="alert" className="text-body-sm text-danger-600 mb-3">
              {deriveError}
            </p>
          )}

          {!deriveError && deriveSubmitted && candidates.length === 0 && (
            <p className="text-body-sm text-text-secondary mb-3">
              Inga förslag för den titeln. Du kan välja yrkesgrupper i listan
              nedan i stället.
            </p>
          )}

          {candidates.length > 0 && (
            <div className="mb-4">
              <p className="text-body-sm text-text-secondary mb-1.5">
                Förslag — välj de som passar för att lägga till dem:
              </p>
              <div role="group" aria-label="Föreslagna yrkesgrupper">
                {candidates.map((c) => (
                  <CheckItem
                    key={c.occupationGroupConceptId}
                    label={c.occupationGroupLabel}
                    checked={occupationGroups.includes(
                      c.occupationGroupConceptId
                    )}
                    onToggle={() =>
                      setOccupationGroups((prev) =>
                        toggle(prev, c.occupationGroupConceptId)
                      )
                    }
                  />
                ))}
              </div>
            </div>
          )}

          {/* Filter + scrollbar lista (~400 ssyk-4-grupper). */}
          <div className="flex flex-col gap-1.5 mb-2">
            <Label htmlFor="match-occupation-filter">
              Filtrera yrkesgrupper
            </Label>
            <Input
              id="match-occupation-filter"
              type="text"
              value={occupationFilter}
              onChange={(e) => setOccupationFilter(e.target.value)}
              maxLength={80}
            />
          </div>
          <p
            id="match-occupation-help"
            className="text-body-sm text-text-secondary mb-2"
          >
            Välj de yrkesgrupper du söker jobb inom. Lämnar du tomt matchar vi
            inte på yrke — det är helt i sin ordning.
          </p>

          <div className="jp-matchprefs__scroll">
            {filteredOccupations.length === 0 ? (
              <p className="text-body-sm text-text-secondary px-4 py-3">
                Ingen yrkesgrupp matchar filtret.
              </p>
            ) : (
              filteredOccupations.map((o) => (
                <CheckItem
                  key={o.conceptId}
                  label={o.label}
                  checked={occupationGroups.includes(o.conceptId)}
                  onToggle={() =>
                    setOccupationGroups((prev) => toggle(prev, o.conceptId))
                  }
                />
              ))
            )}
          </div>
        </div>

        {/* ── Regioner ───────────────────────────────── */}
        <div
          role="group"
          aria-label="Regioner"
          aria-describedby="match-region-help"
        >
          <div className="jp-panel__sectionhead">
            <span className="jp-popover__title">Regioner</span>
            {selectedRegions.length > 0 && (
              <button
                type="button"
                className="jp-clearlink"
                onClick={() => setSelectedRegions([])}
              >
                Rensa
              </button>
            )}
          </div>
          <p
            id="match-region-help"
            className="text-body-sm text-text-secondary mb-2"
          >
            Välj var du vill arbeta. Tomt fält betyder att du är öppen för hela
            landet.
          </p>
          <div className="jp-matchprefs__scroll">
            {regionOptions.map((o) => (
              <CheckItem
                key={o.conceptId}
                label={o.label}
                checked={selectedRegions.includes(o.conceptId)}
                onToggle={() =>
                  setSelectedRegions((prev) => toggle(prev, o.conceptId))
                }
              />
            ))}
          </div>
        </div>

        {/* ── Anställningsformer ──────────────────────── */}
        <div
          role="group"
          aria-label="Anställningsformer"
          aria-describedby="match-employment-help"
        >
          <div className="jp-panel__sectionhead">
            <span className="jp-popover__title">Anställningsformer</span>
            {selectedEmployment.length > 0 && (
              <button
                type="button"
                className="jp-clearlink"
                onClick={() => setSelectedEmployment([])}
              >
                Rensa
              </button>
            )}
          </div>
          <p
            id="match-employment-help"
            className="text-body-sm text-text-secondary mb-2"
          >
            Välj de anställningsformer som passar dig. Tomt fält betyder att alla
            former visas.
          </p>
          <div>
            {employmentOptions.map((o) => (
              <CheckItem
                key={o.conceptId}
                label={o.label}
                checked={selectedEmployment.includes(o.conceptId)}
                onToggle={() =>
                  setSelectedEmployment((prev) => toggle(prev, o.conceptId))
                }
              />
            ))}
          </div>
        </div>
      </div>

      {/* ── Spara ──────────────────────────────────── */}
      {saveError && (
        <p role="alert" className="text-body-sm text-danger-600 mt-5">
          {saveError}
        </p>
      )}
      {savedAt && !saveError && (
        <p
          role="status"
          aria-live="polite"
          className="text-body-sm text-text-secondary mt-5"
        >
          Sparat {savedAt.toLocaleTimeString("sv-SE", {
            hour: "2-digit",
            minute: "2-digit",
          })}
          .
        </p>
      )}
      <div className="mt-5">
        <Button type="button" onClick={onSave} disabled={isSaving}>
          {isSaving ? "Sparar…" : "Spara matchningsönskemål"}
        </Button>
      </div>
    </section>
  );
}
