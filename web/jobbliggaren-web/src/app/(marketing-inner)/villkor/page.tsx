import type { Metadata } from "next";

export const metadata: Metadata = {
  title: "Användarvillkor — Jobbliggaren",
  description:
    "Jobbliggarens användarvillkor. Sluten beta — full text publiceras innan första öppna registrering.",
};

/**
 * Placeholder för användarvillkor. Versionerad policy-text är öppen fråga i
 * BUILD.md §20 — levereras av Klas innan första prod-deploy med riktig
 * användarbas.
 */
export default function VillkorPage() {
  return (
    <>
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
            Jobbliggaren befinner sig i sluten beta. Fullständiga användarvillkor
            publiceras innan tjänsten öppnas för allmänheten.
          </p>
          <p className="text-body text-text-secondary">
            Under beta-perioden gäller följande:
          </p>
          <ul className="flex flex-col gap-2 text-body text-text-secondary">
            <li>
              Tjänsten levereras i befintligt skick — funktioner kan ändras
              eller tillkomma.
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
        </section>
      </main>
    </>
  );
}
