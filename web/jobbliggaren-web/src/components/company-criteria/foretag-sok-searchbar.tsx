"use client";

import {
  useEffect,
  useId,
  useMemo,
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
import { BranschTypeahead, type BranschOption } from "./bransch-typeahead";
import { CompanyBrowseList } from "./company-browse-list";
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
 * from the applied URL); the org.nr branch and the JS controls (typeahead, popover) require JS.
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
 *   hint, not the input), and the button now matches the field's 48px so the row reads as one control;
 * - the bransch/ort controls sit in a hairline-separated `role="group"` — the two interaction models
 *   (name SUBMITS, filters narrow) are drawn rather than explained in more prose;
 * - the org.nr answer renders through `CompanyBrowseList` instead of a hand-rolled card, so the two
 *   surfaces cannot drift in columns, masking or Bevaka affordance;
 * - "Rensa sökningen" NAVIGATES and clears the name too — it previously nulled two draft fields and
 *   was hidden behind `hasFilter`, so a pure name search had no clear path at all;
 * - a delayed, height-reserved pending line (measured: submit → painted results was 2 403 ms with no
 *   visible pending state, because the URL resolves in ~190 ms while the view keeps the old rows).
 */

/** `useSyncExternalStore` with a never-firing subscription: the cheapest "am I hydrated" signal. */
const emptySubscribe = () => () => {};

/**
 * Delay before the pending line appears. Below this a search is perceived as instant, and a line that
 * flashes for 80 ms is noise; above it the view is silently stale, which is what Klas measured.
 */
const PENDING_DELAY_MS = 250;

type OrgNrState =
  | { kind: "idle" }
  | { kind: "pending" }
  | { kind: "refused" }
  | { kind: "rateLimited"; seconds: number }
  | { kind: "error" }
  | { kind: "found"; result: NonNullable<OrgNrSearchResult> }
  | { kind: "notFound" };

/** The single selected bransch draft: the display label + the SNI leaf codes the URL `sni` axis carries. */
interface SelectedBranch {
  readonly label: string;
  readonly leafCodes: ReadonlyArray<string>;
}

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

/** Section + division + leaf names as searchable options, each carrying the leaf codes it expands to. */
function buildBranschOptions(reference: CriterionReference): BranschOption[] {
  const out: BranschOption[] = [];
  for (const section of reference.sni) {
    const sectionLeaves = section.divisions.flatMap((d) =>
      d.leaves.map((l) => l.code),
    );
    if (sectionLeaves.length > 0) {
      out.push({
        key: `sec:${section.code}`,
        label: section.name,
        leafCodes: sectionLeaves,
      });
    }
    for (const division of section.divisions) {
      const divisionLeaves = division.leaves.map((l) => l.code);
      if (divisionLeaves.length > 0) {
        out.push({
          key: `div:${division.code}`,
          label: division.name,
          leafCodes: divisionLeaves,
        });
      }
      for (const leaf of division.leaves) {
        out.push({
          key: `leaf:${leaf.code}`,
          label: leaf.name,
          leafCodes: [leaf.code],
        });
      }
    }
  }
  return out;
}

/** True when `list` and `set` hold exactly the same codes (order-independent). */
function sameCodeSet(
  list: ReadonlyArray<string>,
  set: ReadonlySet<string>,
): boolean {
  return list.length === set.size && list.every((code) => set.has(code));
}

/**
 * Seed the bransch chip from the URL `sni` prop (CTO GO-point): find the option whose leaf-code set
 * equals the `sni` set; a clean match seeds that chip. If `sni` is non-empty but matches no single
 * option (an arbitrary/legacy link), a generic chip keeps the active filter visible + clearable rather
 * than silently swallowing it. Same-wave links are clean matches, so this is the rare fallback.
 */
function seedBranch(
  options: ReadonlyArray<BranschOption>,
  sni: ReadonlyArray<string>,
  genericLabel: string,
): SelectedBranch | null {
  if (sni.length === 0) return null;
  const target = new Set(sni);
  const match = options.find((o) => sameCodeSet(o.leafCodes, target));
  if (match) return { label: match.label, leafCodes: match.leafCodes };
  return { label: genericLabel, leafCodes: [...sni] };
}

