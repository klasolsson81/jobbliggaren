"use client";

import {
  useId,
  useMemo,
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
import { buildTaxonomyLabelResolver } from "@/lib/job-ads/chip-models";
import {
  applyClaimsDelta,
  buildLabelIndex,
  enforceClaims,
  getTokenRange,
  isTextRepresentable,
  parseSearchText,
  sameUrlState,
  serializeSearchText,
  updateTextForStateChange,
  type ParsedClaims,
} from "@/lib/job-ads/tokenize";
import { JobAdTypeahead } from "./job-ad-typeahead";

/**
 * Hero-sökruta som SPEGLAR söket (Fas E2i, Klas-val 2026-06-11 "Normal ruta
 * som speglar söket" — ersätter E2h:s chips-i-fältet som blev fult renderat
 * och inte visade alla taggar i filter-raden).
 *
 * Modell (CTO VAL 1 = Variant C′, docs/reviews/2026-06-11-sok-paritet-
 * e2i-cto.md): fältets text är ANVÄNDARENS buffert; URL:en är persistent
 * sanning; invariant I1: parse(text) ⊆ state (delmängd — state får bära MER:
 * popover-valda dimensioner och icke-representabla labels lever enbart i
 * filter-raden under träffarna, som är TOTAL spegel; fältet är best-effort).
 *
 * - **Egen skrivning = delta-parse vid commit-punkter** (avgränsar-keystroke,
 *   Enter/Sök, förslags-val): skillnaden mellan föregående och nya text-
 *   anspråk appliceras på staten — popover-valda filter som texten aldrig
 *   gjort anspråk på rörs inte. Ordet under caret är pågående (CTO VAL 3 —
 *   radering mitt i ett ord släpper inte filtret per keystroke).
 * - **Texten behålls** — ord försvinner ALDRIG ur fältet vid taggning
 *   (E2d/E2h-felklassen). Tar man bort en tagg i filter-raden (×) uppdateras
 *   texten via extern-divergens-synken (kirurgisk borttagning som bevarar
 *   ordningen; annars kanonisk serialize).
 * - **Popover-val skrivs INTE in i texten** (CTO VAL 4a) — fältet visar det
 *   SKRIVNA; filter-raden visar allt.
 * - `router.replace` + `{scroll:false}` (E2h VAL 2 består); toolbar pushar.
 * - No-JS/pre-hydration: rått `<input name="q">`; efter hydration är synliga
 *   inputen NAMNLÖS (texten "Göteborg systemutvecklare" som q vore dubbel-
 *   filtrering) — hidden inputs bär de riktiga parametrarna.
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

  const hydrated = useSyncExternalStore(
    emptySubscribe,
    () => true,
    () => false,
  );

  const labelIndex = useMemo(() => buildLabelIndex(taxonomy), [taxonomy]);
  const resolveLabel = useMemo(
    () => buildTaxonomyLabelResolver(taxonomy),
    [taxonomy],
  );

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

  // Fältets text — användarens buffert. Initieras till kanonisk spegel av
  // landnings-staten (recent-/direktlänk-navigering visar sitt sök).
  const [text, setText] = useState(() =>
    serializeSearchText(base, resolveLabel, labelIndex),
  );
  const [caret, setCaret] = useState<number | null>(null);
  const [limitNotice, setLimitNotice] = useState(false);
  const [announcement, setAnnouncement] = useState("");

  // Senast applicerade text-anspråk (delta-basen). Init mot start-texten
  // så landnings-spegeln inte re-committas som "nya" anspråk. State (inte
  // ref) — läses/skrivs i render-sentinelen nedan (react-hooks/refs
  // förbjuder ref-access under render).
  const [prevClaims, setPrevClaims] = useState<ParsedClaims>(() =>
    parseSearchText(text, labelIndex, null),
  );

  // ARBETS-STATEN (delta-basen): URL-sanningen sådan vi känner den
  // INKLUSIVE egna in-flight-commits. Delta-appliceringen går mot denna —
  // INTE mot en useOptimistic-overlay, som reverterar till (stale) base
  // mellan transitions och då skulle tappa nyss committade dimensioner ur
  // nästa delta-bas (CTO-addendum BESLUT 3, deviation-ACK).
  const [lastCommitted, setLastCommitted] = useState<JobbUrlState>(base);
  // Own-roundtrip-DETEKTORN (CTO-addendum BESLUT 1): lista över egna
  // commits i flykt — singel-värdet räckte inte (två commits i flykt →
  // mellanliggande egen props-leverans mis-klassades som extern och
  // serialiserade om texten mitt under skrivning, E2d/E2h-felklassen).
  // Base som matchar NÅGON post = egen (prune t.o.m. träffen; lastCommitted
  // RÖRS EJ — den ligger före base tills listan är tom); annars extern.
  const [recentCommits, setRecentCommits] = useState<JobbUrlState[]>([]);
  const [prevBase, setPrevBase] = useState(base);
  if (base !== prevBase) {
    const hitIndex = recentCommits.findIndex((s) => sameUrlState(base, s));
    if (hitIndex >= 0) {
      setRecentCommits(recentCommits.slice(hitIndex + 1));
      // Adoptera sort/pageSize ur basen — sameUrlState jämför dem inte, så
      // en extern sort-ändring vars filter-axlar matchar en in-flight-post
      // får inte lämna stale sortBy i delta-basen (code-reviewer re-review
      // Minor: nästa text-commit skulle annars tyst revertera sort-valet).
      if (
        lastCommitted.sortBy !== base.sortBy ||
        lastCommitted.pageSize !== base.pageSize
      )
        setLastCommitted({
          ...lastCommitted,
          sortBy: base.sortBy,
          pageSize: base.pageSize,
        });
    } else {
      // EXTERN divergens (toolbar-×/Rensa/recent-nav): synka texten,
      // nollställ delta-bokföringen + caret/notis/annons (annars kan en
      // stale suggestQuery hålla listan öppen och en identisk framtida
      // annons-sträng utebli — code-reviewer Minor 2 + design Mi2).
      const nextText = updateTextForStateChange(
        text,
        prevBase,
        base,
        resolveLabel,
        labelIndex,
      );
      setText(nextText);
      setPrevClaims(parseSearchText(nextText, labelIndex, null));
      setLimitNotice(false);
      setCaret(null);
      setAnnouncement("");
      setLastCommitted(base);
      setRecentCommits([]);
    }
    setPrevBase(base);
  }

  function commit(next: JobbUrlState, announce: string) {
    setLastCommitted(next);
    setRecentCommits((prev) => [...prev, next].slice(-10));
    startTransition(() => {
      router.replace(buildJobbHref(next), { scroll: false });
    });
    if (announce) setAnnouncement(announce);
  }

  // Delta-commit (C′ regel 1): parse → diff mot förra anspråken → applicera.
  function runDelta(nextText: string, caretIndex: number | null) {
    const claims = parseSearchText(nextText, labelIndex, caretIndex);
    const result = applyClaimsDelta(lastCommitted, prevClaims, claims, taxonomy);
    setPrevClaims(result.appliedClaims);
    setLimitNotice(result.rejectedQ.length > 0);
    if (!sameUrlState(result.next, lastCommitted)) {
      commit(
        result.next,
        [
          ...result.addedLabels.map((l) => `Lade till ${l}`),
          ...result.removedLabels.map((l) => `Tog bort ${l}`),
        ].join(". "),
      );
    }
  }

  function onFieldChange(nextText: string, caretIndex: number | null) {
    setText(nextText);
    setCaret(caretIndex);
    // Commit-punkt = tecknet före caret är en avgränsare (ordet avslutades
    // nyss). Ren radering committas inte per keystroke — deltat landar vid
    // nästa commit-punkt/Enter (CTO VAL 3, dokumenterad konsekvens).
    const justTyped = caretIndex !== null ? nextText[caretIndex - 1] : null;
    if (justTyped === " " || justTyped === ",")
      runDelta(nextText, caretIndex);
  }

  // Förslags-val (klick / Tab / pil+Enter). Text-insert är GATED: labeln
  // skrivs in ENDAST om parse bevisligen återfinner den (dimensions-label:
  // isTextRepresentable; Title-label: inga taxonomi-ord — annars skulle
  // texten claima en dimension staten inte fick, I1-brott; code-reviewer
  // Major 2). State går via DELTA-vägen (enforcement-täckt, CTO BESLUT 2-
  // synergin) + en garanterad compose av själva valet (täcker icke-
  // insertbara: ambiguös label/Title-med-taxonomi-ord — staten får valet,
  // texten claimar det inte) + slutlig enforceClaims (compose-vägens
  // normalisering får inte släcka text-claimade dimensioner).
  function onSelectSuggestion(suggestion: SuggestionDto) {
    const range =
      caret !== null
        ? getTokenRange(text, caret)
        : getTokenRange(text, text.length);
    const insertable =
      suggestion.kind === "Title"
        ? parseSearchText(suggestion.label, labelIndex, null).matches
            .length === 0
        : suggestion.conceptId !== null &&
          isTextRepresentable(
            suggestion.label,
            { kind: suggestion.kind, conceptId: suggestion.conceptId },
            labelIndex,
          );
    const insert = insertable ? `${suggestion.label} ` : "";
    const nextText = range
      ? text.slice(0, range.start) + insert + text.slice(range.end)
      : text + (text.length > 0 && !/[ ,]$/.test(text) ? " " : "") + insert;

    const claims = parseSearchText(nextText, labelIndex, null);
    const delta = applyClaimsDelta(lastCommitted, prevClaims, claims, taxonomy);
    const withSelection = enforceClaims(
      composeSuggestionChip(suggestion, delta.next, taxonomy),
      delta.appliedClaims,
      taxonomy,
    );

    setText(nextText);
    setCaret(null);
    setPrevClaims(delta.appliedClaims);
    setLimitNotice(delta.rejectedQ.length > 0);
    if (!sameUrlState(withSelection, lastCommitted))
      commit(withSelection, `Lade till ${suggestion.label}`);
  }

  // Sök/Enter utan markerat förslag: finalisera HELA texten (inget caret-
  // undantag) — pågående ord committas.
  function onSubmitText() {
    runDelta(text, null);
  }

  // Suggest-prefix = ordet under caret (fältet bär hela söktexten — förslag
  // ska gälla det man skriver, inte hela strängen).
  const caretToken =
    caret !== null ? getTokenRange(text, caret) : null;
  const suggestQuery = caretToken
    ? text.slice(caretToken.start, caretToken.end)
    : "";

  const committedQ = lastCommitted.q.trim();

  return (
    <form
      action="/jobb"
      method="get"
      className="jp-hero__searchblock"
      onSubmit={(e) => {
        e.preventDefault();
        onSubmitText();
      }}
    >
      <label htmlFor="jobb-q" className="jp-hero__searchlabels">
        Sök efter yrke, arbetsgivare eller ort
      </label>
      <div className="jp-hero__searchrow">
        {hydrated ? (
          <JobAdTypeahead
            id="jobb-q"
            value={text}
            suggestQuery={suggestQuery}
            onChange={onFieldChange}
            onSelect={onSelectSuggestion}
            selectOnTab
            wrapperClassName="jp-hero__searchfield"
            inputClassName="jp-hero__input"
            ariaDescribedBy={helpId}
          />
        ) : (
          // Pre-hydration/no-JS: rått q-fält — native GET-submit bär hela
          // söktexten som q (backend-parsern är SPOT och tål rå sträng).
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
      {/* Hjälptext bär tagg-/Tab-instruktionen (ALDRIG placeholder — Klas
          hård regel). role="status" så q-max-skiftet annonseras. */}
      <p id={helpId} role="status" className="jp-hero__searchhelp">
        {limitNotice
          ? `Söktexten är full (max ${Q_MAX_LENGTH} tecken). Ta bort en tagg för att lägga till fler ord.`
          : "Ord blir taggar i filterraden vid träffarna när du skriver mellanslag eller komma. Välj förslag med piltangenterna och Tab."}
      </p>

      {/* aria-live-annons för tagg-tillägg/-borttagning — viktigare än i
          E2h: den visuella feedbacken (taggarna) sitter nu i filter-raden
          under träfflistan, långt från fältet. */}
      <p role="status" aria-live="polite" className="sr-only">
        {announcement}
      </p>

      {/* No-JS-fallback: aktiva filter som hidden inputs. Synliga inputen
          är NAMNLÖS efter hydration — spegel-texten som q vore dubbel-
          filtrering; committad residual-q bärs som hidden input. */}
      {hydrated && committedQ.length > 0 && (
        <input type="hidden" name="q" value={committedQ} />
      )}
      {lastCommitted.occupationGroup.map((v) => (
        <input
          key={`occupationGroup-${v}`}
          type="hidden"
          name="occupationGroup"
          value={v}
        />
      ))}
      {lastCommitted.region.map((v) => (
        <input key={`region-${v}`} type="hidden" name="region" value={v} />
      ))}
      {lastCommitted.municipality.map((v) => (
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
