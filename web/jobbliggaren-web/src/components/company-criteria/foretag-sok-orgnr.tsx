"use client";

import { useId, useRef, useState } from "react";
import { useTranslations } from "next-intl";
import { ShieldAlert } from "lucide-react";
import {
  isPersonnummerShapedOrgNr,
  normalizeOrgNrInput,
} from "@/lib/dto/company-registry";
import {
  companyBrowseSchema,
  type CompanyBrowse,
} from "@/lib/dto/company-criteria";
import { formatOrgNr } from "@/lib/company-follows/org-nr";

/**
 * #560 PR-B — the org.nr search island on `/foretag/sok`. The org.nr term must never enter a browser
 * URL (ADR 0087 D8(c): a sole-prop org.nr can equal a personnummer, and URLs reach access logs +
 * history), so it lives HERE, in client state, and POSTs to the `/api/foretag/sok` BFF — separate from
 * the URL-driven name/SNI/kommun search. An org.nr search is therefore deliberately not shareable.
 *
 * Exact lookup (10 digits → 0 or 1 company). A plain labelled form (no typeahead, no debounce; the
 * LABEL carries the instruction, never a placeholder — Klas hard rule). A personnummer-shaped value
 * renders the refuse state LOCALLY and is never POSTed anywhere (data minimisation; the backend remains
 * the enforcing authority). The result card is compact and reference-free (rendered from the DTO's own
 * fields) so the ~100 kB SCB reference is not shipped to this island.
 */

// The 0/1 result the BFF returns: the single CompanyBrowseDto, or null when the register has no match.
const resultSchema = companyBrowseSchema.nullable();

type OrgNrState =
  | { kind: "idle" }
  | { kind: "pending" }
  | { kind: "refused" }
  | { kind: "rateLimited"; seconds: number }
  | { kind: "error" }
  | { kind: "found"; company: CompanyBrowse }
  | { kind: "notFound" };

export function ForetagSokOrgnr() {
  const t = useTranslations("pages.foretag.sok");
  const inputId = useId();
  const hintId = useId();
  const abortRef = useRef<AbortController | null>(null);
  const resultRef = useRef<HTMLDivElement>(null);

  const [value, setValue] = useState("");
  const [state, setState] = useState<OrgNrState>({ kind: "idle" });

  const normalized = normalizeOrgNrInput(value);

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!normalized || state.kind === "pending") return;

    // Refuse a personnummer-shaped value LOCALLY, before any transmission (the value never leaves the
    // browser — not even to our own BFF).
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
      const res = await fetch("/api/foretag/sok", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ organizationNumber: normalized }),
        signal: controller.signal,
      });

      if (res.status === 429) {
        const retryAfter = Number.parseInt(res.headers.get("Retry-After") ?? "60", 10);
        setState({
          kind: "rateLimited",
          seconds: Number.isFinite(retryAfter) ? retryAfter : 60,
        });
      } else if (res.ok) {
        const parsed = resultSchema.safeParse(await res.json());
        if (!parsed.success) setState({ kind: "error" });
        else if (parsed.data === null) setState({ kind: "notFound" });
        else setState({ kind: "found", company: parsed.data });
      } else {
        setState({ kind: "error" });
      }
    } catch {
      if (controller.signal.aborted) return; // superseded by a newer submit
      setState({ kind: "error" });
    }
    resultRef.current?.focus();
  }

  return (
    <section aria-labelledby={`${inputId}-heading`} className="mt-8">
      <h2
        id={`${inputId}-heading`}
        className="text-h3 font-semibold text-text-primary"
      >
        {t("orgNrHeading")}
      </h2>
      <p className="jp-hint mt-1">{t("orgNrLede")}</p>

      <form onSubmit={onSubmit} className="mt-3 flex max-w-xl items-end gap-2">
        <div className="jp-field grow">
          <label htmlFor={inputId} className="jp-label">
            {t("orgNrLabel")}
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
            {t("orgNrHint")}
          </span>
        </div>
        <button
          type="submit"
          className="jp-btn jp-btn--primary"
          disabled={!normalized}
          aria-busy={state.kind === "pending" || undefined}
        >
          {t("orgNrSubmit")}
        </button>
      </form>

      {/* The answer lands here: programmatic focus after a submit (reachable without hunting) and a
          polite live-region so the found card (which carries no role=status) is announced. A nested
          role=alert on error/rateLimited overrides the politeness. */}
      <div
        ref={resultRef}
        tabIndex={-1}
        aria-live="polite"
        className="mt-4 max-w-xl outline-none"
      >
        {state.kind === "found" && (
          <article className="rounded-md border border-border px-6 py-4">
            <h3 className="text-h4 font-semibold text-text-primary">
              {state.company.name}
            </h3>
            <div className="mt-1 flex flex-wrap gap-x-4 gap-y-1 text-body-sm text-text-primary">
              {/* org.nr renders only for an unmasked legal entity (D8(c)); a refused pnr-shaped value
                  never reaches here, so this is defense-in-depth. */}
              {state.company.isProtectedIdentity ? (
                <span className="inline-flex items-center gap-1 text-warning-700">
                  <ShieldAlert size={13} aria-hidden="true" />
                  {t("orgNrProtected")}
                </span>
              ) : (
                state.company.organizationNumber && (
                  <span className="font-mono">
                    {t("orgNrCardOrgNr", {
                      orgNr: formatOrgNr(state.company.organizationNumber),
                    })}
                  </span>
                )
              )}
              <span>
                {state.company.seatMunicipalityName ??
                  state.company.seatMunicipalityCode}{" "}
                <span className="font-mono text-text-secondary">
                  ({state.company.seatMunicipalityCode})
                </span>
              </span>
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
    </section>
  );
}
