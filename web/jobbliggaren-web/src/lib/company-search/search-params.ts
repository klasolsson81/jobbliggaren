/**
 * #560 PR-B — the centralized searchParams builder/parser for `/foretag/sok` (the /jobb
 * `search-params.ts` mold). The filter island (name + SNI + kommun) and the pagination both write the
 * same URL, so param-preservation must be symmetric: both builders share one `appendFilterAxes` so
 * they can never erase each other's params (the same SPOT discipline as `buildJobbHref` vs
 * `buildPageHref`).
 *
 * Contract:
 * - `sni` / `kommun` = repeated query params (raw SCB leaf codes; the picker expands a section/
 *   division/whole-län selection to its leaves — ADR 0042 Beslut B). Sorted so shared links get a
 *   stable form.
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
 * simply never call. The gate fires on the class {@link normalizeOrgNrInput} accepts (the EXACT
 * ten-digit class), not on the narrower personnummer heuristic: the JS path already routes every
 * such value away from the name branch, and gating the two paths on different predicates would
 * make one rule into two. It is deliberately fail-safe in this direction and must not later be
 * narrowed to "pnr-shaped only" — that inverts the posture.
 */

import { normalizeOrgNrInput } from "@/lib/dto/company-registry";

const ROUTE = "/foretag/sok";

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

/** Serialize the filter axes onto `params` (shared by both href builders — the SPOT). */
function appendFilterAxes(params: URLSearchParams, state: ForetagSokUrlState): void {
  for (const code of [...state.sni].sort()) params.append("sni", code);
  for (const code of [...state.kommun].sort()) params.append("kommun", code);
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

/** Normalize a repeated query param to a string[] (drops empty values). */
export function toStringList(raw: string | string[] | undefined): string[] {
  if (raw === undefined) return [];
  return (Array.isArray(raw) ? raw : [raw]).filter((value) => value.length > 0);
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
 * island uses to route a value to the org.nr branch ({@link normalizeOrgNrInput}: strip spaces and
 * hyphens, then require exactly ten digits). One knowledge piece, one place — the two paths agree by
 * construction instead of by two normalisers that can drift. Nothing is lost: a company whose name
 * normalises to exactly ten digits is not a Swedish name class, and with JS on that input already
 * takes the org.nr branch, so the gate removes an inconsistency rather than adding a restriction.
 */
export function parseNamn(raw: string | string[] | undefined): ParsedNamn {
  const first = (Array.isArray(raw) ? raw[0] : raw)?.trim() ?? "";
  if (normalizeOrgNrInput(first) !== null) return { kind: "orgNrShaped" };
  return { kind: "name", value: first.slice(0, MAX_NAME_PREFIX_LENGTH) };
}

/**
 * Build the wash target for a refused ten-digit `?namn=`: the filter axes WITHOUT the name, plus the
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
export function buildOrgNrRefusedHref(state: ForetagSokUrlState): string {
  const params = new URLSearchParams();
  appendFilterAxes(params, state);
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
