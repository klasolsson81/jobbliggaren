/**
 * #560 PR-B — the centralized searchParams builder/parser for `/foretag/sok` (the /jobb
 * `search-params.ts` mold). The filter island (name + SNI + kommun) and the pagination both write the
 * same URL, so param-preservation must be symmetric: both builders share one `appendFilterAxes` so
 * they can never erase each other's params (the same SPOT discipline as `buildJobbHref` vs
 * `buildPageHref`).
 *
 * Contract:
 * - `sni` / `kommun` = ONE query param per axis, carrying the raw SCB leaf codes joined by
 *   {@link AXIS_SEPARATOR} (the picker expands a section/division/whole-län selection to its
 *   leaves — ADR 0042 Beslut B). Sorted so shared links get a stable form. They were REPEATED
 *   params until 2026-07-29; {@link parseCodeAxis} still accepts that form, so every previously
 *   shared link keeps working. The change is not cosmetic — see `appendFilterAxes` for the router
 *   cache collision it removes.
 * - `namn` = the name PREFIX (case-insensitive, backend-anchored). Written only when non-empty.
 * - `sida` (page) = omitted always by the filter builder (a filter/name change resets to page 1);
 *   only `buildPageHref` writes it, and only when > 1.
 * - Sort is A→Ö only (no `sortBy` param) and `pageSize` is fixed (not serialized).
 * - org.nr is DELIBERATELY ABSENT from this state and every URL: a sole-prop org.nr can equal a
 *   personnummer (ADR 0087 D8(c)), so it lives only in the org.nr island's POST body, never here.
 *
 * The reference-based drop-unknown for `sni`/`kommun` (the dynamic SCB allowlist) is applied in
 * `page.tsx` via {@link normalizeCodes}, not in the builder — so the builder stays reference-free and
 * unit-testable, exactly as `matchGrades` filtering lives in `jobb/page.tsx`, not `search-params.ts`.
 *
 * ENFORCING the org.nr exclusion (2026-07-26, CTO bind
 * `docs/reviews/2026-07-26-foretag-sok-pr4-nojs-pnr-cto.md`). The paragraph above was true at the
 * TYPE level and false at the VALUE level: `ForetagSokUrlState` has no `organizationNumber` field,
 * but a ten-digit value forwarded perfectly well as `namn` — into `body.name`, into a name-prefix
 * scan, into the address bar, into history. `parseNamn` was `trim` + `slice` and nothing else, so
 * the guard CLAUDE.md §5 ranks highest existed only in the two places that require the client to
 * run (`ForetagSokSearchbar`, the BFF route). The `namn` axis had no guard at any layer, and a
 * native GET (JS off, or Enter before hydration) or a hand-typed URL walked straight through it.
 * Measured 2026-07-26 against HEAD: typing `1010101010` and serialising the form as a browser
 * would produced `/foretag/sok?namn=1010101010`.
 *
 * {@link parseNamn} now returns a discriminated union so the refusal cannot be forgotten at the
 * call site — the compiler pins it, rather than a second `isOrgNrShaped…` predicate a caller may
 * simply never call. The gate fires on the class {@link normalizeOrgNrInput} accepts (every written
 * org.nr form), not on the narrower personnummer heuristic: the JS path already routes every
 * such value away from the name branch, and gating the two paths on different predicates would
 * make one rule into two. It is deliberately fail-safe in this direction and must not later be
 * narrowed to "pnr-shaped only" — that inverts the posture.
 */

import { normalizeOrgNrInput } from "@/lib/dto/company-registry";

/** The route these builders serialize. Exported so the proxy can match it without a second literal. */
export const FORETAG_SOK_ROUTE = "/foretag/sok";
const ROUTE = FORETAG_SOK_ROUTE;

/**
 * The PII-FREE flag the wash redirect carries so the refusal can be explained instead of silently
 * swallowing what the user typed. It names WHAT was refused, never the value. Neither
 * {@link buildForetagSokHref} nor {@link buildPageHref} emits it, so it cannot survive the user's
 * next action.
 */
export const ORG_NR_REFUSED_PARAM = "avvisat";
const ORG_NR_REFUSED_VALUE = "orgnr";

/** Caps mirroring the backend `CompanyRegisterSearchCriteria` (the last barrier is still the server). */
export const MAX_NAME_PREFIX_LENGTH = 100;
export const MAX_SNI_CODES = 1000;
export const MAX_MUNICIPALITY_CODES = 290;
export const MAX_PAGE = 100;
export const PAGE_SIZE = 20;

