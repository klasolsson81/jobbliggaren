import { Suspense } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { LoginForm } from "@/components/forms/LoginForm";
import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("pages");
  return { title: t("auth.login.meta.title") };
}

export default function LoggaInPage() {
  const t = useTranslations("pages");
  return (
    <div className="flex flex-col gap-8">
      <h1 className="text-h1 font-bold text-heading-1">
        {t("auth.login.title")}
      </h1>

      <Suspense fallback={null}>
        <LoginForm />
      </Suspense>

      <p className="text-body-sm leading-5 text-text-primary text-center">
        {t("auth.login.noAccount")}{" "}
        <Link
          href="/registrera"
          className="text-brand-600 hover:text-brand-700 underline underline-offset-2"
        >
          {t("auth.login.createAccount")}
        </Link>
      </p>
    </div>
  );
}
