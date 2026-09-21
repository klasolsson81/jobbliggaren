"use client";

import type { ReactNode } from "react";
import Link from "next/link";
import { useFormatter, useTranslations } from "next-intl";
import type { LoginFlowOutcome } from "@/lib/auth/login-flow";
import { useFocusOnMount } from "@/lib/hooks/use-focus-on-mount";
import { formatDate } from "@/lib/i18n/format";

// A login that ended somewhere other than a session: the account is being deleted, registration
// is closed, or this address can neither log in nor register right now. It REPLACES the form, so
// the page's h1 no longer describes what is on screen and the panel carries its own h2, which is
// the panel's first sentence rather than a heading restating it (design-reviewer, #1738).
//
// Never the error channel: nothing here is the user's fault and nothing can be corrected.
// `role="status"` announces a CHANGE to a region that already exists, and this one mounts already
// filled, which NVDA and JAWS routinely miss. The focus move is what delivers it
// (`RegisterForm.tsx` has the long form of this).

const TEXT_LINK = "text-brand-700 underline underline-offset-2";

/** The address has one home, the message itself: the link reads it out of the `<mail>` chunk. */
function mailLink(chunks: ReactNode) {
  const address = Array.isArray(chunks) ? chunks.join("") : String(chunks);
  return (
    <a href={`mailto:${address}`} className={TEXT_LINK}>
      {chunks}
    </a>
  );
}

export function LoginOutcomePanel({ result }: { result: LoginFlowOutcome }) {
  const t = useTranslations("pages");
  const format = useFormatter();
  const panelRef = useFocusOnMount<HTMLDivElement>();

  const title =
    result.outcome === "pendingDeletion"
      ? t("auth.passwordless.outcome.pendingDeletion.title", {
          date: formatDate(format, result.permanentDeletionDate) ?? result.permanentDeletionDate,
        })
      : t(`auth.passwordless.outcome.${result.outcome}.title`);

  return (
    <div className="flex flex-col gap-4">
      <div
        ref={panelRef}
        tabIndex={-1}
        role="status"
        aria-live="polite"
        className="flex flex-col gap-1"
      >
        <h2 className="text-body font-bold text-heading-1">{title}</h2>
        {result.outcome !== "registrationClosed" && (
          <p className="text-body text-text-primary">
            {t.rich(`auth.passwordless.outcome.${result.outcome}.body`, { mail: mailLink })}
          </p>
        )}
      </div>
      {/* A sibling of the live region, never inside it. */}
      {result.outcome === "registrationClosed" && (
        <p className="text-body-sm text-text-primary">
          <Link href="/" className={TEXT_LINK}>
            {t("auth.passwordless.outcome.registrationClosed.homeLink")}
          </Link>
        </p>
      )}
    </div>
  );
}
