import { useTranslations } from "next-intl";
import { AdminEmailAllowance } from "@/components/admin/admin-email-allowance";
import { AdminEmailDelivery } from "@/components/admin/admin-email-delivery";
import { AdminPageHeader } from "@/components/admin/admin-page-header";

/**
 * The page while its reads are pending: the same page, with the allowance, the summary and the failures loading. The
 * per-type table says "Kommer snart", since it is not built. No period is marked: this state cannot read the URL, and
 * a deep link to `?period=h24` must not show 7 dygn as chosen.
 */
export default function AdminEmailLoading() {
  const t = useTranslations("admin");
  return (
    <div className="flex flex-col gap-8">
      <AdminPageHeader title={t("email.heading")} lede={t("email.lede")} />
      <AdminEmailAllowance region={{ kind: "loading" }} />
      <AdminEmailDelivery pagePath="/admin/e-post" period={null} summary={{ kind: "loading" }} failures={{ kind: "loading" }} />
    </div>
  );
}
