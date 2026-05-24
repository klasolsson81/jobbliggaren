import type { Metadata } from "next";

export const metadata: Metadata = {
  title: "Cookies — JobbPilot",
  description:
    "Information om cookies på JobbPilot. Endast nödvändiga cookies används under sluten beta.",
};

/**
 * Placeholder för cookie-policy. Versionerad policy-text är öppen fråga i
 * BUILD.md §20 — levereras av Klas innan första prod-deploy.
 */
export default function CookiesPage() {
  return (
    <>
      <header className="jp-pagehero">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <p className="jp-pagehero__kicker">Sluten beta</p>
            <h1 className="jp-pagehero__title">Cookies</h1>
          </div>
        </div>
      </header>

      <main className="mx-auto w-full max-w-2xl px-6 py-12">
        <section className="flex flex-col gap-4">
          <p className="text-body text-text-primary">
            JobbPilot använder endast nödvändiga cookies för att tjänsten ska
            fungera under sluten beta.
          </p>
          <p className="text-body text-text-secondary">Det innebär:</p>
          <ul className="flex flex-col gap-2 text-body text-text-secondary">
            <li>
              <span className="font-medium">Session-cookies</span> håller dig
              inloggad när du har ett konto.
            </li>
            <li>
              <span className="font-medium">CSRF-skydd</span> skyddar dina
              uppgifter från manipulation.
            </li>
            <li>
              Inga cookies används för analys, spårning eller marknadsföring.
            </li>
          </ul>

          <p className="text-body text-text-secondary pt-2">
            En fullständig cookie-policy publiceras innan tjänsten öppnas för
            allmänheten.
          </p>
        </section>
      </main>
    </>
  );
}
