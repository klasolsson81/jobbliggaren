/**
 * A button label that reads its busy form while a command runs and keeps the button's width
 * (DESIGN.md §6): both forms share one grid cell, and the one not shown is hidden from sight and
 * from the accessible name.
 */
export function AdminBusyLabel({
  busy,
  label,
  busyLabel,
}: {
  readonly busy: boolean;
  readonly label: string;
  readonly busyLabel: string;
}) {
  return (
    <span className="jp-adminbusy">
      <span className={busy ? "jp-adminbusy__off" : undefined} aria-hidden={busy || undefined}>
        {label}
      </span>
      <span className={busy ? undefined : "jp-adminbusy__off"} aria-hidden={!busy || undefined}>
        {busyLabel}
      </span>
    </span>
  );
}
