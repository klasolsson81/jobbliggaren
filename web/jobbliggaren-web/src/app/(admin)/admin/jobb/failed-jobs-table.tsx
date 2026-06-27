import { useTranslations } from "next-intl";
import { formatDateTime, type JpFormatter } from "@/lib/i18n/format";
import type { FailedJobsResponse } from "@/lib/dto/admin";

interface FailedJobsTableProps {
  data: FailedJobsResponse;
  format: JpFormatter;
}

/**
 * Civic table of failed Hangfire jobs (TD-83 / #204, read-side). Pure non-async
 * RSC. SECURITY: renders ONLY jobId / jobType / failedAt / errorCategory. There
 * is no detail view, tooltip, expand, or extra cell — `errorCategory` is a
 * PII-free exception type name, never a stack trace, message, or argument.
 *
 * `totalCount === 0` renders a calm civic empty-state (good news, no
 * celebration, no exclamation). When `totalCount > returned` the truncation is
 * surfaced honestly so a 50-row cap never reads as "no failures left".
 */
export function FailedJobsTable({ data, format }: FailedJobsTableProps) {
  const t = useTranslations("admin.jobb");

  if (data.totalCount === 0) {
    return (
      <div
        className="rounded-md border border-success-600/30 bg-success-50 px-6 py-4 text-success-700"
        role="status"
      >
        <p className="text-body font-medium">{t("failed.empty.title")}</p>
        <p className="mt-1 text-body-sm">{t("failed.empty.body")}</p>
      </div>
    );
  }

  const truncated = data.totalCount > data.returned;

  return (
    <div className="flex flex-col gap-3">
      <p className="text-body-sm text-text-secondary" role="status">
        {truncated
          ? t("failed.note.truncated", {
              returned: data.returned,
              totalCount: data.totalCount,
            })
          : t("failed.note.all", { totalCount: data.totalCount })}
      </p>
      <div className="overflow-x-auto">
        <table
          className="jp-table w-full"
          aria-label={t("failed.table.ariaLabel")}
        >
          <caption className="sr-only">{t("failed.table.caption")}</caption>
          <thead>
            <tr>
              <th scope="col">{t("failed.table.jobId")}</th>
              <th scope="col">{t("failed.table.jobType")}</th>
              <th scope="col">{t("failed.table.failedAt")}</th>
              <th scope="col">{t("failed.table.errorCategory")}</th>
            </tr>
          </thead>
          <tbody>
            {data.items.map((job) => {
              const failedAt = formatDateTime(format, job.failedAt);
              return (
                <tr key={job.jobId} className="text-text-primary">
                  <td className="whitespace-nowrap font-mono text-[13px] text-text-secondary">
                    {job.jobId}
                  </td>
                  <td>{job.jobType}</td>
                  <td className="whitespace-nowrap font-mono text-[13px] text-text-secondary">
                    {failedAt ?? <span className="text-text-tertiary">–</span>}
                  </td>
                  <td className="font-medium text-danger-700">
                    {job.errorCategory}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}
