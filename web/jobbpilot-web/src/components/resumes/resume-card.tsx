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
      className="flex items-center justify-between border-b border-border-default px-3 py-4 text-sm transition-colors duration-75 last:border-b-0 hover:bg-surface-tertiary"
    >
      <div className="flex items-center gap-3">
        <span className="text-[15px] font-medium tracking-[-0.005em] text-text-primary">
          {resume.name}
        </span>
        <span className="jp-pill jp-pill--neutral">
          {resume.versionCount}{" "}
          {resume.versionCount === 1 ? "version" : "versioner"}
        </span>
      </div>
      <span className="font-mono text-[11.5px] text-text-secondary">
        {updatedAt}
      </span>
    </Link>
  );
}
