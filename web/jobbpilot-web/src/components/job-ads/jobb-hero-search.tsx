"use client";

import {
  useId,
  useMemo,
  useOptimistic,
  useState,
  useSyncExternalStore,
  useTransition,
} from "react";
import { useRouter } from "next/navigation";
import { Search } from "lucide-react";
import {
  Q_MAX_LENGTH,
  type JobAdSortBy,
  type SuggestionDto,
} from "@/lib/dto/job-ads";
import type { TaxonomyTree } from "@/lib/dto/taxonomy";
import {
  buildJobbHref,
  DEFAULT_SORT_BY,
  type JobbUrlState,
} from "@/lib/job-ads/search-params";
import { composeSuggestionChip } from "@/lib/job-ads/chip-composition";
import {
  buildChipModels,
  buildTaxonomyLabelResolver,
  removeChipFromState,
  type SearchChip,
} from "@/lib/job-ads/chip-models";
import {
  buildLabelIndex,
  sameUrlState,
  tokenizeDraft,
} from "@/lib/job-ads/tokenize";
import { ChipSearchField } from "./chip-search-field";

/**
 * Hero-sökruta med chips-i-fältet (Fas E2h, Klas produktspec 2026-06-11 —
 * ersätter E2d:s "välj förslag = sök direkt + töm fältet").
 *
 * - **Chips deriveras HELT ur URL:en** (CTO VAL 1 = A, E2g-principen):
 *   dimension-params → chips via taxonomy-label-lookup; q → en chip per ord
 *   (wire-ärligt — websearch_to_tsquery AND:ar ord som lexem, ADR 0062).
 *   Enda lokala staten är utkast-ordet. × i fältet = samma state-operation
 *   som toolbar-chip-× (delade chip-models-helpers, SPOT).
 * - **Val av förslag (klick / Tab / pil+Enter) → chip i fältet + live-
 *   commit.** Fältet töms ALDRIG av ett val — utkastet ersätts av chipet
 *   och man skriver vidare ("systemutvecklare göteborg heltid"-flödet).
 * - **Mellanslag/komma avslutar ord** → tokenizern (lib/job-ads/tokenize)
 *   gör exakt-unik taxonomi-match → dimension-chip, annars fritext-q-chip.
 * - **`router.replace` + {scroll:false}** för fältets commits (CTO VAL 2 =
 *   B): att komponera EN sökning är EN logisk akt — history speglar
 *   sökningar, inte tangenttryck. Toolbar-× pushar fortsatt (dokumenterad
 *   asymmetri: fältet = pågående komposition, toolbaren = redigering).
 * - **No-JS/pre-hydration** (architect F5): rått `<input name="q">` med
 *   hela committade q + hidden inputs — native GET-submit fungerar utan JS.
 *   Efter hydration växlar fältet till chips-läge (inputen bär bara
 *   utkastet; hidden q-input behålls som degraderings-försäkring).
 */

interface JobbHeroSearchProps {
  taxonomy: TaxonomyTree | null;
  q: string;
  occupationGroup: ReadonlyArray<string>;
  region: ReadonlyArray<string>;
  municipality: ReadonlyArray<string>;
  sortBy: JobAdSortBy;
  pageSize?: string;
}

const emptySubscribe = () => () => {};

