"use client";

import { useId, useRef, useState, useTransition } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { Check, ShieldAlert } from "lucide-react";
import {
  companyLookupSchema,
  isPersonnummerShapedOrgNr,
  normalizeOrgNrInput,
  type CompanyLookup,
} from "@/lib/dto/company-registry";
import { followCompanyAction } from "@/lib/actions/company-follows";
import { formatOrgNr } from "@/lib/company-follows/org-nr";

/**
 * #454 (ADR 0088) — the /foretag company-lookup island: type an org.nr, see the company (name +
 * counts) EVEN when it has zero ads in our feed, and choose "se annonser" / "se matchande
 * annonser" / "bevaka". Exact lookup (10 digits) — a plain labelled form, NOT a typeahead (no
 * debounce; submit-gated on a normalisable value; the LABEL carries the instruction, never a
 * placeholder — Klas hard rule). Plain useState + AbortController (codebase single-field precedent;
 * no TanStack Query).
 *
 * Personnummer refuse-posture (ADR 0088 D4, Klas 2026-07-02 "Refuse i v1 + #456 avgör"): a
 * pnr-shaped value renders the refuse state LOCALLY — it is never POSTed anywhere (not even our
 * BFF), never echoed back in copy, and the refuse card is a clean stop with NO bevaka affordance
 * (CTO sub-bind; enskilda firmor stay followable from their ads via #455). The backend handler is
 * the enforcing authority; this gate is UX + data-minimisation.
 *
 * The "se annonser" links render ONLY for an unmasked legal entity (`!isProtectedIdentity` +
 * non-null org.nr) — a pnr-shaped value must never enter a browser URL (D8(c)).
 */

// SPOT parity with company-watch-row.tsx (design-reviewer Major 1): the matchNudge line renders on
// primary ink (never gray) and its CTA carries explicit link affordance — Tailwind Preflight strips
// anchor color/underline, so an unstyled <Link> would read as gray body text.
const MATCH_SETTINGS_HREF = "/installningar#matchning";

type LookupState =
  | { kind: "idle" }
  | { kind: "pending" }
  | { kind: "refused" }
  | { kind: "rateLimited"; seconds: number }
  | { kind: "error" }
  | { kind: "result"; data: CompanyLookup; orgNr: string };

