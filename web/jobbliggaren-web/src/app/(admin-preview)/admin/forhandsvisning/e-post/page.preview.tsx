import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { emailDeliveryPeriod } from "@/lib/admin/email-delivery";
import { ADMIN_PREVIEW_ROUTE } from "@/lib/admin-preview/gate.cjs";
import { requireAdminPreview } from "@/lib/admin-preview/runtime-gate";
import { PREVIEW_EMAIL_ALLOWANCE, PREVIEW_EMAIL_DELIVERY } from "@/lib/admin-preview/fixtures";
import { PreviewEmail } from "../_preview/preview-pages.preview";

export const dynamic = "force-dynamic";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("admin-preview.meta");
  return { title: t("email"), robots: { index: false, follow: false } };
}

/** The e-post page over fictional reads, its Leverans period read from the URL as the live page's is (ADR 0150 D5). */
export default async function AdminPreviewEmailPage({
  searchParams,
}: {
  searchParams: Promise<Readonly<Record<string, string | string[] | undefined>>>;
}) {
  requireAdminPreview();
  const period = emailDeliveryPeriod((await searchParams).period);
  return (
    <PreviewEmail
      allowances={PREVIEW_EMAIL_ALLOWANCE}
      deliveries={PREVIEW_EMAIL_DELIVERY}
      period={period}
      pagePath={`${ADMIN_PREVIEW_ROUTE}/e-post`}
    />
  );
}
