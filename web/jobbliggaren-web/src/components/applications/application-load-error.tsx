interface ApplicationLoadErrorProps {
  title: string;
  body: string;
}

/**
 * The error block for an /ansokningar read that failed, rate-limited or not (#1827 M6): one
 * block for every such branch. The caller keeps its own navigation around it, such as the
 * detail page's back link.
 */
export function ApplicationLoadError({ title, body }: ApplicationLoadErrorProps) {
  return (
    <div
      role="alert"
      className="rounded-md border border-danger-600/30 bg-danger-50 px-6 py-4 text-danger-700"
    >
      <p className="text-body font-medium">{title}</p>
      <p className="mt-1 text-body-sm">{body}</p>
    </div>
  );
}
