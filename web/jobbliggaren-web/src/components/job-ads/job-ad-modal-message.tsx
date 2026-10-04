import { JobAdModalShell } from "@/components/job-ads/job-ad-modal-shell";

/**
 * The job modal when it has no ad to show: a rate limit, a load error, or an ad
 * that is gone (ADR 0053 Amendment 2026-10-04). It keeps the job modal's own sheet,
 * so the loading shell gives way to the same shape.
 */
export function JobAdModalMessage({ title, body }: { title: string; body: string }) {
  return (
    <JobAdModalShell title={title} company="" meta={null}>
      <div className="jp-modal__body">
        <p className="text-body-sm text-text-primary">{body}</p>
      </div>
      <div className="jp-modal__foot">
        <span className="jp-modal__foot__spacer" />
      </div>
    </JobAdModalShell>
  );
}
