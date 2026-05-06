import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";

export default function LandingPage() {
  return (
    <main className="mx-auto max-w-2xl px-6 py-12 flex flex-col gap-10">
      <section className="flex flex-col gap-3">
        <h1 className="text-h1 font-medium text-text-primary">JobbPilot</h1>
        <p className="text-body text-text-secondary">
          JobbPilot hjälper dig att hålla ordning på dina jobbansökningar —
          vad du sökt, hos vem, och var i processen du befinner dig. AI-stöd
          finns där det gör nytta, utan att ta över. Design och funktion
          följer civic-utility-principen: enkelt, pålitligt och respektfullt
          mot din tid.
        </p>
      </section>

      <section className="flex flex-col gap-6">
        <h2 className="text-h3 font-medium text-text-primary">Designsystem</h2>

        <div className="flex flex-col gap-2">
          <p className="text-label text-text-secondary">Knappar</p>
          <div className="flex flex-wrap gap-3">
            <Button variant="default">Primär åtgärd</Button>
            <Button variant="secondary">Sekundär</Button>
            <Button variant="ghost">Tertiär</Button>
          </div>
        </div>

        <div className="flex flex-col gap-2">
          <label
            htmlFor="demo-email"
            className="text-label font-medium text-text-primary"
          >
            E-postadress
          </label>
          <Input
            id="demo-email"
            type="email"
            placeholder="din.email@exempel.se"
            className="max-w-sm"
          />
        </div>

        <Card className="max-w-sm">
          <CardHeader>
            <CardTitle>Civic-utility i praktiken</CardTitle>
          </CardHeader>
          <CardContent>
            <p className="text-body text-text-secondary">
              Alla färger, typsnitt och radier hämtas från ett låst
              token-system i globals.css. Ingen komponent väljer egna värden.
            </p>
          </CardContent>
        </Card>
      </section>
    </main>
  );
}
