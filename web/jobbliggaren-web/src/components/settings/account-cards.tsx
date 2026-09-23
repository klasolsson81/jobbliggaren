import { ChangeEmailCard } from "./change-email-card";
import { PrivacyCard } from "./privacy-card";
import { LogoutCard } from "./logout-card";

/**
 * The /mina-sidor cards that read only the session's address, never the profile. They render on
 * every profile branch of the page, because changing the address, deleting the account and logging
 * out must not depend on a profile the page could not show (design-reviewer Major 3, #1740).
 */
export function AccountCards({ email }: { email: string }) {
  return (
    <>
      <ChangeEmailCard currentEmail={email} />
      <PrivacyCard userEmail={email} />
      <LogoutCard />
    </>
  );
}
