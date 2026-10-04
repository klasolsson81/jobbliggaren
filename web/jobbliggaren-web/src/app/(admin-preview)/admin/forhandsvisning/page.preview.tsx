import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { ADMIN_PREVIEW_ROUTE } from "@/lib/admin-preview/gate.cjs";
import { requireAdminPreview } from "@/lib/admin-preview/runtime-gate";
import { PREVIEW_OVERVIEW, PREVIEW_OVERVIEW_ZERO } from "@/lib/admin-preview/fixtures";
import { PreviewOverview } from "./_preview/preview-pages.preview";

export const dynamic = "force-dynamic";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("admin-preview.meta");
  return { title: t("overview"), robots: { index: false, follow: false } };
}

export default function AdminPreviewOverviewPage() {
  requireAdminPreview();
  return <PreviewOverview data={PREVIEW_OVERVIEW} zero={PREVIEW_OVERVIEW_ZERO} basePath={ADMIN_PREVIEW_ROUTE} />;
}
