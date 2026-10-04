import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { AdminPageHeader } from "@/components/admin/admin-page-header";
import { requireAdminPreview } from "@/lib/admin-preview/runtime-gate";
import { PREVIEW_FEEDBACK } from "@/lib/admin-preview/fixtures";
import { PreviewFeedback } from "../_preview/preview-pages.preview";

export const dynamic = "force-dynamic";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("admin-preview.meta");
  return { title: t("feedback"), robots: { index: false, follow: false } };
}

export default async function AdminPreviewFeedbackPage() {
  requireAdminPreview();
  const t = await getTranslations("admin.feedback");
  return (
    <div className="flex flex-col gap-6">
      <AdminPageHeader title={t("heading")} />
      <PreviewFeedback items={PREVIEW_FEEDBACK} />
    </div>
  );
}
