import type { Metadata } from "next";
import { getFormatter, getTranslations } from "next-intl/server";
import { getFailedJobs, getRecurringJobs } from "@/lib/api/admin";
import { assertNever, type ApiResult } from "@/lib/dto/_helpers";
import { RecurringJobsTable } from "./recurring-jobs-table";
import { FailedJobsTable } from "./failed-jobs-table";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("admin.jobb");
  return { title: t("meta.title") };
}

/**
 * Admin operator page for the Hangfire background jobs (TD-83 / issue #204,
 * PR1 read-side — NO trigger/retry mutations; those land in PR2). Variant B:
 * a custom civic page, not the built-in Hangfire dashboard (CTO-bound,
 * security-approved). Server Component: both endpoints are fetched in parallel
 * and rendered server-side; the admin layout already gates on the Admin role.
 *
 * Each section degrades independently: a failed recurring fetch shows an
 * ErrorBlock there while the failed-jobs section may still render, and vice
 * versa. ApiResult handling is exhaustive per section (ADR 0030).
 */
export default async function JobbPage() {
  const t = await getTranslations("admin.jobb");
  const format = await getFormatter();

  const [recurring, failed] = await Promise.all([
    getRecurringJobs(),
    getFailedJobs(),
  ]);

  return (
    <div className="flex flex-col gap-10">
      <div>
        <h1 className="jp-h1">{t("heading")}</h1>
        <p className="jp-lede">{t("lede")}</p>
      </div>

      <section className="flex flex-col gap-4" aria-labelledby="recurring-heading">
        <h2 id="recurring-heading" className="jp-h2">
          {t("recurring.heading")}
        </h2>
        {recurring.kind === "ok" ? (
          <RecurringJobsTable jobs={recurring.data} format={format} />
        ) : (
          <SectionError result={recurring} t={t} />
        )}
      </section>

      <section className="flex flex-col gap-4" aria-labelledby="failed-heading">
        <h2 id="failed-heading" className="jp-h2">
          {t("failed.heading")}
        </h2>
        {failed.kind === "ok" ? (
          <FailedJobsTable data={failed.data} format={format} />
        ) : (
          <SectionError result={failed} t={t} />
        )}
      </section>
    </div>
  );
}

// Narrowed translator signature (mirrors lib/job-ads/status): only the error
// keys this helper uses, with the optional ICU `seconds` value for rateLimited.
// Passing the full `getTranslations` return type here triggers a TS2589
// "excessively deep" instantiation against the typed message catalog.
type ErrorTranslator = (
  key:
    | "errors.rateLimited.title"
    | "errors.rateLimited.body"
    | "errors.forbidden.title"
    | "errors.forbidden.body"
    | "errors.unauthorized.title"
    | "errors.unauthorized.body"
    | "errors.error.title"
    | "errors.error.body",
  values?: { seconds: number },
) => string;

/**
 * Exhaustive ApiResult → ErrorBlock mapping shared by both sections. `notFound`
 * is unreachable at runtime for these endpoints (neither sets includeNotFound),
 * but the discriminated union requires the case for exhaustiveness (ADR 0030).
 */
function SectionError({
  result,
  t,
}: {
  result: Exclude<ApiResult<unknown>, { kind: "ok" }>;
  t: ErrorTranslator;
}) {
  switch (result.kind) {
    case "rateLimited":
      return (
        <ErrorBlock
          title={t("errors.rateLimited.title")}
          body={t("errors.rateLimited.body", {
            seconds: result.retryAfterSeconds,
          })}
        />
      );
    case "forbidden":
      return (
        <ErrorBlock
          title={t("errors.forbidden.title")}
          body={t("errors.forbidden.body")}
        />
      );
    case "unauthorized":
      return (
        <ErrorBlock
          title={t("errors.unauthorized.title")}
          body={t("errors.unauthorized.body")}
        />
      );
    case "notFound":
    case "error":
      return (
        <ErrorBlock
          title={t("errors.error.title")}
          body={t("errors.error.body")}
        />
      );
    default:
      return assertNever(result);
  }
}

function ErrorBlock({ title, body }: { title: string; body: string }) {
  return (
    <div className="rounded-md border border-danger-600/30 bg-danger-50 px-6 py-4 text-danger-700">
      <p className="text-body font-medium">{title}</p>
      <p className="mt-1 text-body-sm">{body}</p>
    </div>
  );
}
