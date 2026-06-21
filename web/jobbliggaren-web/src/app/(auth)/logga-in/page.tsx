import { Suspense } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { LoginForm } from "@/components/forms/LoginForm";

export default function LoggaInPage() {
  const t = useTranslations("pages");
  return (
    <div className="flex flex-col gap-8">
      <div className="flex flex-col gap-1">
        <h1 className="text-h2 font-medium text-text-primary">
          {t("auth.login.title")}
        </h1>
        <p className="text-body text-text-secondary">{t("auth.login.brand")}</p>
      </div>

      <Suspense fallback={null}>
        <LoginForm />
      </Suspense>

      <p className="text-sm text-text-secondary text-center">
        {t("auth.login.noAccount")}{" "}
        <Link
          href="/vantelista"
          className="text-brand-600 hover:text-brand-700 underline underline-offset-2"
        >
          {t("auth.login.joinWaitlist")}
        </Link>
      </p>
    </div>
  );
}
