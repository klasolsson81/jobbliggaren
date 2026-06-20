"use client";

// "use client": dialogen håller DRAFT-state för tre dimensioner, en
// kaskad-väljare med aktiv-kolumn-state, filter-input, två förslags-affordanser
// (yrkestitel + CV) med pending/diskriminerat resultat-state, och en
// useTransition runt save-action. Inget av detta går i en Server Component.

import { useMemo, useState, useTransition } from "react";
import { Check, ChevronRight } from "lucide-react";
import { Dialog, DialogContent, DialogTitle, DialogDescription } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import type {
  TaxonomyOccupationField,
  TaxonomyOption,
  TaxonomyRegion,
} from "@/lib/dto/taxonomy";
import type { OccupationCandidate } from "@/lib/dto/match-preferences";
import {
  deriveOccupationsAction,
  suggestOccupationsFromCvAction,
  updateMatchPreferencesAction,
  type CvSuggestResult,
} from "@/lib/actions/match-preferences";
import {
  flattenOccupationGroups,
  filterOptions,
  labelsForSelected,
  toggle,
  type Option,
} from "./match-preferences-shared";
import { PreferenceChip } from "./preference-chip";

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

/** En kryssrute-rad (.jp-checkitem-mönstret, delat med kortet/jobb-panelen). */
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

