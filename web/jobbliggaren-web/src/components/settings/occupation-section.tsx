"use client";

// "use client": yrkes-sektionen håller filter-/aktiv-kolumn-state, två
// förslags-affordanser (yrkestitel-derive + CV-suggest) med pending/diskriminerat
// resultat-state. Extraherad ur match-preferences-dialog (ADR 0077 STEG 5) och
// delad med match-setup-wizard utan beteendeändring — DRY (en enda kopia av
// derive/CV-suggest/kaskad-logiken). INGEN AI (deterministisk, ADR 0071);
// förslag skrivs ALDRIG (propose-and-approve, ADR 0040 Beslut 4).

import { useEffect, useMemo, useRef, useState, useTransition } from "react";
import { ChevronRight } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import type { TaxonomyOccupationField } from "@/lib/dto/taxonomy";
import type { OccupationCandidate } from "@/lib/dto/match-preferences";
import {
  deriveOccupationsAction,
  suggestOccupationsFromCvAction,
  suggestOccupationsFromParsedResumeAction,
  type CvSuggestResult,
} from "@/lib/actions/match-preferences";
import {
  filterOptions,
  flattenOccupationGroups,
  labelsForSelected,
} from "./match-preferences-shared";
import { CheckItem, PinnedChips } from "./section-helpers";

interface OccupationSectionProps {
  readonly occupationFields: ReadonlyArray<TaxonomyOccupationField>;
  /** Valda yrkesgrupp-concept-id (draft). */
  readonly selected: ReadonlyArray<string>;
  /** Toggla ett yrkesgrupp-concept-id i draften. */
  readonly onToggle: (conceptId: string) => void;
  /** Töm yrkes-valet helt. */
  readonly onClear: () => void;
  /** CV-importflödets route (tom-state-länken i CV-förslaget). */
  readonly importCvHref: string;
  /**
   * Unik DOM-id-prefix så sektionen kan monteras i flera värdar utan
   * id-kollision (dialog vs wizard). Default behåller dialogens tidigare id:n.
   */
  readonly idPrefix?: string;
  /** rubrik-id som värden kopplar `aria-labelledby` mot (för role=group). */
  readonly headingId?: string;
  /**
   * Wizard-prefill: kör CV-förslaget automatiskt när sektionen monteras (en
   * gång). Förslagen skrivs ALDRIG — de visas pre-kryssade och bekräftas av
   * användaren (propose-and-approve). I dialogen är detta `false` (knapp-driven).
   */
  readonly autoSuggestFromCv?: boolean;
  /**
   * Fas 4 onboarding (CTO Variant B): id för det just uppladdade `parsed_resume`:t
   * (welcome-flödet). När satt läses CV-förslaget ur den staging-artefakten
   * (`occupation_proposals`, ingen DEK/CV-PII) i stället för ur det promotade
   * `Resume`:ts `latestRole` — en ny användare har ännu inget promotat Resume.
   * Utelämnat (dialog/`/cv`/`/installningar`) → faller tillbaka på latestRole-vägen.
   */
  readonly parsedResumeId?: string;
}

/**
 * YRKEN-sektionen: pinnade chips + yrkestitel-derive + CV-suggest +
 * filter/tvåkolumns-kaskad. Den rikaste preferens-sektionen. Återanvänds av
 * BÅDE match-preferences-dialog och match-setup-wizard.
 */
