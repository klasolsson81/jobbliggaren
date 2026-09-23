export function PendingLabel({ pending, idle, busy }: { pending: boolean; idle: string; busy: string }) {
  return (
    <span className="grid">
      <span
        className={pending ? "invisible col-start-1 row-start-1" : "col-start-1 row-start-1"}
        aria-hidden={pending || undefined}
      >
        {idle}
      </span>
      <span
        className={pending ? "col-start-1 row-start-1" : "invisible col-start-1 row-start-1"}
        aria-hidden={!pending || undefined}
      >
        {busy}
      </span>
    </span>
  );
}
