import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { getServerSession } from "@/lib/auth/session";
import { getResumeById } from "@/lib/api/resumes";
import { assertNever } from "@/lib/dto/_helpers";
import { findMasterVersion, emptyContent } from "@/lib/resumes/content-utils";
import { Button } from "@/components/ui/button";
import { ResumeContentForm } from "@/components/resumes/resume-content-form";
import { RenameResumeForm } from "@/components/resumes/rename-resume-form";
import { DeleteResumeDialog } from "@/components/resumes/delete-resume-dialog";

interface Props {
  params: Promise<{ id: string }>;
}

export default async function CvDetailPage({ params }: Props) {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const { id } = await params;
  const result = await getResumeById(id);
  switch (result.kind) {
    case "ok":
      break;
    case "unauthorized":
      redirect("/logga-in");
    case "notFound":
      notFound();
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
          <div>
            <Button asChild variant="outline">
              <Link href="/cv">Tillbaka till CV</Link>
            </Button>
          </div>
        </div>
      );
    case "forbidden":
    case "error":
      return (
        <div className="flex flex-col gap-4">
          <h1 className="text-h1 font-medium text-text-primary">
            Kunde inte ladda CV
          </h1>
          <p className="text-body text-text-secondary">
            Ett tekniskt fel uppstod. Försök ladda om sidan eller gå tillbaka
            till CV-listan.
          </p>
          <div>
            <Button asChild variant="outline">
              <Link href="/cv">Tillbaka till CV</Link>
            </Button>
          </div>
        </div>
      );
    default:
      return assertNever(result);
  }

  const resume = result.data;
  const updatedAt = new Date(resume.updatedAt).toLocaleDateString("sv-SE");
  const master = findMasterVersion(resume);
  const initialContent = master?.content ?? emptyContent();

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center gap-3">
        <Link
          href="/cv"
          className="text-body-sm text-text-secondary hover:text-text-primary"
        >
          CV
        </Link>
        <span className="text-text-tertiary">/</span>
        <span className="text-body-sm text-text-secondary">{resume.name}</span>
      </div>

      <div className="flex items-start justify-between gap-4">
        <div className="flex flex-col gap-1">
          <h1 className="text-h1 font-medium text-text-primary">
            {resume.name}
          </h1>
          <p className="text-body-sm text-text-secondary">
            Senast uppdaterad: {updatedAt}
          </p>
        </div>
        <div className="flex items-center gap-2">
          <RenameResumeForm resumeId={id} currentName={resume.name} />
          <DeleteResumeDialog resumeId={id} resumeName={resume.name} />
        </div>
      </div>

      <hr className="border-border" />

      <ResumeContentForm resumeId={id} initialContent={initialContent} />
    </div>
  );
}