export function OccupationSection({
  occupationFields,
  selected,
  onToggle,
  onClear,
  importCvHref,
  idPrefix = "match-dialog",
  headingId,
  autoSuggestFromCv = false,
  parsedResumeId,
}: OccupationSectionProps) {
  const occupationOptions = useMemo(
    () => flattenOccupationGroups(occupationFields),
    [occupationFields]
  );

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

  function runCvSuggest() {
    setCvResult(null);
    startCvSuggest(async () => {
      // Welcome-flödet (parsedResumeId satt) → läs den just uppladdade staging-CV:ns
      // proposals; annars promotade Resume:ts latestRole (dialog/`/cv`/`/installningar`).
      const result = parsedResumeId
        ? await suggestOccupationsFromParsedResumeAction(parsedResumeId)
        : await suggestOccupationsFromCvAction();
      setCvResult(result);
    });
  }

  // Wizard-prefill: kör CV-suggest en gång vid montering. "use client"-effekt
  // motiverad — den läser CV:t (server-action) först efter att klient-ön är
  // hydrerad; inget av det går i en Server Component. Körs EN gång (ref-vakt).
  const autoRan = useRef(false);
  useEffect(() => {
    if (autoSuggestFromCv && !autoRan.current) {
      autoRan.current = true;
      runCvSuggest();
    }
    // En-gångs-körning vid montering via ref-vakt; runCvSuggest behöver inte
    // vara i deps (ej refererad reaktivt — ref-vakten gör körningen idempotent).
  }, [autoSuggestFromCv]);

  const filteredOccupations = useMemo(
    () => filterOptions(occupationOptions, occupationFilter),
    [occupationOptions, occupationFilter]
  );
  const isFiltering = occupationFilter.trim().length > 0;
  const occupationChips = labelsForSelected(selected, occupationOptions);
  const activeGroups =
    occupationFields.find((f) => f.conceptId === activeField)?.occupationGroups ??
    [];

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

  const filterHelpId = `${idPrefix}-occ-filter-help`;
  const deriveHelpId = `${idPrefix}-derive-help`;

  return (
    <>
      <div className="jp-matchdialog__sectionhead">
        <span id={headingId} className="jp-popover__title">
          Yrken
        </span>
        {selected.length > 0 && (
          <button type="button" className="jp-clearlink" onClick={onClear}>
            Rensa
          </button>
        )}
      </div>

      <PinnedChips items={occupationChips} onRemove={onToggle} ariaLabel="Valda yrken" />

      {/* Förslag utifrån en yrkestitel (titel-derive-fallback). */}
      <form onSubmit={onDeriveTitle} className="flex flex-col gap-1.5 mb-3">
        <Label htmlFor={`${idPrefix}-derive-title`}>
          Föreslå utifrån en yrkestitel
        </Label>
        <div className="flex gap-2">
          <Input
            id={`${idPrefix}-derive-title`}
            type="text"
            value={deriveTitle}
            onChange={(e) => setDeriveTitle(e.target.value)}
            maxLength={120}
            disabled={isDeriving}
            aria-describedby={deriveHelpId}
          />
          <Button
            type="submit"
            variant="secondary"
            disabled={isDeriving || deriveTitle.trim().length === 0}
          >
            {isDeriving ? "Söker…" : "Föreslå"}
          </Button>
        </div>
        <p id={deriveHelpId} className="text-body-sm text-text-secondary">
          Skriv en yrkestitel så föreslår vi yrken att lägga till. Du väljer
          själv vilka som tas med.
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
            Förslag: välj de som passar för att lägga till dem:
          </p>
          <div role="group" aria-label="Föreslagna yrken utifrån titel">
            {titleCandidates.map((c) => (
              <CheckItem
                key={c.occupationGroupConceptId}
                label={c.occupationGroupLabel}
                checked={selected.includes(c.occupationGroupConceptId)}
                onToggle={() => onToggle(c.occupationGroupConceptId)}
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
          onClick={runCvSuggest}
        >
          {isCvSuggesting ? "Läser ditt CV…" : "Föreslå utifrån mitt CV"}
        </Button>
        <CvSuggestPanel
          result={cvResult}
          pending={isCvSuggesting}
          selected={selected}
          onToggle={onToggle}
          importCvHref={importCvHref}
        />
      </div>

      {/* Filter + tvåkolumns-kaskad (yrkesområde → yrkesgrupper). */}
      <div className="flex flex-col gap-1.5 mb-2">
        <Label htmlFor={`${idPrefix}-occ-filter`}>Filtrera yrkesgrupper</Label>
        <Input
          id={`${idPrefix}-occ-filter`}
          type="text"
          value={occupationFilter}
          onChange={(e) => setOccupationFilter(e.target.value)}
          maxLength={80}
          aria-describedby={filterHelpId}
        />
        <p id={filterHelpId} className="text-body-sm text-text-secondary">
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
                checked={selected.includes(o.conceptId)}
                onToggle={() => onToggle(o.conceptId)}
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
                  selected.includes(g.conceptId)
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
          <div className="jp-matchdialog__cascade-col" aria-label="Yrkesgrupper">
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
                  checked={selected.includes(g.conceptId)}
                  onToggle={() => onToggle(g.conceptId)}
                />
              ))
            )}
          </div>
        </div>
      )}
    </>
  );
}

/**
 * CV-förslagets fyra distinkta UI-states. Renderas under "Föreslå utifrån mitt
 * CV"-knappen. Deterministisk läsning — copy säger ALDRIG "AI".
 */
function CvSuggestPanel({
  result,
  pending,
  selected,
  onToggle,
  importCvHref,
}: {
  readonly result: CvSuggestResult | null;
  readonly pending: boolean;
  readonly selected: ReadonlyArray<string>;
  readonly onToggle: (conceptId: string) => void;
  readonly importCvHref: string;
}) {
  if (pending) {
    return (
      <p role="status" aria-live="polite" className="text-body-sm text-text-secondary">
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
            Föreslår yrken utifrån ditt CV. Du väljer vilka som tas med:
          </p>
          <div role="group" aria-label="Föreslagna yrkesgrupper">
            {result.candidates.map((c) => (
              <CheckItem
                key={c.occupationGroupConceptId}
                label={c.occupationGroupLabel}
                checked={selected.includes(c.occupationGroupConceptId)}
                onToggle={() => onToggle(c.occupationGroupConceptId)}
              />
            ))}
          </div>
        </div>
      );
  }
}
