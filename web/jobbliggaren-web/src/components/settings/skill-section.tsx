"use client";

// "use client": the skill section holds search-box state (query, debounced
// abortable typeahead results, a transition for the search action), a CV-suggest
// (pending / discriminated result state), a disclosure toggle for the
// "Lägg till kompetens"-search, and a label store (conceptId → Swedish label)
// for chip rendering. Mirrors OccupationSection's STRUCTURE (ADR 0079 STEG 3),
// but the flat 20k skill vocabulary has NO hierarchy, so a SEARCH box replaces
// the cascade. INGEN AI (deterministic, ADR 0071); CV proposals are PRE-ADDED to
// the draft as chips but never written to the server until the host's
// "Spara matchning" (propose-and-approve, ADR 0040 Beslut 4 / 0079 Beslut 1).

import {
  useCallback,
  useEffect,
  useId,
  useMemo,
  useRef,
  useState,
  useTransition,
} from "react";
import { Plus } from "lucide-react";
import { useTranslations } from "next-intl";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  searchSkillsAction,
  suggestSkillsFromParsedResumeAction,
  type SkillSuggestResult,
} from "@/lib/actions/match-preferences";
import type { SkillOption } from "@/lib/dto/skills";
import { labelsForSelected, type Option } from "./match-preferences-shared";
import { PinnedChips } from "./section-helpers";

/** Debounce window for the typeahead (Klas: search-as-you-type, server-resolved). */
const SEARCH_DEBOUNCE_MS = 250;
/** Min query length before a backend round-trip (matches the BFF/backend contract). */
const SEARCH_MIN_CHARS = 2;

interface SkillSectionProps {
  /** Valda kompetens-concept-id (draft). */
  readonly selected: ReadonlyArray<string>;
  /** Toggla ett kompetens-concept-id i draften (chip-borttagning + sök-träff-tillägg). */
  readonly onToggle: (conceptId: string) => void;
  /**
   * Ersätt hela kompetens-valet (draft). Används för CV-förslagets pre-add
   * (merge av kandidater in i draften) — chips, inte en separat checklista.
   */
  readonly onReplace: (next: string[]) => void;
  /** Töm kompetens-valet helt. */
  readonly onClear: () => void;
  /**
   * Unik DOM-id-prefix så sektionen kan monteras i flera värdar utan
   * id-kollision (dialog vs wizard).
   */
  readonly idPrefix?: string;
  /** rubrik-id som värden kopplar `aria-labelledby` mot (för role=group). */
  readonly headingId?: string;
  /**
   * Visa sektionens egna "Kompetenser"-rubrik. Default true (dialogen).
   * Wizarden sätter false — där bär DialogTitle rubriken.
   */
  readonly showHeading?: boolean;
  /**
   * Wizard-prefill: kör CV-förslaget automatiskt när sektionen monteras (en
   * gång). Förslagen PRE-ADDAS till draften (chips) — de skrivs ALDRIG till
   * servern (propose-and-approve). I dialogen är detta `false`.
   */
  readonly autoSuggestFromCv?: boolean;
  /**
   * STEG 3 / ADR 0079: id för det just uppladdade `parsed_resume`:t
   * (welcome/just-uppladdat-flödet). När satt läses CV-förslaget ur den
   * staging-artefaktens `/skills`-projektion (ingen DEK/CV-PII). Utelämnat →
   * inget CV-förslag (settings-användare täcks av sök-tillägget; ingen
   * promotad-CV-auto-suggest byggd ännu, noterad uppföljning).
   */
  readonly parsedResumeId?: string;
  /**
   * Pre-fyllda labels för redan-sparade kompetens-concept-id (settings-
   * pre-fill). Den platta skill-taxonomin skickas ALDRIG till FE som träd, så
   * en sparad chip har ingen träd-uppslagning — värden läser tillbaka labels
   * (när de finns) och utelämnade faller tillbaka på id-strängen.
   */
  readonly initialLabels?: ReadonlyArray<Option>;
  /**
   * Speglar sektionens label-store (conceptId → label) ut till värden vid varje
   * förändring (CV-förslag + sök-träffar). Låter t.ex. kortet adoptera labels
   * efter save så dess egna chips kan rendera namn i stället för id (det finns
   * ingen träd-uppslagning för skills). Ren mirror — sektionen äger sanningen.
   */
  readonly onLabelsChange?: (labels: ReadonlyArray<Option>) => void;
}

