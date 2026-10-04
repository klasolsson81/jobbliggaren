import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { AdminFeedbackView } from "@/components/admin/admin-feedback-view";
import { AdminPageHeader } from "@/components/admin/admin-page-header";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("admin.feedback");
  return { title: t("meta.title") };
}

/**
 * `/admin/feedback` — reports from the app's feedback button, as a master/detail layout
 * (ADR 0150). The feedback function itself is #1979, so the region is unavailable here.
 */
export default async function AdminFeedbackPage() {
  const t = await getTranslations("admin.feedback");

  return (
    <div className="flex flex-col gap-6">
      <AdminPageHeader title={t("heading")} />
      <AdminFeedbackView region={{ kind: "unavailable" }} />
    </div>
  );
}
