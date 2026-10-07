import { JobAdModalShell } from "@/components/job-ads/job-ad-modal-shell";

export function JobAdModalMessage({ title, body }: { title: string; body: string }) {
  return (
    <JobAdModalShell
      title={title}
      company=""
      meta={null}
      describedBy="jp-modal-desc"
      showCloseFooter
    >
      <div className="jp-modal__body">
        <p id="jp-modal-desc" className="text-body-sm text-text-primary">
          {body}
        </p>
      </div>
    </JobAdModalShell>
  );
}
