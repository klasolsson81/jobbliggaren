import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { requireAdminPreview } from "@/lib/admin-preview/runtime-gate";
import AdminEmailDeliveryPage from "@/app/(admin)/admin/e-post/page";

export const dynamic = "force-dynamic";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("admin-preview.meta");
  return { title: t("email"), robots: { index: false, follow: false } };
}

/** The page as it ships today; it reads no backend and links nowhere. */
export default function AdminPreviewEmailPage() {
  requireAdminPreview();
  return <AdminEmailDeliveryPage />;
}