export function ForetagSokSearchbar({
  reference,
  referenceOk,
  namn,
  sni,
  kommun,
}: ForetagSokSearchbarProps) {
  const t = useTranslations("pages.foretag.sok");
  const router = useRouter();
  const hydrated = useSyncExternalStore(
    emptySubscribe,
    () => true,
    () => false,
  );

  const searchInputId = useId();
  const searchHintId = useId();
  const branschInputId = useId();
  const branschHintId = useId();
  const branschNoticeId = useId();
  const filterGroupId = useId();
  const orgNrLabelId = useId();

  const abortRef = useRef<AbortController | null>(null);
  const resultRef = useRef<HTMLDivElement>(null);
  const ortBtnRef = useRef<HTMLButtonElement>(null);

  // Bransch options + lookups, derived client-side from the already-loaded reference (no fetch).
  const branschOptions = useMemo(() => buildBranschOptions(reference), [reference]);
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

  // The whole draft: the field value, the single bransch, and the orter — one island, seeded from the URL.
  const [value, setValue] = useState(namn);
  const [branch, setBranch] = useState<SelectedBranch | null>(() =>
    seedBranch(branschOptions, sni, t("branschGeneric")),
  );
  const [orter, setOrter] = useState<string[]>(() => [...kommun]);
  const [ortOpen, setOrtOpen] = useState(false);
  const [state, setState] = useState<OrgNrState>({ kind: "idle" });
  const [isNavPending, startNavTransition] = useTransition();

  // Draft-vs-applied: the chips + field show the DRAFT; the streamed results below show the APPLIED URL
  // filter. Compute the divergence so it can be surfaced honestly (never a second competing button). The
  // dirty line is meaningless for an org.nr-shaped value (that path ignores the filter), so it is gated
  // on the name branch.
  const appliedSni = useMemo(() => new Set(sni), [sni]);
  const appliedKommun = useMemo(() => new Set(kommun), [kommun]);
  const isOrgNrValue = normalizeOrgNrInput(value) !== null;
  const draftDiffersFromApplied =
    !isOrgNrValue &&
    (value.trim() !== namn ||
      !sameCodeSet(branch?.leafCodes ?? [], appliedSni) ||
      !sameCodeSet(orter, appliedKommun));

  const hasFilter = branch !== null || orter.length > 0;
  const hasOrgNrResult = state.kind !== "idle";
  // The clear control's gate is the WHOLE search, not just the filter axes: gated on `hasFilter` alone
  // (as it was), a pure name search — the most common one — had no clear path at all (finding 6).
  const showClear =
    hasFilter || hasOrgNrResult || value.trim().length > 0 || namn.length > 0;

  /**
   * The pending line is DELAYED, not immediate: a soft navigation resolves in ~190 ms, so an
   * undelayed line would flash on every fast search. `isNavPending` is the only available signal —
   * the Suspense fallback (the skeleton) renders on a HARD load only, so "the skeleton appears"
   * is not the indication (design-review V3).
   */
  const [showPending, setShowPending] = useState(false);
  useEffect(() => {
    // Early-return rather than resetting here: a synchronous setState in an effect body triggers
    // cascading renders (and the lint rule that lands it). The reset belongs in cleanup, which runs
    // when the navigation settles — so the line clears itself and the next search starts delayed
    // again rather than flashing on instantly.
    if (!isNavPending) return;
    const timer = window.setTimeout(() => setShowPending(true), PENDING_DELAY_MS);
    return () => {
      window.clearTimeout(timer);
      setShowPending(false);
    };
  }, [isNavPending]);

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

    // name + filter branch — clear any org.nr result and commit the shareable URL, carrying the field
    // value, the draft bransch (its leaf codes) and the draft orter TOGETHER (no silent drop).
    abortRef.current?.abort();
    setState({ kind: "idle" });
    startNavTransition(() => {
      router.push(
        buildForetagSokHref({
          namn: value.trim(),
          sni: branch ? [...branch.leafCodes] : [],
          kommun: [...orter],
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
    setBranch(null);
    setOrter([]);
    setState({ kind: "idle" });
    startNavTransition(() => {
      router.push(buildForetagSokHref({ namn: "", sni: [], kommun: [] }));
    });
  }

  return (
    <div className="mt-2 flex max-w-2xl flex-col gap-5">
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
              baseline rather than the input's (finding 1). The button is sized to the field's 48px so
              the pair reads as one control; `aria-describedby` is position-independent and follows. */}
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
              className="jp-btn jp-btn--primary h-12 shrink-0"
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

      {/* Row 2 — bransch (single-select typeahead) + ort (multi-select cascade popover), side by side,
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
        <span id={filterGroupId} className="jp-label">
          {t("filterGroupLabel")}
        </span>
        <div className="mt-3 grid gap-4 md:grid-cols-2">
          <div className="jp-field">
            <label htmlFor={branschInputId} className="jp-label">
              {t("branschLabel")}
            </label>
            <BranschTypeahead
              id={branschInputId}
              options={branschOptions}
              onSelect={(option) =>
                setBranch({ label: option.label, leafCodes: option.leafCodes })
              }
              disabled={!referenceOk}
              ariaDescribedBy={
                referenceOk ? branschHintId : `${branschHintId} ${branschNoticeId}`
              }
            />
            {/* Kept for now — unlike `ortHint`, this one is still load-bearing: the control is a
                typeahead whose affordance is not self-evident. It goes when PR 5 replaces the
                typeahead with a self-naming "Välj bransch"-trigger (#999). */}
            <span id={branschHintId} className="jp-hint">
              {t("branschHint")}
            </span>
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

      {/* The ort cascade. Degenerate single-axis case (our URL contract has only a `kommun` axis, no
          `lan`): groupAxis is OMITTED, so "Hela {län}" materialises the län's kommun codes into
          `selected`. `counts={null}` — no facet counts (Klas locked FOCUSED). key-remount on open resets
          the popover's active-left column. */}
      <JobbFilterPopover
        key={ortOpen ? "ort-open" : "ort-closed"}
        open={ortOpen}
        groups={lanGroups}
        selected={orter}
        onChange={(next) => setOrter(next)}
        onClose={() => setOrtOpen(false)}
        onClearAll={() => setOrter([])}
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
          {hasFilter && (
            <ul className="jp-chiplist">
              {branch && (
                <li>
                  <span className="jp-chip jp-chip--removable">
                    <span className="jp-chip__label" title={branch.label}>
                      {branch.label}
                    </span>
                    <button
                      type="button"
                      className="jp-chip__remove"
                      aria-label={t("branschRemove")}
                      onClick={() => setBranch(null)}
                    >
                      <X size={14} aria-hidden="true" />
                    </button>
                  </span>
                </li>
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
                        onClick={() =>
                          setOrter((prev) => prev.filter((c) => c !== code))
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
            className="jp-btn jp-btn--ghost"
            onClick={onClearSearch}
          >
            {t("clearButton")}
          </button>
        </div>
      )}

      {/* Draft-vs-applied honesty (design-reviewer gate): a discreet polite line, active only while the
          draft diverges from the applied URL filter. It never competes with the submit's own label. */}
      {draftDiffersFromApplied && (
        <p aria-live="polite" className="text-body-sm text-text-secondary">
          {t("unappliedChanges")}
        </p>
      )}

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
            className="mb-3 text-body-sm font-semibold text-text-primary"
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
            <p className="text-body-sm text-text-primary">{t("orgNrNotFoundBody")}</p>
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

      {/* Finding 2 — the pending line, immediately above the streamed results. Measured: submit →
          painted results 2 403 ms with NO visible pending state, because the navigation resolves in
          ~190 ms while the view keeps the old rows.
          Deliberate form (design-review V3): no spinner (the doctrine reserves BrandSpinner for
          formless waits and forbids it where the wait is short), no opacity dimming of the stale list
          (dimmed body text falls below 4.5:1, and stale is not disabled), and the region is mounted
          UNCONDITIONALLY with the text swapped in — a live region injected together with its content
          is not reliably announced. The slot reserves its own height so nothing below shifts. */}
      <div className="min-h-6">
        <p role="status" aria-live="polite" className="text-body-sm text-text-primary">
          {showPending ? t("loadingResults") : ""}
        </p>
      </div>
    </div>
  );
}
