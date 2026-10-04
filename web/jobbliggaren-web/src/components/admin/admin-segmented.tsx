export interface AdminSegmentedOption {
  readonly key: string;
  readonly label: string;
}

/**
 * A filter or period group: separate buttons, the selected one tinted, its state carried by
 * `aria-pressed` (ADR 0150 D8). While the data behind it is unbuilt the group is natively
 * disabled and described by the region's "Kommer snart" line.
 */
export function AdminSegmented({
  label,
  options,
  selected,
  disabled = false,
  describedBy,
}: {
  readonly label: string;
  readonly options: ReadonlyArray<AdminSegmentedOption>;
  readonly selected: string;
  readonly disabled?: boolean;
  readonly describedBy?: string;
}) {
  return (
    <div role="group" aria-label={label} className="jp-adminseg">
      {options.map((option) => (
        <button
          key={option.key}
          type="button"
          className="jp-adminseg__opt"
          aria-pressed={option.key === selected}
          disabled={disabled}
          aria-describedby={describedBy}
        >
          {option.label}
        </button>
      ))}
    </div>
  );
}