export function JobbHeroSearch({
  taxonomy,
  q,
  occupationGroup,
  region,
  municipality,
  sortBy,
  pageSize,
}: JobbHeroSearchProps) {
  const router = useRouter();
  const [, startTransition] = useTransition();
  const helpId = useId();

  // false på servern/hydration-renderingen, true direkt efter — standard-
  // mönstret för progressivt JS-läge utan effect (ingen lint-träff, ingen
  // hydration-mismatch).
  const hydrated = useSyncExternalStore(
    emptySubscribe,
    () => true,
    () => false,
  );

  // Utkast-ordet — ENDA lokala staten (chips bor i URL:en). Lämnas orört
  // vid externa URL-ändringar (ett halvskrivet ord är användarens, inte
  // URL:ens — E2d:s prev-prop-sentinel utgick med draft/q-dualiteten).
  const [draft, setDraft] = useState("");
  // q-max-guard-notis (architect F2): ord som vägrades commit.
  const [limitNotice, setLimitNotice] = useState(false);
  // aria-live-annons för chip-tillägg/-borttagning (F4-mitigering 3).
  const [announcement, setAnnouncement] = useState("");

  // URL-sanningen som bas + optimistiskt overlay under pågående replace-
  // transition (E2g-mönstret — chipen syns omedelbart).
  const base = useMemo<JobbUrlState>(
    () => ({
      q,
      occupationGroup: [...occupationGroup],
      region: [...region],
      municipality: [...municipality],
      sortBy,
      pageSize,
    }),
    [q, occupationGroup, region, municipality, sortBy, pageSize],
  );
  const [urlState, setOptimisticState] = useOptimistic(
    base,
    (_current, next: JobbUrlState) => next,
  );

  const labelIndex = useMemo(() => buildLabelIndex(taxonomy), [taxonomy]);
  const resolveLabel = useMemo(
    () => buildTaxonomyLabelResolver(taxonomy),
    [taxonomy],
  );
  const chips = buildChipModels(urlState, resolveLabel, { includeQ: true });

  // q-max-notisen nollas när URL-staten byts (egen commit ELLER extern
  // navigation — i båda fallen är notisens premiss förbrukad: en borttagen
  // tagg frigör plats, en extern navigering byter kontext). Prev-prop-
  // sentinel UNDER render — Reacts "justera state vid prop-skifte", ingen
  // effect. (code-reviewer Major 1 E2h: utan denna överlevde notisen en
  // recent-search-navigering.)
  const [prevBase, setPrevBase] = useState(base);
  if (base !== prevBase) {
    setPrevBase(base);
    setLimitNotice(false);
  }

  function commit(next: JobbUrlState, announce?: string) {
    startTransition(() => {
      setOptimisticState(next);
      router.replace(buildJobbHref(next), { scroll: false });
    });
    if (announce) setAnnouncement(announce);
  }

  // Tokenisera vid avgränsare (mellanslag/komma) — färdiga ord blir chips
  // och live-committas; pågående ord stannar i utkastet.
  function onDraftChange(value: string) {
    const result = tokenizeDraft(value, urlState, taxonomy, labelIndex, {
      finalizeAll: false,
    });
    // Kongruensfri annons-form (design-reviewer M3 — "Stockholms län
    // tillagd" är fel svenska för ett-genus; "Lade till X" är genusfri).
    if (result.changed)
      commit(
        result.next,
        result.addedLabels.map((l) => `Lade till ${l}`).join(". "),
      );
    setDraft(result.remainder);
    setLimitNotice(result.rejected.length > 0);
  }

  // Förslags-val (klick / Tab / pil+Enter): chip på rätt dimension via
  // composeSuggestionChip (SPOT — samma väg som tokenizern). Utkastet
  // ersätts av chipet; fältet behålls för nästa ord. No-op-guard: redan
  // valt förslag ger ingen commit/annons (code-reviewer Minor 2).
  function onSelectSuggestion(suggestion: SuggestionDto) {
    const next = composeSuggestionChip(suggestion, urlState, taxonomy);
    if (!sameUrlState(next, urlState))
      commit(next, `Lade till ${suggestion.label}`);
    setDraft("");
    setLimitNotice(false);
  }

  // Sök-knappen/Enter utan markerat förslag: finalisera HELA utkastet
  // (architect F5 — Sök = utkast-commit + stäng; allt annat är redan live).
  function onSubmitDraft() {
    const result = tokenizeDraft(draft, urlState, taxonomy, labelIndex, {
      finalizeAll: true,
    });
    if (result.changed)
      commit(
        result.next,
        result.addedLabels.map((l) => `Lade till ${l}`).join(". "),
      );
    setDraft(result.remainder);
    setLimitNotice(result.rejected.length > 0);
  }

  function onRemoveChip(chip: SearchChip) {
    commit(removeChipFromState(urlState, chip), `Tog bort ${chip.label}`);
    setLimitNotice(false);
  }

  function onRemoveLast() {
    const last = chips[chips.length - 1];
    if (last) onRemoveChip(last);
  }

  const committedQ = urlState.q.trim();

  return (
    <form
      action="/jobb"
      method="get"
      className="jp-hero__searchblock"
      onSubmit={(e) => {
        e.preventDefault();
        onSubmitDraft();
      }}
    >
      <label htmlFor="jobb-q" className="jp-hero__searchlabels">
        Sök efter yrke, arbetsgivare eller ort
      </label>
      <div className="jp-hero__searchrow">
        {hydrated ? (
          <ChipSearchField
            id="jobb-q"
            chips={chips}
            onRemoveChip={onRemoveChip}
            value={draft}
            onChange={onDraftChange}
            onSelect={onSelectSuggestion}
            onRemoveLast={onRemoveLast}
            ariaDescribedBy={helpId}
          />
        ) : (
          // Pre-hydration/no-JS: rått q-fält — native GET-submit bär hela
          // söktexten (chips-läget skulle annars tappa committade q-ord
          // vid en JS-fri submit, architect F5-fallgropen).
          <input
            id="jobb-q"
            name="q"
            type="search"
            defaultValue={q}
            className="jp-hero__input"
            aria-describedby={helpId}
          />
        )}
        <button type="submit" className="jp-hero__searchbtn">
          <Search size={18} aria-hidden="true" /> Sök
        </button>
      </div>
      {/* Hjälptext bär Tab-instruktionen (F4-mitigering 4 — ALDRIG
          placeholder, Klas hård regel). role="status" så q-max-skiftet
          annonseras för skärmläsare (design-reviewer M2 — swap-in-place
          är rätt mönster, men bytet får inte vara tyst). */}
      <p id={helpId} role="status" className="jp-hero__searchhelp">
        {limitNotice
          ? `Söktexten är full (max ${Q_MAX_LENGTH} tecken). Ta bort en tagg för att lägga till fler ord.`
          : "Ord blir taggar när du skriver mellanslag eller komma. Välj förslag med piltangenterna och Tab."}
      </p>

      {/* aria-live-annons för chip-tillägg/-borttagning (F4-mitigering 3). */}
      <p role="status" aria-live="polite" className="sr-only">
        {announcement}
      </p>

      {/* No-JS-fallback: aktiva filter som hidden inputs så en native
          GET-submit inte tappar dem (`page` utelämnas → sida 1). I chips-
          läget är committad q en hidden input (degraderings-försäkring —
          synliga inputen bär bara utkastet). */}
      {hydrated && committedQ.length > 0 && (
        <input type="hidden" name="q" value={committedQ} />
      )}
      {urlState.occupationGroup.map((v) => (
        <input
          key={`occupationGroup-${v}`}
          type="hidden"
          name="occupationGroup"
          value={v}
        />
      ))}
      {urlState.region.map((v) => (
        <input key={`region-${v}`} type="hidden" name="region" value={v} />
      ))}
      {urlState.municipality.map((v) => (
        <input
          key={`municipality-${v}`}
          type="hidden"
          name="municipality"
          value={v}
        />
      ))}
      {sortBy !== DEFAULT_SORT_BY && (
        <input type="hidden" name="sortBy" value={sortBy} />
      )}
      {pageSize && <input type="hidden" name="pageSize" value={pageSize} />}
    </form>
  );
}
