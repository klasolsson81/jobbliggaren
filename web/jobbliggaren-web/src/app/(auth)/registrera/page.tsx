import { Suspense } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { RegisterForm } from "@/components/forms/RegisterForm";

/**
 * There is no waitlist and no invitation gate (Klas 2026-06-27, ADR 0083; supersedes ADR 0005
 * Amendment). The waitlist surface was retired in #265 and the Waitlist/Invitations backend
 * contexts in #266, and that teardown stands.
 *
 * But registration is no longer unconditionally OPEN: ADR 0083 Amendment 2026-08-03 added the
 * `Auth:RegistrationsOpen` kill-switch, which defaults CLOSED while the app is reachable before its
 * launch gates are green. This page deliberately still renders the live RegisterForm and reports the
 * closed state at submit — the alternative would require the frontend to hold its own copy of the
 * flag, i.e. a second source of truth that can drift open. `RegisterForm` renders the refusal as a
 * role="status" panel, not as a validation error.
 */
export default function RegistreraPage() {
  const t = useTranslations("pages");
  // `landing.auth.free` is also the last line of the landing's account card
  // (landing-account-card.tsx); the data line lives only here. A visitor
  // arriving from the footer or from /logga-in met neither before #1493.
  const tAuth = useTranslations("landing.auth");
  return (
    <div className="flex flex-col gap-8">
      <h1 className="text-h1 font-bold text-heading-1">
        {t("auth.register.title")}
      </h1>

      <Suspense fallback={null}>
        <RegisterForm />
      </Suspense>

      <div className="flex flex-col gap-3">
        <p className="jp-auth-free">{tAuth("free")}</p>
        <p className="jp-auth-fine">{tAuth("fine")}</p>
      </div>

      <p className="text-body-sm leading-5 text-text-primary text-center">
        {t("auth.register.haveAccount")}{" "}
        <Link
          href="/logga-in"
          className="text-brand-600 hover:text-brand-700 underline underline-offset-2"
        >
          {t("auth.register.logIn")}
        </Link>
      </p>
    </div>
  );
}
