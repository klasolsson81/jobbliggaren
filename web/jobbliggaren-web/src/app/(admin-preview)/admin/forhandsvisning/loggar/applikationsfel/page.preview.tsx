import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { ADMIN_PREVIEW_ROUTE } from "@/lib/admin-preview/gate.cjs";
import { requireAdminPreview } from "@/lib/admin-preview/runtime-gate";
import { PREVIEW_ERROR_LOG, PREVIEW_IMPORT_LOG, PREVIEW_SECURITY_LOG } from "@/lib/admin-preview/fixtures";
import { PreviewLogs } from "../../_preview/preview-pages.preview";

export const dynamic = "force-dynamic";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("admin-preview.meta");
  return { title: t("logsErrors"), robots: { index: false, follow: false } };
}

export default function AdminPreviewErrorLogsPage() {
  requireAdminPreview();
  return (
    <PreviewLogs
      view="errors"
      basePath={ADMIN_PREVIEW_ROUTE}
      security={PREVIEW_SECURITY_LOG}
      errors={PREVIEW_ERROR_LOG}
      imports={PREVIEW_IMPORT_LOG}
    />
  );
}
