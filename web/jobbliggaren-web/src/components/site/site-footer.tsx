import Link from "next/link";

/**
 * SiteFooter — delad footer för marketing-inre sidor och auth-sidor.
 * Minimal länkrad: Om / Användarvillkor / Cookies / Logga in. Civic-utility-
 * tonen: ingen marknadsföring, inga sociala media-länkar, inga branding-
 * gradients.
 */
export function SiteFooter() {
  return (
    <footer className="border-t border-border bg-surface-primary py-6">
      <div className="mx-auto flex w-full max-w-6xl flex-col items-start gap-3 px-6 sm:flex-row sm:items-center sm:justify-between">
        <p className="text-body-sm text-text-secondary">
          © Jobbliggaren · sluten beta
        </p>
        <nav aria-label="Sidfot" className="flex flex-wrap gap-4">
          <Link
            href="/villkor"
            className="text-body-sm text-text-secondary hover:text-text-primary"
          >
            Användarvillkor
          </Link>
          <Link
            href="/cookies"
            className="text-body-sm text-text-secondary hover:text-text-primary"
          >
            Cookies
          </Link>
          <Link
            href="/logga-in"
            className="text-body-sm text-text-secondary hover:text-text-primary"
          >
            Logga in
          </Link>
        </nav>
      </div>
    </footer>
  );
}
