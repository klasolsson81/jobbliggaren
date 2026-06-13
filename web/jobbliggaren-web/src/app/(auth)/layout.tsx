import { SiteHeader } from "@/components/site/site-header";
import { SiteFooter } from "@/components/site/site-footer";

/**
 * Auth-layout — wrappar /logga-in (och tidigare /registrera, nu 308-redirect)
 * med SiteHeader (brand-länk till /) + SiteFooter. Klas-direktiv 2026-05-24:
 * login-sidan ska ha samma "vanliga layout" som övriga marketing-sidor så
 * användare alltid kan navigera tillbaka.
 *
 * Login-formuläret centreras inom ett max-w-sm-block; SiteHeader visar inte
 * "Logga in"-länk här (redundant — användaren är redan på login-sidan).
 */
export default function AuthLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <div className="flex min-h-screen flex-col bg-surface-secondary text-text-primary">
      <SiteHeader showLogin={false} />
      <main className="flex flex-1 items-center justify-center px-6 py-12">
        <div className="w-full max-w-sm">{children}</div>
      </main>
      <SiteFooter />
    </div>
  );
}