export function CompanyLookup() {
  const t = useTranslations("jobads.companyLookup");
  const tWatch = useTranslations("jobads.companyWatches");
  const inputId = useId();
  const hintId = useId();
  const abortRef = useRef<AbortController | null>(null);
  const resultRef = useRef<HTMLDivElement>(null);

  const [value, setValue] = useState("");
  const [state, setState] = useState<LookupState>({ kind: "idle" });
  // "bevaka" from the found card: server action inside a transition (parity FollowCompanyToggle).
  const [isFollowPending, startFollowTransition] = useTransition();
  const [followedId, setFollowedId] = useState<string | null>(null);
  const [followError, setFollowError] = useState<string | null>(null);

  const normalized = normalizeOrgNrInput(value);

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!normalized || state.kind === "pending") return;

    setFollowedId(null);
    setFollowError(null);

    // D4 refuse — locally, before ANY transmission (the value never leaves the browser).
    if (isPersonnummerShapedOrgNr(normalized)) {
      setState({ kind: "refused" });
      resultRef.current?.focus();
      return;
    }

    abortRef.current?.abort();
    const controller = new AbortController();
    abortRef.current = controller;
    setState({ kind: "pending" });

    try {
      const res = await fetch("/api/foretag/lookup", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ organizationNumber: normalized }),
        signal: controller.signal,
      });

      if (res.status === 429) {
        const retryAfter = Number.parseInt(res.headers.get("Retry-After") ?? "60", 10);
        setState({ kind: "rateLimited", seconds: Number.isFinite(retryAfter) ? retryAfter : 60 });
      } else if (res.ok) {
        const parsed = companyLookupSchema.safeParse(await res.json());
        setState(
          parsed.success
            ? { kind: "result", data: parsed.data, orgNr: normalized }
            : { kind: "error" }
        );
      } else {
        setState({ kind: "error" });
      }
    } catch {
      if (controller.signal.aborted) return; // superseded by a newer submit
      setState({ kind: "error" });
    }
    resultRef.current?.focus();
  }

  function onFollow(orgNr: string) {
    setFollowError(null);
    startFollowTransition(async () => {
      const result = await followCompanyAction(orgNr);
      if (result.success) setFollowedId(result.companyWatchId);
      else setFollowError(result.error);
    });
  }

  return (
    <section aria-labelledby={`${inputId}-heading`} className="mb-8">
      <h2 id={`${inputId}-heading`} className="text-h3 font-semibold text-text-primary">
        {t("heading")}
      </h2>
      <p className="jp-hint mt-1">{t("lede")}</p>

      <form onSubmit={onSubmit} className="mt-3 flex max-w-xl items-end gap-2">
        <div className="jp-field grow">
          <label htmlFor={inputId} className="jp-label">
            {t("label")}
          </label>
          <input
            id={inputId}
            className="jp-input"
            inputMode="numeric"
            autoComplete="off"
            aria-describedby={hintId}
            value={value}
            onChange={(e) => setValue(e.target.value)}
          />
          <span id={hintId} className="jp-hint">
            {t("help")}
          </span>
        </div>
        <button
          type="submit"
          className="jp-btn jp-btn--primary"
          disabled={!normalized}
          aria-busy={state.kind === "pending" || undefined}
        >
          {t("submit")}
        </button>
      </form>

      {/* Result region: focus lands here after a submit (a11y — the answer is reachable without
          hunting) AND the region is a polite live-region so the happy path (found-kortet, som inte
          bär role=status) annonseras för skärmläsare (design-reviewer Major 3). tabIndex -1 =
          programmatic focus target only; nested role=alert (error/rateLimited) overridar politeness. */}
      <div
        ref={resultRef}
        tabIndex={-1}
        aria-live="polite"
        className="mt-4 max-w-xl outline-none"
      >
        {state.kind === "result" && state.data.status === "found" && (
          <article className="jp-job" style={{ gridTemplateColumns: "1fr" }}>
            <div className="jp-job__body">
              <h3 className="jp-job__title">
                {state.data.companyName ?? tWatch("unknownCompany")}
              </h3>
              <div className="jp-job__meta">
                {!state.data.isProtectedIdentity && state.data.organizationNumber && (
                  <span>
                    {tWatch("orgNr", {
                      orgNr: formatOrgNr(state.data.organizationNumber),
                    })}
                  </span>
                )}
                <span className="tabular-nums">
                  {tWatch("activeAds", { count: state.data.activeAdCount })}
                </span>
                {state.data.matchingAdCount !== null && (
                  <span className="tabular-nums">
                    {tWatch("matchingAds", { count: state.data.matchingAdCount })}
                  </span>
                )}
              </div>
              {state.data.matchingAdCount === null && (
                <p className="jp-matchline">
                  {tWatch("matchNudge")}{" "}
                  <Link href={MATCH_SETTINGS_HREF} className="jp-nudgelink">
                    {tWatch("matchNudgeCta")}
                  </Link>
                </p>
              )}
              {/* Actions: links ONLY for an unmasked legal entity (D8(c) — a pnr-shaped value
                  never enters a browser URL). Under the v1 refuse-posture found is always
                  unmasked; the gate is defense-in-depth. */}
              <div className="mt-3 flex flex-wrap items-center gap-2">
                {!state.data.isProtectedIdentity && state.data.organizationNumber && (
                  <>
                    <Link
                      className="jp-btn jp-btn--secondary"
                      href={`/jobb?employer=${state.data.organizationNumber}`}
                    >
                      {t("seeAds")}
                    </Link>
                    <Link
                      className="jp-btn jp-btn--secondary"
                      href={`/jobb?employer=${state.data.organizationNumber}&baraMatchade=on`}
                    >
                      {t("seeMatchingAds")}
                    </Link>
                  </>
                )}
                {followedId || state.data.companyWatchId ? (
                  <span className="jp-hint inline-flex items-center gap-1">
                    <Check size={14} aria-hidden="true" /> {t("alreadyFollowing")}
                  </span>
                ) : (
                  <button
                    type="button"
                    className="jp-btn jp-btn--secondary"
                    aria-busy={isFollowPending || undefined}
                    onClick={() => onFollow(state.orgNr)}
                  >
                    {t("follow")}
                  </button>
                )}
              </div>
              {followError && (
                <p role="alert" className="mt-2 text-body-sm text-danger-700">
                  {followError}
                </p>
              )}
            </div>
          </article>
        )}

        {state.kind === "result" && state.data.status === "notFound" && (
          <div role="status" className="jp-empty">
            <div className="jp-empty__title">{t("notFoundTitle")}</div>
            <p>{t("notFoundBody")}</p>
          </div>
        )}

        {state.kind === "result" && state.data.status === "unavailable" && (
          <div
            role="status"
            className="rounded-md border border-warning-700/30 bg-warning-50 px-6 py-4 text-warning-700"
          >
            <p className="text-body font-medium">{t("unavailableTitle")}</p>
            <p className="mt-1 text-body-sm">{t("unavailableBody")}</p>
          </div>
        )}

        {state.kind === "refused" && (
          <div
            role="status"
            className="rounded-md border border-warning-700/30 bg-warning-50 px-6 py-4 text-warning-700"
          >
            <p className="text-body font-medium">
              <ShieldAlert size={14} aria-hidden="true" className="mr-1 inline" />
              {t("refusedTitle")}
            </p>
            <p className="mt-1 text-body-sm">{t("refusedBody")}</p>
          </div>
        )}

        {state.kind === "rateLimited" && (
          <div
            role="alert"
            className="rounded-md border border-warning-700/30 bg-warning-50 px-6 py-4 text-warning-700"
          >
            <p className="text-body font-medium">{t("rateLimitedTitle")}</p>
            <p className="mt-1 text-body-sm">
              {t("rateLimitedBody", { seconds: state.seconds })}
            </p>
          </div>
        )}

        {state.kind === "error" && (
          <div
            role="alert"
            className="rounded-md border border-danger-600/30 bg-danger-50 px-6 py-4 text-danger-700"
          >
            <p className="text-body font-medium">{t("errorTitle")}</p>
            <p className="mt-1 text-body-sm">{t("errorBody")}</p>
          </div>
        )}
      </div>
    </section>
  );
}
