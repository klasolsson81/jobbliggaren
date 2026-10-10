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
  Q_MIN_LENGTH,
  type JobAdSortBy,
  type SuggestionDto,
} from "@/lib/dto/job-ads";
import type { TaxonomyTree } from "@/lib/dto/taxonomy";
import {
  buildJobbHref,
  DEFAULT_SORT_BY,
  DISTANS_ON_VALUE,
  DISTANS_PARAM,
  serializeJobbAxis,
  withCommitFlag,
  type JobbUrlState,
} from "@/lib/job-ads/search-params";
import { composeSuggestionChip } from "@/lib/job-ads/chip-composition";
import { buildTaxonomyLabelResolver } from "@/lib/job-ads/chip-models";
import { codedTaxonomyName } from "@/lib/i18n/coded-taxonomy";
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
  type ClaimsDeltaResult,
  type ParsedClaims,
} from "@/lib/job-ads/tokenize";
import { JobAdTypeahead } from "./job-ad-typeahead";

/**
 * The hero search field, which mirrors the current search as text.
 *
 * The field's text is the user's buffer and the URL is the source of truth. The
 * invariant is parse(text) ⊆ state: the state may hold more than the text, because
 * filters chosen in the popovers, and labels that cannot be written as text, appear
 * only in the filter row under the results. That row mirrors everything; the field
 * is best effort.
 *
 * - **Typing applies a delta at commit points** (a separator key, Enter or the
 *   search button, picking a suggestion): the difference between the previous and
 *   the new text claims is applied to the state, so filters the text never claimed
 *   are left alone. The word under the caret is still being typed, so deleting
 *   inside a word does not drop its filter on every keystroke.
 * - **Text is never removed by tagging.** Removing a chip in the filter row updates
 *   the text through the external-change sync: a surgical removal that keeps the
 *   order, otherwise a canonical re-serialisation.
 * - **Popover choices are not written into the text.** The field shows what was
 *   typed; the filter row shows everything.
 * - Navigation uses `router.replace` with `{ scroll: false }`; the toolbar pushes.
 * - Before hydration and without JavaScript the same field is named `q` and holds
 *   `q`. Once hydrated it has no name, since submitting the mirrored text as q would
 *   filter twice; hidden inputs carry the real parameters.
 */

interface JobbHeroSearchProps {
  taxonomy: TaxonomyTree | null;
  q: string;
  occupationGroup: ReadonlyArray<string>;
  region: ReadonlyArray<string>;
  municipality: ReadonlyArray<string>;
  // The filters below are never written as text in the field, but every commit and
  // the no-JS form must carry them. Otherwise a commit from the field (Enter, the
  // search button, a suggestion, clearing, or a live delta while typing) would build
  // a URL without them and silently drop an active filter.
  remote: boolean;
  employmentType: ReadonlyArray<string>;
  worktimeExtent: ReadonlyArray<string>;
  matchGrades: ReadonlyArray<string>;
  employer: ReadonlyArray<string>;
  sortBy: JobAdSortBy;
  pageSize?: string;
  // Whether the URL this page loaded with carried `?commit=true`. If it did, the
  // search is already saved and the "Spara sökningen" link is hidden; a shared or
  // bookmarked URL without it shows the link, so its recipient can save the search.
  initialCommitted: boolean;
}

const emptySubscribe = () => () => {};

/**
 * The help line's notice, one at a time:
 * - `limit`: the search text is full (Q_MAX_LENGTH), so further words are not committed.
 * - `tooShort`: the combined search text is shorter than Q_MIN_LENGTH and is not used
 *   in the search; the filters still apply, as in the backend's SearchQueryParser.
 */
type Notice =
  | { kind: "limit" }
  | { kind: "tooShort"; word: string }
  | null;

