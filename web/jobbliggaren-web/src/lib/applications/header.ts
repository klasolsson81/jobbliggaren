import { adIdentityOf } from "@/components/applications/ad-identity";
import { formatDate, type JpFormatter } from "@/lib/i18n/format";
import type { ApplicationDetailDto } from "@/lib/dto/applications";

/**
 * The `pages` keys the header reads. The caller passes `getTranslations("pages")`, so this
 * stays free of request context.
 */
export type DetailHeaderTranslator = (
  key:
    | "ansokningar.detail.fallbackTitle"
    | "ansokningar.detail.subtitle"
    | "ansokningar.detail.createdSubtitle"
    | "ansokningar.detail.adRemoved",
  values?: Record<string, string>,
) => string;

export interface ApplicationDetailHeader {
  title: string;
  subtitle: string;
}

/**
 * The title and subtitle above the detail body, in the modal's head and on the full page.
 *
 * The header makes no claim about the ad being live: an ad row supplies its own identity,
 * archived or not, and the body (`SourceAdSection`) says what became of the ad. The one
 * exception is an erased ad (#892, CTO R1): its row carries the preserved identity, which
 * would look alive without the removed marker.
 */
export function applicationDetailHeader(
  application: Pick<ApplicationDetailDto, "id" | "createdAt" | "jobAd">,
  t: DetailHeaderTranslator,
  format: JpFormatter,
): ApplicationDetailHeader {
  const { adRemoved, title, company } = adIdentityOf(application.jobAd);
  const base =
    company != null
      ? t("ansokningar.detail.subtitle", { company })
      : t("ansokningar.detail.createdSubtitle", {
          date: formatDate(format, application.createdAt) ?? "",
        }).trim();
  return {
    title:
      title ??
      t("ansokningar.detail.fallbackTitle", {
        shortId: application.id.slice(0, 8),
      }),
    subtitle: adRemoved
      ? `${base} · ${t("ansokningar.detail.adRemoved")}`
      : base,
  };
}
