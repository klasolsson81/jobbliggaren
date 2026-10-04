import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { AdminPageHeader } from "@/components/admin/admin-page-header";
import { AdminSegment } from "@/components/admin/admin-segment";
import { AdminTableScroll } from "@/components/admin/admin-table-scroll";
import { ComingSoon } from "@/components/admin/coming-soon";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("admin.email");
  return { title: t("meta.title") };
}

const SOON_ID = "admin-email-soon";
const CAPTION_ID = "admin-email-caption";

/**
 * `/admin/e-post` — email delivery per email type (ADR 0150). The provider's outcomes are #1981:
 * until then the period group is disabled, the three totals are unknown (an en-dash, never 0),
 * and both lists hold one "Kommer snart" line (D2). No provider is named before an outcome from
 * it is shown.
 */
export default async function AdminEmailDeliveryPage() {
  const t = await getTranslations("admin.email");
  const unavailable = await getTranslations("admin.unavailable");
  const dash = unavailable("unknownValue");

  return (
    <div className="flex flex-col gap-6">
      <AdminPageHeader
        title={t("heading")}
        lede={t("lede")}
        aside={
          <AdminSegment
            label={t("period.label")}
            options={[
              { value: "h24", label: t("period.h24") },
              { value: "d3", label: t("period.d3") },
              { value: "d7", label: t("period.d7") },
            ]}
            value="d7"
            describedBy={SOON_ID}
          />
        }
      />

      <dl className="jp-adminmailsum">
        <div>
          <dt>{t("summary.sent")}</dt>
          <dd>{dash}</dd>
        </div>
        <div>
          <dt>{t("summary.failed")}</dt>
          <dd>{dash}</dd>
        </div>
        <div>
          <dt>{t("summary.noRecipient")}</dt>
          <dd>{dash}</dd>
        </div>
      </dl>

      <AdminTableScroll labelledBy={CAPTION_ID}>
        <table className="jp-table jp-admintable">
          <caption id={CAPTION_ID} className="sr-only">
            {t("table.caption")}
          </caption>
          <thead>
            <tr>
              <th scope="col">{t("table.function")}</th>
              <th scope="col">{t("table.sent")}</th>
              <th scope="col">{t("table.failed")}</th>
              <th scope="col">{t("table.lastError")}</th>
            </tr>
          </thead>
          <tbody>
            <tr>
              <td colSpan={4} className="jp-admintable__soon">
                <ComingSoon id={SOON_ID} />
              </td>
            </tr>
          </tbody>
        </table>
      </AdminTableScroll>

      <section aria-labelledby="admin-email-failures" className="flex flex-col gap-4">
        <h2 id="admin-email-failures" className="jp-h2">
          {t("failures.heading")}
        </h2>
        <ComingSoon />
      </section>
    </div>
  );
}