export function JobbHeroSearch({
  taxonomy,
  q,
  occupationGroup,
  region,
  municipality,
  remote,
  employmentType,
  worktimeExtent,
  matchGrades,
  employer,
  sortBy,
  pageSize,
  initialCommitted,
}: JobbHeroSearchProps) {
  const router = useRouter();
  const t = useTranslations("jobads.ui");
  const tEnum = useTranslations("jobads.enums");
  const [, startTransition] = useTransition();
  const helpId = useId();
  const noticeId = useId();

  const hydrated = useSyncExternalStore(
    emptySubscribe,
    () => true,
    () => false,
  );

  const labelIndex = useMemo(() => buildLabelIndex(taxonomy), [taxonomy]);
  const resolveLabel = useMemo(
    () =>
      buildTaxonomyLabelResolver(taxonomy, {
        coded: (conceptId, fallback) => codedTaxonomyName(tEnum, conceptId, fallback),
        unknownCode: (code) => t("toolbar.unknownCode", { code }),
      }),
    [taxonomy, t, tEnum],
  );

  const base = useMemo<JobbUrlState>(
    () => ({
      q,
      occupationGroup: [...occupationGroup],
      region: [...region],
      municipality: [...municipality],
      remote,
      employmentType: [...employmentType],
      worktimeExtent: [...worktimeExtent],
      matchGrades: [...matchGrades],
      employer,
      sortBy,
      pageSize,
    }),
    [
      q,
      occupationGroup,
      region,
      municipality,
      remote,
      employmentType,
      worktimeExtent,
      matchGrades,
      employer,
      sortBy,
      pageSize,
    ],
  );

  // The field's text starts as the canonical mirror of the URL the page loaded with,
  // so a recent search or a direct link shows its search.
  const [text, setText] = useState(() =>
    serializeSearchText(base, resolveLabel, labelIndex),
  );
  const [caret, setCaret] = useState<number | null>(null);
  // Both notices guard the same thing: navigating with a q the backend would reject
  // with 400, which would show the technical-error card while the user types.
  const [notice, setNotice] = useState<Notice>(null);
  const [announcement, setAnnouncement] = useState("");

  // Whether this search is saved in recent searches. It is true after a deliberate
  // commit (Enter, the search button, a suggestion, clearing) and false after a live
  // delta. commit() sets it, and an external change sets it to true, since navigating
  // to a recent search means it is already saved. `savedNotice` is the short inline
  // confirmation after the user clicks save; the next edit clears it.
  const [savedByIntent, setSavedByIntent] = useState(initialCommitted);
  const [savedNotice, setSavedNotice] = useState(false);

  // The text claims last applied: the base for the next delta. It starts from the
  // initial text, so the mirrored search is not committed again as new claims. It is
  // state, not a ref, because the render-time check below reads and writes it.
  const [prevClaims, setPrevClaims] = useState<ParsedClaims>(() =>
    parseSearchText(text, labelIndex, null),
  );

  // The working state: the URL state as this component knows it, including its own
  // commits still in flight. Deltas apply to this, not to a useOptimistic overlay,
  // which reverts to the stale base between transitions and would drop just-committed
  // filters from the next delta's base.
  const [lastCommitted, setLastCommitted] = useState<JobbUrlState>(base);
  // Detects this component's own round trips. It must be a list: with two commits in
  // flight, a single value misclassified the first arriving props as an external
  // change and re-serialised the text mid-typing. A base matching any entry is our
  // own (the list is pruned up to the match and lastCommitted is left alone, since it
  // is ahead of base until the list empties); anything else is external.
  const [recentCommits, setRecentCommits] = useState<JobbUrlState[]>([]);
  const [prevBase, setPrevBase] = useState(base);
  if (base !== prevBase) {
    const hitIndex = recentCommits.findIndex((s) => sameUrlState(base, s));
    const adoptSortPageSize = () => {
      // sameUrlState ignores sort and page size, so they are taken from the base here.
      // Otherwise an external sort change with unchanged filters would leave a stale
      // sortBy in the delta base, and the next text commit would silently revert it.
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
      // Our own commit has arrived: the text stays as it is.
      setRecentCommits(recentCommits.slice(hitIndex + 1));
      adoptSortPageSize();
    } else if (sameUrlState(base, lastCommitted)) {
      // The filters match what was last committed; only a parameter outside the
      // state changed (the commit flag, sort or page size). The text already mirrors
      // this state, so it is not re-synced. This keeps StripCommitParam's removal of
      // `?commit=true` after mount from re-serialising the user's text. The comparison
      // is against lastCommitted, not prevBase: prevBase can be stale, and a real
      // external "clear all" must not be mistaken for a no-op.
      adoptSortPageSize();
    } else {
      // An external change (a chip removed in the toolbar, "clear all", a recent
      // search): sync the text and reset the delta bookkeeping, caret, notice and
      // announcement. Otherwise a stale suggestion query could keep the list open,
      // and an identical later announcement would not be read out.
      const nextText = updateTextForStateChange(
        text,
        prevBase,
        base,
        resolveLabel,
        labelIndex,
      );
      setText(nextText);
      setPrevClaims(parseSearchText(nextText, labelIndex, null));
      setNotice(null);
      setCaret(null);
      setAnnouncement("");
      setLastCommitted(base);
      setRecentCommits([]);
      // State that came from the URL counts as saved: a recent search already is.
      setSavedByIntent(true);
      setSavedNotice(false);
    }
    setPrevBase(base);
  }

  // `markCommit` marks a deliberate commit (Enter, the search button, a suggestion,
  // clearing): the URL gets `?commit=true` so the backend saves the search. A live
  // delta leaves it out. The flag is not part of JobbUrlState, buildJobbHref or
  // sameUrlState; it is added to the navigation URL only, and StripCommitParam removes
  // it after mount.
  function commit(next: JobbUrlState, announce: string, markCommit = false) {
    setLastCommitted(next);
    setRecentCommits((prev) => [...prev, next].slice(-10));
    // The only place, apart from the save click, that sets whether the search is saved.
    setSavedByIntent(markCommit);
    startTransition(() => {
      const href = buildJobbHref(next);
      router.replace(markCommit ? withCommitFlag(href) : href, {
        scroll: false,
      });
    });
    if (announce) setAnnouncement(announce);
  }

  // The one place that decides which notice wins. `limit` outranks `tooShort`: at the
  // limit the user loses typed text, while a short word is merely not used.
  function noticeFor(result: ClaimsDeltaResult): Notice {
    if (result.rejectedQ.length > 0) return { kind: "limit" };
    const tooShort = result.tooShortQ[0];
    if (tooShort !== undefined) return { kind: "tooShort", word: tooShort };
    return null;
  }

  // Notices are announced through the component's single live region. The help line
  // is therefore not a role="status" of its own, which would read the whole help text
  // aloud again every time a notice cleared.
  function noticeText(next: Notice): string | null {
    if (next?.kind === "limit")
      return t("heroSearch.limitNotice", { max: Q_MAX_LENGTH });
    if (next?.kind === "tooShort")
      return t("heroSearch.minNotice", {
        min: Q_MIN_LENGTH,
        word: next.word,
      });
    return null;
  }

  function noticeAnnouncement(next: Notice): string[] {
    const text = noticeText(next);
    return text ? [text] : [];
  }

  // Parse the text, diff it against the previous claims, and apply the difference.
  function runDelta(nextText: string, caretIndex: number | null) {
    const claims = parseSearchText(nextText, labelIndex, caretIndex);
    const result = applyClaimsDelta(lastCommitted, prevClaims, claims, taxonomy);
    setPrevClaims(result.appliedClaims);
    const nextNotice = noticeFor(result);
    setNotice(nextNotice);
    const announce = [
      ...result.addedLabels.map((l) =>
        t("heroSearch.announceAdded", { label: l }),
      ),
      ...result.removedLabels.map((l) =>
        t("heroSearch.announceRemoved", { label: l }),
      ),
      ...noticeAnnouncement(nextNotice),
    ].join(". ");
    if (!sameUrlState(result.next, lastCommitted)) {
      commit(result.next, announce);
    } else if (announce) {
      // Nothing to commit (for example a one-letter word), but the notice must still
      // reach a screen reader.
      setAnnouncement(announce);
    }
  }

  // #1787 — the server-rendered field shows `q` and is the same element once hydrated.
  // React keeps what was typed into it, but the first hydrated render would write `text`
  // over it. So it is adopted verbatim, with the claims the field showed as delta base:
  // Sök applies exactly that edit and leaves the URL's other filters alone.
  function adoptTypedText(typed: string) {
    setText(typed);
    setCaret(null);
    setPrevClaims(parseSearchText(q, labelIndex, null));
  }

  function adoptTextTypedBeforeHydration(input: HTMLInputElement | null) {
    if (!hydrated && input !== null && input.value !== q)
      adoptTypedText(input.value);
  }

  function onFieldChange(nextText: string, caretIndex: number | null) {
    setText(nextText);
    setCaret(caretIndex);
    // Any edit makes a "saved" confirmation stale, from the first keystroke.
    if (savedNotice) setSavedNotice(false);
    // The too-short notice describes the last attempted search, so any edit makes it
    // stale. A word in progress is not a commit point, so without this the notice could
    // stay while the field showed more characters, or while the field was empty and had
    // no clear button to dismiss it. If the text is still too short, the next commit
    // sets the notice again.
    if (notice?.kind === "tooShort") {
      setNotice(null);
      // Clear the announcement too. Otherwise the next too-short attempt produces an
      // identical string, the live region does not change, and a screen reader hears
      // nothing (WCAG 4.1.3). Pressing search twice on the same short word without an
      // edit stays silent on purpose: nothing new has happened.
      setAnnouncement("");
    }
    // A commit point is a separator just before the caret: a word was just finished.
    // Deleting alone is not committed per keystroke; the delta lands at the next
    // commit point or Enter.
    const justTyped = caretIndex !== null ? nextText[caretIndex - 1] : null;
    if (justTyped === " " || justTyped === ",")
      runDelta(nextText, caretIndex);
  }

  // A suggestion picked by click, Tab or arrow keys and Enter. Its label is written
  // into the text only if parsing would find it again (isTextRepresentable for a
  // filter; a title must contain no taxonomy words), or the text would claim a filter
  // the state does not have. The state goes through the delta path, then the pick
  // itself is composed in (so a label that cannot be inserted still reaches the
  // state), and enforceClaims runs last so that composing cannot drop a filter the
  // text claims.
  function onSelectSuggestion(suggestion: SuggestionDto) {
    const range =
      caret !== null
        ? getTokenRange(text, caret)
        : getTokenRange(text, text.length);
    const insertable =
      suggestion.kind === "Title"
        ? parseSearchText(suggestion.label, labelIndex, null).matches
            .length === 0
        : // An employer is never written into the field: if it were, "Volvo" typed as
          // free text would claim an `?employer=` the user never chose. The employer
          // filter is set only through composeSuggestionChip and removed through the
          // toolbar chip. It is checked explicitly rather than relying on employers
          // having no conceptId.
          suggestion.kind !== "Employer" &&
          suggestion.conceptId !== null &&
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
    // composeSuggestionChip can append a title word to q after applyClaimsDelta has
    // applied the minimum-length rule, so a one-letter title suggestion could commit
    // `?q=C` without a notice. The server-side clamp in page.tsx still prevents a 400.
    const selectNotice = noticeFor(delta);
    setNotice(selectNotice);
    // A pick always commits with intent, so the search is saved, even when it does not
    // change the filters (picking an applied suggestion again means "run it again").
    commit(
      withSelection,
      [
        t("heroSearch.announceAdded", { label: suggestion.label }),
        ...noticeAnnouncement(selectNotice),
      ].join(". "),
      true,
    );
  }

  // Search or Enter without a highlighted suggestion: the whole text is final, including
  // the word in progress. It always commits with intent (`?commit=true`), even when the
  // filters are unchanged, because searching again should move the search to the top of
  // the recent list.
  function onSubmitText() {
    const claims = parseSearchText(text, labelIndex, null);
    const result = applyClaimsDelta(lastCommitted, prevClaims, claims, taxonomy);
    setPrevClaims(result.appliedClaims);
    const submitNotice = noticeFor(result);
    setNotice(submitNotice);
    commit(
      result.next,
      [
        ...result.addedLabels.map((l) =>
          t("heroSearch.announceAdded", { label: l }),
        ),
        ...result.removedLabels.map((l) =>
          t("heroSearch.announceRemoved", { label: l }),
        ),
        ...noticeAnnouncement(submitNotice),
      ].join(". "),
      true,
    );
  }

  // The clear button removes the text and the filters the text claimed, but not filters
  // chosen in the popovers: a delta against empty claims removes exactly prevClaims.
  // It commits through commit(), so the returning props are recognised as our own and
  // the text is not re-serialised. It commits with intent.
  function onClear() {
    const delta = applyClaimsDelta(lastCommitted, prevClaims, EMPTY_CLAIMS, taxonomy);
    setText("");
    setCaret(null);
    setPrevClaims(EMPTY_CLAIMS);
    setNotice(null);
    commit(delta.next, t("heroSearch.announceCleared"), true);
  }

  // Suggestions are for the word under the caret, not the whole search text.
  const caretToken =
    caret !== null ? getTokenRange(text, caret) : null;
  const suggestQuery = caretToken
    ? text.slice(caretToken.start, caretToken.end)
    : "";

  const committedQ = lastCommitted.q.trim();

  // The no-JS fallback's axis fields, serialised through the SAME writer the two
  // URL builders use so this producer cannot drift from them onto the repeated
  // form. Empty axes are dropped, so a native GET keeps writing a clean URL.
  const axisInputs: ReadonlyArray<readonly [string, string]> = (
    [
      ["occupationGroup", lastCommitted.occupationGroup],
      ["region", lastCommitted.region],
      ["municipality", lastCommitted.municipality],
      ["employmentType", lastCommitted.employmentType],
      ["worktimeExtent", lastCommitted.worktimeExtent],
      ["matchGrades", lastCommitted.matchGrades],
      ["employer", lastCommitted.employer ?? []],
    ] as const
  )
    .map(([name, values]) => [name, serializeJobbAxis(values)] as const)
    .filter(([, joined]) => joined.length > 0);

  // Mirrors the backend's capture rule in RecentJobSearchCaptureBehavior: a search is
  // savable when it has q, an occupation, a place, remote work, an employment type or
  // working hours. Match grades, sort and view toggles do not count.
  const hasSavableSearch =
    committedQ.length > 0 ||
    lastCommitted.occupationGroup.length > 0 ||
    lastCommitted.region.length > 0 ||
    lastCommitted.municipality.length > 0 ||
    lastCommitted.remote ||
    lastCommitted.employmentType.length > 0 ||
    lastCommitted.worktimeExtent.length > 0;

  // "Spara sökningen" shows when a savable search has not been committed with intent,
  // for example one composed from suggestions without Enter. Clicking it commits the
  // current state again with intent, so the backend saves it, and commit() hides the link.
  function onSaveSearch() {
    // The confirmation is announced through the persistent live region below. A live
    // region mounted together with its content is not reliably announced by every
    // screen reader, so the visible confirmation is aria-hidden.
    commit(lastCommitted, t("heroSearch.saved"), true);
    setSavedNotice(true);
  }
  const showSaveAction = hydrated && hasSavableSearch && !savedByIntent;

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
        {/* Pre-hydration/no-JS the field is named q, so a native GET carries what
            is typed as q (the backend parser is the SPOT and takes a raw string). */}
        <JobAdTypeahead
          id="jobb-q"
          name={hydrated ? undefined : "q"}
          value={hydrated ? text : q}
          inputRef={adoptTextTypedBeforeHydration}
          combobox={hydrated}
          suggestQuery={suggestQuery}
          onChange={hydrated ? onFieldChange : adoptTypedText}
          onSelect={onSelectSuggestion}
          selectOnTab
          wrapperClassName="jp-hero__searchfield"
          inputClassName="jp-hero__input"
          ariaDescribedBy={notice ? `${helpId} ${noticeId}` : helpId}
        />
        {/* Replaces the browser's own clear button (hidden in CSS), which cleared the
            text without committing a delta, so the filters survived. Shown only when
            there is text to clear. */}
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
      {/* The help text carries the instruction; the field has no placeholder. It is not
          a role="status": notices are announced through the component's single live
          region. A notice is added below the help text rather than replacing it, so the
          instruction stays visible and in the field's description when it is most
          needed. Both lines are in aria-describedby (GOV.UK's hint and message). */}
      <p id={helpId} className="jp-hero__searchhelp">
        {t("heroSearch.help")}
      </p>
      {noticeText(notice) !== null && (
        <p
          id={noticeId}
          className="jp-hero__searchhelp jp-hero__searchhelp--notice"
        >
          {noticeText(notice)}
        </p>
      )}

      {/* "Spara sökningen" is a button, not a link, since it does not navigate. After a
          click a short inline confirmation stays until the search changes. */}
      {showSaveAction && (
        <button
          type="button"
          className="jp-hero__searchsaveaction"
          onClick={onSaveSearch}
        >
          {t("heroSearch.save")}
        </button>
      )}
      {savedNotice && (
        // Visual only: the live region below announces it.
        <p className="jp-hero__searchsaved" aria-hidden="true">
          {t("heroSearch.saved")}
        </p>
      )}

      {/* Announces added and removed filters. The visible feedback is in the filter
          row under the results, far from the field. */}
      <p role="status" aria-live="polite" className="sr-only">
        {announcement}
      </p>

      {/* The active filters as hidden inputs. After hydration the visible field has no
          name, so the committed free-text q travels in a hidden input instead. */}
      {hydrated && committedQ.length > 0 && (
        <input type="hidden" name="q" value={committedQ} />
      )}
      {/* ONE hidden input per axis, carrying the values joined — NOT one input
          per value.
          This form is the route's THIRD producer of these axes — after
          `buildJobbHref` and `buildPageHref`, the only two builders on this
          route — and the only one that cannot call a URL builder: a native GET serialises whatever shape
          these fields have. Emitting one input per value would make a no-JS
          submit write the REPEATED form, which is exactly the router-cache
          collision `serializeJobbAxis` exists to remove — and one producer still
          writing it is enough to put the defect back for everyone who submits
          this form (that is how it survived the first pass on `/foretag/sok`,
          code-reviewer #1134). Serialising through the shared writer is what
          keeps this producer from drifting from the two builders. */}
      {axisInputs.map(([name, joined]) => (
        <input key={name} type="hidden" name={name} value={joined} />
      ))}
      {/* Remote work is a flag with one value, not a joined id list, so it sits outside
          axisInputs. Without it a native GET before hydration would drop `?distans=on`. */}
      {lastCommitted.remote && (
        <input type="hidden" name={DISTANS_PARAM} value={DISTANS_ON_VALUE} />
      )}
      {sortBy !== DEFAULT_SORT_BY && (
        <input type="hidden" name="sortBy" value={sortBy} />
      )}
      {pageSize && <input type="hidden" name="pageSize" value={pageSize} />}
      {/* A no-JS submit is always a deliberate search, so it carries commit=true and the
          backend saves it. ASP.NET's bool binding does not accept "1". Once hydrated,
          onSubmit prevents the native submit and the router adds the flag instead. */}
      <input type="hidden" name="commit" value="true" />
    </form>
  );
}
