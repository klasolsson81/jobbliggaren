"use client";

import {
  useEffect,
  useId,
  useMemo,
  useOptimistic,
  useRef,
  useState,
  useSyncExternalStore,
  useTransition,
} from "react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { ChevronDown, ShieldAlert, X } from "lucide-react";
import {
  isPersonnummerShapedOrgNr,
  normalizeOrgNrInput,
} from "@/lib/dto/company-registry";
import {
  orgNrSearchResultSchema,
  type OrgNrSearchResult,
} from "@/lib/dto/company-search";
import {
  buildForetagSokHref,
  serializeCodeAxis,
} from "@/lib/company-search/search-params";
import {
  JobbFilterPopover,
  type PopoverGroup,
} from "@/components/job-ads/jobb-filter-popover";
import { BranschPopover } from "./bransch-popover";
import { CompanyBrowseList } from "./company-browse-list";
import {
  buildSniNodes,
  decomposeSelection,
} from "@/lib/company-criteria/criterion-options";
import { toggleGroup } from "@/lib/company-criteria/criterion-selection";
import type { CriterionReference } from "@/lib/dto/company-criteria";

/**
 * The ONE `/foretag/sok` search island (#997/S2), now on the LIVE-COMMIT model (#1125).
 *
 * TWO interaction models live here on purpose, and the surface draws the difference rather than
 * explaining it. The bransch and ort filters COMMIT IMMEDIATELY: the URL is the single truth and
 * `useOptimistic` is the overlay that makes a chip respond while the RSC navigation is in flight
 * (see `commit`). The NAME field is the one axis that still requires an explicit submit, because it
 * is the one axis whose value must pass the org.nr gate before it may reach a URL at all.
 *
 * The single remaining draft is therefore the FIELD VALUE. That is what `unappliedChanges` is about,
 * and it is why `draftDiffersFromApplied` has only a name axis: the filters cannot diverge from the
 * URL by construction. Anything below that still says "draft" about the filters is stale — the
 * axes were draft state until #1125 and are not any more.
 *
 * #997's original requirement is unchanged and still met: the former two-island layout (name field +
 * filter box) carried SEPARATE drafts with SEPARATE submits, so submitting one silently dropped the
 * other control's unapplied edit. One island cannot drop itself. It replaces both `ForetagSokSearch`
 * (org.nr logic folded in verbatim) and `ForetagSokFilters` (deleted).
 *
 * Submit dispatch (SECURITY-CRITICAL, preserved exactly — the pnr guard runs BEFORE either branch):
 * - a value that normalises to an org.nr → the ORG.NR branch. A personnummer-shaped value renders the
 *   refuse state LOCALLY and is never POSTed anywhere (data minimisation; the backend stays the enforcing
 *   authority). Otherwise it POSTs to `/api/foretag/sok` and renders the 0/1 register hit in client state
 *   — the org.nr term NEVER enters the URL (ADR 0087 D8(c): a sole-prop org.nr can equal a personnummer,
 *   and query strings reach access logs + history). The applied filter axes are irrelevant to an org.nr
 *   lookup and are ignored.
 * - anything else → the NAME + FILTER branch: `router.push(buildForetagSokHref({ namn, sni, kommun }))`
 *   commits the shareable URL, carrying the selected bransch (as its leaf codes) and orter together.
 *
 * The invariant: a pnr-shaped value can NEVER reach `?namn=` and NEVER POST — only a value that does
 * not normalise to an org.nr at all takes the name branch. No-JS degrades to a native GET name
 * search (`namn` + hidden `sni`/`kommun` from the applied URL); the org.nr branch and both filter
 * popovers require JS.
 *
 * HYDRATION SPLIT (2026-07-26, the `/jobb` mirror — `jobb-hero-search.tsx:545-568`, `:641-643`).
 * Once hydrated the visible input is NAMELESS and a hidden input carries the APPLIED name (the
 * `namn` prop, which has already passed the server gate), so a native GET can only ever re-submit a
 * value that was already accepted — never whatever is currently typed. Before hydration the input
 * keeps `name="namn"` so no-JS submits work at all.
 *
 * Read the scope of this honestly: it is defence-in-depth for the case where `onSubmit` fails to
 * run, NOT the fix for the no-JS window. With JS disabled `hydrated` never becomes true, so the
 * input keeps its name forever and a typed ten-digit value still reaches `?namn=`. That window is
 * closed on the SERVER, by `parseNamn`'s org.nr gate + the wash redirect in `page.tsx` — the only
 * layer all three client states (JS off, pre-hydration, hand-typed URL) pass through. CTO bind:
 * `docs/reviews/2026-07-26-foretag-sok-pr4-nojs-pnr-cto.md`.
 *
 * Follow-up round (2026-07-26) — the live-review fixes, per
 * `docs/reviews/2026-07-25-foretag-sok-followup-design.md`:
 * - the search hint moved OUT of the form row (`items-end` was bottom-aligning the button against the
 *   hint, not the input) AND the button is height-paired to the field via `.jp-btn--field`. Measured
 *   after the fix: `{"input":48,"button":48,"aligned":true}`. A Tailwind `h-12` was tried first and
 *   is a silent no-op — see the comment at the form row;
 * - the bransch/ort controls sit in a hairline-separated `role="group"` — the two interaction models
 *   (name SUBMITS, filters narrow) are drawn rather than explained in more prose;
 * - the org.nr answer renders through `CompanyBrowseList` instead of a hand-rolled card, so the two
 *   surfaces cannot drift in columns, masking or Bevaka affordance;
 * - "Rensa sökningen" NAVIGATES and clears the name too — it previously nulled two draft fields and
 *   was hidden behind `hasFilter`, so a pure name search had no clear path at all;
 * Finding 2 (loading indication) is deliberately NOT here, and the reason is measured rather than
 * argued. `page.tsx` wraps the results in `<Suspense key={suspenseKey}>`, and the key changes on
 * every applied search — so the fallback REMOUNTS and suspends immediately, which ends the
 * transition long before any delay could elapse. Probed against the running stack: the skeleton
 * appears at 158 ms already carrying `loadingResults`, while an island
 * pending line sampled every 60 ms across the whole navigation never rendered a single character.
 * A second line here would at best be dead code and at worst a duplicate announcement of the same
 * sentence. The design framing assumed the fallback does not show on a soft navigation; it does.
 */

/** `useSyncExternalStore` with a never-firing subscription: the cheapest "am I hydrated" signal. */
const emptySubscribe = () => () => {};