/**
 * The shareable URL-state: the three filter axes. NO `organizationNumber`/`orgnr` field — org.nr is
 * structurally excluded from the URL (D8(c)). `sida` is NOT part of the state (it is orthogonal to the
 * filter; a filter change resets it), mirroring `JobbUrlState` which likewise omits `page`.
 */
export interface ForetagSokUrlState {
  namn: string;
  sni: ReadonlyArray<string>;
  kommun: ReadonlyArray<string>;
}

/**
 * The separator that joins the codes of ONE axis into ONE query value.
 *
 * `-` is chosen for two measured reasons. `URLSearchParams.toString()` leaves it unencoded, where
 * `,` becomes `%2C` and would disfigure every shared link on a surface whose whole value is being
 * shareable. And no code on either axis can contain it: SNI leaves are five digits and kommun codes
 * are four.
 *
 * That digits-only argument carries the safety on its own, and it has to — an earlier version of
 * this comment added "and `normalizeCodes` drops anything outside the SCB reference anyway", which
 * is FALSE at three call sites where no allowlist is passed (`page.tsx`'s wash redirect,
 * `proxy.ts`, and `page.tsx` again when the reference failed to load). There, codes are only
 * deduped and capped. An untrue safety clause in a docblock is worse than none: it licenses the
 * wrong reasoning at the next edit (code-reviewer, #1134).
 */
const AXIS_SEPARATOR = "-";

/**
 * Serialize ONE code axis into ONE query value — the counterpart to {@link parseCodeAxis}, and the
 * single place the joined form is produced.
 *
 * Exported because the route has a producer that cannot call a URL builder: the search island's
 * no-JS `<form>` serialises its own hidden fields, so a native GET writes whatever shape those
 * fields have. Before this existed that form emitted one input per code and therefore kept writing
 * the REPEATED shape — a fourth producer, silently disagreeing with the three builders, and enough
 * on its own to put the router-cache collision back (code-reviewer, #1134). Sorting lives here too,
 * so the two producers cannot drift on ordering either.
 */
export function serializeCodeAxis(codes: ReadonlyArray<string>): string {
  return [...codes].sort().join(AXIS_SEPARATOR);
}
/*
 * Preconditions, stated here rather than left to be read off the current callers — the export
 * widens the audience past the two that exist today.
 *
 * It assumes codes already normalised by {@link normalizeCodes}: it does NOT dedupe
 * (`["0180","0180"]` → `"0180-0180"`), and it does NOT validate that a code is separator-free
 * (`["01-80","1480"]` → `"01-80-1480"`, which parses back as three codes). Both omissions are
 * deliberate — dedupe belongs to `normalizeCodes`, and the separator's safety rests on the
 * digits-only argument at {@link AXIS_SEPARATOR} — but a caller has to know they are omissions
 * rather than guarantees.
 */

/**
 * Serialize the filter axes onto `params` — ONE occurrence per key, never a repeated one.
 *
 * **Why one occurrence, and why this is not cosmetic.** Next's client router cache keys a route by
 * its URL, and it collapses REPEATED query keys to the last value only. So `?kommun=A&kommun=B` and
 * `?kommun=B` hash to the same entry: navigating from the first to the second — which is what
 * removing the first of two chips does — targets a URL the cache believes it already holds. No RSC
 * request is made, the page never re-renders, and the surface ends up with the URL saying one filter
 * while the controls and the results below still show the other. Upstream: vercel/next.js#92152 and
 * its fix PR #93368 (both open on 2026-07-29; we run 16.2.9).
 *
 * Joining the codes removes the collision at its source rather than repairing it afterwards: two
 * different applied states **that this module writes** can no longer produce the same cache key, at
 * any latency, on any machine. A timing-based workaround was built and measured first
 * (`router.refresh()` beside the push) and it FAILED — correct for one removal, but two or more in
 * quick succession were undone entirely once server latency passed ~600 ms, which is inside the
 * range this surface already measures.
 *
 * **The residual, accepted deliberately (code-reviewer bind, #1134).** This cannot retro-fix a URL
 * it did not write. Arriving on a link shared before 2026-07-29 — which carries the repeated form —
 * the FIRST filter change still collides, because the URL in the address bar is the old shape and
 * the cache collapses it. Measured: seven joined transitions all fetch, the legacy arrival does not.
 * It is SELF-HEALING, and that is what makes accepting it right: every writer now emits the joined
 * form, including the no-JS form, so the first commit of any kind replaces the old URL and every
 * navigation after it is correct. Before the form was fixed the old shape was self-RENEWING
 * instead, and accepting it then would have been wrong.
 *
 * A normalising redirect in `page.tsx` was considered and REJECTED on this repo's own measurement:
 * a `redirect()` on this route cannot answer 3xx once the `(app)` layout has begun streaming (see
 * `page.tsx`), so it would cost a served document, ~1s of dwell and the URL in six `Referer`
 * headers on EVERY legacy arrival, to avoid one possibly-stale render. The proxy is the right layer
 * if this is ever closed — as its own PR, strictly under the auth branch, preserving `sida` and
 * `avvisat`, with a no-loop pin.
 *
 * Shared by all three href builders in this module, and — through the exported
 * {@link serializeCodeAxis} — by the search island's no-JS form, which is the one producer that
 * cannot call a builder. That is every writer of these two axes; none can drift from the others.
 */
