import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { requireAdminPreview } from "@/lib/admin-preview/runtime-gate";
import AdminFeedbackPage from "@/app/(admin)/admin/feedback/page";

export const dynamic = "force-dynamic";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("admin-preview.meta");
  return { title: t("feedback"), robots: { index: false, follow: false } };
}

/** The page as it ships today; it reads no backend and links nowhere. */
export default function AdminPreviewFeedbackPage() {
  requireAdminPreview();
  return <AdminFeedbackPage />;
}
