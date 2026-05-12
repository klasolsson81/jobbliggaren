import type { Metadata } from "next";
import { WaitlistForm } from "@/components/forms/WaitlistForm";

export const metadata: Metadata = {
  title: "Väntelista — JobbPilot",
  description:
    "Anmäl ditt intresse för JobbPilot. Vi släpper in användare i kontrollerade pulser.",
};

export default function VantelistaPage() {
  return (
    <main className="mx-auto max-w-2xl px-6 py-12 flex flex-col gap-8">
      <header className="flex flex-col gap-3">
        <h1 className="text-h1 font-medium text-text-primary">Väntelista</h1>
        <p className="text-body text-text-secondary">
          JobbPilot är just nu i sluten beta. Vi släpper in användare i
          kontrollerade pulser för att kunna ge alla god service. Anmäl dig
          här så hör vi av oss när nästa plats är ledig.
        </p>
      </header>

      <section aria-labelledby="form-heading" className="flex flex-col gap-4">
        <h2 id="form-heading" className="sr-only">
          Anmäl till väntelista
        </h2>
        <WaitlistForm />
      </section>

      <footer className="flex flex-col gap-2 border-t border-border pt-5">
        <p className="text-sm text-text-secondary">
          Vi sparar din e-postadress endast för väntelistan. Du kan be oss
          radera den när som helst genom att svara på bekräftelsemejlet.
        </p>
      </footer>
    </main>
  );
}
