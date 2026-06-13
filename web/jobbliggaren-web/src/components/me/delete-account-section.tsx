import { DeleteAccountDialog } from "./delete-account-dialog";

interface DeleteAccountSectionProps {
  currentEmail: string;
}

/**
 * "Farligt område"-section för /mig — separator-pattern (banking/GitHub-mönster)
 * som signalerar gravitet utan att gömma funktionen. Modal-trigger är client
 * component; resten av sektionen är server-renderad text.
 */
export function DeleteAccountSection({ currentEmail }: DeleteAccountSectionProps) {
  return (
    <section
      aria-labelledby="delete-account-heading"
      className="flex flex-col gap-3 border-t border-border pt-6"
    >
      <h2
        id="delete-account-heading"
        className="text-h3 font-medium text-text-primary"
      >
        Farligt område
      </h2>
      <p className="text-body text-text-secondary">
        Du kan radera ditt konto permanent. Åtgärden går inte att ångra och
        loggar ut dig från alla enheter.
      </p>
      <div>
        <DeleteAccountDialog currentEmail={currentEmail} />
      </div>
    </section>
  );
}
