import Link from "next/link";
import { redirect } from "next/navigation";
import { Plus } from "lucide-react";
import { getServerSession } from "@/lib/auth/session";
import { getResumes } from "@/lib/api/resumes";
import { assertNever } from "@/lib/dto/_helpers";
import { ResumeCard } from "@/components/resumes/resume-card";
import { AnpassaCvBanner } from "@/components/resumes/anpassa-cv-banner";

/**
 * /cv-listvyn (F6 P3a, HANDOVER §7.4 + målbild 09-cv-light.png).
 *
 * Backend 19cde94 (Resume-DTO-utvidgning) gör att alla 5 nya fält
 * (isPrimary/language/latestRole/sectionCount/topSkills) finns på
 * `ResumeListItemDto` och kan renderas direkt via `<ResumeCard />` i
 * v3-grid. AnpassaCvBanner renderas under grid:en **endast** om listan
 * inte är tom (Klas-prompt §F: "Banner ska INTE renderas om CV-listan
 * är tom — skapa CV först").
 */
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
          <h1 className="jp-h1">För många förfrågningar</h1>
          <p className="jp-lede">
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
          <h1 className="jp-h1">Kunde inte ladda CV</h1>
          <p className="jp-lede">
            Ett tekniskt fel uppstod. Försök ladda om sidan om en stund.
          </p>
        </div>
      );
    default:
      return assertNever(result);
  }

  const items = result.data.items;
  // API returnerar redan sorterat på senast uppdaterad; defensive sort.
  const sorted = [...items].sort(
    (a, b) =>
      new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime(),
  );

  return (
    <>
      {/* F6 P5 Punkt 6 — page-hero (HANDOVER-v4 §2.4). */}
      <section className="jp-pagehero">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <h1 className="jp-pagehero__title">CV</h1>
            <p className="jp-pagehero__lede">
              Hantera dina CV-varianter. AI-stöd hjälper dig anpassa innehållet
              per ansökan, men du behåller alltid kontrollen.
            </p>
          </div>
          <div className="jp-pagehero__aside">
            <Link href="/cv/ny" className="jp-btn jp-btn--primary">
              <Plus size={16} aria-hidden="true" />
              <span>Nytt CV</span>
            </Link>
          </div>
        </div>
      </section>

      <div className="jp-container jp-page">
        {sorted.length === 0 ? (
          <div className="jp-empty jp-empty--brand">
            <div className="jp-empty__kicker">CV-varianter</div>
            <div className="jp-empty__title">Inga CV ännu</div>
            <p className="jp-empty__body">
              Skapa ditt första CV för att komma igång. Du kan ha flera
              varianter — t.ex. en för ledarskap och en för teknisk roll —
              och välja rätt CV per ansökan.
            </p>
            <div className="jp-empty__actions">
              <Link href="/cv/ny" className="jp-btn jp-btn--primary">
                <Plus size={14} aria-hidden="true" /> Skapa första CV
              </Link>
            </div>
          </div>
        ) : (
          <>
            <div className="jp-cvgrid">
              {sorted.map((resume) => (
                <ResumeCard key={resume.id} resume={resume} />
              ))}
            </div>
            <AnpassaCvBanner />
          </>
        )}
      </div>
    </>
  );
}
