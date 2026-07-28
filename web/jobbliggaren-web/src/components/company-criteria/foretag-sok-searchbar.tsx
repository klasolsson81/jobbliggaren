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
import { buildForetagSokHref } from "@/lib/company-search/search-params";
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
 * #997 (S2) — the ONE `/foretag/sok` search island. It holds the WHOLE draft in one client component —
 * the unified field value + the single selected bransch + the selected orter — and ONE submit commits
 * them together. This is the #1 hard requirement (design-reviewer Blocker): the former two-island layout
 * (name field + filter box) carried SEPARATE drafts with SEPARATE submits, so submitting one silently
 * dropped the other control's unapplied edit. Here a single draft cannot drop itself. It replaces both
 * `ForetagSokSearch` (org.nr logic folded in verbatim) and `ForetagSokFilters` (deleted).
 *
 * Submit dispatch (SECURITY-CRITICAL, preserved exactly — the pnr guard runs BEFORE either branch):
 * - a value that normalises to 10 digits → the ORG.NR branch. A personnummer-shaped value renders the
 *   refuse state LOCALLY and is never POSTed anywhere (data minimisation; the backend stays the enforcing
 *   authority). Otherwise it POSTs to `/api/foretag/sok` and renders the 0/1 register hit in client state
 *   — the org.nr term NEVER enters the URL (ADR 0087 D8(c): a sole-prop org.nr can equal a personnummer,
 *   and query strings reach access logs + history). The bransch/ort drafts are irrelevant to an org.nr
 *   lookup and are ignored.
 * - anything else → the NAME + FILTER branch: `router.push(buildForetagSokHref({ namn, sni, kommun }))`
 *   commits the shareable URL, carrying the draft bransch (as its leaf codes) and orter together.
 *
 * The invariant: a pnr-shaped 10-digit value can NEVER reach `?namn=` and NEVER POST — only a NON-10-digit
 * value takes the name branch. No-JS degrades to a native GET name search (`namn` + hidden `sni`/`kommun`
 * from the applied URL); the org.nr branch and both filter popovers require JS.
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
 * appears at 158 ms already carrying `loadingResults` in its own `role="status"`, while an island
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
  /** The active (applied) filter axes parsed from the URL — the draft seeds from these. */
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
  const branschNoticeId = useId();
  const filterGroupId = useId();
  const orgNrLabelId = useId();

  const abortRef = useRef<AbortController | null>(null);
  const resultRef = useRef<HTMLDivElement>(null);
  const ortBtnRef = useRef<HTMLButtonElement>(null);
  const branschBtnRef = useRef<HTMLButtonElement>(null);

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

  // The whole draft: the field value, the bransch leaf codes, and the orter — one island, seeded from
  // the URL. `sniSelected` seeds by construction rather than by lookup: the URL axis IS the leaf set,
  // which is why the old `seedBranch` (find the ONE option whose expansion equals `sni`, else a generic
  // "Vald bransch" chip) has no successor. Multi-select removed the need for the guess, not just the
  // fallback — the chips are derived from the set instead (`branschChips`).
  const [value, setValue] = useState(namn);
  const [ortOpen, setOrtOpen] = useState(false);
  const [branschOpen, setBranschOpen] = useState(false);
  const [state, setState] = useState<OrgNrState>({ kind: "idle" });
  const [isNavPending, startNavTransition] = useTransition();
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
   * Re-seed the NAME FIELD when the applied name changes underneath us — and only then.
   *
   * `value` is the last `useState` initialiser left, so it is still the one piece that runs once at
   * mount while the island never remounts: press Back and the field keeps the name you just left.
   * The filter axes no longer need this at all — they derive from props through the overlay above.
   *
   * **The gate is `namn`, NOT the whole applied signature, and that is load-bearing now.** Under
   * draft-commit a filter change and a name change always arrived together, so a signature-wide gate
   * was harmless. Under live commit a chip click changes the signature on its own — a signature-wide
   * gate would reset the field to the applied name on every chip, wiping a half-typed company name
   * mid-keystroke. Gating on `namn` means a filter commit provably cannot touch `value`: `commit()`
   * carries the `namn` PROP unchanged, so the gate cannot fire from one.
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
  }

  /**
   * A standing org.nr answer is dropped whenever the APPLIED search moves on — including on a filter
   * commit, which under live commit happens on every chip. The answer is client-only state that no
   * navigation would otherwise clear, and leaving it stranded above a list that has moved on is the
   * defect `onClearSearch` already guards against. The lookup itself stays independent of the filter
   * axes (an org.nr search ignores them, by design); this is only about not leaving a stale row.
   */
  const appliedSignature = `${namn}|${[...sni].sort().join(",")}|${[...kommun].sort().join(",")}`;
  const [seededFrom, setSeededFrom] = useState(appliedSignature);
  if (seededFrom !== appliedSignature) {
    setSeededFrom(appliedSignature);
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

  // Aborting belongs in an effect, not in the re-seed above: refs may not be touched during render.
  // Without it, a lookup already in flight when the URL changes would resolve afterwards and put the
  // row back — hiding it is not the same as cancelling it.
  useEffect(() => {
    abortRef.current?.abort();
  }, [seededFrom]);

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
    resultRef.current?.focus();
  }

  function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (state.kind === "pending") return;

    const orgNr = normalizeOrgNrInput(value);
    if (orgNr !== null) {
      // org.nr branch (10 digits) — client POST (pnr refused inside), never the URL. The draft filter is
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
            inputs. With JS, onSubmit intercepts and reads the whole draft from state. */}
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
                aria-describedby={searchHintId}
                value={value}
                onChange={(e) => setValue(e.target.value)}
              />
              <button
                type="submit"
                className="jp-btn jp-btn--primary jp-btn--field shrink-0"
                aria-busy={state.kind === "pending" || isNavPending || undefined}
              >
                {t("searchSubmit")}
              </button>
            </div>
            <span id={searchHintId} className="jp-hint">
              {t("searchHint")}
            </span>
          </div>

          {/* Post-hydration: the APPLIED name rides a hidden input, so a native GET (an onSubmit that
              failed to run) re-submits what the server already accepted — never the current draft.
              The prop cannot be org.nr-shaped: `parseNamn` refuses that class before this renders. */}
          {hydrated && namn.length > 0 && (
            <input type="hidden" name="namn" value={namn} />
          )}

          {/* No-JS: preserve the APPLIED code axes so a native name submit does not erase the filter
              (ignored when JS handles onSubmit — then the draft is the source of truth). */}
          {sni.map((code) => (
            <input key={`sni-${code}`} type="hidden" name="sni" value={code} />
          ))}
          {kommun.map((code) => (
            <input key={`kommun-${code}`} type="hidden" name="kommun" value={code} />
          ))}
        </form>

        {/* Row 2 — bransch (SNI tree popover) + ort (cascade popover), both multi-select, side by side,
            behind a hairline and a caption. The two interaction models on this surface differ — the name
            is SUBMITTED, these narrow an ongoing browse — and after the live review the fix was to DRAW
            that difference (a group with its own caption) rather than explain it in more hint prose,
            which was the opposite finding (9). `role="group"` + `aria-labelledby` against the visible
            caption; deliberately NOT a third <h2>, which would be heading noise for two controls.
            Deliberately OUTSIDE the <form>: these are JS-only draft controls with no submitted name, and
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
            commitSni(
              [...next],
              t(added ? "announceFilterAdded" : "announceFilterRemoved", {
                namn: t("branschLabel"),
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
                namn: t("ortLabel"),
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

        {/* Row 3 — the DRAFT chips (bransch + orter) + the ONE clear control. Editing a chip edits the
            draft only; the filter is applied on the next "Sök företag". The clear control is NOT gated on
            there being chips — see `showClear`. */}
        {showClear && (
          <div className="flex flex-wrap items-center gap-3">
            {/* Never an empty list: a `<ul>` with no `<li>` is announced as "list, 0 items". */}
            {branschChipCount + orter.length > 0 && (
              // `items-center`: `.jp-chiplist` declares no `align-items`, so a 44px button inside one
              // `<li>` would stretch the row and leave the ort chips hanging at the top edge.
              <ul className="jp-chiplist items-center">
                {branschSummary ? (
                  <li>
                    {/* The summary REPORTS; it does not delete. A `jp-chip__remove` × here would be
                        pixel-identical to the ones the user just learned remove ONE thing, while
                        dropping the whole draft — possibly 800 codes, with no undo. The removal is a
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
                        onClick={() => commitSni([], t("announceBranschCleared"))}
                      >
                        {t("branschRemoveAll")}
                      </button>
                    </span>
                  </li>
                ) : (
                  branschChips.map((chip) => (
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
                {orter.map((code) => {
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
                          onClick={() =>
                            commitKommun(
                              orter.filter((c) => c !== code),
                              t("announceFilterRemoved", { namn: name }),
                            )
                          }
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
        <p aria-live="polite" className="sr-only">
          {announcement}
        </p>

        {/* Draft-vs-applied honesty (design-reviewer gate): a discreet polite line, active only while the
            draft diverges from the applied URL filter. It never competes with the submit's own label. */}
        {draftDiffersFromApplied && (
          <p aria-live="polite" className="text-body-sm text-text-secondary">
            {t("unappliedChanges")}
          </p>
        )}
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
          hasOrgNrResult
            ? "mt-1 border-t border-border pt-6 outline-none"
            : "outline-none"
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
