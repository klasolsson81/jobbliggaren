import { useTranslations } from "next-intl";
import { AdminEmailAllowance } from "@/components/admin/admin-email-allowance";
import { AdminEmailDelivery } from "@/components/admin/admin-email-delivery";
import { AdminPageHeader } from "@/components/admin/admin-page-header";

/** The page while its allowance read is pending: the same page, with the allowance region in its loading state. */
export default function AdminEmailLoading() {
  const t = useTranslations("admin");
  return (
    <div className="flex flex-col gap-8">
      <AdminPageHeader title={t("email.heading")} lede={t("email.lede")} />
      <AdminEmailAllowance region={{ kind: "loading" }} />
      <AdminEmailDelivery region={{ kind: "unavailable" }} />
    </div>
  );
}
