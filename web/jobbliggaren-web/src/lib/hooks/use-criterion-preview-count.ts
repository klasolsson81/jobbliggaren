"use client";

// "use client": a debounced fetch hook holding preview state + an AbortController — browser-only.

import { useEffect, useState } from "react";

const DEBOUNCE_MS = 400;

/** The honest magnitude a preview resolves to (mirrors backend `CriterionMatchMagnitudeDto`). */
export interface PreviewMagnitude {
  readonly magnitude: number;
  readonly saturated: boolean;
}

/**
 * #560 PR-3 — the live magnitude preview for the criterion picker (the `use-draft-match-count`
 * precedent). Counts (debounced ~400 ms) how many register companies match the draft predicate, via
 * the BFF route `POST /api/me/criterion-preview-count`.
 *
 * Contract:
 * - The key is built from the two axes (sorted), so only a SET change (not order) triggers a refetch.
 * - Both axes are REQUIRED: the endpoint 400s a missing axis, so the hook does not call until each
 *   axis has at least one code. While either is empty, `preview` stays null and `loading` false.
 * - `AbortController` cancels an in-flight request when the draft changes (the latest wins; a
 *   superseded request never touches state).
 * - On any failure/degradation `preview` is nulled (a neutral placeholder, never a false 0).
 * - `enabled` (default true) short-circuits the effect when false (the dialog is closed) — no
 *   background poll against the rate-limited endpoint.
 */
export function useCriterionPreviewCount(
  draft: { readonly sniCodes: ReadonlyArray<string>; readonly municipalityCodes: ReadonlyArray<string> },
  enabled = true,
): {
  readonly preview: PreviewMagnitude | null;
  readonly loading: boolean;
} {
  const [preview, setPreview] = useState<PreviewMagnitude | null>(null);
  const [loading, setLoading] = useState(false);

  // Stable key: sorted copies so only a set change triggers a refetch.
  const key = JSON.stringify({
    s: [...draft.sniCodes].sort(),
    m: [...draft.municipalityCodes].sort(),
  });

  useEffect(() => {
    if (!enabled) return;

    const { s, m } = JSON.parse(key) as { s: string[]; m: string[] };

    // Both axes required — the endpoint 400s a missing axis. Skip the call entirely (no synchronous
    // setState in the effect body). The consumer hides the count while an axis is empty via its own
    // "both chosen" guard, so a retained last value is never shown.
    if (s.length === 0 || m.length === 0) return;

    const controller = new AbortController();

    const timer = setTimeout(() => {
      // setState deferred to the timer callback so `loading` means an actual in-flight request, not
      // the debounce wait.
      setLoading(true);
      void (async () => {
        try {
          const res = await fetch("/api/me/criterion-preview-count", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ sniCodes: s, municipalityCodes: m }),
            signal: controller.signal,
          });
          if (controller.signal.aborted) return;
          if (!res.ok) {
            setPreview(null);
            setLoading(false);
            return;
          }
          const data = (await res.json()) as {
            magnitude?: unknown;
            saturated?: unknown;
          };
          if (controller.signal.aborted) return;
          setPreview(
            typeof data.magnitude === "number" && typeof data.saturated === "boolean"
              ? { magnitude: data.magnitude, saturated: data.saturated }
              : null,
          );
          setLoading(false);
        } catch {
          // AbortError (superseded) → let the new request own state; otherwise null it.
          if (!controller.signal.aborted) {
            setPreview(null);
            setLoading(false);
          }
        }
      })();
    }, DEBOUNCE_MS);

    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [key, enabled]);

  return { preview, loading };
}
