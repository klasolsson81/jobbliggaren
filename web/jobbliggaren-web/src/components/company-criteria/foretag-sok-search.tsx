"use client";

import { useId, useRef, useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { ShieldAlert } from "lucide-react";
import {
  isPersonnummerShapedOrgNr,
  normalizeOrgNrInput,
} from "@/lib/dto/company-registry";
import {
  orgNrSearchResultSchema,
  type OrgNrSearchResult,
} from "@/lib/dto/company-search";
import { buildForetagSokHref } from "@/lib/company-search/search-params";
import { formatOrgNr } from "@/lib/company-follows/org-nr";
import { CompanyFollowButton } from "@/components/company-follows/company-follow-button";

/**
 * #997 (S2) — the ONE unified `/foretag/sok` search field: a company NAME or an organisationsnummer,
 * "the form decides". It replaces the two former inputs (the name field in `ForetagSokFilters` + the
 * separate `ForetagSokOrgnr` island).
 *
 * Dispatch (pnr-guard BEFORE either branch — the security invariant the merge introduces):
 * - a value that normalises to 10 digits → the ORG.NR branch. A personnummer-shaped value renders the
 *   refuse state LOCALLY and is never POSTed anywhere (data minimisation; the backend remains the
 *   enforcing authority). Otherwise it POSTs to `/api/foretag/sok` and renders the 0/1 register hit in
 *   client state — the org.nr term NEVER enters the URL (ADR 0087 D8(c): a sole-prop org.nr can equal a
 *   personnummer, and query strings reach access logs + history).
 * - anything else → the NAME branch: `router.push(buildForetagSokHref({ namn, … }))` seeds the
 *   URL-driven, shareable prefix search (`ForetagSokResults` streams; per-row follow already works). The
 *   current SNI/kommun axes are carried through so a name search never erases an active filter.
 *
 * A pnr-shaped string can therefore NEVER be routed to `?namn=` (that would put a personnummer in an
 * access log): only a non-10-digit value reaches the name branch. No-JS degrades to a native GET name
 * search (the org.nr branch requires JS, as before — a client POST cannot be a `<form method=get>`).
 *
 * A plain labelled field (Klas hard rule: the LABEL carries the instruction, never a placeholder). The
 * org.nr result is compact + reference-free (rendered from the DTO's own fields) so the ~100 kB SCB
 * reference is not shipped here; it carries a Bevaka affordance via the shared `CompanyFollowButton`
 * (#997 caveat: preserve follow-via-org.nr, which reaches 0-ad companies).
 */

type OrgNrState =
  | { kind: "idle" }
  | { kind: "pending" }
  | { kind: "refused" }
  | { kind: "rateLimited"; seconds: number }
  | { kind: "error" }
  | { kind: "found"; result: NonNullable<OrgNrSearchResult> }
  | { kind: "notFound" };

interface ForetagSokSearchProps {
  /** The active name prefix parsed from the URL — seeds the field so a shared/bookmarked search shows it. */
  readonly namn: string;
  /** The active SNI/kommun axes — carried through a name-branch navigation so it never erases a filter. */
  readonly sni: ReadonlyArray<string>;
  readonly kommun: ReadonlyArray<string>;
}

export function ForetagSokSearch({ namn, sni, kommun }: ForetagSokSearchProps) {
  const t = useTranslations("pages.foretag.sok");
  const router = useRouter();
  const inputId = useId();
  const hintId = useId();
  const abortRef = useRef<AbortController | null>(null);
  const resultRef = useRef<HTMLDivElement>(null);

  const [value, setValue] = useState(namn);
  const [state, setState] = useState<OrgNrState>({ kind: "idle" });
  const [isNavPending, startNavTransition] = useTransition();

  async function onOrgNrSubmit(orgNr: string) {
    // Refuse a personnummer-shaped value LOCALLY, before any transmission (the value never leaves the
    // browser — not even to our own BFF).
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
      // org.nr branch (10 digits) — client POST, never the URL.
      void onOrgNrSubmit(orgNr);
      return;
    }

    // name branch — clear any org.nr result and commit the shareable prefix search to the URL, carrying
    // the current SNI/kommun so the search does not erase an active filter.
    abortRef.current?.abort();
    setState({ kind: "idle" });
    startNavTransition(() => {
      router.push(
        buildForetagSokHref({ namn: value.trim(), sni: [...sni], kommun: [...kommun] }),
      );
    });
  }

  return (
    <div className="mt-2">
      {/* No-JS fallback: a native GET to /foretag/sok as a NAME search (?namn=…), with the current
          SNI/kommun preserved via hidden inputs. With JS, onSubmit intercepts and dispatches
          name-vs-org.nr. The org.nr branch requires JS (a client POST — the term must never be a GET
          query per D8(c)); a no-JS submit is always treated as a name search. */}
      <form
        action="/foretag/sok"
        method="get"
        onSubmit={onSubmit}
        className="flex max-w-2xl items-end gap-2"
      >
        <div className="jp-field grow">
          <label htmlFor={inputId} className="jp-label">
            {t("searchLabel")}
          </label>
          <input
            id={inputId}
            name="namn"
            className="jp-input"
            type="text"
            autoComplete="off"
            aria-describedby={hintId}
            value={value}
            onChange={(e) => setValue(e.target.value)}
          />
          <span id={hintId} className="jp-hint">
            {t("searchHint")}
          </span>
        </div>
        <button
          type="submit"
          className="jp-btn jp-btn--primary"
          aria-busy={state.kind === "pending" || isNavPending || undefined}
        >
          {t("searchSubmit")}
        </button>

        {/* Preserve the current code axes for the no-JS name-search submit (ignored when JS handles it). */}
        {sni.map((code) => (
          <input key={`sni-${code}`} type="hidden" name="sni" value={code} />
        ))}
        {kommun.map((code) => (
          <input key={`kommun-${code}`} type="hidden" name="kommun" value={code} />
        ))}
      </form>

      {/* The org.nr answer lands here: programmatic focus after a submit (reachable without hunting) and
          a polite live-region so the found card (which carries no role=status) is announced. A nested
          role=alert on error/rateLimited overrides the politeness. The name branch renders nothing here
          — its results stream below in ForetagSokResults. */}
      <div
        ref={resultRef}
        tabIndex={-1}
        aria-live="polite"
        className="mt-4 max-w-2xl outline-none"
      >
        {state.kind === "found" && (
          <article className="rounded-md border border-border px-6 py-4">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <h3 className="text-h4 font-semibold text-text-primary">
                  {state.result.company.name}
                </h3>
                <div className="mt-1 flex flex-wrap gap-x-4 gap-y-1 text-body-sm text-text-primary">
                  {/* org.nr renders only for an unmasked legal entity (D8(c)); a refused pnr-shaped
                      value never reaches here, so this is defense-in-depth. */}
                  {state.result.company.isProtectedIdentity ? (
                    <span className="inline-flex items-center gap-1 text-warning-700">
                      <ShieldAlert size={13} aria-hidden="true" />
                      {t("orgNrProtected")}
                    </span>
                  ) : (
                    state.result.company.organizationNumber && (
                      <span className="font-mono">
                        {t("orgNrCardOrgNr", {
                          orgNr: formatOrgNr(state.result.company.organizationNumber),
                        })}
                      </span>
                    )
                  )}
                  <span>
                    {state.result.company.seatMunicipalityName ??
                      state.result.company.seatMunicipalityCode}{" "}
                    <span className="font-mono text-text-secondary">
                      ({state.result.company.seatMunicipalityCode})
                    </span>
                  </span>
                </div>
              </div>

              {/* Bevaka — only an unmasked legal entity carries the org.nr follow key (D8(c)). Reuses the
                  same per-row toggle + follow-state the streamed results use (#997 caveat preserved). */}
              {state.result.company.organizationNumber &&
                !state.result.company.isProtectedIdentity && (
                  <CompanyFollowButton
                    orgNr={state.result.company.organizationNumber}
                    companyName={state.result.company.name}
                    initialCompanyWatchId={state.result.companyWatchId}
                  />
                )}
            </div>
          </article>
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
      </div>
    </div>
  );
}
