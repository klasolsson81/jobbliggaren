import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { AdminOverview } from "@/components/admin/admin-overview";
import { ADMIN_PREVIEW_ROUTE } from "@/lib/admin-preview/gate.cjs";
import { requireAdminPreview } from "@/lib/admin-preview/runtime-gate";

export const dynamic = "force-dynamic";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("admin-preview.meta");
  return { title: t("overview"), robots: { index: false, follow: false } };
}

export default function AdminPreviewOverviewPage() {
  requireAdminPreview();
  return <AdminOverview basePath={ADMIN_PREVIEW_ROUTE} />;
}
