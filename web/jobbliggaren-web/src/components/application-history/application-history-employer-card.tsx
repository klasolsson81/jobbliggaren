import { useId } from "react";
import { useFormatter, useTranslations } from "next-intl";
import { ShieldAlert } from "lucide-react";
import { formatDate } from "@/lib/i18n/format";
import { formatOrgNr } from "@/lib/company-follows/org-nr";
import { applicationStatusLabel, getStatusPillClass } from "@/lib/applications/status";
import { applicationStatusSchema } from "@/lib/dto/applications";
import type { EmployerApplicationHistory } from "@/lib/dto/application-history";

interface ApplicationHistoryEmployerCardProps {
  item: EmployerApplicationHistory;
}

/**
 * #311 #448 (ADR 0087 D2/D8(c); DPIA #456 / ADR 0090 D1/D2) — one employer group in the `/foretag`
 * "Ansökningshistorik" section. Mirrors `CompanyWatchRow`'s org.nr contract: the employer is identified
 * by `companyName` (public Platsbanken data, server-resolved); a legal-entity org.nr renders only when
 * the backend supplied it (`!isProtectedIdentity && organizationNumber`), while a personnummer-shaped
 * sole-prop org.nr arrives masked (`organizationNumber` null, `isProtectedIdentity` true) and is shown
 * as a "Skyddad identitet" note — never a raw number. The FE adds NO shape-heuristic; the backend
 * (ADR 0087 D8(c)) is the single authoritative guard, pinned by `OrganizationNumberSurfacingGuardTests`.
 *
 * <para>`applicationCount` is the per-employer historik-räknare (#444's projection). The entries live in
 * a native `<details>` (kept collapsed so a long history does not dominate the page — no JS). Each entry
 * shows only WHEN the user applied + the application's current status (ADR 0090 D2 R-A4 firewall: no
 * application id, no JobAdId, no title, no link to the individual application). Status resolves through
 * the shared `applications.enums.status` SPOT + pill (parity /ansokningar); an unknown status token
 * (deploy-skew) falls back to the raw string rather than throwing.</para>
 */
export function ApplicationHistoryEmployerCard({
  item,
}: ApplicationHistoryEmployerCardProps) {
  const t = useTranslations("jobads.applicationHistory");
  const tStatus = useTranslations("applications.enums");
  const format = useFormatter();
  const hintId = useId();

  const displayName = item.companyName ?? t("unknownCompany");

  return (
    <li>
      <article className="jp-job" style={{ gridTemplateColumns: "1fr" }}>
        <div className="jp-job__body">
          <h3 className="jp-job__title">{displayName}</h3>
          {/* Per-employer historik-räknare (#444). High-contrast primary ink + tabular-nums, parity the
              sibling counters on this page (CompanyWatchRow). A count, never a score/percentage. */}
          <p className="jp-matchline tabular-nums">
            {t("applicationCount", { count: item.applicationCount })}
          </p>
          <div className="jp-job__meta">
            {item.isProtectedIdentity ? (
              <>
                <span aria-describedby={hintId}>
                  <ShieldAlert size={14} aria-hidden="true" /> {t("protectedIdentity")}
                </span>
                <span id={hintId} className="sr-only">
                  {t("protectedIdentityHint")}
                </span>
              </>
            ) : (
              item.organizationNumber && (
                <span>{t("orgNr", { orgNr: formatOrgNr(item.organizationNumber) })}</span>
              )
            )}
          </div>
          {item.applications.length > 0 && (
            <details className="mt-2">
              <summary className="cursor-pointer text-body-sm font-medium">
                {t("showApplications", { count: item.applications.length })}
              </summary>
              <ul className="mt-2 flex flex-col gap-2">
                {item.applications.map((entry, index) => {
                  const parsed = applicationStatusSchema.safeParse(entry.statusName);
                  const appliedAt = formatDate(format, entry.appliedAt) ?? entry.appliedAt;
                  return (
                    <li
                      // Composite key (parity the list-level group key) — the entries carry no id
                      // (ADR 0090 R-A4), so compose stable fields + index rather than a bare index.
                      key={`${entry.appliedAt}-${entry.statusName}-${index}`}
                      className="flex items-center gap-3 text-body-sm"
                    >
                      <span className="tabular-nums">
                        {t("appliedAt", { date: appliedAt })}
                      </span>
                      {parsed.success ? (
                        <span className={getStatusPillClass(parsed.data)}>
                          {applicationStatusLabel(tStatus, parsed.data)}
                        </span>
                      ) : (
                        <span>{entry.statusName}</span>
                      )}
                    </li>
                  );
                })}
              </ul>
            </details>
          )}
        </div>
      </article>
    </li>
  );
}
