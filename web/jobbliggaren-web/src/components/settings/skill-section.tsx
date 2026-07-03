"use client";

// "use client": the skill section holds search-box state (query, debounced
// abortable typeahead results, a transition for the search action), a CV-suggest
// (pending / discriminated result state), a disclosure toggle for the
// "Lägg till kompetens"-search, and a group store (canonical conceptId →
// SkillGroup) for chip rendering. Mirrors OccupationSection's STRUCTURE (ADR
// 0079 STEG 3), but the flat 20k skill vocabulary has NO hierarchy, so a SEARCH
// box replaces the cascade. INGEN AI (deterministic, ADR 0071); CV proposals are
// PRE-ADDED to the draft as chips but never written to the server until the
// host's "Spara matchning" (propose-and-approve, ADR 0040 Beslut 4 / 0079
// Beslut 1).
//
// #277 (twin chips): the unit of selection is a GROUP (one chip per shared
// exact-label surface). Adding a group adds ALL its member ids; removing a chip
// removes ALL its member ids. The draft `selected` stays a FLAT string[] and the
// save payload stays a flat union (grade-inert) — grouping is a read/offer
// projection. The BE-provided `memberConceptIds` is consumed VERBATIM (the FE
// never re-derives membership from raw taxonomy).

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
import type { SkillGroup } from "@/lib/dto/skills";
import {
  addSkillGroup,
  groupsForSelected,
  isSkillGroupSelected,
  removeSkillGroup,
} from "./match-preferences-shared";
import { PinnedChips } from "./section-helpers";

/** Debounce window for the typeahead (Klas: search-as-you-type, server-resolved). */
const SEARCH_DEBOUNCE_MS = 250;
/** Min query length before a backend round-trip (matches the BFF/backend contract). */
const SEARCH_MIN_CHARS = 2;

interface SkillSectionProps {
  /** Valda kompetens-concept-id (draft) — en FLAT lista av ALLA member-id. */
  readonly selected: ReadonlyArray<string>;
  /**
   * Ersätt hela kompetens-valet (draft). Enheten är en GRUPP (#277), så ALLA
   * mutationer går via full-replace: grupp-tillägg (union av member-id),
   * chip-borttagning (differens av member-id) och CV-förslagets pre-add — chips,
   * inte en separat checklista. (Det tidigare per-id `onToggle` är borttaget:
   * en twin-chip kan aldrig adderas/tas bort ett enskilt id i taget.)
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
   * Pre-fyllda GRUPPER för redan-sparade kompetenser (settings-pre-fill /
   * welcome-förslag). Den platta skill-taxonomin skickas ALDRIG till FE som träd,
   * så en sparad chip har ingen träd-uppslagning — värden bär in den BE-resolvade
   * gruppmetadatan (#277: canonical + label + member-id) så ett sparat twin-par
   * renderas som EN chip. Utelämnade id faller tillbaka på id-strängen.
   */
  readonly initialGroups?: ReadonlyArray<SkillGroup>;
  /**
   * Speglar sektionens grupp-store (canonical → SkillGroup) ut till värden vid
   * varje förändring (seed ∪ sök-träffar ∪ CV-förslag). Låter wizardens steg-5
   * och kortet rendera EN chip per grupp för manuellt tillagda kompetenser (#253-
   * mekanismen, nu grupp-medveten). Ren mirror — sektionen äger sanningen.
   */
  readonly onGroupsChange?: (groups: ReadonlyArray<SkillGroup>) => void;
}

/**
 * KOMPETENSER-sektionen (ADR 0079 STEG 3 Beslut 1): pinnade chips (inkl.
 * CV-förslag pre-addade) + EN tydlig "Lägg till kompetens"-CTA som öppnar en
 * inline-disclosure med ett sök-fält (search-as-you-type, server-resolverat).
 * Klonar OccupationSections struktur men ersätter kaskaden med sök, eftersom
 * skill-taxonomin är ett platt 20k-vokabulär utan hierarki. Återanvänds av
 * BÅDE match-preferences-dialog/-card och match-setup-rail-modal.
 *
 * #277: enheten är en GRUPP — EN chip per delad exakt-etikett-yta (twin-par),
 * lagrad som ALLA member-id i den platta draften (grad-inert).
 */
