import Link from "next/link";
import { redirect } from "next/navigation";
import { getServerSession } from "@/lib/auth/session";
import { getResumes } from "@/lib/api/resumes";
import { assertNever } from "@/lib/dto/_helpers";
import { ResumeCard } from "@/components/resumes/resume-card";
import { Button } from "@/components/ui/button";

export default async function CvListPage() {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const result = await getResumes();
  switch (result.kind) {
    case "ok":
      break;
    case "unauthorized":
      redirect("/logga-in");
    case "rateLimited":
      return (
        <div className="flex flex-col gap-4">
          <h1 className="text-h1 font-medium text-text-primary">
            För många förfrågningar
          </h1>
          <p className="text-body text-text-secondary">
            Du har gjort för många förfrågningar på kort tid. Försök igen om{" "}
            {result.retryAfterSeconds} sekunder.
          </p>
        </div>
      );
    case "notFound":
    case "forbidden":
    case "error":
      return (
        <div className="flex flex-col gap-4">
          <h1 className="text-h1 font-medium text-text-primary">
            Kunde inte ladda CV
          </h1>
          <p className="text-body text-text-secondary">
            Ett tekniskt fel uppstod. Försök ladda om sidan om en stund.
          </p>
        </div>
      );
    default:
      return assertNever(result);
  }

  const items = result.data.items;
  // API returnerar redan sorterat på senast uppdaterad — denna sortering är defensiv.
  const sorted = [...items].sort(
    (a, b) =>
      new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime()
  );

  return (
    <div className="flex flex-col">
      <div className="flex items-end justify-between">
        <div>
          <h1 className="jp-h1">CV</h1>
          <p className="jp-lede">
            Hantera dina CV-varianter. AI-stöd hjälper dig anpassa innehållet
            per ansökan — du behåller alltid kontrollen.
          </p>
        </div>
        <Button asChild>
          <Link href="/cv/ny">Nytt CV</Link>
        </Button>
      </div>

      {sorted.length === 0 ? (
        <div className="mt-7 border-y border-border-default px-1 py-12 text-center">
          <p className="text-body text-text-primary">Inga CV ännu</p>
          <p className="mt-1 text-body-sm text-text-secondary">
            Skapa ditt första CV för att komma igång.
          </p>
        </div>
      ) : (
        <div className="mt-8 flex flex-col border-t border-border-default">
          {sorted.map((resume) => (
            <ResumeCard key={resume.id} resume={resume} />
          ))}
        </div>
      )}
    </div>
  );
}
