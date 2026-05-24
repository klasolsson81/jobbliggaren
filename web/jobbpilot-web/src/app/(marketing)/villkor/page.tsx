import type { Metadata } from "next";
import Link from "next/link";

export const metadata: Metadata = {
  title: "Användarvillkor — JobbPilot",
  description:
    "JobbPilots användarvillkor. Sluten beta — full text publiceras innan första öppna registrering.",
};

/**
 * Placeholder-sida för användarvillkor. Versionerad policy-text är öppen
 * fråga i BUILD.md §20 — levereras av Klas innan första prod-deploy med
 * riktig användarbas. Tills dess visar sidan en notis om sluten beta.
 */
export default function VillkorPage() {
  return (
    <div className="flex min-h-screen flex-col bg-surface-primary text-text-primary">
      <header className="jp-pagehero">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <p className="jp-pagehero__kicker">Sluten beta</p>
            <h1 className="jp-pagehero__title">Användarvillkor</h1>
          </div>
        </div>
      </header>

      <main className="mx-auto w-full max-w-2xl px-6 py-12">
        <section className="flex flex-col gap-4">
          <p className="text-body text-text-primary">
            JobbPilot befinner sig i sluten beta. Fullständiga
            användarvillkor publiceras innan tjänsten öppnas för
            allmänheten.
          </p>
          <p className="text-body text-text-secondary">
            Under beta-perioden gäller följande:
          </p>
          <ul className="flex flex-col gap-2 text-body text-text-secondary">
            <li>
              Tjänsten levereras i befintligt skick — funktioner kan
              ändras eller tillkomma.
            </li>
            <li>
              Vi sparar dina uppgifter endast så länge det krävs för att
              hantera din anmälan eller ditt konto.
            </li>
            <li>
              Du kan be oss radera dina uppgifter när som helst genom att
              svara på bekräftelsemejlet eller kontakta oss.
            </li>
          </ul>

          <p className="text-body-sm text-text-secondary pt-4">
            <Link
              href="/vantelista"
              className="text-brand-600 underline underline-offset-2 hover:text-brand-700"
            >
              Tillbaka till väntelistan
            </Link>
          </p>
        </section>
      </main>
    </div>
  );
}