/**
 * KOMPETENSER-sektionen (ADR 0079 STEG 3 Beslut 1): pinnade chips (inkl.
 * CV-förslag pre-addade) + EN tydlig "Lägg till kompetens"-CTA som öppnar en
 * inline-disclosure med ett sök-fält (search-as-you-type, server-resolverat).
 * Klonar OccupationSections struktur men ersätter kaskaden med sök, eftersom
 * skill-taxonomin är ett platt 20k-vokabulär utan hierarki. Återanvänds av
 * BÅDE match-preferences-dialog/-card och match-setup-wizard.
 */
export function SkillSection({
  selected,
  onToggle,
  onReplace,
  onClear,
  idPrefix = "match-dialog-skill",
  headingId,
  showHeading = true,
  autoSuggestFromCv = false,
  parsedResumeId,
  initialLabels = [],
  onLabelsChange,
}: SkillSectionProps) {
  const t = useTranslations("settings");

  // Label store: conceptId → Swedish label. Seeded from the pre-fill labels and
  // grown as CV-proposals + search results arrive. Chips read from here so a
  // selected skill renders its label rather than its raw concept-id. Unknown id
  // falls back to the id string (labelsForSelected behaviour).
  const [labelStore, setLabelStore] = useState<ReadonlyMap<string, string>>(
    () => new Map(initialLabels.map((o) => [o.conceptId, o.label]))
  );
  const rememberLabels = useCallback((options: ReadonlyArray<SkillOption>) => {
    if (options.length === 0) return;
    setLabelStore((prev) => {
      const next = new Map(prev);
      for (const o of options) next.set(o.conceptId, o.label);
      return next;
    });
  }, []);

  const skillOptions = useMemo<ReadonlyArray<Option>>(
    () =>
      [...labelStore.entries()].map(([conceptId, label]) => ({
        conceptId,
        label,
      })),
    [labelStore]
  );
  const skillChips = labelsForSelected(selected, skillOptions);

  // Mirror the label store out to the host (kept off the render path via a ref
  // so an unstable callback prop can't loop). The host adopts labels so its own
  // chips can render names after save. The ref is written in an effect (never
  // during render) and the mirror runs only when the label set changes.
  const onLabelsChangeRef = useRef(onLabelsChange);
  useEffect(() => {
    onLabelsChangeRef.current = onLabelsChange;
  }, [onLabelsChange]);
  useEffect(() => {
    onLabelsChangeRef.current?.(skillOptions);
  }, [skillOptions]);

  // ── "Lägg till kompetens"-disclosure (search) ──
  const [pickerOpen, setPickerOpen] = useState(false);
  const panelRef = useRef<HTMLDivElement | null>(null);

  // Search state: query, latest results, a pending flag and an abort handle so a
  // stale in-flight request never overwrites a newer one.
  const [query, setQuery] = useState("");
  const [results, setResults] = useState<ReadonlyArray<SkillOption>>([]);
  const [searched, setSearched] = useState(false);
  const [isSearching, startSearching] = useTransition();
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  // Monotonic request id: only the latest search's result is applied (drops
  // out-of-order responses — a server action cannot be AbortController-cancelled
  // on the server, so we guard on the client instead).
  const requestSeq = useRef(0);

  function runSearch(raw: string) {
    const q = raw.trim();
    if (q.length < SEARCH_MIN_CHARS) {
      setResults([]);
      setSearched(false);
      return;
    }
    const seq = ++requestSeq.current;
    startSearching(async () => {
      const result = await searchSkillsAction(q);
      // Drop if a newer search has started since this one was dispatched.
      if (seq !== requestSeq.current) return;
      const options = result.success ? result.options : [];
      rememberLabels(options);
      setResults(options);
      setSearched(true);
    });
  }

  function onQueryChange(value: string) {
    setQuery(value);
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => runSearch(value), SEARCH_DEBOUNCE_MS);
  }

  // Clear the pending debounce on unmount (no setState-after-unmount).
  useEffect(() => {
    return () => {
      if (debounceRef.current) clearTimeout(debounceRef.current);
    };
  }, []);

  function addSkill(option: SkillOption) {
    rememberLabels([option]);
    if (!selected.includes(option.conceptId)) onToggle(option.conceptId);
  }

  // ── CV-förslag (pending/diskriminerat) ──
  const [cvResult, setCvResult] = useState<SkillSuggestResult | null>(null);
  const [isCvSuggesting, startCvSuggest] = useTransition();

  const runCvSuggest = useCallback(() => {
    // Settings-användare har ingen CV-källa här (ingen promotad-CV-väg byggd
    // ännu) → utan parsedResumeId finns inget att läsa. Welcome/just-uppladdat
    // bär parsedResumeId.
    if (!parsedResumeId) return;
    setCvResult(null);
    startCvSuggest(async () => {
      const result = await suggestSkillsFromParsedResumeAction(parsedResumeId);
      setCvResult(result);
      if (result.kind === "candidates" && result.candidates.length > 0) {
        rememberLabels(result.candidates);
        const candidateIds = result.candidates.map((c) => c.conceptId);
        // PRE-ADD: merge into the draft as removable chips (propose-and-approve,
        // draft-only); dedupe against an existing manual selection.
        onReplace([...new Set([...selected, ...candidateIds])]);
      }
    });
    // `selected`/`onReplace` are intentionally captured at call time, not deps:
    // the auto-run is a one-shot mount effect guarded by a ref below.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [parsedResumeId, rememberLabels]);

  // Wizard-prefill: run CV-suggest once on mount. "use client" justified — it
  // reads the CV (server action) only after the client island hydrates. One-shot
  // via a ref guard.
  const autoRan = useRef(false);
  useEffect(() => {
    if (autoSuggestFromCv && !autoRan.current && parsedResumeId) {
      autoRan.current = true;
      runCvSuggest();
    }
  }, [autoSuggestFromCv, parsedResumeId, runCvSuggest]);

  // Stable panel id (aria-controls). useId is hydration-safe.
  const reactId = useId();
  const panelId = `${idPrefix}-skill-picker-${reactId}`;
  const searchHelpId = `${idPrefix}-skill-search-help`;
  const resultsId = `${idPrefix}-skill-results`;

  function openPicker() {
    setPickerOpen(true);
    // Move focus into the panel after commit (WCAG 2.4.3). queueMicrotask runs
    // after the React commit so the input is mounted.
    queueMicrotask(() => {
      panelRef.current?.querySelector<HTMLElement>("input")?.focus();
    });
  }

  const trimmed = query.trim();
  const showNoResults =
    searched && !isSearching && trimmed.length >= SEARCH_MIN_CHARS && results.length === 0;

  return (
    <>
      {/* Sektionshuvud: rubrik (dialogen) eller bara Rensa-länken (wizarden,
          där DialogTitle bär "Kompetenser"). Behåll Rensa när något är valt. */}
      {showHeading ? (
        <div className="jp-matchdialog__sectionhead">
          <span id={headingId} className="jp-popover__title">
            {t("matchPrefs.facetSkills")}
          </span>
          {selected.length > 0 && (
            <button type="button" className="jp-clearlink" onClick={onClear}>
              {t("matchPrefs.clear")}
            </button>
          )}
        </div>
      ) : (
        selected.length > 0 && (
          <div className="jp-matchdialog__sectionhead jp-matchdialog__sectionhead--clearonly">
            <button type="button" className="jp-clearlink" onClick={onClear}>
              {t("matchPrefs.clear")}
            </button>
          </div>
        )
      )}

      <PinnedChips
        items={skillChips}
        onRemove={onToggle}
        ariaLabel={t("matchPrefs.selectedSkills")}
      />

      {/* CV-förslagets honest states (pending/noCv/noRole/error/unauthorized).
          "candidates" renderas INTE här (de blev chips ovan via pre-add). Bara
          relevant i welcome/just-uppladdat-flödet (parsedResumeId satt). */}
      {parsedResumeId && (
        <SkillCvSuggestStatus result={cvResult} pending={isCvSuggesting} />
      )}

      {/* Manuell tillägg: EN tydlig CTA → inline-disclosure (kollapsad default).
          Sök-fält i stället för kaskad (platt skill-vokabulär utan hierarki). */}
      <div className="jp-occpicker">
        <button
          type="button"
          className="jp-occpicker__cta"
          aria-expanded={pickerOpen}
          aria-controls={panelId}
          onClick={() => (pickerOpen ? setPickerOpen(false) : openPicker())}
        >
          <Plus size={16} aria-hidden="true" />
          {t("matchPrefs.skill.addSkill")}
        </button>

        {pickerOpen && (
          <div
            id={panelId}
            ref={panelRef}
            className="jp-occpicker__panel"
            role="group"
            aria-label={t("matchPrefs.skill.addSkill")}
          >
            <div className="flex flex-col gap-1.5 mb-2">
              <Label htmlFor={`${idPrefix}-skill-search`}>
                {t("matchPrefs.skill.searchLabel")}
              </Label>
              {/* A plain search field, NOT a combobox: the results below are
                  add-buttons (role="group" + aria-pressed rows), not a
                  listbox/options model, and there is no aria-activedescendant /
                  arrow-key navigation — so role="combobox"/aria-expanded/
                  aria-controls would be a false a11y promise. The honest pattern
                  is results-as-buttons + the role="status" live-region that
                  announces "Söker…"/result count. */}
              <Input
                id={`${idPrefix}-skill-search`}
                type="search"
                value={query}
                onChange={(e) => onQueryChange(e.target.value)}
                maxLength={80}
                autoComplete="off"
                aria-describedby={searchHelpId}
              />
              <p id={searchHelpId} className="text-body-sm text-text-secondary">
                {t("matchPrefs.skill.searchHint")}
              </p>
            </div>

            {/* Resultat-list: en knapp-rad per träff (klick adderar till draften
                som chip). role="listbox" inte använt — raderna är add-knappar,
                inte ett single-select. Lugn live-region för status. */}
            <div id={resultsId} className="jp-matchdialog__list" role="group">
              {isSearching ? (
                <p
                  role="status"
                  aria-live="polite"
                  className="text-body-sm text-text-secondary px-4 py-3"
                >
                  {t("matchPrefs.skill.searching")}
                </p>
              ) : showNoResults ? (
                <p className="text-body-sm text-text-secondary px-4 py-3">
                  {t("matchPrefs.skill.noResults")}
                </p>
              ) : results.length === 0 ? (
                <p className="text-body-sm text-text-secondary px-4 py-3">
                  {t("matchPrefs.skill.searchPrompt")}
                </p>
              ) : (
                results.map((o) => {
                  const already = selected.includes(o.conceptId);
                  return (
                    <button
                      key={o.conceptId}
                      type="button"
                      className="jp-popover-row"
                      aria-pressed={already}
                      onClick={() => addSkill(o)}
                    >
                      <span>{o.label}</span>
                      {already ? (
                        <span className="text-body-sm text-text-secondary">
                          {t("matchPrefs.skill.added")}
                        </span>
                      ) : (
                        <Plus
                          size={14}
                          className="jp-popover-row__chev"
                          aria-hidden="true"
                        />
                      )}
                    </button>
                  );
                })
              )}
            </div>
          </div>
        )}
      </div>
    </>
  );
}

/**
 * CV-förslagets honest non-candidate states (pending/noCv/noRole/error/
 * unauthorized). "candidates" → null (pre-addade som chips av föräldern).
 * Deterministisk läsning — copy säger ALDRIG "AI".
 */
function SkillCvSuggestStatus({
  result,
  pending,
}: {
  readonly result: SkillSuggestResult | null;
  readonly pending: boolean;
}) {
  const t = useTranslations("settings");
  return (
    <div className="jp-matchdialog__suggest">
      {pending && (
        <p
          role="status"
          aria-live="polite"
          className="text-body-sm text-text-secondary"
        >
          {t("matchPrefs.skill.suggesting")}
        </p>
      )}
      {!pending && result !== null && <SkillCvSuggestMessage result={result} />}
    </div>
  );
}

function SkillCvSuggestMessage({
  result,
}: {
  readonly result: SkillSuggestResult;
}) {
  const t = useTranslations("settings");
  switch (result.kind) {
    case "candidates":
      // Pre-addade som chips av föräldern — ingen separat lista.
      return null;
    case "noCv":
      // Ett just uppladdat parsed_resume hittades inte (ovanligt i detta flöde)
      // → lugn rad, ingen larm-state (användaren kan söka manuellt).
      return (
        <p role="status" className="text-body-sm text-text-secondary">
          {t("matchPrefs.skill.noCv")}
        </p>
      );
    case "noRole":
      return (
        <p role="status" className="text-body-sm text-text-secondary">
          {t("matchPrefs.skill.noSkills")}
        </p>
      );
    case "unauthorized":
      return (
        <p role="alert" className="text-body-sm text-danger-600">
          {t("matchPrefs.skill.unauthorized")}
        </p>
      );
    case "error":
      return (
        <p role="alert" className="text-body-sm text-danger-600">
          {t("matchPrefs.skill.error")}
        </p>
      );
  }
}
