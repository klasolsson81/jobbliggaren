"use client";

import {
  useId,
  useMemo,
  useState,
  useSyncExternalStore,
  useTransition,
} from "react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { Search, X } from "lucide-react";
import {
  Q_MAX_LENGTH,
  type JobAdSortBy,
  type SuggestionDto,
} from "@/lib/dto/job-ads";
import type { TaxonomyTree } from "@/lib/dto/taxonomy";
import {
  buildJobbHref,
  DEFAULT_SORT_BY,
  withCommitFlag,
  type JobbUrlState,
} from "@/lib/job-ads/search-params";
import { composeSuggestionChip } from "@/lib/job-ads/chip-composition";
import { buildTaxonomyLabelResolver } from "@/lib/job-ads/chip-models";
import {
  applyClaimsDelta,
  buildLabelIndex,
  EMPTY_CLAIMS,
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
  // Klass 2 (2026-06-13) — panel-valda anställningsform/omfattning. Aldrig
  // text-representabla i fältet (som popover-dimensionerna, CTO VAL 4a) —
  // de bärs bara genom delta-/commit-vägen så en sökord-ändring inte raderar
  // ett aktivt Klass-2-filter (buildJobbHref kräver dem; utan denna tråd
  // skulle fältets commit bygga en href som tappar dem).
  employmentType: ReadonlyArray<string>;
  worktimeExtent: ReadonlyArray<string>;
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
  employmentType,
  worktimeExtent,
  sortBy,
  pageSize,
}: JobbHeroSearchProps) {
  const router = useRouter();
  const t = useTranslations("jobads.ui");
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
      employmentType: [...employmentType],
      worktimeExtent: [...worktimeExtent],
      sortBy,
      pageSize,
    }),
    [
      q,
      occupationGroup,
      region,
      municipality,
      employmentType,
      worktimeExtent,
      sortBy,
      pageSize,
    ],
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
    const adoptSortPageSize = () => {
      // Adoptera sort/pageSize ur basen — sameUrlState jämför dem inte, så
      // en extern sort-ändring vars filter-axlar matchar (in-flight-post
      // eller oförändrad state) får inte lämna stale sortBy i delta-basen
      // (code-reviewer re-review Minor: nästa text-commit skulle annars
      // tyst revertera sort-valet).
      if (
        lastCommitted.sortBy !== base.sortBy ||
        lastCommitted.pageSize !== base.pageSize
      )
        setLastCommitted({
          ...lastCommitted,
          sortBy: base.sortBy,
          pageSize: base.pageSize,
        });
    };
    if (hitIndex >= 0) {
      // Egen roundtrip (in-flight-commit landar) — texten orörd.
      setRecentCommits(recentCommits.slice(hitIndex + 1));
      adoptSortPageSize();
    } else if (sameUrlState(base, lastCommitted)) {
      // E2j skip-guard: den inkommande basens filter-state (q + dimensioner)
      // matchar vad vi SENAST committade — endast en icke-state-param
      // (commit-flaggan, sort eller pageSize) skiftade. Texten speglar redan
      // den staten → ingen resync, ingen extern-divergens-klassning. Detta
      // skyddar strip-efter-mount (?commit=1-borttagning, StripCommitParam)
      // från att felaktigt serialisera om användarens text (E2d/E2h-felklassen).
      // Jämförs mot lastCommitted (hero:ns auktoritativa state), INTE prevBase
      // — prevBase kan vara stale (props uppdateras inte synkront med egna
      // commits) och en äkta extern "Rensa allt" till tomt får då inte
      // miss-klassas som no-op. sort/pageSize adopteras fortfarande.
      adoptSortPageSize();
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

  // markCommit (E2j) = avsiktlig commit (Enter/Sök/förslags-val/×-clear) →
  // ?commit=1-suffix så backend auto-capturerar. Live-delta (onFieldChange)
  // utelämnar det. commit-flaggan ligger UTANFÖR JobbUrlState/buildJobbHref/
  // sameUrlState (transient signal) — den adderas bara på navigerings-
  // strängen och strippas efter mount (StripCommitParam).
  function commit(next: JobbUrlState, announce: string, markCommit = false) {
    setLastCommitted(next);
    setRecentCommits((prev) => [...prev, next].slice(-10));
    startTransition(() => {
      const href = buildJobbHref(next);
      router.replace(markCommit ? withCommitFlag(href) : href, {
        scroll: false,
      });
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
          ...result.addedLabels.map((l) =>
            t("heroSearch.announceAdded", { label: l }),
          ),
          ...result.removedLabels.map((l) =>
            t("heroSearch.announceRemoved", { label: l }),
          ),
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
    // Förslags-val är en commit-punkt (E2j): committa ALLTID med commit-intent
    // så sökningen auto-capturas — även i det sällsynta fall valet inte
    // ändrar filter-staten (re-val av redan applicerat förslag = "kör igen").
    commit(
      withSelection,
      t("heroSearch.announceAdded", { label: suggestion.label }),
      true,
    );
  }

  // Sök/Enter utan markerat förslag: finalisera HELA texten (inget caret-
  // undantag) — pågående ord committas. E2j: detta är den primära commit-
  // punkten → committa ALLTID med commit-intent (?commit=1), även när filter-
  // staten är oförändrad. "Sök" betyder "kör/spara den här sökningen" — en
  // re-sökning på samma filter ska bumpa recency, inte vara en no-op.
  function onSubmitText() {
    const claims = parseSearchText(text, labelIndex, null);
    const result = applyClaimsDelta(lastCommitted, prevClaims, claims, taxonomy);
    setPrevClaims(result.appliedClaims);
    setLimitNotice(result.rejectedQ.length > 0);
    commit(
      result.next,
      [
        ...result.addedLabels.map((l) =>
          t("heroSearch.announceAdded", { label: l }),
        ),
        ...result.removedLabels.map((l) =>
          t("heroSearch.announceRemoved", { label: l }),
        ),
      ].join(". "),
      true,
    );
  }

  // ×-clear (E2j, CTO VAL 4 = semantik ii): rensa texten + de filter texten
  // gjorde anspråk på (parse(text)-delmängden) — INTE popover-valda
  // dimensioner (I1: state får bära mer än texten). Delta mot tomma claims
  // tar bort exakt prevClaims ur staten; popover-dim överlever. Egen commit
  // via commit()-vägen (recentCommits-registrering) så texten inte
  // serialiseras om vid props-retur. commit-intent satt (CTO VAL 5 punkt 3).
  function onClear() {
    const delta = applyClaimsDelta(lastCommitted, prevClaims, EMPTY_CLAIMS, taxonomy);
    setText("");
    setCaret(null);
    setPrevClaims(EMPTY_CLAIMS);
    setLimitNotice(false);
    commit(delta.next, t("heroSearch.announceCleared"), true);
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
        {t("heroSearch.fieldLabel")}
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
        {/* Kontrollerad ×-clear (E2j): ersätter native
            ::-webkit-search-cancel-button (suppress:ad i CSS) som bara
            rensade texten utan att committa en delta → filtren överlevde.
            Denna går genom onClear → applyClaimsDelta(EMPTY_CLAIMS) (semantik
            ii). Visas bara när det finns text att rensa. */}
        {hydrated && text.length > 0 && (
          <button
            type="button"
            className="jp-hero__clearbtn"
            onClick={onClear}
            aria-label={t("heroSearch.clearField")}
          >
            <X size={18} aria-hidden="true" />
          </button>
        )}
        <button type="submit" className="jp-hero__searchbtn">
          <Search size={18} aria-hidden="true" /> {t("heroSearch.submit")}
        </button>
      </div>
      {/* Hjälptext bär tagg-/Tab-instruktionen (ALDRIG placeholder — Klas
          hård regel). role="status" så q-max-skiftet annonseras. */}
      <p id={helpId} role="status" className="jp-hero__searchhelp">
        {limitNotice
          ? t("heroSearch.limitNotice", { max: Q_MAX_LENGTH })
          : t("heroSearch.help")}
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
      {/* Klass 2 — no-JS-submit bär aktiva anställningsform/omfattning-filter
          så en sökord-sökning utan JS inte tappar panelvalen. */}
      {lastCommitted.employmentType.map((v) => (
        <input
          key={`employmentType-${v}`}
          type="hidden"
          name="employmentType"
          value={v}
        />
      ))}
      {lastCommitted.worktimeExtent.map((v) => (
        <input
          key={`worktimeExtent-${v}`}
          type="hidden"
          name="worktimeExtent"
          value={v}
        />
      ))}
      {sortBy !== DEFAULT_SORT_BY && (
        <input type="hidden" name="sortBy" value={sortBy} />
      )}
      {pageSize && <input type="hidden" name="pageSize" value={pageSize} />}
      {/* E2j — no-JS-submit ÄR per definition en commit (användaren tryckte
          Sök) → statiskt commit=1 så backend auto-capturerar. Vid hydration
          interceptas submit (onSubmit preventDefault) och router-vägen bär
          commit som transient suffix istället — denna åker då aldrig.
          Värde "true" (ASP.NET bool-binding tar inte "1"). */}
      <input type="hidden" name="commit" value="true" />
    </form>
  );
}