export function SkillSection({
  selected,
  onReplace,
  onClear,
  idPrefix = "match-dialog-skill",
  headingId,
  showHeading = true,
  autoSuggestFromCv = false,
  parsedResumeId,
  initialGroups = [],
  onGroupsChange,
}: SkillSectionProps) {
  const t = useTranslations("settings");

  // Group store: canonical conceptId → SkillGroup. Seeded from the pre-fill
  // groups and grown as CV-proposals + search results arrive. Chips read from
  // here so a selected skill renders ONE chip per group (twin partner included).
  // The BE-provided member ids are kept verbatim. Keyed by canonical id; a later
  // (richer) group for the same canonical replaces an earlier (e.g. a singleton
  // seed superseded by a real twin-group from search).
  const [groupStore, setGroupStore] = useState<ReadonlyMap<string, SkillGroup>>(
    () => new Map(initialGroups.map((g) => [g.conceptId, g]))
  );
  const rememberGroups = useCallback((groups: ReadonlyArray<SkillGroup>) => {
    if (groups.length === 0) return;
    setGroupStore((prev) => {
      const next = new Map(prev);
      for (const g of groups) next.set(g.conceptId, g);
      return next;
    });
  }, []);

  const knownGroups = useMemo<ReadonlyArray<SkillGroup>>(
    () => [...groupStore.values()],
    [groupStore]
  );
  const skillChips = groupsForSelected(selected, knownGroups);

  // Mirror the group store out to the host (kept off the render path via a ref so
  // an unstable callback prop can't loop). The host adopts groups so the wizard
  // step-5 / card can render ONE chip per group after save. The ref is written in
  // an effect (never during render) and the mirror runs only when groups change.
  const onGroupsChangeRef = useRef(onGroupsChange);
  useEffect(() => {
    onGroupsChangeRef.current = onGroupsChange;
  }, [onGroupsChange]);
  useEffect(() => {
    onGroupsChangeRef.current?.(knownGroups);
  }, [knownGroups]);

  // ── "Lägg till kompetens"-disclosure (search) ──
  const [pickerOpen, setPickerOpen] = useState(false);
  const panelRef = useRef<HTMLDivElement | null>(null);

  // Search state: query, latest results (GROUPS), a pending flag and an abort
  // handle so a stale in-flight request never overwrites a newer one.
  const [query, setQuery] = useState("");
  const [results, setResults] = useState<ReadonlyArray<SkillGroup>>([]);
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
      const groups = result.success ? result.options : [];
      rememberGroups(groups);
      setResults(groups);
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

  // Add a GROUP: union ALL its member ids into the flat draft (idempotent). The
  // store is updated first so the chip renders the canonical label, not an id.
  function addGroup(group: SkillGroup) {
    rememberGroups([group]);
    if (!isSkillGroupSelected(selected, group)) {
      onReplace(addSkillGroup(selected, group));
    }
  }

  // Remove a chip: drop EVERY member id of its group (difference). Removing a
  // twin chip removes BOTH twin ids in one action. `members` comes from the
  // derived chip (already scoped to the actually-selected member ids).
  function removeChip(members: ReadonlyArray<string>) {
    onReplace(removeSkillGroup(selected, members));
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
        rememberGroups(result.candidates);
        // PRE-ADD: union EVERY candidate group's member ids into the draft as
        // removable chips (propose-and-approve, draft-only); dedupe against an
        // existing manual selection via the union helper.
        let next = [...selected];
        for (const group of result.candidates) next = addSkillGroup(next, group);
        onReplace(next);
      }
    });
    // `selected`/`onReplace` are intentionally captured at call time, not deps:
    // the auto-run is a one-shot mount effect guarded by a ref below.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [parsedResumeId, rememberGroups]);

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

  // Pinned chips: PinnedChips removes by the chip's canonical conceptId, but the
  // group's member ids must ALL be dropped. Resolve the canonical → its chip's
  // member set (already scoped to selected) and remove the difference.
  const chipMembersByCanonical = new Map(
    skillChips.map((c) => [c.conceptId, c.memberConceptIds])
  );
  function onRemovePinned(canonicalId: string) {
    const members = chipMembersByCanonical.get(canonicalId);
    // Defensive fallback (canonical always present): drop just the id.
    removeChip(members ?? [canonicalId]);
  }

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
        onRemove={onRemovePinned}
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
              <p id={searchHelpId} className="text-body-sm text-text-primary">
                {t("matchPrefs.skill.searchHint")}
              </p>
            </div>

            {/* Resultat-list: en knapp-rad per GRUPP (klick adderar HELA gruppen
                till draften som EN chip). role="listbox" inte använt — raderna är
                add-knappar, inte ett single-select. Lugn live-region för status. */}
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
                <p className="text-body-sm text-text-primary px-4 py-3">
                  {t("matchPrefs.skill.noResults")}
                </p>
              ) : results.length === 0 ? (
                <p className="text-body-sm text-text-primary px-4 py-3">
                  {t("matchPrefs.skill.searchPrompt")}
                </p>
              ) : (
                results.map((group) => {
                  // "Already added" = ALL of the group's member ids selected (a
                  // half-selected twin is still addable so the pair completes).
                  const already = isSkillGroupSelected(selected, group);
                  return (
                    <button
                      key={group.conceptId}
                      type="button"
                      className="jp-popover-row"
                      aria-pressed={already}
                      onClick={() => addGroup(group)}
                    >
                      <span>{group.label}</span>
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
        <p role="status" className="text-body-sm text-text-primary">
          {t("matchPrefs.skill.noCv")}
        </p>
      );
    case "noRole":
      return (
        <p role="status" className="text-body-sm text-text-primary">
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
