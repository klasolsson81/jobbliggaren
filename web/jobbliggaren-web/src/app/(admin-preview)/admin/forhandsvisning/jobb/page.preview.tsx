import type { Metadata } from "next";
import { getFormatter, getTranslations } from "next-intl/server";
import { FailedJobsTable } from "@/app/(admin)/admin/jobb/failed-jobs-table";
import { RecurringJobsTable } from "@/app/(admin)/admin/jobb/recurring-jobs-table";
import { requireAdminPreview } from "@/lib/admin-preview/runtime-gate";
import { PREVIEW_FAILED_JOBS, PREVIEW_RECURRING_JOBS } from "@/lib/admin-preview/fixtures";

export const dynamic = "force-dynamic";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("admin-preview.meta");
  return { title: t("jobs"), robots: { index: false, follow: false } };
}

/** Bakgrundsjobb's two tables over fixtures, so the preview reads no backend (ADR 0150 D5). */
export default async function AdminPreviewJobsPage() {
  requireAdminPreview();
  const t = await getTranslations("admin.jobb");
  const format = await getFormatter();

  return (
    <div className="flex flex-col gap-10">
      <div>
        <h1 className="jp-h1">{t("heading")}</h1>
      </div>
      <section className="flex flex-col gap-4" aria-labelledby="recurring-heading">
        <h2 id="recurring-heading" className="jp-h2">
          {t("recurring.heading")}
        </h2>
        <RecurringJobsTable jobs={PREVIEW_RECURRING_JOBS} format={format} />
      </section>
      <section className="flex flex-col gap-4" aria-labelledby="failed-heading">
        <h2 id="failed-heading" className="jp-h2">
          {t("failed.heading")}
        </h2>
        <FailedJobsTable data={PREVIEW_FAILED_JOBS} format={format} />
      </section>
    </div>
  );
}
