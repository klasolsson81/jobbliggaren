import { describe, expect, it } from "vitest";
import type { JpFormatter } from "@/lib/i18n/format";
import { byteUnitFor, formatBinaryBytes } from "./format-bytes";

// The next-intl formatter, with the Swedish locale it resolves in production.
const sv: JpFormatter = {
  number: (value: number, options?: Intl.NumberFormatOptions) => new Intl.NumberFormat("sv-SE", options).format(value),
  dateTime: () => "",
} as unknown as JpFormatter;

// 8 135 992 kB, the MemTotal read on the production box 2026-10-10 (runbook admin-host-observations.md).
const MEM_TOTAL_BYTES = 8_135_992 * 1024;

describe("byteUnitFor", () => {
  it.each([
    [0, "B"],
    [1023, "B"],
    [1024, "KiB"],
    [1024 ** 2, "MiB"],
    [MEM_TOTAL_BYTES, "GiB"],
    [1024 ** 4, "TiB"],
    [1024 ** 5, "TiB"],
  ] as const)("picks the unit for %d bytes", (bytes, unit) => {
    expect(byteUnitFor(bytes)).toBe(unit);
  });
});

describe("formatBinaryBytes", () => {
  it("writes a total in binary gigabytes with a decimal comma", () => {
    expect(formatBinaryBytes(sv, MEM_TOTAL_BYTES)).toEqual({ value: "7,8", unit: "GiB" });
  });

  it("lets a pair share the unit of its total", () => {
    const used = 2.4 * 1024 ** 3;
    expect(formatBinaryBytes(sv, used, byteUnitFor(MEM_TOTAL_BYTES))).toEqual({ value: "2,4", unit: "GiB" });
  });

  it("keeps a real zero as 0, not an empty value", () => {
    expect(formatBinaryBytes(sv, 0)).toEqual({ value: "0", unit: "B" });
  });

  it("writes whole bytes without a fraction", () => {
    expect(formatBinaryBytes(sv, 512)).toEqual({ value: "512", unit: "B" });
  });
});
