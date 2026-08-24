import { useTranslations } from "next-intl";
import { ForgotPasswordForm } from "@/components/forms/ForgotPasswordForm";
import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("pages");
  return { title: t("auth.forgotPassword.meta.title") };
}

// #1171 — PUBLIC forgot-password request page. Lives under (auth)/ rather than (app)/ on purpose: the
// visitor has lost access by definition, so it must be reachable without a session. Because it is not
// an (app)/ segment it is not in PROTECTED_PREFIXES (protected-routes.test.ts derives that set from the
// (app)/ directory), so the proxy never redirects it to /logga-in.
//
// No metadata.robots override, unlike /aterstall-losenord: this URL carries no token and nothing
// secret. It is an ordinary public page, in the same class as /logga-in and /registrera.

export default function GlomtLosenordPage() {
  const t = useTranslations("pages");
  return (
    <div className="flex flex-col gap-8">
      {/* h1 only. The instruction lives inside the form branch so it unmounts with the field it
          describes — a page-level intro survives into the sent and refused panels and contradicts
          them. */}
      <h1 className="text-h1 font-bold text-heading-1">
        {t("auth.forgotPassword.title")}
      </h1>

      <ForgotPasswordForm />
    </div>
  );
}