function appendFilterAxes(params: URLSearchParams, state: ForetagSokUrlState): void {
  if (state.sni.length > 0) params.set("sni", serializeCodeAxis(state.sni));
  if (state.kommun.length > 0)
    params.set("kommun", serializeCodeAxis(state.kommun));
  const namn = state.namn.trim();
  if (namn.length > 0) params.set("namn", namn);
}

/**
 * Build the href for a filter/name change. `sida` is never emitted (reset to page 1 — otherwise the
 * user could land on a page that no longer exists under the new filter).
 */
export function buildForetagSokHref(state: ForetagSokUrlState): string {
  const params = new URLSearchParams();
  appendFilterAxes(params, state);
  const qs = params.toString();
  return qs.length > 0 ? `${ROUTE}?${qs}` : ROUTE;
}

/**
 * Build a pagination href: the current filter state + the target page. `sida` is written only when
 * > 1 (page 1 is the param's absence — a clean URL). Same axis serialization as
 * {@link buildForetagSokHref}, so the two builders cannot drift.
 */
export function buildPageHref(state: ForetagSokUrlState, targetPage: number): string {
  const params = new URLSearchParams();
  appendFilterAxes(params, state);
  if (targetPage > 1) params.set("sida", String(targetPage));
  const qs = params.toString();
  return qs.length > 0 ? `${ROUTE}?${qs}` : ROUTE;
}

/**
 * Parse a CODE AXIS (`sni` / `kommun`) out of a query param into its codes.
 *
 * Accepts BOTH forms, and that is the whole back-compat story: the joined form this module writes
 * from 2026-07-29 (`?sni=a-b`) and the repeated form it wrote before that (`?sni=a&sni=b`), which
 * every previously shared or bookmarked link still carries. Both parse to the same codes, so no
 * redirect and no migration are needed — a reader cannot tell which form produced the state.
 *
 * It also trims each value, which the old parser did not: `?kommun=%200180` is now accepted rather
 * than silently dropped. Pinned below.
 *
 * Named for the axis rather than the shape. It used to be `toStringList`, which described the
 * array/scalar normalisation and nothing else; now that it also splits, that name would be true of
 * half of what it does. `/jobb` keeps its own separate local parser (`jobb/page.tsx`) and has NOT
 * moved to the joined form. The reason is not that its taxonomy IDs contain `_` — an underscore is
 * orthogonal to a `-` split, and a sweep of the repo's whole ID corpus found none containing `-`
 * either (code-reviewer, #1134). It is that JobTech IDs are drawn from the base64url alphabet,
 * where `-` is a legal character, so `/jobb` owes its own separator decision across its own six
 * axes and their own caps rather than inheriting a choice justified by SCB's digits.
 */
export function parseCodeAxis(raw: string | string[] | undefined): string[] {
  if (raw === undefined) return [];
  return (Array.isArray(raw) ? raw : [raw])
    .flatMap((value) => value.split(AXIS_SEPARATOR))
    .map((value) => value.trim())
    .filter((value) => value.length > 0);
}

/**
 * The outcome of parsing `?namn=`. A union rather than a bare string so a call site cannot read the
 * name without deciding what to do about the refusal — the compiler pins it (ADR 0087 D8(c);
 * CLAUDE.md §5 ranks the personnummer guard highest, and an axis with no guard at any layer is the
 * defect this closes). Named `orgNrShaped`, not `refused`: the island already uses "refused" for the
 * narrower personnummer-shaped case, and the two are not the same class.
 */
export type ParsedNamn =
  | { readonly kind: "name"; readonly value: string }
  | { readonly kind: "orgNrShaped" };

