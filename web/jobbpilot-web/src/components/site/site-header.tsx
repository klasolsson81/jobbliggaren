import Link from "next/link";

/**
 * SiteHeader — delad topbar för marketing-inre sidor (/vantelista, /villkor,
 * /cookies) och auth-sidor (/logga-in). Enkel brand-länk till landing +
 * "Logga in"-länk till höger så användare kan navigera tillbaka eller
 * fortsätta till login.
 *
 * Skiljer sig från `<LandingTopbar />` som har stats — inre sidor visar inte
 * statsräknor (förvirrande utanför landing-kontexten). Använder samma
 * `.jp-land-top`-klass för visuell paritet (vit bg, navy ink, sticky top).
 */
export function SiteHeader({ showLogin = true }: { showLogin?: boolean }) {
  return (
    <header className="jp-land-top">
      <div className="jp-land-top__inner">
        <Link href="/" className="jp-brand" aria-label="JobbPilot — startsida">
          <span className="jp-brand__mark" aria-hidden="true">
            J
          </span>
          <span className="jp-brand__word">JobbPilot</span>
        </Link>
        {showLogin && (
          <Link
            href="/logga-in"
            className="text-body-sm font-medium text-text-primary underline-offset-4 hover:underline"
          >
            Logga in
          </Link>
        )}
      </div>
    </header>
  );
}
