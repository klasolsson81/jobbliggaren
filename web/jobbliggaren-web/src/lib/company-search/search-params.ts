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
 */

const ROUTE = "/foretag/sok";

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
 * Parse the `namn` param: first value, trimmed, truncated to {@link MAX_NAME_PREFIX_LENGTH}. Returns
 * "" when absent. Unlike /jobb's `clampSubMinimumQ` there is NO sub-minimum: the backend has no
 * `NameTooShort` — a one-character prefix is a valid, index-served range scan.
 */
export function parseNamn(raw: string | string[] | undefined): string {
  const first = (Array.isArray(raw) ? raw[0] : raw)?.trim() ?? "";
  return first.slice(0, MAX_NAME_PREFIX_LENGTH);
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
