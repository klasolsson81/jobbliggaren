"use client";

import { useId, useState, useTransition } from "react";
import type { CSSProperties } from "react";
import Link from "next/link";
import { useFormatter, useTranslations } from "next-intl";
import { ShieldAlert, Trash2 } from "lucide-react";
import { formatDate } from "@/lib/i18n/format";
import { unfollowCompanyAction } from "@/lib/actions/company-follows";
import type { CompanyWatch } from "@/lib/dto/company-follows";

/**
 * #452 — which per-company count the hub toggle is emphasising. `matching` (default) leads with the
 * "X matchande annonser" signal (or the not-assessed nudge); `all` leads with the public active-ad
 * count. The active-ad count is always kept as a secondary fact regardless of mode (#447/#448).
 */
export type CompanyWatchViewMode = "matching" | "all";

// #452 — the canonical route the "set up matching" nudge links to, kept as a repeated inline literal
// per the established pattern (JobAdMatchSection, oversikt-page, jobb-results-toolbar all inline it).
const MATCH_SETTINGS_HREF = "/installningar#matchning";

// The primary per-company matching line sits between the title and the meta row. Token-styled inline
// (no new globals.css rule): primary ink for high contrast (never gray, per design), sized/weighted
// like `.jp-job__company`. Both themes resolve via the same `--jp-*` tokens.
const MATCH_LINE_STYLE: CSSProperties = {
  margin: "6px 0 0",
  fontSize: 15,
  fontWeight: 500,
  color: "var(--jp-ink-1)",
};

// Nudge link — mirrors the JobAdMatchSection not-assessed signpost link style (SPOT).
const NUDGE_LINK_STYLE: CSSProperties = {
  color: "var(--jp-accent-700)",
  fontWeight: 600,
  textDecoration: "underline",
};

interface CompanyWatchRowProps {
  item: CompanyWatch;
  mode: CompanyWatchViewMode;
}

/**
 * Formats a 10-digit legal-entity org.nr as NNNNNN-NNNN. Only ever called with a non-null
 * `organizationNumber`, which the backend guarantees is NOT personnummer-shaped (a sole-prop org.nr
 * arrives masked to null, ADR 0087 D8(c)). Any other length is shown verbatim rather than mis-split.
 */
function formatOrgNr(orgNr: string): string {
  return orgNr.length === 10 ? `${orgNr.slice(0, 6)}-${orgNr.slice(6)}` : orgNr;
}

/**
 * #311 #448 (ADR 0087 D2/D8(c)) — one followed-company row on `/foretag`. Identifies the employer by
 * `companyName` (public Platsbanken data resolved server-side). org.nr is rendered ONLY when the
 * backend supplied it (`!isProtectedIdentity && organizationNumber` — a legal-entity number); a
 * personnummer-shaped org.nr arrives masked (`organizationNumber` null, `isProtectedIdentity` true)
 * and is shown as a "skyddad identitet" note, never a raw number. `activeAdCount` is public open-role
 * data (#447), surfaced even when the org.nr is masked.
 *
 * <para>#452 — the primary per-company signal follows `mode`: in `matching` mode the row leads with the
 * "X matchande annonser" count (ads of this employer matching the user's profile at grade >= Good),
 * or an honest not-assessed nudge when `matchingAdCount` is null (the user stated no occupation — never
 * a false "0", parity /jobb + /matchningar). In `all` mode that line is hidden. The public
 * "X aktiva annonser just nu" count (#447) is always kept as a secondary fact. The matching count is a
 * count of ADS over a named grade threshold — never rendered as a score, percentage, or meter
 * (Goodhart, ADR 0071).</para>
 *
 * <para>Unfollow: server action + `revalidatePath` drives the row removal (CTO Q4 2026-07-01 — server
 * state over a client-side optimistic copy, §5). `useTransition` covers the DELETE latency
 * (`aria-busy`); parity with FollowCompanyToggle the button is never `disabled` (the backend is
 * idempotent, so a mis-click is recoverable). On failure the row stays and shows the error inline.</para>
 */
