import type { JpFormatter } from "@/lib/i18n/format";

/** IEC binary units. The symbols are language-neutral, so they are constants rather than copy. */
export type ByteUnit = "B" | "KiB" | "MiB" | "GiB" | "TiB";

const UNITS: ReadonlyArray<ByteUnit> = ["B", "KiB", "MiB", "GiB", "TiB"];

/** The largest unit in which `bytes` is at least 1, so that a total reads as "7,8 GiB", not "8 135 992 KiB". */
export function byteUnitFor(bytes: number): ByteUnit {
  let unit: ByteUnit = "B";
  let scaled = Math.abs(bytes);
  for (const next of UNITS.slice(1)) {
    if (scaled < 1024) break;
    scaled /= 1024;
    unit = next;
  }
  return unit;
}

/**
 * `bytes` as a locale-grouped number in the given unit (default: the unit that suits `bytes`). Taking the
 * unit lets a pair such as used and total share one, which is how "2,4 av 7,8 GiB" is read.
 * One decimal for KiB and above, none for bytes.
 */
export function formatBinaryBytes(
  format: JpFormatter,
  bytes: number,
  unit: ByteUnit = byteUnitFor(bytes),
): { readonly value: string; readonly unit: ByteUnit } {
  const power = UNITS.indexOf(unit);
  return {
    value: format.number(bytes / 1024 ** power, {
      minimumFractionDigits: 0,
      maximumFractionDigits: power === 0 ? 0 : 1,
    }),
    unit,
  };
}
