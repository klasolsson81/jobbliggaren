/**
 * #560 PR-D — the criterion-shaped gate for the "Spara som smart bevakning" CTA on `/foretag/sok`.
 *
 * A `CompanyWatchCriterion` is SNI ∧ kommun — BOTH axes are required (the Domain forbids a
 * kommun-only or SNI-only criterion: `CompanyWatchCriteriaSpec.Create` returns `SniRequired` /
 * `MunicipalityRequired`) — and it has NO text axis. So the active search filter can be saved as a
 * watch only when it is exactly criterion-shaped: SNI non-empty ∧ kommun non-empty ∧ no name term.
 *
 * The name term is checked FIRST (highest precedence): with a name present there is no way to
 * "complete" the filter into a criterion without DROPPING the text constraint, which would silently
 * create a watch matching something different from what is on screen — the silent-drift cardinal sin
 * (CTO F4, 2026-07-18). No amount of added branch/kommun makes a name search saveable, so the name
 * reason wins over the axis-missing reasons.
 *
 * org.nr is deliberately absent: it never enters the `/foretag/sok` URL state (D8(c), it lives only
 * in the org.nr island's POST body), so it is not — and cannot be — an axis of this gate.
 *
 * Pure + reference-free so it is unit-testable in isolation, exactly as `normalizeCodes` /
 * `parseNamn` live in `search-params.ts`. The component maps the non-ready kinds to an i18n reason.
 */
export type FollowAllGate =
  | { readonly kind: "ready" }
  | { readonly kind: "nameTerm" }
  | { readonly kind: "empty" }
  | { readonly kind: "sniMissing" }
  | { readonly kind: "kommunMissing" };

/** Every non-ready gate kind — the exhaustive set the component must map to an explainer string. */
export type FollowAllBlockedKind = Exclude<FollowAllGate["kind"], "ready">;

/**
 * Decide whether the active filter (name prefix + SNI leaves + kommun codes) can be saved as a
 * criterion watch. Returns a discriminated union; only `"ready"` enables the CTA.
 */
export function evaluateFollowAllGate(
  namn: string,
  sni: ReadonlyArray<string>,
  kommun: ReadonlyArray<string>,
): FollowAllGate {
  if (namn.trim().length > 0) return { kind: "nameTerm" };
  if (sni.length === 0 && kommun.length === 0) return { kind: "empty" };
  if (sni.length === 0) return { kind: "sniMissing" };
  if (kommun.length === 0) return { kind: "kommunMissing" };
  return { kind: "ready" };
}
