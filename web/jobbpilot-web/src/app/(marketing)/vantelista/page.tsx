import type { Metadata } from "next";
import Link from "next/link";
import { WaitlistForm } from "@/components/forms/WaitlistForm";

export const metadata: Metadata = {
  title: "Väntelista — JobbPilot",
  description:
    "JobbPilot är i sluten beta. Anmäl ditt intresse så hör vi av oss när vi har kapacitet att släppa in fler.",
};

export default function VantelistaPage() {
  return (
    <div className="flex min-h-screen flex-col bg-surface-primary text-text-primary">
      <header className="jp-pagehero">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <p className="jp-pagehero__kicker">Sluten beta</p>
            <h1 id="vantelista-heading" className="jp-pagehero__title">
              Anmäl dig till väntelistan
            </h1>
            <p className="jp-pagehero__lede">
              JobbPilot är i sluten beta. Vi släpper in användare när vi har
              kapacitet — inga datum lovas. Anmäl ditt intresse så hör vi av
              oss när nästa plats är ledig.
            </p>
          </div>
        </div>
      </header>

      <main className="jp-container jp-page">
        <section
          aria-labelledby="vantelista-heading"
          className="mx-auto flex max-w-2xl flex-col gap-6"
        >
          <WaitlistForm />
        </section>

        <footer className="mx-auto mt-12 flex max-w-2xl flex-col gap-3 border-t border-border pt-6">
          <p className="text-body-sm text-text-secondary">
            Vi sparar dina uppgifter endast för väntelistan. Du kan be oss
            radera dem när som helst genom att svara på bekräftelsemejlet.
          </p>
          <p className="text-body-sm text-text-secondary">
            Har du redan ett konto?{" "}
            <Link
              href="/logga-in"
              className="text-brand-600 underline underline-offset-2 hover:text-brand-700"
            >
              Logga in
            </Link>
          </p>
        </footer>
      </main>
    </div>
  );
}
