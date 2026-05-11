import Link from "next/link";
import { redirect } from "next/navigation";
import { getServerSession } from "@/lib/auth/session";
import { getResumes } from "@/lib/api/resumes";
import { ResumeCard } from "@/components/resumes/resume-card";
import { Button } from "@/components/ui/button";

export default async function CvListPage() {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const result = await getResumes();
  const items = result?.items ?? [];
  // API returnerar redan sorterat på senast uppdaterad — denna sortering är defensiv.
  const sorted = [...items].sort(
    (a, b) =>
      new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime()
  );

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <h1 className="text-h1 font-medium text-text-primary">CV</h1>
        <Button asChild>
          <Link href="/cv/ny">Nytt CV</Link>
        </Button>
      </div>

      {sorted.length === 0 ? (
        <div className="rounded-md border border-border bg-surface-secondary px-6 py-10 text-center">
          <p className="text-body text-text-secondary">Inga CV ännu</p>
          <p className="mt-1 text-body-sm text-text-secondary">
            Skapa ditt första CV för att komma igång.
          </p>
        </div>
      ) : (
        <div className="flex flex-col gap-2">
          {sorted.map((resume) => (
            <ResumeCard key={resume.id} resume={resume} />
          ))}
        </div>
      )}
    </div>
  );
}