/** Pinnade, borttagbara chips överst i en sektion. Tom mängd → inget renderas. */
function PinnedChips({
  items,
  onRemove,
  ariaLabel,
}: {
  items: ReadonlyArray<Option>;
  onRemove: (conceptId: string) => void;
  ariaLabel: string;
}) {
  if (items.length === 0) return null;
  return (
    <ul className="jp-chiplist jp-matchdialog__pinned" aria-label={ariaLabel}>
      {items.map((it) => (
        <li key={it.conceptId}>
          <PreferenceChip label={it.label} onRemove={() => onRemove(it.conceptId)} />
        </li>
      ))}
    </ul>
  );
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
  const occupationOptions = useMemo(
    () => flattenOccupationGroups(occupationFields),
    [occupationFields]
  );
  const regionOptions = useMemo<ReadonlyArray<Option>>(
    () => regions.map((r) => ({ conceptId: r.conceptId, label: r.label })),
    [regions]
  );
  const employmentOptions = useMemo<ReadonlyArray<Option>>(
    () =>
      employmentTypes.map((e) => ({ conceptId: e.conceptId, label: e.label })),
    [employmentTypes]
  );

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

  // Yrkesgrupp-filter + aktiv kaskad-kolumn.
  const [occupationFilter, setOccupationFilter] = useState("");
  const [activeField, setActiveField] = useState<string | null>(null);

  // Yrkestitel-förslag.
  const [deriveTitle, setDeriveTitle] = useState("");
  const [titleCandidates, setTitleCandidates] = useState<
    ReadonlyArray<OccupationCandidate>
  >([]);
  const [titleSubmitted, setTitleSubmitted] = useState(false);
  const [titleError, setTitleError] = useState<string | null>(null);
  const [isDeriving, startDeriving] = useTransition();

  // CV-förslag.
  const [cvResult, setCvResult] = useState<CvSuggestResult | null>(null);
  const [isCvSuggesting, startCvSuggest] = useTransition();

  // Save.
  const [isSaving, startSaving] = useTransition();
  const [saveError, setSaveError] = useState<string | null>(null);

  // Seed/återställ drafterna när dialogen öppnas; nollställ förslags-state när
  // den stängs. Render-tids-flagga (react-hooks/set-state-in-effect-fri).
  if (open && !seededFor) {
    setSeededFor(true);
    setDraftOccupations(persistedOccupationGroups);
    setDraftRegions(persistedRegions);
    setDraftEmployment(persistedEmploymentTypes);
    setOccupationFilter("");
    setActiveField(null);
    setDeriveTitle("");
    setTitleCandidates([]);
    setTitleSubmitted(false);
    setTitleError(null);
    setCvResult(null);
    setSaveError(null);
  }
  if (!open && seededFor) {
    setSeededFor(false);
  }

  const filteredOccupations = useMemo(
    () => filterOptions(occupationOptions, occupationFilter),
    [occupationOptions, occupationFilter]
  );
  const isFiltering = occupationFilter.trim().length > 0;

  const occupationChips = labelsForSelected(draftOccupations, occupationOptions);
  const regionChips = labelsForSelected(draftRegions, regionOptions);
  const employmentChips = labelsForSelected(draftEmployment, employmentOptions);

  const activeGroups =
    occupationFields.find((f) => f.conceptId === activeField)?.occupationGroups ??
    [];

  function toggleOccupation(conceptId: string) {
    setDraftOccupations((prev) => toggle(prev, conceptId));
  }

  function onDeriveTitle(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    const title = deriveTitle.trim();
    if (title.length === 0) return;
    setTitleError(null);
    startDeriving(async () => {
      const result = await deriveOccupationsAction(title);
      setTitleSubmitted(true);
      if (result.success) {
        setTitleCandidates(result.candidates);
      } else {
        setTitleCandidates([]);
        setTitleError(result.error);
      }
    });
  }

  function onSuggestFromCv() {
    setCvResult(null);
    startCvSuggest(async () => {
      const result = await suggestOccupationsFromCvAction();
      setCvResult(result);
    });
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
      <DialogContent className="jp-matchdialog" aria-describedby="match-dialog-intro">
        <div className="jp-matchdialog__head">
          <DialogTitle className="jp-matchdialog__title">
            Lägg till i matchning
          </DialogTitle>
          <DialogDescription
            id="match-dialog-intro"
            className="jp-matchdialog__intro"
          >
            Sök och välj yrken, regioner och anställningsformer. Valda visas
            överst i varje del — ta bort med kryssikonen.
          </DialogDescription>
          {/* Stäng-knappen = shadcn/radix Close inbyggd i DialogContent (civic-
              restylad i globals.css), inte en egen knapp — undviker dubblerad
              "Stäng" för skärmläsare och ärver ESC/fokus-retur. */}
        </div>

        <div className="jp-matchdialog__body">
          {/* ── YRKEN ──────────────────────────────────── */}
          <section
            className="jp-matchdialog__section"
            role="group"
            aria-labelledby="match-dialog-occ-head"
          >
            <div className="jp-matchdialog__sectionhead">
              <span id="match-dialog-occ-head" className="jp-popover__title">
                Yrken
              </span>
              {draftOccupations.length > 0 && (
                <button
                  type="button"
                  className="jp-clearlink"
                  onClick={() => setDraftOccupations([])}
                >
                  Rensa
                </button>
              )}
            </div>

            <PinnedChips
              items={occupationChips}
              onRemove={toggleOccupation}
              ariaLabel="Valda yrken"
            />

            {/* Förslag utifrån en yrkestitel (behålls). */}
            <form
              onSubmit={onDeriveTitle}
              className="flex flex-col gap-1.5 mb-3"
            >
              <Label htmlFor="match-dialog-derive-title">
                Föreslå utifrån en yrkestitel
              </Label>
              <div className="flex gap-2">
                <Input
                  id="match-dialog-derive-title"
                  type="text"
                  value={deriveTitle}
                  onChange={(e) => setDeriveTitle(e.target.value)}
                  maxLength={120}
                  disabled={isDeriving}
                  aria-describedby="match-dialog-derive-help"
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
                id="match-dialog-derive-help"
                className="text-body-sm text-text-secondary"
              >
                Skriv en yrkestitel så föreslår vi yrken att lägga till. Du
                väljer själv vilka som tas med.
              </p>
            </form>

            {titleError && (
              <p role="alert" className="text-body-sm text-danger-600 mb-3">
                {titleError}
              </p>
            )}
            {!titleError && titleSubmitted && titleCandidates.length === 0 && (
              <p className="text-body-sm text-text-secondary mb-3">
                Inga förslag för den titeln. Du kan välja yrken i listan nedan i
                stället.
              </p>
            )}
            {titleCandidates.length > 0 && (
              <div className="mb-3">
                <p className="text-body-sm text-text-secondary mb-1.5">
                  Förslag — välj de som passar för att lägga till dem:
                </p>
                <div role="group" aria-label="Föreslagna yrken utifrån titel">
                  {titleCandidates.map((c) => (
                    <CheckItem
                      key={c.occupationGroupConceptId}
                      label={c.occupationGroupLabel}
                      checked={draftOccupations.includes(
                        c.occupationGroupConceptId
                      )}
                      onToggle={() =>
                        toggleOccupation(c.occupationGroupConceptId)
                      }
                    />
                  ))}
                </div>
              </div>
            )}

            {/* Förslag utifrån mitt CV (deterministisk läsning, propose-and-approve). */}
            <div className="jp-matchdialog__suggest">
              <Button
                type="button"
                variant="secondary"
                disabled={isCvSuggesting}
                onClick={onSuggestFromCv}
              >
                {isCvSuggesting ? "Läser ditt CV…" : "Föreslå utifrån mitt CV"}
              </Button>
              <CvSuggestPanel
                result={cvResult}
                pending={isCvSuggesting}
                draftOccupations={draftOccupations}
                onToggle={toggleOccupation}
                importCvHref={importCvHref}
              />
            </div>

            {/* Filter + tvåkolumns-kaskad (yrkesområde → yrkesgrupper). */}
            <div className="flex flex-col gap-1.5 mb-2">
              <Label htmlFor="match-dialog-occ-filter">
                Filtrera yrkesgrupper
              </Label>
              <Input
                id="match-dialog-occ-filter"
                type="text"
                value={occupationFilter}
                onChange={(e) => setOccupationFilter(e.target.value)}
                maxLength={80}
                aria-describedby="match-dialog-occ-filter-help"
              />
              <p
                id="match-dialog-occ-filter-help"
                className="text-body-sm text-text-secondary"
              >
                Skriv för att smalna av listan, eller bläddra via yrkesområde.
              </p>
            </div>

            {isFiltering ? (
              <div className="jp-matchdialog__list">
                {filteredOccupations.length === 0 ? (
                  <p className="text-body-sm text-text-secondary px-4 py-3">
                    Ingen yrkesgrupp matchar filtret.
                  </p>
                ) : (
                  filteredOccupations.map((o) => (
                    <CheckItem
                      key={o.conceptId}
                      label={o.label}
                      checked={draftOccupations.includes(o.conceptId)}
                      onToggle={() => toggleOccupation(o.conceptId)}
                    />
                  ))
                )}
              </div>
            ) : (
              <div className="jp-matchdialog__cascade">
                <div
                  className="jp-matchdialog__cascade-col"
                  role="listbox"
                  aria-label="Yrkesområde"
                >
                  <div className="jp-matchdialog__cascade-colhead">
                    <span className="jp-popover__title">Yrkesområde</span>
                  </div>
                  {occupationFields.length === 0 ? (
                    <p className="text-body-sm text-text-secondary px-4 py-3">
                      Yrkesområdena kunde inte läsas in just nu.
                    </p>
                  ) : (
                    occupationFields.map((f) => {
                      const active = f.conceptId === activeField;
                      const hasSel = f.occupationGroups.some((g) =>
                        draftOccupations.includes(g.conceptId)
                      );
                      return (
                        <div
                          key={f.conceptId}
                          className="jp-popover-row"
                          role="option"
                          aria-selected={active}
                          tabIndex={0}
                          onClick={() => setActiveField(f.conceptId)}
                          onKeyDown={(e) => {
                            if (e.key === "Enter" || e.key === " ") {
                              e.preventDefault();
                              setActiveField(f.conceptId);
                            }
                          }}
                        >
                          <span className="flex items-center gap-2">
                            {hasSel && !active && (
                              <span
                                aria-hidden="true"
                                className="inline-block size-2 rounded-full bg-(--jp-accent-700)"
                              />
                            )}
                            {f.label}
                          </span>
                          <ChevronRight
                            size={14}
                            className="jp-popover-row__chev"
                            aria-hidden="true"
                          />
                        </div>
                      );
                    })
                  )}
                </div>
                <div
                  className="jp-matchdialog__cascade-col"
                  aria-label="Yrkesgrupper"
                >
                  <div className="jp-matchdialog__cascade-colhead">
                    <span className="jp-popover__title">Yrkesgrupper</span>
                  </div>
                  {activeField === null ? (
                    <p className="text-body-sm text-text-secondary px-4 py-3">
                      Välj ett yrkesområde till vänster.
                    </p>
                  ) : (
                    activeGroups.map((g) => (
                      <CheckItem
                        key={g.conceptId}
                        label={g.label}
                        checked={draftOccupations.includes(g.conceptId)}
                        onToggle={() => toggleOccupation(g.conceptId)}
                      />
                    ))
                  )}
                </div>
              </div>
            )}
          </section>

          {/* ── REGIONER ───────────────────────────────── */}
          <section
            className="jp-matchdialog__section"
            role="group"
            aria-labelledby="match-dialog-region-head"
          >
            <div className="jp-matchdialog__sectionhead">
              <span id="match-dialog-region-head" className="jp-popover__title">
                Regioner
              </span>
              {draftRegions.length > 0 && (
                <button
                  type="button"
                  className="jp-clearlink"
                  onClick={() => setDraftRegions([])}
                >
                  Rensa
                </button>
              )}
            </div>
            <PinnedChips
              items={regionChips}
              onRemove={(id) => setDraftRegions((prev) => toggle(prev, id))}
              ariaLabel="Valda regioner"
            />
            <div className="jp-matchdialog__list">
              {regionOptions.map((o) => (
                <CheckItem
                  key={o.conceptId}
                  label={o.label}
                  checked={draftRegions.includes(o.conceptId)}
                  onToggle={() =>
                    setDraftRegions((prev) => toggle(prev, o.conceptId))
                  }
                />
              ))}
            </div>
          </section>

          {/* ── ANSTÄLLNINGSFORMER ─────────────────────── */}
          <section
            className="jp-matchdialog__section"
            role="group"
            aria-labelledby="match-dialog-employment-head"
          >
            <div className="jp-matchdialog__sectionhead">
              <span
                id="match-dialog-employment-head"
                className="jp-popover__title"
              >
                Anställningsformer
              </span>
              {draftEmployment.length > 0 && (
                <button
                  type="button"
                  className="jp-clearlink"
                  onClick={() => setDraftEmployment([])}
                >
                  Rensa
                </button>
              )}
            </div>
            <PinnedChips
              items={employmentChips}
              onRemove={(id) => setDraftEmployment((prev) => toggle(prev, id))}
              ariaLabel="Valda anställningsformer"
            />
            <div className="jp-matchdialog__list">
              {employmentOptions.map((o) => (
                <CheckItem
                  key={o.conceptId}
                  label={o.label}
                  checked={draftEmployment.includes(o.conceptId)}
                  onToggle={() =>
                    setDraftEmployment((prev) => toggle(prev, o.conceptId))
                  }
                />
              ))}
            </div>
          </section>
        </div>

        <div className="jp-matchdialog__foot">
          <Button type="button" onClick={onSave} disabled={isSaving}>
            {isSaving ? "Sparar…" : "Spara matchning"}
          </Button>
          <Button
            type="button"
            variant="ghost"
            onClick={() => onOpenChange(false)}
            disabled={isSaving}
          >
            Avbryt
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

/**
 * CV-förslagets fyra distinkta UI-states (RE-BIND §3). Renderas under
 * "Föreslå utifrån mitt CV"-knappen. Deterministisk läsning — copy säger
 * aldrig "AI".
 */
function CvSuggestPanel({
  result,
  pending,
  draftOccupations,
  onToggle,
  importCvHref,
}: {
  result: CvSuggestResult | null;
  pending: boolean;
  draftOccupations: ReadonlyArray<string>;
  onToggle: (conceptId: string) => void;
  importCvHref: string;
}) {
  if (pending) {
    return (
      <p
        role="status"
        aria-live="polite"
        className="text-body-sm text-text-secondary"
      >
        Läser ditt CV…
      </p>
    );
  }
  if (result === null) return null;

  switch (result.kind) {
    case "noCv":
      return (
        <div
          role="status"
          className="rounded-md border border-border-default bg-surface-secondary p-3"
        >
          <p className="text-body-sm text-text-primary font-medium">
            Inget CV uppladdat
          </p>
          <p className="text-body-sm text-text-secondary mt-1">
            Ladda upp ett CV så kan vi föreslå yrken utifrån din erfarenhet. Du
            väljer själv vilka förslag som tas med.
          </p>
          <Button asChild variant="secondary" className="mt-2.5">
            <a href={importCvHref}>Importera CV</a>
          </Button>
        </div>
      );
    case "noRole":
      return (
        <p role="status" className="text-body-sm text-text-secondary">
          Vi kunde inte läsa ett yrke ur ditt CV. Du kan välja yrken i listan i
          stället.
        </p>
      );
    case "unauthorized":
      return (
        <p role="alert" className="text-body-sm text-danger-600">
          Du är inte inloggad. Logga in och försök igen.
        </p>
      );
    case "error":
      return (
        <p role="alert" className="text-body-sm text-danger-600">
          Kunde inte läsa ditt CV just nu. Försök igen om en stund.
        </p>
      );
    case "candidates":
      return (
        <div>
          <p className="text-body-sm text-text-secondary mb-1.5">
            Föreslår yrken utifrån ditt CV — du väljer vilka som tas med:
          </p>
          <div role="group" aria-label="Föreslagna yrkesgrupper">
            {result.candidates.map((c) => (
              <CheckItem
                key={c.occupationGroupConceptId}
                label={c.occupationGroupLabel}
                checked={draftOccupations.includes(c.occupationGroupConceptId)}
                onToggle={() => onToggle(c.occupationGroupConceptId)}
              />
            ))}
          </div>
        </div>
      );
  }
}
