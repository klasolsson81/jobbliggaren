import { useTranslations } from "next-intl";
import { ForgotPasswordForm } from "@/components/forms/ForgotPasswordForm";

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
      <div className="flex flex-col gap-1">
        <h1 className="text-h1 font-bold text-heading-1">
          {t("auth.forgotPassword.title")}
        </h1>
        <p className="text-body text-text-secondary">
          {t("auth.forgotPassword.intro")}
        </p>
      </div>

      <ForgotPasswordForm />
    </div>
  );
}
