import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { AdminLogsView } from "@/components/admin/admin-logs-view";
import { ADMIN_PREVIEW_ROUTE } from "@/lib/admin-preview/gate.cjs";
import { requireAdminPreview } from "@/lib/admin-preview/runtime-gate";

export const dynamic = "force-dynamic";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("admin-preview.meta");
  return { title: t("logsImports"), robots: { index: false, follow: false } };
}

export default function AdminPreviewImportLogsPage() {
  requireAdminPreview();
  return <AdminLogsView view="imports" basePath={ADMIN_PREVIEW_ROUTE} />;
}
