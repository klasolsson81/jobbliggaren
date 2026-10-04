import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { Send } from "lucide-react";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { AdminPageHeader } from "@/components/admin/admin-page-header";
import { AdminSegmented } from "@/components/admin/admin-segmented";
import { ComingSoon } from "@/components/admin/coming-soon";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("admin.feedback");
  return { title: t("meta.title") };
}

const LIST_SOON_ID = "admin-feedback-list-soon";
const DETAIL_SOON_ID = "admin-feedback-detail-soon";

/**
 * `/admin/feedback` — reports from the app's feedback button, as a master/detail layout
 * (ADR 0150). The feedback function itself is #1979: until then the status filter, the list and
 * the reply form keep their structure, disabled and described by their region's "Kommer snart"
 * line, and no count, report or reply is shown (D2).
 */
export default async function AdminFeedbackPage() {
  const t = await getTranslations("admin.feedback");

  return (
    <div className="flex flex-col gap-6">
      <AdminPageHeader title={t("heading")} />

      <AdminSegmented
        label={t("filter.label")}
        options={[
          { key: "all", label: t("filter.all") },
          { key: "new", label: t("filter.new") },
          { key: "inProgress", label: t("filter.inProgress") },
          { key: "resolved", label: t("filter.resolved") },
          { key: "skipped", label: t("filter.skipped") },
        ]}
        selected="all"
        disabled
        describedBy={LIST_SOON_ID}
      />

      <div className="jp-adminfeedback">
        <section aria-labelledby="admin-feedback-list">
          <h2 id="admin-feedback-list" className="sr-only">
            {t("list.label")}
          </h2>
          <ComingSoon id={LIST_SOON_ID} region />
        </section>

        <section aria-labelledby="admin-feedback-detail" className="jp-adminfeedback__detail">
          <h2 id="admin-feedback-detail" className="sr-only">
            {t("detail.label")}
          </h2>
          <div className="jp-adminfeedback__reply">
            <Label htmlFor="admin-feedback-reply">{t("detail.reply")}</Label>
            <Textarea id="admin-feedback-reply" disabled aria-describedby={DETAIL_SOON_ID} />
            <div>
              <button
                type="button"
                className="jp-btn jp-btn--secondary"
                disabled
                aria-describedby={DETAIL_SOON_ID}
              >
                <Send size={16} aria-hidden="true" />
                {t("detail.send")}
              </button>
            </div>
          </div>
          <ComingSoon id={DETAIL_SOON_ID} />
        </section>
      </div>
    </div>
  );
}
