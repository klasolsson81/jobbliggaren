"use client";

import { useState, useTransition } from "react";
import { useFormatter, useTranslations } from "next-intl";
import { ShieldAlert, Trash2 } from "lucide-react";
import { formatDate } from "@/lib/i18n/format";
import { unfollowCompanyAction } from "@/lib/actions/company-follows";
import type { CompanyWatch } from "@/lib/dto/company-follows";

interface CompanyWatchRowProps {
  item: CompanyWatch;
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
 * <para>Unfollow: server action + `revalidatePath` drives the row removal (CTO Q4 2026-07-01 — server
 * state over a client-side optimistic copy, §5). `useTransition` covers the DELETE latency
 * (`aria-busy`); parity with FollowCompanyToggle the button is never `disabled` (the backend is
 * idempotent, so a mis-click is recoverable). On failure the row stays and shows the error inline.</para>
 */
export function CompanyWatchRow({ item }: CompanyWatchRowProps) {
  const t = useTranslations("jobads.companyWatches");
  const format = useFormatter();
  const [error, setError] = useState<string | null>(null);
  const [isPending, startTransition] = useTransition();

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
      <article
        className="jp-job"
        style={{ gridTemplateColumns: "1fr auto" }}
        aria-busy={isPending || undefined}
      >
        <div className="jp-job__body">
          <h3 className="jp-job__title">{displayName}</h3>
          <div className="jp-job__meta">
            {item.isProtectedIdentity ? (
              <span title={t("protectedIdentityHint")}>
                <ShieldAlert size={14} aria-hidden="true" /> {t("protectedIdentity")}
              </span>
            ) : (
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
