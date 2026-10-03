import { useFormatter, useTranslations } from "next-intl";
import { Calendar, Clock } from "lucide-react";
import { jobAdStatusLabel } from "@/lib/job-ads/status";
import { formatDate } from "@/lib/i18n/format";
import type { JobAdDetailDto } from "@/lib/dto/job-ads";

/**
 * The job-ad header's date line (#1963, ADR 0053 Amendment 2026-10-03): "Arkiverad" leads an
 * archived ad, then the application deadline, then the publishing date. Rendered under the company
 * in the modal's header and in the full page's own header.
 */
export function JobAdDetailMeta({
  jobAd,
}: {
  jobAd: Pick<JobAdDetailDto, "status" | "publishedAt" | "expiresAt">;
}) {
  const tEnums = useTranslations("jobads.enums");
  const t = useTranslations("jobads.ui.detail");
  const format = useFormatter();
  const deadline = formatDate(format, jobAd.expiresAt);
  const published = formatDate(format, jobAd.publishedAt);

  return (
    <div className="jp-modal__meta">
      {jobAd.status === "Archived" && (
        <span className="jp-pill jp-pill--neutral">
          <span className="jp-pill__dot" aria-hidden="true" />
          {jobAdStatusLabel(tEnums, jobAd.status)}
        </span>
      )}
      {deadline && (
        <span className="jp-modal__date">
          <Calendar size={14} className="jp-modal__date-icon" aria-hidden="true" />
          <span>
            {t("lastApplicationDay")}{" "}
            <b className="jp-modal__date-value jp-modal__date-value--deadline">{deadline}</b>
          </span>
        </span>
      )}
      {published && (
        <span className="jp-modal__date">
          <Clock size={14} className="jp-modal__date-icon" aria-hidden="true" />
          <span>
            {t("published")} <b className="jp-modal__date-value">{published}</b>
          </span>
        </span>
      )}
    </div>
  );
}