type OrgNrState =
  | { kind: "idle" }
  | { kind: "pending" }
  | { kind: "refused" }
  | { kind: "rateLimited"; seconds: number }
  | { kind: "error" }
  | { kind: "found"; result: NonNullable<OrgNrSearchResult> }
  | { kind: "notFound" };

/**
 * Above this many bransch chips the row collapses to ONE summary chip, with the axis-wide removal as a
 * labelled button beside it rather than as a × of its own. A broad pick — a whole SNI section is up to
 * 24 divisions — otherwise produces a chip wall that eats the surface it is supposed to describe.
 */
const MAX_BRANSCH_CHIPS = 8;

interface ForetagSokSearchbarProps {
  /** The SCB reference tree — the source of the bransch options + the ort cascade. Empty when degraded. */
  readonly reference: CriterionReference;
  /** Whether the reference loaded. False → the bransch field disables civilly; name search still works. */
  readonly referenceOk: boolean;
  /** The active name prefix parsed from the URL — seeds the field so a shared/bookmarked search shows it. */
  readonly namn: string;
  /** The active (applied) filter axes parsed from the URL — the optimistic overlay's base. */
  readonly sni: ReadonlyArray<string>;
  readonly kommun: ReadonlyArray<string>;
}

/**
 * The two shareable filter axes, committed together. Both live in the URL; this type exists so a
 * commit can only ever push the WHOLE state rather than a delta (see `commit`).
 */
interface FilterSelection {
  readonly sni: ReadonlyArray<string>;
  readonly kommun: ReadonlyArray<string>;
}

