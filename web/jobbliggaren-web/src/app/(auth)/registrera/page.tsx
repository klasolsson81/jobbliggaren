import { Suspense } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { RegisterForm } from "@/components/forms/RegisterForm";

/**
 * Public registration is OPEN (Klas 2026-06-27 — no closed beta, no waitlist;
 * supersedes ADR 0005 Amendment). `/registrera` renders the live RegisterForm,
 * mirroring `(auth)/logga-in`. The waitlist surface was retired in #265 and the
 * Waitlist/Invitations backend contexts in #266.
 */
export default function RegistreraPage() {
  const t = useTranslations("pages");
  return (
    <div className="flex flex-col gap-8">
      <div className="flex flex-col gap-1">
        <h1 className="text-h2 font-medium text-text-primary">
          {t("auth.register.title")}
        </h1>
        <p className="text-body text-text-secondary">{t("auth.register.brand")}</p>
      </div>

      <Suspense fallback={null}>
        <RegisterForm />
      </Suspense>

      <p className="text-body-sm leading-5 text-text-secondary text-center">
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
