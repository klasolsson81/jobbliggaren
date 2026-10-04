"use client";

// "use client": it wraps the house Segment, a client control, for Server Components to render.
import { Segment } from "@/components/ui/segment";

interface AdminSegmentProps<T extends string> {
  readonly label: string;
  readonly options: ReadonlyArray<{ readonly value: T; readonly label: string }>;
  readonly value: T;
  /** Absent for an unbuilt region: every option is then disabled (ADR 0150 D2). */
  readonly onChange?: (value: T) => void;
  /** The region's "Kommer snart" line, read with the group while it is disabled. */
  readonly describedBy?: string;
}

/**
 * One choice among several on the admin surface (a status filter, a period): the house `Segment`,
 * so the admin surface and the rest of the app share one control. A Server Component can render it
 * without a handler, which is the disabled form an unbuilt region uses.
 */
export function AdminSegment<T extends string>({
  label,
  options,
  value,
  onChange,
  describedBy,
}: AdminSegmentProps<T>) {
  return (
    <div className="jp-adminsegment">
      <Segment
        aria-label={label}
        aria-describedby={describedBy}
        value={value}
        onChange={onChange ?? ignore}
        options={options}
        disabled={onChange === undefined}
      />
    </div>
  );
}

function ignore(): void {}
