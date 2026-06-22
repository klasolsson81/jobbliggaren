import Link from "next/link";
import { useTranslations } from "next-intl";

/**
 * SiteFooter — delad footer för marketing-inre sidor och auth-sidor.
 * Minimal länkrad: Om / Användarvillkor / Cookies / Logga in. Civic-utility-
 * tonen: ingen marknadsföring, inga sociala media-länkar, inga branding-
 * gradients. Sync RSC → `useTranslations` resolveras synkront.
 */
export function SiteFooter() {
  const t = useTranslations("landing");
  return (
    <footer className="border-t border-border bg-surface-primary py-6">
      <div className="mx-auto flex w-full max-w-6xl flex-col items-start gap-3 px-6 sm:flex-row sm:items-center sm:justify-between">
        <p className="text-body-sm text-text-secondary">
          {t("siteFooter.copyright")}
        </p>
        <nav aria-label={t("common.footerNavLabel")} className="flex flex-wrap gap-4">
          <Link
            href="/villkor"
            className="text-body-sm text-text-secondary hover:text-text-primary"
          >
            {t("siteFooter.linkTerms")}
          </Link>
          <Link
            href="/cookies"
            className="text-body-sm text-text-secondary hover:text-text-primary"
          >
            {t("siteFooter.linkCookies")}
          </Link>
          <Link
            href="/logga-in"
            className="text-body-sm text-text-secondary hover:text-text-primary"
          >
            {t("siteFooter.linkLogin")}
          </Link>
        </nav>
      </div>
    </footer>
  );
}
