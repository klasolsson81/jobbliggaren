import { useTranslations } from "next-intl";
import { formatDateTime, type JpFormatter } from "@/lib/i18n/format";
import { JobStateBadge } from "./job-state-badge";
import type { RecurringJobStatusDto } from "@/lib/dto/admin";

interface RecurringJobsTableProps {
  jobs: ReadonlyArray<RecurringJobStatusDto>;
  format: JpFormatter;
}

/**
 * Civic table of recurring (scheduled) Hangfire jobs (TD-83 / #204, read-side).
 * Pure non-async RSC: a synchronous next-intl translator + a formatter passed
 * in by the page (acquired via `await getFormatter()`). Surfaces ONLY the five
 * DTO fields — no detail view, tooltip, or field that could carry job args.
 *
 * A null `lastExecution` renders "Aldrig körd"; `cron` is shown in monospace so
 * schedules align for column comparison. `nextExecution` is always present per
 * the backend contract but is null-guarded defensively all the same.
 */
export function RecurringJobsTable({ jobs, format }: RecurringJobsTableProps) {
  const t = useTranslations("admin.jobb");

  if (jobs.length === 0) {
    return (
      <div
        className="border-y border-border-default px-1 py-12 text-center"
        role="status"
      >
        <p className="text-body text-text-primary">
          {t("recurring.empty.title")}
        </p>
        <p className="mt-1 text-body-sm text-text-secondary">
          {t("recurring.empty.body")}
        </p>
      </div>
    );
  }

  return (
    <div className="overflow-x-auto">
      <table
        className="jp-table w-full"
        aria-label={t("recurring.table.ariaLabel")}
      >
        <caption className="sr-only">{t("recurring.table.caption")}</caption>
        <thead>
          <tr>
            <th scope="col">{t("recurring.table.id")}</th>
            <th scope="col">{t("recurring.table.cron")}</th>
            <th scope="col">{t("recurring.table.lastExecution")}</th>
            <th scope="col">{t("recurring.table.status")}</th>
            <th scope="col">{t("recurring.table.nextExecution")}</th>
          </tr>
        </thead>
        <tbody>
          {jobs.map((job) => {
            const last = formatDateTime(format, job.lastExecution);
            const next = formatDateTime(format, job.nextExecution);
            return (
              <tr key={job.id} className="text-text-primary">
                <td className="whitespace-nowrap font-medium">{job.id}</td>
                <td className="whitespace-nowrap font-mono text-[13px] text-text-secondary">
                  {job.cron ?? (
                    <span className="text-text-tertiary">
                      {t("recurring.table.noCron")}
                    </span>
                  )}
                </td>
                <td className="whitespace-nowrap font-mono text-[13px] text-text-secondary">
                  {last ?? (
                    <span className="text-text-secondary">
                      {t("recurring.table.neverRun")}
                    </span>
                  )}
                </td>
                <td>
                  <JobStateBadge state={job.lastJobState} />
                </td>
                <td className="whitespace-nowrap font-mono text-[13px] text-text-secondary">
                  {next ?? <span className="text-text-tertiary">–</span>}
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
