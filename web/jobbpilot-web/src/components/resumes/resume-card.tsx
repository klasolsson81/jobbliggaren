import Link from "next/link";
import type { ResumeListItemDto } from "@/lib/types/resumes";

interface ResumeCardProps {
  resume: ResumeListItemDto;
}

export function ResumeCard({ resume }: ResumeCardProps) {
  const updatedAt = new Date(resume.updatedAt).toLocaleDateString("sv-SE");

  return (
    <Link
      href={`/cv/${resume.id}`}
      className="flex items-center justify-between rounded-md border border-border bg-card px-4 py-3 text-sm hover:bg-surface-secondary transition-colors"
    >
      <div className="flex items-center gap-3">
        <span className="text-body font-medium text-text-primary">
          {resume.name}
        </span>
        <span className="inline-flex items-center rounded-pill bg-surface-tertiary px-2 py-0.5 text-xs font-medium text-text-secondary">
          {resume.versionCount} {resume.versionCount === 1 ? "version" : "versioner"}
        </span>
      </div>
      <span className="text-body-sm text-text-tertiary">{updatedAt}</span>
    </Link>
  );
}
