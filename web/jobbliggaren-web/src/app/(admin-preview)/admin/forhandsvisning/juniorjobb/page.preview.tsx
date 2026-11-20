import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { parseJuniorJobsQuery, type JuniorJobsSearchParams } from "@/lib/admin/junior-jobs";
import { ADMIN_PREVIEW_ROUTE } from "@/lib/admin-preview/gate.cjs";
import { requireAdminPreview } from "@/lib/admin-preview/runtime-gate";
import { PREVIEW_JUNIOR_JOBS } from "@/lib/admin-preview/fixtures";
import { PreviewJuniorJobs } from "../_preview/preview-pages.preview";

export const dynamic = "force-dynamic";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("admin-preview.meta");
  return { title: t("juniorJobs"), robots: { index: false, follow: false } };
}

/** The junior queue over fictional ads, read from its URL as the live page is (ADR 0150 D5). */
export default async function AdminPreviewJuniorJobsPage({
  searchParams,
}: {
  searchParams: Promise<JuniorJobsSearchParams>;
}) {
  requireAdminPreview();
  const query = parseJuniorJobsQuery(await searchParams);
  return (
    <div className="flex flex-col gap-6">
      <PreviewJuniorJobs ads={PREVIEW_JUNIOR_JOBS} query={query} basePath={`${ADMIN_PREVIEW_ROUTE}/juniorjobb`} />
    </div>
  );
}
