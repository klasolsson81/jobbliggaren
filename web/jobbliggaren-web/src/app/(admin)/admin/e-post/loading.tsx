import { useTranslations } from "next-intl";
import { AdminEmailAllowance } from "@/components/admin/admin-email-allowance";
import { AdminEmailDelivery } from "@/components/admin/admin-email-delivery";
import { AdminEmailDmarc } from "@/components/admin/admin-email-dmarc";
import { AdminPageHeader } from "@/components/admin/admin-page-header";

/**
 * The page while its reads are pending: the same page, with the allowance, the summary, the failures, the count per
 * mail kind and the DMARC reports loading. No period is marked: this state cannot read the URL, and a deep link to `?period=h24` must not
 * show 7 dygn as chosen.
 */
export default function AdminEmailLoading() {
  const t = useTranslations("admin");
  return (
    <div className="flex flex-col gap-8">
      <AdminPageHeader title={t("email.heading")} lede={t("email.lede")} />
      <AdminEmailAllowance region={{ kind: "loading" }} />
      <AdminEmailDelivery
        pagePath="/admin/e-post"
        period={null}
        summary={{ kind: "loading" }}
        failures={{ kind: "loading" }}
        kinds={{ kind: "loading" }}
      />
      <AdminEmailDmarc read={{ kind: "loading" }} />
    </div>
  );
}
