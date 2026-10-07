import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { AdminPageHeader } from "@/components/admin/admin-page-header";
import { parseFeedbackQuery, type FeedbackSearchParams } from "@/lib/admin/feedback";
import { ADMIN_PREVIEW_ROUTE } from "@/lib/admin-preview/gate.cjs";
import { requireAdminPreview } from "@/lib/admin-preview/runtime-gate";
import {
  FIXTURE_NOW,
  PREVIEW_FEEDBACK,
  PREVIEW_FEEDBACK_AVAILABILITY,
  PREVIEW_FEEDBACK_SUMMARY,
} from "@/lib/admin-preview/fixtures";
import { PreviewFeedback } from "../_preview/preview-pages.preview";

export const dynamic = "force-dynamic";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("admin-preview.meta");
  return { title: t("feedback"), robots: { index: false, follow: false } };
}

/** The feedback page over fictional submissions, read from its URL as the live page is (ADR 0150 D5). */
export default async function AdminPreviewFeedbackPage({
  searchParams,
}: {
  searchParams: Promise<FeedbackSearchParams>;
}) {
  requireAdminPreview();
  const t = await getTranslations("admin.feedback");
  const query = parseFeedbackQuery(await searchParams);
  return (
    <div className="flex flex-col gap-6">
      <AdminPageHeader title={t("heading")} />
      <PreviewFeedback
        items={PREVIEW_FEEDBACK}
        summaries={PREVIEW_FEEDBACK_SUMMARY}
        availability={PREVIEW_FEEDBACK_AVAILABILITY.availability}
        query={query}
        basePath={`${ADMIN_PREVIEW_ROUTE}/feedback`}
        now={FIXTURE_NOW}
      />
    </div>
  );
}
