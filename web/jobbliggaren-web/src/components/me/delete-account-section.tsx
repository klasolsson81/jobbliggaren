import { useTranslations } from "next-intl";
import { DeleteAccountDialog } from "./delete-account-dialog";

interface DeleteAccountSectionProps {
  currentEmail: string;
}

/**
 * "Farligt område"-section för /mig — separator-pattern (banking/GitHub-mönster)
 * som signalerar gravitet utan att gömma funktionen. Modal-trigger är client
 * component; resten av sektionen är server-renderad text. `useTranslations`
 * resolverar synkront i en sync server component (next-intl v4).
 */
export function DeleteAccountSection({ currentEmail }: DeleteAccountSectionProps) {
  const t = useTranslations("settings");
  return (
    <section
      aria-labelledby="delete-account-heading"
      className="flex flex-col gap-3 border-t border-border pt-6"
    >
      <h2
        id="delete-account-heading"
        className="text-h3 font-medium text-text-primary"
      >
        {t("account.danger.title")}
      </h2>
      <p className="text-body text-text-secondary">
        {t("account.danger.description")}
      </p>
      <div>
        <DeleteAccountDialog currentEmail={currentEmail} />
      </div>
    </section>
  );
}
