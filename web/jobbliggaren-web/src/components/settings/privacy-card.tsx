import { useTranslations } from "next-intl";
import { mailLink } from "@/components/auth/mail-link";
import { DeleteAccountSection } from "@/components/me/delete-account-section";
import { Button } from "@/components/ui/button";

interface PrivacyCardProps {
  userEmail: string;
}

const EXPORT_CONTACT_ID = "privacy-export-contact";

/**
 * The Sekretess och data section of /mina-sidor (#1891): the data export and deleting the account
 * (`DeleteAccountSection`, typed confirmation and re-authentication by code).
 *
 * The export is not built yet, and Klas keeps it visible (2026-09-27). It takes the
 * login page's inactive-provider form (ADR 0142 D8): an `aria-disabled` outline button whose name
 * carries "Kommer snart" in words, so touch and screen readers get what a `title` tooltip never gave
 * them, without `.jp-btn`'s dimmed disabled look. Until it is built, the line under it names the route
 * that works today (ADR 0144 row 19).
 */
export function PrivacyCard({ userEmail }: PrivacyCardProps) {
  const t = useTranslations("settings");
  return (
    <section className="jp-card">
      <h2 className="jp-card__title">{t("privacy.title")}</h2>
      <div className="jp-settings-group">
        <Button
          type="button"
          variant="outline"
          aria-disabled="true"
          aria-describedby={EXPORT_CONTACT_ID}
          className="h-auto min-h-10 cursor-default gap-3 py-2 text-left whitespace-normal hover:bg-background [@media(max-width:768px)]:min-h-11"
        >
          <span>{t("privacy.export")}</span>{" "}
          <span className="text-body-sm text-text-primary">{t("privacy.comingSoon")}</span>
        </Button>
        <p id={EXPORT_CONTACT_ID} className="mt-3 text-body-sm text-text-primary">
          {t.rich("privacy.contactRoute", { mail: mailLink })}
        </p>
      </div>
      <DeleteAccountSection currentEmail={userEmail} />
    </section>
  );
}
