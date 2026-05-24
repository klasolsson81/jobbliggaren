import type { Metadata } from "next";
import { WaitlistForm } from "@/components/forms/WaitlistForm";

export const metadata: Metadata = {
  title: "Väntelista — JobbPilot",
  description:
    "JobbPilot är i sluten beta. Anmäl ditt intresse så hör vi av oss när vi har kapacitet att släppa in fler.",
};

export default function VantelistaPage() {
  return (
    <>
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

      <main className="mx-auto w-full max-w-2xl px-6 py-12">
        <section aria-labelledby="vantelista-heading" className="flex flex-col gap-8">
          <WaitlistForm />

          <div className="border-t border-border pt-6">
            <p className="text-body-sm text-text-secondary">
              Vi sparar dina uppgifter endast för väntelistan. Du kan be oss
              radera dem när som helst genom att svara på bekräftelsemejlet.
            </p>
          </div>
        </section>
      </main>
    </>
  );
}
