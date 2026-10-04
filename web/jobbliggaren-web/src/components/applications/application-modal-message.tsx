import { ApplicationModalShell } from "@/components/applications/application-modal-shell";

/**
 * The application modal when it has no application to show: a rate limit, a load
 * error, or an application that is gone (ADR 0053 Amendment 2026-10-04). The body
 * carries `id="jp-modal-desc"`, which the shell's `aria-describedby` names.
 */
export function ApplicationModalMessage({ title, body }: { title: string; body: string }) {
  return (
    <ApplicationModalShell title={title} subtitle="">
      <div className="jp-modal__body">
        <p id="jp-modal-desc" className="text-body-sm text-text-primary">
          {body}
        </p>
      </div>
    </ApplicationModalShell>
  );
}
