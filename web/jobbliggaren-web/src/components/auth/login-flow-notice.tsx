"use client";

import { useTranslations } from "next-intl";
import { mailLink } from "@/components/auth/mail-link";
import type { ExternalProviderKey } from "@/lib/auth/external-login";
import type { LoginFlowNotice as Notice } from "@/lib/auth/login-flow";
import { useFocusOnMount } from "@/lib/hooks/use-focus-on-mount";

// Client because it takes focus when it mounts (`useFocusOnMount`).
//
// Why a visitor is on `/logga-in`: a grant that could not be used, a login that ran out before the
// code was submitted, or the account that was just deleted (#1740). It arrives with a navigation, so it
// sits ABOVE the form, which stays live; the remedy is the form itself. A status, never an alert and
// never danger colour.
//
// Carried by the flow cookie's `notice` phase. A Server Component cannot clear a cookie, so the
// notice lives its 120 seconds or until the form is submitted, and a reload inside that window
// shows it again. It is still true then, and focus moves to it each time.
export function LoginFlowNotice({
  notice,
  provider,
}: {
  notice: Notice;
  /** Set for an external login's two notices, which name it (#1744). */
  provider?: ExternalProviderKey;
}) {
  const t = useTranslations("pages");
  const providerName = provider ? t(`auth.passwordless.external.providerNames.${provider}`) : "";
  const panelRef = useFocusOnMount<HTMLDivElement>();

  return (
    <div
      ref={panelRef}
      tabIndex={-1}
      role="status"
      aria-live="polite"
      className="flex flex-col gap-1"
    >
      <h2 className="text-body font-bold text-heading-1">
        {t(`auth.passwordless.notice.${notice}.title`, { provider: providerName })}
      </h2>
      <p className="text-body text-text-primary">
        {t.rich(`auth.passwordless.notice.${notice}.body`, { mail: mailLink, provider: providerName })}
      </p>
    </div>
  );
}
