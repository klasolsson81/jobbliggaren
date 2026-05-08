import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { getServerSession } from "@/lib/auth/session";
import { getResumeById } from "@/lib/api/resumes";
import { findMasterVersion, emptyContent } from "@/lib/resumes/content-utils";
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
  const resume = await getResumeById(id);
  if (!resume) notFound();

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
        <span className="text-body-sm text-text-tertiary">{resume.name}</span>
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
