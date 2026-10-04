import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { requireAdminPreview } from "@/lib/admin-preview/runtime-gate";
import { PREVIEW_EMAIL_DELIVERY, PREVIEW_EMAIL_DELIVERY_ZERO } from "@/lib/admin-preview/fixtures";
import { PreviewEmail } from "../_preview/preview-pages.preview";

export const dynamic = "force-dynamic";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("admin-preview.meta");
  return { title: t("email"), robots: { index: false, follow: false } };
}

export default function AdminPreviewEmailPage() {
  requireAdminPreview();
  return <PreviewEmail byPeriod={PREVIEW_EMAIL_DELIVERY} zero={PREVIEW_EMAIL_DELIVERY_ZERO} />;
}