/**
 * Parse the `namn` param: first value, trimmed, truncated to {@link MAX_NAME_PREFIX_LENGTH}. Returns
 * `{ kind: "name", value: "" }` when absent. Unlike /jobb's `clampSubMinimumQ` there is NO
 * sub-minimum: the backend has no `NameTooShort` — a one-character prefix is a valid, index-served
 * range scan.
 *
 * The org.nr gate runs FIRST, on the untruncated trimmed value, and on the SAME predicate the search
 * island uses to route a value to the org.nr branch ({@link normalizeOrgNrInput}: strip separators,
 * then accept the domain's written-form contract). One knowledge piece, one place — the two paths agree
 * by construction instead of by two normalisers that can drift. Nothing is lost: a company whose name
 * normalises to an org.nr is not a Swedish name class, and with JS on that input already takes the
 * org.nr branch, so the gate removes an inconsistency rather than adding a restriction.
 */
export function parseNamn(raw: string | string[] | undefined): ParsedNamn {
  const values = raw === undefined ? [] : Array.isArray(raw) ? raw : [raw];
  // EVERY value is gated, not just the one this function goes on to use. A repeated
  // `?namn=&namn=1010101010` puts the ten digits in a position `raw[0]` never reads — measured
  // 2026-07-26 against the running dev server: it rendered the page with no wash at all. What
  // reaches history, a re-shared link and the access log is the WHOLE query string, not the slice
  // of it the parser happens to consume, so the gate has to read the whole thing too.
  if (values.some((value) => normalizeOrgNrInput(value.trim()) !== null)) {
    return { kind: "orgNrShaped" };
  }
  const first = values[0]?.trim() ?? "";
  return { kind: "name", value: first.slice(0, MAX_NAME_PREFIX_LENGTH) };
}

/**
 * Build the wash target for a refused org.nr `?namn=`: the filter axes WITHOUT the name, plus the
 * PII-free refusal flag. It shares {@link appendFilterAxes} with both commit builders deliberately —
 * concatenating a flag suffix onto `buildForetagSokHref(...)` at the redirect site would create a
 * second serialisation site for this route, the exact drift that helper exists to prevent.
 *
 * `sida` is NOT carried: dropping the name filter changes the result set, so a page number from the
 * old one can be out of range — the same reason the filter builder never emits it.
 *
 * The target carries no `namn`, so parsing it returns `{ kind: "name", value: "" }` and the redirect
 * terminates. That is pinned by a test rather than reasoned about.
 */
export function buildOrgNrRefusedHref(
  state: Omit<ForetagSokUrlState, "namn">,
): string {
  const params = new URLSearchParams();
  // `namn: ""` is supplied here, not accepted from the caller: this builder's whole purpose is to
  // produce a URL WITHOUT a name, so taking one would be the same type-level/value-level slip this
  // change exists to close.
  appendFilterAxes(params, { ...state, namn: "" });
  params.set(ORG_NR_REFUSED_PARAM, ORG_NR_REFUSED_VALUE);
  return `${ROUTE}?${params.toString()}`;
}

/** Whether the URL carries the refusal flag {@link buildOrgNrRefusedHref} sets. */
export function parseOrgNrRefused(raw: string | string[] | undefined): boolean {
  const first = Array.isArray(raw) ? raw[0] : raw;
  return first === ORG_NR_REFUSED_VALUE;
}

/** Parse the `sida` param to a positive integer in [1, {@link MAX_PAGE}], defaulting to 1. */
export function parseSida(raw: string | string[] | undefined): number {
  const first = Array.isArray(raw) ? raw[0] : raw;
  const value = typeof first === "string" ? Number.parseInt(first, 10) : NaN;
  if (!Number.isInteger(value) || value < 1) return 1;
  return Math.min(value, MAX_PAGE);
}

/**
 * Drop-unknown + dedupe + cap for the `sni`/`kommun` code lists. A manipulated URL must never 400 the
 * search (the drop-unknown discipline, parity `matchGrades`): unknown codes are dropped against the
 * dynamic SCB allowlist rather than rejected. When the reference degrades (no allowlist available),
 * pass `allowed` as undefined — codes are then deduped + capped only, and the backend is the last
 * barrier. Order is preserved; the cap bounds the worst-case body size.
 */
export function normalizeCodes(
  codes: ReadonlyArray<string>,
  cap: number,
  allowed?: ReadonlySet<string>,
): string[] {
  const seen = new Set<string>();
  const out: string[] = [];
  for (const code of codes) {
    if (allowed && !allowed.has(code)) continue;
    if (seen.has(code)) continue;
    seen.add(code);
    out.push(code);
    if (out.length >= cap) break;
  }
  return out;
}