export function ForetagSokSearchbar({
  reference,
  referenceOk,
  namn,
  sni,
  kommun,
}: ForetagSokSearchbarProps) {
  const t = useTranslations("pages.foretag.sok");
  // The bransch axis's own strings live one scope up, shared with the popover and the criterion
  // dialog that render the same picker (#999).
  const tc = useTranslations("pages.foretag.criteria");
  const router = useRouter();
  const hydrated = useSyncExternalStore(
    emptySubscribe,
    () => true,
    () => false,
  );

  const searchInputId = useId();
  const searchHintId = useId();
  const unappliedId = useId();
  const branschNoticeId = useId();
  const filterGroupId = useId();
  const orgNrLabelId = useId();

  const abortRef = useRef<AbortController | null>(null);
  const resultRef = useRef<HTMLDivElement>(null);
  const ortBtnRef = useRef<HTMLButtonElement>(null);
  const branschBtnRef = useRef<HTMLButtonElement>(null);
  const chipListRef = useRef<HTMLUListElement>(null);
  const searchInputRef = useRef<HTMLInputElement>(null);

  /**
   * Where focus goes after a live commit UNMOUNTS the control that was clicked.
   *
   * A chip's × removes its own chip, so the focused button stops existing and the browser drops
   * focus to `<body>`: the next Tab restarts from the top of the document, and removing three chips
   * traverses the whole page three times. "Rensa sökningen" does the same to itself — `showClear`
   * goes false the moment the overlay is emptied inside the transition, so the button tears itself
   * out from under the pointer.
   *
   * The unmounting predates live commit. What changed is that the chip × is now the PRIMARY filter
   * gesture on this surface rather than an edit to a draft you were going to submit anyway, and a
   * known weakness in a secondary gesture is not the same thing in a primary one
   * (`jobbpilot-design-a11y` §2; design-reviewer M3).
   *
   * The intent is recorded on the click and applied in the effect below, because at click time the
   * element to move to has not rendered yet. Same shape the house already uses for roving focus in
   * `jobb-klass2-panel.tsx:196-198`.
   */
  const pendingFocusRef = useRef<
    | { kind: "chip"; index: number; axis: React.RefObject<HTMLButtonElement | null> }
    | { kind: "field" }
    | { kind: "trigger"; axis: React.RefObject<HTMLButtonElement | null> }
    | null
  >(null);

  /**
   * Counts focus placements this component has made. `onOrgNrSubmit` captures it before its fetch
   * and only takes focus afterwards if it is unchanged — see the comment there.
   */
  const focusGenerationRef = useRef(0);

  // No dependency array: the effect runs after every render and returns immediately unless an
  // intent is pending. It relies on the FIRST render after the click already carrying the optimistic
  // chip — otherwise it would focus the pre-removal neighbour and consume the intent. That holds
  // because a discrete click runs in React's SyncLane and `useOptimistic` commits in the same pass;
  // it is measured rather than assumed by the "moves to the chip that took the removed one's place"
  // test, which would focus the wrong chip if the ordering were the other way.
  useEffect(() => {
    const pending = pendingFocusRef.current;
    if (pending === null) return;
    pendingFocusRef.current = null;
    focusGenerationRef.current += 1;
    if (pending.kind === "field") {
      searchInputRef.current?.focus();
      return;
    }
    if (pending.kind === "trigger") {
      pending.axis.current?.focus();
      return;
    }
    const buttons =
      chipListRef.current?.querySelectorAll<HTMLElement>("button.jp-chip__remove");
    // The chip that took this index is the one that moved up into the removed chip's place; with
    // nothing after it, the new last chip; with the list gone, the axis's own trigger.
    const target = buttons?.[pending.index] ?? buttons?.[buttons.length - 1];
    if (target) target.focus();
    else pending.axis.current?.focus();
  });

  // Bransch tree + ort groups, derived client-side from the already-loaded reference (no fetch).
  const sniNodes = useMemo(() => buildSniNodes(reference), [reference]);
  const lanGroups = useMemo<PopoverGroup[]>(
    () =>
      reference.lan.map((lan) => ({
        conceptId: lan.code,
        label: lan.name,
        items: lan.kommuner.map((k) => ({ conceptId: k.code, label: k.name })),
      })),
    [reference],
  );
  const kommunNameByCode = useMemo(() => {
    const map = new Map<string, string>();
    for (const lan of reference.lan)
      for (const k of lan.kommuner) map.set(k.code, k.name);
    return map;
  }, [reference]);
  // Which län a kommun belongs to. Needed only to NAME a bulk change: the popover's one bulk
  // control is "Hela {län}", so a change of more than one code is always exactly one län.
  const lanNameByKommunCode = useMemo(() => {
    const map = new Map<string, string>();
    for (const lan of reference.lan)
      for (const k of lan.kommuner) map.set(k.code, lan.name);
    return map;
  }, [reference]);

  // The field value — the ONE axis still submitted explicitly — one island, seeded from
  // the URL. `sniSelected` seeds by construction rather than by lookup: the URL axis IS the leaf set,
  // which is why the old `seedBranch` (find the ONE option whose expansion equals `sni`, else a generic
  // "Vald bransch" chip) has no successor. Multi-select removed the need for the guess, not just the
  // fallback — the chips are derived from the set instead (`branschChips`).
  const [value, setValue] = useState(namn);
  const [ortOpen, setOrtOpen] = useState(false);
  const [branschOpen, setBranschOpen] = useState(false);
  const [state, setState] = useState<OrgNrState>({ kind: "idle" });
  const [isNavPending, startNavTransition] = useTransition();
  /**
   * The live region's text. Ordinary `useState`, not the optimistic overlay — it must survive the
   * transition that carries the navigation, or the sentence would vanish before it is read.
   *
   * KNOWN LIMITATION, declared rather than papered over (design-reviewer round 2, m8): it is never
   * reset, so pressing Back and then repeating the *same* removal produces an identical string, and
   * an identical string is no DOM mutation and therefore no announcement. The obvious repair —
   * blanking it when the applied props change — is WRONG here and was rejected on that ground: a
   * filter commit changes those props itself, roughly 900 ms after the announcement was set, so the
   * blank would land on the very sentence it was meant to preserve. Closing it properly needs a
   * token that distinguishes two identical sentences without being read aloud; not in this PR.
   */
  const [announcement, setAnnouncement] = useState("");

  /**
   * The filter axes are NOT draft state any more (#1122 follow-up, framing §2.3). The URL is the one
   * truth and `useOptimistic` is the overlay that makes a chip respond instantly while the RSC
   * navigation is in flight — the same shape `jobb-hero-filters.tsx` has carried since E2g.
   *
   * This deletes a whole class of bug rather than just the latency: the island is rendered without a
   * `key` and so never remounts, so the old `useState` initialisers ran exactly once. Press Back and
   * the chips kept showing the search you had just left. An overlay derived from props cannot.
   */
  const base = useMemo<FilterSelection>(
    () => ({ sni: [...sni], kommun: [...kommun] }),
    [sni, kommun],
  );
  const [selection, setOptimisticSelection] = useOptimistic(
    base,
    (_current, next: FilterSelection) => next,
  );
  const sniSelected = useMemo<ReadonlySet<string>>(
    () => new Set(selection.sni),
    [selection.sni],
  );
  const orter = selection.kommun;

  /**
   * Re-seed the NAME FIELD, and drop a standing org.nr answer, when the applied NAME changes
   * underneath us — and only then. ONE gate, on one axis, doing both.
   *
   * `value` is the last `useState` initialiser left, so it is still the one piece that runs once at
   * mount while the island never remounts: press Back and the field keeps the name you just left.
   * The filter axes no longer need this at all — they derive from props through the overlay above.
   *
   * **The gate is `namn`, NOT the whole applied signature, and that is load-bearing twice over.**
   * Under draft-commit a filter change and a name change always arrived together, so a
   * signature-wide gate was harmless. Under live commit a chip click changes the signature on its
   * own, and a signature-wide gate would (a) reset the field to the applied name on every chip,
   * wiping a half-typed company name mid-keystroke, and (b) drop the org.nr answer on every chip.
   * Gating on `namn` means a filter commit provably cannot touch either: `commit()` carries the
   * `namn` PROP unchanged, so the gate cannot fire from one.
   *
   * **Why the org.nr answer SURVIVES a filter commit** (design-reviewer bind, 2026-07-29). Under
   * draft-commit the coupling was honest: changing a filter meant pressing the same control that
   * produced the answer, so clearing it was supersession. Live commit severs that — the lookup is
   * independent of the filter axes by design (the org.nr branch ignores them), and the answer is
   * rendered as its own headed, rule-separated section precisely so it never reads as part of the
   * browse (framing §3.2). If it is not part of the browse, the browse cannot make it stale. Task B
   * silently destroying the result of task A is the ADR 0047 failure this now avoids.
   *
   * React's documented "adjust state when props change" pattern — during render, not in an effect,
   * so it neither cascades nor trips the lint rule against synchronous setState in effects.
   *
   * One limit worth knowing rather than discovering: characters typed DURING an in-flight NAME
   * navigation are overwritten when it commits — the window is the navigation itself (~0.9s
   * measured), and the alternative (not re-seeding) is the bug this fixes.
   */
  const [seededName, setSeededName] = useState(namn);
  if (seededName !== namn) {
    setSeededName(namn);
    setValue(namn);
    setState({ kind: "idle" });
  }

  /**
   * ONE commit for every filter change: it sets the optimistic overlay AND navigates inside the SAME
   * transition — `setOptimisticSelection` outside a transition is rejected by React outright.
   *
   * Two rules, both copied from `jobb-hero-filters.tsx` rather than re-derived:
   *
   * 1. **`next` is always built from `selection`** (the optimistic value), never from props. Build it
   *    from props and removal #2 and #3 in a fast sequence each undo what the one before did, because
   *    the props have not landed yet.
   * 2. **The WHOLE state is pushed, never a delta.** Every commit is then idempotent, so a temporary
   *    revert of the overlay between two transitions is self-healing.
   *
   * **V1 (ADR 0087 D8(c), Blocker class): a filter commit carries the `namn` PROP — what is already
   * applied — and NEVER the field's `value`.** Click a bransch chip while ten digits sit unsubmitted
   * in the field and a `value`-carrying commit would put a possible personnummer into `?namn=`, in
   * history, in access logs and in any shared link. That is precisely what the org.nr branch exists
   * to make impossible, and it must not be reachable through a chip. A dirty field stays dirty until
   * the user presses Sök. `foretag-sok-searchbar.test.tsx` pins it.
   *
   * `scroll: false` because a chip narrows the list you are already reading; jumping to the top of
   * the page on every toggle would be its own defect. The explicit name submit keeps default scroll.
   */
  function commit(next: FilterSelection, announced: string) {
    // Outside the transition: the live region should update as soon as the intent is known, not when
    // the navigation settles.
    setAnnouncement(announced);
    startNavTransition(() => {
      setOptimisticSelection(next);
      router.push(
        buildForetagSokHref({
          namn,
          sni: [...next.sni],
          kommun: [...next.kommun],
        }),
        { scroll: false },
      );
    });
  }

  const commitSni = (nextSni: ReadonlyArray<string>, announced: string) =>
    commit({ ...selection, sni: [...nextSni] }, announced);
  const commitKommun = (nextKommun: ReadonlyArray<string>, announced: string) =>
    commit({ ...selection, kommun: [...nextKommun] }, announced);

  /**
   * An announcement must name the OBJECT that changed, never the AXIS it belongs to.
   *
   * Naming the axis makes the second change in a row produce a byte-identical string: React bails
   * out on `Object.is`, the DOM never mutates, and `aria-live` never fires. The user hears the first
   * pick and total silence for every one after it (WCAG 4.1.3). Measured in Chromium before the fix:
   * ticking Upplands Väsby then Vallentuna produced `"Filtret Ort eller län är tillagt."` twice.
   * `jobb-hero-search.tsx` documents the same trap in prose and passes a per-item label for it.
   *
   * The bulk case is named by its län rather than by its kommuner. The property that makes that
   * sound is NOT "there is only one bulk control" — there are two ("Hela {län}" and the right
   * column's "Rensa", `jobb-filter-popover.tsx`) — but that this surface omits `groupAxis`, so
   * EVERY `onChange` the popover emits is scoped to the one active group, i.e. one län. A change
   * spanning several län is therefore unreachable here; the axis label is kept as its fallback so
   * an unreachable state degrades to the old, merely-vague behaviour rather than to an empty
   * announcement. The `first === undefined` branch is unreachable for the same reason — no producer
   * emits an unchanged list — and degrades the same way.
   */
  function ortChangeLabel(next: ReadonlyArray<string>): string {
    const added = next.filter((c) => !orter.includes(c));
    const changed = added.length > 0 ? added : orter.filter((c) => !next.includes(c));
    const first = changed[0];
    if (first === undefined) return t("ortLabel");
    if (changed.length === 1) return kommunNameByCode.get(first) ?? first;
    const lan = lanNameByKommunCode.get(first);
    return lan !== undefined &&
      changed.every((c) => lanNameByKommunCode.get(c) === lan)
      ? lan
      : t("ortLabel");
  }

  // Aborting belongs in an effect, not in the re-seed above: refs may not be touched during render.
  // Keyed on `namn` for the same reason the re-seed gate is: a filter commit must not cancel a
  // lookup the user started and whose answer now deliberately survives it. Without this, a lookup
  // in flight when the applied NAME changes would resolve afterwards and put the row back — hiding
  // it is not the same as cancelling it.
  //
  // A filter commit deliberately does NOT abort. That is not an oversight in the symmetry with
  // `onSubmit`/`onClearSearch`: those two REPLACE the answer (a new lookup) or REMOVE it (a clear),
  // and a filter commit does neither.
  useEffect(() => {
    abortRef.current?.abort();
  }, [namn]);

  // Draft-vs-applied now has exactly ONE axis left: the name. The filters commit live, so they cannot
  // diverge from the URL by construction — the overlay IS the URL plus an in-flight transition. What
  // remains is the honest case the live-commit model creates: type "Volvo", click a bransch, and the
  // list narrows without ever having searched for Volvo. The line says which control applies it. It
  // is meaningless for an org.nr-shaped value (that path ignores the filter axes), so it stays gated
  // on the name branch.
  const isOrgNrValue = normalizeOrgNrInput(value) !== null;
  const draftDiffersFromApplied = !isOrgNrValue && value.trim() !== namn;

  // The FEWEST nodes that describe the selected leaf set: a whole section reads as one chip named after
  // the section, not as its 24 divisions. Derived, never stored — the Set is the only truth.
  const branschChips = useMemo(
    () => decomposeSelection(sniNodes, sniSelected),
    [sniNodes, sniSelected],
  );

  /**
   * When the bransch axis cannot be shown as individual chips. Two causes, one shape:
   *
   * 1. Too many to read. A section pick decomposes to one chip, but a hand-assembled selection can
   *    decompose to dozens, and a chip wall eats the surface it exists to describe.
   * 2. **No decomposition exists at all.** With `referenceOk === false` the page passes
   *    `EMPTY_REFERENCE`, so `sniNodes` is empty and nothing can be named — while `page.tsx` also
   *    passes NO allowlist to `normalizeCodes`, so the whole applied `sni` axis survives into the
   *    island. The filter is applied to the streamed results below. Before this branch existed the
   *    row rendered an empty `<ul>`: an active, invisible, unremovable filter, which is the exact
   *    defect this component's own docblock says it closed ("the results below kept answering a
   *    search the controls no longer showed"). `seedBranch`'s generic chip used to cover it, and
   *    deleting `seedBranch` deleted the cover with it.
   *
   * The two cases count different things and say so: case 1 knows what was picked (nodes), case 2
   * only knows how many codes are applied.
   */
  const branschSummary =
    sniSelected.size > 0 &&
    (branschChips.length === 0 || branschChips.length > MAX_BRANSCH_CHIPS);
  const branschSummaryLabel = branschSummary
    ? branschChips.length === 0
      ? // The degraded case cannot name anything, so it counts the one thing it knows: codes. Its own
        // key because the unit differs — reusing the shared one would say "branches" about codes.
        t("branschSummaryCodes", { count: sniSelected.size })
      : // The SAME key the popover header uses. It is the same sentence about the same axis, and
        // writing it twice is how the panel and the chips drifted into two numbers to begin with.
        tc("sniSelectedCount", { count: branschChips.length })
    : null;
  const branschChipCount = branschSummary ? 1 : branschChips.length;
  // How many `.jp-chip__remove` buttons the bransch axis contributes — NOT the same as the chip
  // count: the summary renders one chip and zero ×, its removal being a labelled sibling button.
  // Focus restoration indexes into the button list, so it must use this and not `branschChipCount`.
  const removableBranschChipCount = branschSummary ? 0 : branschChips.length;

  const hasFilter = sniSelected.size > 0 || orter.length > 0;
  const hasOrgNrResult = state.kind !== "idle";
  // The clear control's gate is the WHOLE search, not just the filter axes: gated on `hasFilter` alone
  // (as it was), a pure name search — the most common one — had no clear path at all (finding 6).
  // Gated on what is APPLIED (plus a standing org.nr answer), never on the draft `value`. Reading
  // the draft made the control appear on the first keystroke and shove the whole result list 64px
  // down mid-typing — measured. There is also nothing to clear yet at that point: an unsubmitted
  // field is cleared by deleting the text, which is what the user is already doing.
  const showClear = hasFilter || hasOrgNrResult || namn.length > 0;


  async function onOrgNrSubmit(orgNr: string) {
    // Refuse a personnummer-shaped value LOCALLY, before any transmission (it never leaves the browser —
    // not even to our own BFF).
    if (isPersonnummerShapedOrgNr(orgNr)) {
      setState({ kind: "refused" });
      resultRef.current?.focus();
      return;
    }

    abortRef.current?.abort();
    const controller = new AbortController();
    abortRef.current = controller;
    setState({ kind: "pending" });

    /**
     * Where focus stood when this lookup started. The answer deliberately SURVIVES a filter commit
     * (see the re-seed gate), so a chip click during the fetch neither cancels this request nor
     * discards its result — but it DOES move focus, and this request must not take it back 300-900 ms
     * later on a gesture the user has moved on from. WCAG 3.2.1: focus changes follow user action.
     *
     * The two decisions meet exactly here: "the lookup survives a filter commit" and "a commit places
     * focus" are both right, and neither is right if this line runs unconditionally.
     */
    const focusGenerationAtSubmit = focusGenerationRef.current;

    try {
      const res = await fetch("/api/foretag/sok", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ organizationNumber: orgNr }),
        signal: controller.signal,
      });

      if (res.status === 429) {
        const retryAfter = Number.parseInt(res.headers.get("Retry-After") ?? "60", 10);
        setState({
          kind: "rateLimited",
          seconds: Number.isFinite(retryAfter) ? retryAfter : 60,
        });
      } else if (res.ok) {
        const parsed = orgNrSearchResultSchema.safeParse(await res.json());
        if (!parsed.success) setState({ kind: "error" });
        else if (parsed.data === null) setState({ kind: "notFound" });
        else setState({ kind: "found", result: parsed.data });
      } else {
        setState({ kind: "error" });
      }
    } catch {
      if (controller.signal.aborted) return; // superseded by a newer submit
      setState({ kind: "error" });
    }
    // The ANSWER still renders — only the focus move is withheld, and only when something else has
    // placed focus since. A screen reader still hears it: the section is a polite live region.
    if (focusGenerationRef.current === focusGenerationAtSubmit) {
      resultRef.current?.focus();
    }
  }

  function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (state.kind === "pending") return;

    const orgNr = normalizeOrgNrInput(value);
    if (orgNr !== null) {
      // org.nr branch — client POST (pnr refused inside), never the URL. The filter axes are
      // irrelevant to an org.nr lookup and deliberately ignored.
      void onOrgNrSubmit(orgNr);
      return;
    }

    // NAME branch — the only path that may put the field's value into the URL, and the only one that
    // has passed the org.nr gate above to get here. The filter axes ride along from `selection`
    // rather than from props, so a name submit while a chip navigation is still in flight commits
    // the filters the user can SEE rather than the ones the server last confirmed.
    abortRef.current?.abort();
    setState({ kind: "idle" });
    startNavTransition(() => {
      router.push(
        buildForetagSokHref({
          namn: value.trim(),
          sni: [...selection.sni],
          kommun: [...selection.kommun],
        }),
      );
    });
  }

  /**
   * ONE clear control for the WHOLE search (finding 6). It must NAVIGATE, not merely null the draft:
   * the old version left the applied URL filter in place, so the results below kept answering a search
   * the controls no longer showed. It also drops a standing org.nr answer and aborts an in-flight
   * lookup — otherwise a stale org.nr row survives over a cleared page.
   */
  function onClearSearch() {
    abortRef.current?.abort();
    setValue("");
    setState({ kind: "idle" });
    setAnnouncement(t("announceFiltersCleared"));
    // The control removes itself (`showClear` goes false the moment the overlay is emptied), so
    // focus must be placed rather than left to the browser. The name field is where starting over
    // begins, and landing there reads together with `announceFiltersCleared`.
    pendingFocusRef.current = { kind: "field" };
    startNavTransition(() => {
      // The overlay is cleared in the same transition as the navigation, exactly like `commit` — the
      // filters are no longer local state that could be nulled separately from the URL.
      setOptimisticSelection({ sni: [], kommun: [] });
      router.push(buildForetagSokHref({ namn: "", sni: [], kommun: [] }));
    });
  }

  return (
    <div className="mt-2 flex flex-col gap-5">
      {/* The CONTROLS are capped at a comfortable form measure; the ANSWER is not. `max-w-2xl` used to
          sit on the island root, so the org.nr answer — which renders through the same
          `CompanyBrowseList` as the streamed results — came out at 672px while the identical table
          below it filled the 1136px rail. Same component, same five columns, two widths on one page
          (#1090). The cap belongs to the form, so it moved onto the form's own wrapper.

          The `<form>` and its hidden `namn`/`sni`/`kommun` inputs stay together inside this wrapper:
          the no-JS native GET depends on them being in the same form, and moving a wrapper must not
          separate them. */}
      <div className="flex max-w-2xl flex-col gap-5">
        {/* Row 1 — the unified name/org.nr field + the ONE submit. No-JS fallback: a native GET to
            /foretag/sok as a NAME search (?namn=…), with the APPLIED sni/kommun preserved via hidden
            inputs. With JS, onSubmit intercepts and reads the field value plus the live filter overlay. */}
        <form action="/foretag/sok" method="get" onSubmit={onSubmit}>
          <div className="jp-field">
            <label htmlFor={searchInputId} className="jp-label">
              {t("searchLabel")}
            </label>
            {/* The hint lives BELOW the row, not inside the field column: `.jp-field` is a column of
                label → input → hint, so an `items-end` row bottom-aligned the button against the HINT's
                baseline rather than the input's; and `.jp-btn--field` pairs the button to the field's
                48px so the row reads as one control. `aria-describedby` is position-independent and
                follows the move.
                A Tailwind `h-12` was tried first and is a SILENT no-op — `.jp-*` is deliberately
                unlayered (globals.css:615-624) and unlayered CSS beats `@layer utilities`, so
                `.jp-btn { height: 44px }` wins with no error and no warning. Measured live:
                `{"input":48,"button":44}` with `h-12` on the element. Hence a modifier, not a utility;
                DESIGN.md now carries that rule so the next attempt does not repeat it. */}
            <div className="flex gap-2">
              <input
                ref={searchInputRef}
                id={searchInputId}
                // NAMELESS once hydrated: a native GET must never carry whatever is currently typed —
                // the hidden input below carries the already-applied name instead. Before hydration it
                // keeps the name so a no-JS submit still works. Do NOT hardcode `name="namn"` here:
                // that is the exact leak #1078 closed, and `foretag-sok-searchbar.test.tsx`'s
                // call-site pin goes red on it.
                name={hydrated ? undefined : "namn"}
                className="jp-input grow"
                type="text"
                autoComplete="off"
                // Both descriptions when the draft diverges, so a screen reader hears WHY the
                // field is not applied yet as part of the field itself.
                aria-describedby={
                  draftDiffersFromApplied
                    ? `${searchHintId} ${unappliedId}`
                    : searchHintId
                }
                value={value}
                onChange={(e) => setValue(e.target.value)}
              />
              <button
                type="submit"
                className="jp-btn jp-btn--primary jp-btn--field shrink-0"
                // Deliberately BROADER than the submit's own work: `isNavPending` is true during a
                // chip commit too, so this reports busy for a navigation the button did not start.
                // Kept that way because the button is the surface's one submit affordance and the
                // whole island is genuinely mid-navigation — a user who presses it during a chip
                // commit would otherwise get a second navigation queued behind the first. Narrow it
                // to `state.kind === "pending"` only together with a guard on that.
                aria-busy={state.kind === "pending" || isNavPending || undefined}
              >
                {t("searchSubmit")}
              </button>
            </div>
            <span id={searchHintId} className="jp-hint">
              {t("searchHint")}
            </span>
            {/* Draft-vs-applied honesty, INSIDE the field it is about (design-reviewer round 2).
                It used to render four `gap-5` blocks further down — after the filter group, the
                chips and the clear control — while naming the name field, so the sentence and its
                subject were nowhere near each other.

                It carries NO `aria-live`, deliberately, and design-reviewer withdrew the round-1
                finding that asked for one. It was a live region mounted together with its content —
                the exact trap the persistent region below exists to avoid, so it never announced
                reliably anyway. And the sentence is not an EVENT: it is a standing state, true for
                as long as the field diverges, so firing it on the keystroke that makes it true
                would announce on the first character of every search. `aria-describedby` is the
                right mechanism for a standing description, and it puts the sentence in the field's
                own accessible description instead of leaving it to be found by sighted scanning. */}
            {/* ALWAYS rendered, with its height reserved — conditional rendering here shifted the
                chip row, the clear control and the whole streamed table down 26 px on the first
                character typed, and back up when the field matched the applied name again.
                Measured, not feared: 668→694 for the chip row at 1280 px. That is the same defect
                class `showClear` is gated on applied state to avoid ("shove the whole result list
                64 px down mid-typing — measured"), and this element sits ABOVE everything, so it
                moved more than that one did.

                Reserving the element alone is not enough: `.jp-field`'s 6 px gap accounts for only
                6 of the 26, the other 20 being the caption line itself — hence `min-h-5`, one
                caption line, rather than an empty span. The cost is honest and permanent: the field
                block is 26 px taller at all times. That is the price of a surface that does not
                move under the pointer while you type.

                `aria-describedby` stays CONDITIONAL, so the field's description gains a sentence
                only while one is true — an empty description would otherwise be announced as part
                of the field forever. */}
            <span id={unappliedId} className="jp-hint min-h-5">
              {draftDiffersFromApplied ? t("unappliedChanges") : ""}
            </span>
          </div>

          {/* Post-hydration: the APPLIED name rides a hidden input, so a native GET (an onSubmit that
              failed to run) re-submits what the server already accepted — never the current draft.
              The prop cannot be org.nr-shaped: `parseNamn` refuses that class before this renders. */}
          {hydrated && namn.length > 0 && (
            <input type="hidden" name="namn" value={namn} />
          )}

          {/* No-JS: preserve the APPLIED code axes so a native name submit does not erase the filter
              (ignored when JS handles onSubmit — then the overlay over the URL is the source of truth).

              ONE input per axis, joined through the SAME serializer the href builders use. A form
              serialises its own fields, so this is the one producer of these params that cannot go
              through a URL builder — and emitting one input per code would keep writing the
              REPEATED shape, putting the router-cache collision back on every native GET
              (`search-params.ts` documents the mechanism). It also drops a broad bransch pick from
              hundreds of hidden inputs to one. */}
          {sni.length > 0 && (
            <input type="hidden" name="sni" value={serializeCodeAxis(sni)} />
          )}
          {kommun.length > 0 && (
            <input type="hidden" name="kommun" value={serializeCodeAxis(kommun)} />
          )}
        </form>

        {/* Row 2 — bransch (SNI tree popover) + ort (cascade popover), both multi-select, side by side,
            behind a hairline and a caption. The two interaction models on this surface differ — the name
            is SUBMITTED, these narrow an ongoing browse — and after the live review the fix was to DRAW
            that difference (a group with its own caption) rather than explain it in more hint prose,
            which was the opposite finding (9). `role="group"` + `aria-labelledby` against the visible
            caption; deliberately NOT a third <h2>, which would be heading noise for two controls.
            Deliberately OUTSIDE the <form>: these are JS-only live-commit controls with no submitted name, and
            keeping them out of the form means a keystroke in them can never trigger a native GET. */}
        <div
          role="group"
          aria-labelledby={filterGroupId}
          className="border-t border-border pt-5"
        >
          {/* Deliberately NOT `.jp-label`: the two field labels below use it, so a third identical
              label stacked above them draws no grouping at all — it just reads as a label with no
              control (design-review M4). This is the section-caption treatment the system already
              uses for `.jp-popover__title`, composed from the same tokens rather than adding CSS. */}
          <span
            id={filterGroupId}
            className="text-caption font-semibold tracking-[0.06em] text-text-secondary uppercase"
          >
            {t("filterGroupLabel")}
          </span>
          <div className="mt-3 grid gap-4 md:grid-cols-2">
            <div className="jp-field">
              {/* Same label-in-name treatment as the ort trigger below, and for the same reason: a
                  <label htmlFor> pointed at a BUTTON becomes that button's accessible name and
                  overrides its visible text (WCAG 2.5.3). The heading is a plain span; the button names
                  itself. The former `branschHint` ("Skriv och välj en bransch.") is GONE — it described
                  a typeahead that no longer exists, and "Välj bransch" says the rest (finding 9). */}
              <span className="jp-label">{t("branschLabel")}</span>
              <button
                ref={branschBtnRef}
                type="button"
                className="jp-input flex cursor-pointer items-center justify-between gap-2 text-left"
                aria-haspopup="dialog"
                aria-expanded={branschOpen}
                aria-describedby={referenceOk ? undefined : branschNoticeId}
                disabled={!referenceOk}
                onClick={() => setBranschOpen((o) => !o)}
              >
                {t("branschTrigger")}
                <ChevronDown size={16} aria-hidden="true" />
              </button>
              {!referenceOk && (
                <p
                  id={branschNoticeId}
                  role="status"
                  className="text-body-sm text-text-primary"
                >
                  {t("branschUnavailable")}
                </p>
              )}
            </div>

            <div className="jp-field">
              {/* The trigger is a <button>; buttons are labelable, so a <label htmlFor> would make the
                  label text the button's ACCESSIBLE NAME and override its visible text ("Välj ort eller län")
                  — a WCAG 2.5.3 label-in-name break. The field heading is therefore a plain span; the button
                  is self-named by its own text. The former `ortHint` ("Välj ett eller flera län eller
                  kommuner") is GONE: the trigger already says it, so it was prose paying no rent (9). */}
              <span className="jp-label">{t("ortLabel")}</span>
              <button
                ref={ortBtnRef}
                type="button"
                className="jp-input flex cursor-pointer items-center justify-between gap-2 text-left"
                aria-haspopup="dialog"
                aria-expanded={ortOpen}
                onClick={() => setOrtOpen((o) => !o)}
              >
                {t("ortTrigger")}
                <ChevronDown size={16} aria-hidden="true" />
              </button>
            </div>
          </div>
        </div>

        {/* The bransch picker (#999). Same `toggleGroup` semantics as the criterion dialog: a node's
            leaf codes go in as a group, so picking a section selects its whole expansion and the state
            stays a flat leaf Set at every level. No `key`-remount: unlike the ort cascade there is no
            active-column to reset, and remounting would throw away the filter text mid-use. */}
        <BranschPopover
          open={branschOpen}
          onClose={() => setBranschOpen(false)}
          triggerRef={branschBtnRef}
          nodes={sniNodes}
          selected={sniSelected}
          // `toggleGroup` runs against `sniSelected`, which is derived from the OVERLAY — never from
          // props. Two quick picks in the popover are then cumulative; against props the second would
          // undo the first, because the first has not landed yet.
          onToggle={(codes) => {
            const next = toggleGroup(sniSelected, codes);
            const added = next.size > sniSelected.size;
            // Name the NODE that was toggled, not the axis — see `ortChangeLabel` for why the axis
            // label silences every change after the first. `decomposeSelection` is the same
            // function the chips are derived from, so the announcement and the chip that appears
            // cannot disagree; applied to one node's own leaf codes it returns that one node.
            const [node] = decomposeSelection(sniNodes, new Set(codes));
            commitSni(
              [...next],
              t(added ? "announceFilterAdded" : "announceFilterRemoved", {
                namn: node?.name ?? t("branschLabel"),
              }),
            );
          }}
          onClear={() => commitSni([], t("announceBranschCleared"))}
        />

        {/* The ort cascade. Degenerate single-axis case (our URL contract has only a `kommun` axis, no
            `lan`): groupAxis is OMITTED, so "Hela {län}" materialises the län's kommun codes into
            `selected`. `counts={null}` — no facet counts (Klas locked FOCUSED). key-remount on open resets
            the popover's active-left column. */}
        <JobbFilterPopover
          key={ortOpen ? "ort-open" : "ort-closed"}
          open={ortOpen}
          groups={lanGroups}
          selected={orter}
          onChange={(next) =>
            commitKommun(
              next,
              t(next.length > orter.length ? "announceFilterAdded" : "announceFilterRemoved", {
                namn: ortChangeLabel(next),
              }),
            )
          }
          onClose={() => setOrtOpen(false)}
          onClearAll={() => commitKommun([], t("announceOrterCleared"))}
          triggerRef={ortBtnRef}
          leftTitle={t("ortLeftTitle")}
          dialogLabel={t("ortDialogLabel")}
          rightTitle={t("ortRightTitle")}
          selectAllLabel={(g) => t("ortSelectAll", { lan: g.label })}
          emptyText={t("ortEmpty")}
          rightEmptyText={t("ortRightEmpty")}
          counts={null}
        />

        {/* Row 3 — the APPLIED chips (bransch + orter) + the ONE clear control. A chip's × COMMITS the
            filter immediately; there is no draft left on these axes. The clear control is NOT gated on
            there being chips — see `showClear`. */}
        {showClear && (
          <div className="flex flex-wrap items-center gap-3">
            {/* Never an empty list: a `<ul>` with no `<li>` is announced as "list, 0 items". */}
            {branschChipCount + orter.length > 0 && (
              // `items-center`: `.jp-chiplist` declares no `align-items`, so a 44px button inside one
              // `<li>` would stretch the row and leave the ort chips hanging at the top edge.
              <ul ref={chipListRef} className="jp-chiplist items-center">
                {branschSummary ? (
                  <li>
                    {/* The summary REPORTS; it does not delete. A `jp-chip__remove` × here would be
                        pixel-identical to the ones the user just learned remove ONE thing, while
                        dropping the whole bransch axis — possibly 800 codes, with no undo. The removal is a
                        sibling with VISIBLE text, and it is a full `.jp-btn` (44px) rather than a
                        `.jp-clearlink`: that class is 13px caption text, smaller than the chips' own ×
                        at either of its two sizes (24px, and 32px at and below 768px). It stays inside this
                        `<li>` so it sits beside the chip it belongs to, not after the ort chips. */}
                    <span className="flex items-center gap-2">
                      <span className="jp-chip">
                        <span className="jp-chip__label">{branschSummaryLabel}</span>
                      </span>
                      <button
                        type="button"
                        className="jp-btn jp-btn--ghost"
                        onClick={() => {
                          // Clears the whole axis, so there is no neighbouring chip to move to.
                          pendingFocusRef.current = {
                            kind: "trigger",
                            axis: branschBtnRef,
                          };
                          commitSni([], t("announceBranschCleared"));
                        }}
                      >
                        {t("branschRemoveAll")}
                      </button>
                    </span>
                  </li>
                ) : (
                  branschChips.map((chip, index) => (
                    <li key={chip.key}>
                      <span className="jp-chip jp-chip--removable">
                        <span className="jp-chip__label" title={chip.name}>
                          {chip.name}
                        </span>
                        <button
                          type="button"
                          className="jp-chip__remove"
                          // Names the branch, not the axis: "Ta bort bransch" × 5 is unusable in a
                          // screen reader, and `ortRemove` beside it already carries the name.
                          aria-label={t("branschRemove", { namn: chip.name })}
                          // Built from `sniSelected` — the OVERLAY — so three fast removals are
                          // cumulative. Against props, removals two and three would each undo the
                          // one before, because the props have not landed yet (framing §2.3).
                          onClick={() => {
                            pendingFocusRef.current = {
                              kind: "chip",
                              index,
                              axis: branschBtnRef,
                            };
                            const next = new Set(sniSelected);
                            for (const code of chip.leafCodes) next.delete(code);
                            commitSni(
                              [...next],
                              t("announceFilterRemoved", { namn: chip.name }),
                            );
                          }}
                        >
                          <X size={14} aria-hidden="true" />
                        </button>
                      </span>
                    </li>
                  ))
                )}
                {orter.map((code, ortIndex) => {
                  const name = kommunNameByCode.get(code) ?? code;
                  return (
                    <li key={code}>
                      <span className="jp-chip jp-chip--removable">
                        <span className="jp-chip__label" title={name}>
                          {name}
                        </span>
                        <button
                          type="button"
                          className="jp-chip__remove"
                          aria-label={t("ortRemove", { namn: name })}
                          // From the overlay, not from props — see the bransch chip above.
                          onClick={() => {
                            // The × buttons form ONE list across both axes, and the summary chip
                            // contributes none — so the offset is the number of REMOVABLE bransch
                            // chips, which is zero while the axis is summarised.
                            pendingFocusRef.current = {
                              kind: "chip",
                              index: removableBranschChipCount + ortIndex,
                              axis: ortBtnRef,
                            };
                            commitKommun(
                              orter.filter((c) => c !== code),
                              t("announceFilterRemoved", { namn: name }),
                            );
                          }}
                        >
                          <X size={14} aria-hidden="true" />
                        </button>
                      </span>
                    </li>
                  );
                })}
              </ul>
            )}
            <button
              type="button"
              // `--flush` cancels `.jp-btn`'s inline padding with a negative start margin, so the TEXT
              // lands on the rail with the field labels instead of 18px inside it (#1090). It is
              // CSS-scoped to `:first-child`, so it is inert on the rows where a chip list precedes
              // it — there, cancelling the padding would close the gap rather than open the rail.
              className="jp-btn jp-btn--ghost jp-btn--flush"
              onClick={onClearSearch}
            >
              {t("clearButton")}
            </button>
          </div>
        )}

        {/* A filter now applies without moving focus and without any visible control changing state
            beyond the chip itself, so the change has to be announced (WCAG 4.1.3). This mirrors
            `/jobb`: announce the FILTER CHANGE, never the result count — the count arrives with the
            streamed results, and a screen reader would hear a number for a list it has not reached.

            ONE persistent region, always in the DOM and always empty at first paint. A live region
            mounted with its content already in place is not reliably announced (the same trap is
            documented in `jobb-hero-search.tsx`), which is why this is not rendered conditionally. */}
        {/* `role="status"` alongside `aria-live` matches the precedent this mirrors
            (`jobb-hero-search.tsx:634`) and brings `aria-atomic="true"` with it, so a partial update
            is read as one sentence rather than in fragments. */}
        <p role="status" aria-live="polite" className="sr-only">
          {announcement}
        </p>

      </div>

      {/* The org.nr answer (transient client state): programmatic focus after a submit + a polite live
          region so the found row is announced. Kept visually SEPARATED from the streamed filter results
          below (a top rule + its own labelled section) so the two never read as one fused result set. A
          nested role=alert on error/rateLimited overrides the politeness. */}
      <section
        ref={resultRef}
        tabIndex={-1}
        aria-live="polite"
        aria-labelledby={hasOrgNrResult ? orgNrLabelId : undefined}
        className={
          hasOrgNrResult ? "mt-1 border-t border-border pt-6" : undefined
        }
      >
        {hasOrgNrResult && (
          <h2
            id={orgNrLabelId}
            // One step BELOW the streamed results' own <h2> (20px), deliberately: this heads the
            // answer to a single lookup, that heads the browse. What it can no longer be is 14px —
            // it used to head a 672px block, where body-sm was proportionate, and at 1136px it was
            // heading a full-rail section in smaller type than the 16px body text below it.
            className="mb-3 text-h3 font-semibold text-text-primary"
          >
            {t("orgNrResultLabel")}
          </h2>
        )}

        {/* The org.nr hit renders through the SAME table as the streamed results (finding 5). The old
            hand-rolled card re-implemented the identical knowledge — name, mono org.nr, protected-identity
            badge, seat kommun, Bevaka — as a second rendering that could drift from the first, and it
            carried the SCB kommun code the live review flagged (7). `followStateByOrgNr` is always a Map
            (possibly empty) so the Bevaka column renders here too; a masked row is correctly
            non-followable, exactly as in the list. */}
        {state.kind === "found" && (
          <CompanyBrowseList
            items={[state.result.company]}
            reference={reference}
            followStateByOrgNr={
              new Map(
                state.result.company.organizationNumber
                  ? [[state.result.company.organizationNumber, state.result.companyWatchId]]
                  : [],
              )
            }
            labels={{
              tableAria: t("orgNrTableAria"),
              tableCaption: t("orgNrTableCaption"),
            }}
          />
        )}

        {state.kind === "notFound" && (
          <div role="status" className="jp-empty">
            <div className="jp-empty__title">{t("orgNrNotFoundTitle")}</div>
            <p className="jp-empty__body text-body-sm text-text-primary">{t("orgNrNotFoundBody")}</p>
          </div>
        )}

        {state.kind === "refused" && (
          <div
            role="status"
            className="rounded-md border border-warning-700/30 bg-warning-50 px-6 py-4 text-warning-700"
          >
            <p className="text-body font-medium">
              <ShieldAlert size={14} aria-hidden="true" className="mr-1 inline" />
              {t("orgNrRefusedTitle")}
            </p>
            <p className="mt-1 text-body-sm">{t("orgNrRefusedBody")}</p>
          </div>
        )}

        {state.kind === "rateLimited" && (
          <div
            role="alert"
            className="rounded-md border border-warning-700/30 bg-warning-50 px-6 py-4 text-warning-700"
          >
            <p className="text-body font-medium">{t("orgNrRateLimitedTitle")}</p>
            <p className="mt-1 text-body-sm">
              {t("orgNrRateLimitedBody", { seconds: state.seconds })}
            </p>
          </div>
        )}

        {state.kind === "error" && (
          <div
            role="alert"
            className="rounded-md border border-danger-600/30 bg-danger-50 px-6 py-4 text-danger-700"
          >
            <p className="text-body font-medium">{t("orgNrErrorTitle")}</p>
            <p className="mt-1 text-body-sm">{t("orgNrErrorBody")}</p>
          </div>
        )}
      </section>

    </div>
  );
}