export function CompanyWatchRow({ item, mode }: CompanyWatchRowProps) {
  const t = useTranslations("jobads.companyWatches");
  const format = useFormatter();
  const [error, setError] = useState<string | null>(null);
  const [isPending, startTransition] = useTransition();
  const hintId = useId();

  const displayName = item.companyName ?? t("unknownCompany");
  const followedSince = formatDate(format, item.followedAt);

  function handleUnfollow() {
    setError(null);
    startTransition(async () => {
      const result = await unfollowCompanyAction(item.id);
      // On success `revalidatePath("/foretag")` re-renders the RSC list without this row.
      if (!result.success) setError(result.error);
    });
  }

  return (
    <li>
      <article className="jp-job" style={{ gridTemplateColumns: "1fr auto" }}>
        <div className="jp-job__body">
          <h3 className="jp-job__title">{displayName}</h3>
          {mode === "matching" &&
            (item.matchingAdCount === null ? (
              // Honest not-assessed: the user stated no occupation, so matching is undefined. Render a
              // civic nudge to state occupations, never a false "0" (parity /jobb + /matchningar). Copy
              // + link style mirror the JobAdMatchSection not-assessed signpost (SPOT, no drift).
              <p style={MATCH_LINE_STYLE}>
                {t("matchNudge")}{" "}
                <Link href={MATCH_SETTINGS_HREF} style={NUDGE_LINK_STYLE}>
                  {t("matchNudgeCta")}
                </Link>
              </p>
            ) : (
              // A count of ADS over a named grade threshold (>= Good) — high-contrast primary ink,
              // tabular-nums for stable digits (#448), NEVER a score/percentage/meter (ADR 0071).
              <p className="tabular-nums" style={MATCH_LINE_STYLE}>
                {t("matchingAds", { count: item.matchingAdCount })}
              </p>
            ))}
          <div className="jp-job__meta">
            {item.isProtectedIdentity ? (
              <>
                <span aria-describedby={hintId}>
                  <ShieldAlert size={14} aria-hidden="true" /> {t("protectedIdentity")}
                </span>
                {/* The reason the org.nr is hidden, reachable by screen readers
                    (a non-focusable `title` is not) — keeps the meta visually compact. */}
                <span id={hintId} className="sr-only">
                  {t("protectedIdentityHint")}
                </span>
              </>
            ) : (
              // Backend (ADR 0087 D8(c)) is the SINGLE authoritative personnummer guard: a
              // personnummer-shaped sole-prop org.nr arrives as organizationNumber=null +
              // isProtectedIdentity=true, so this branch only ever sees a legal-entity number. The FE
              // renders on that contract and adds NO shape-heuristic of its own — D8 rejected a
              // FE-layer heuristic (imperfect, wrong layer); the regression tripwire is the build-time
              // OrganizationNumberSurfacingGuardTests, not a runtime FE check (senior-cto-advisor 2026-07-01).
              item.organizationNumber && (
                <span>{t("orgNr", { orgNr: formatOrgNr(item.organizationNumber) })}</span>
              )
            )}
            <span className="tabular-nums">
              {t("activeAds", { count: item.activeAdCount })}
            </span>
            {followedSince && <span>{t("followedSince", { date: followedSince })}</span>}
          </div>
          {error && (
            <p role="alert" className="mt-2 text-body-sm text-danger-700">
              {error}
            </p>
          )}
        </div>
        <div
          className="jp-job__actions"
          style={{ flexDirection: "row", alignItems: "center" }}
        >
          <button
            type="button"
            className="jp-icon-btn"
            aria-label={t("unfollowAria", { company: displayName })}
            aria-busy={isPending || undefined}
            onClick={handleUnfollow}
            style={isPending ? { opacity: 0.6 } : undefined}
          >
            <Trash2 size={16} aria-hidden="true" />
          </button>
        </div>
      </article>
    </li>
  );
}
