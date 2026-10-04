import { describe, expect, it } from "vitest";
import { ADMIN_PREVIEW_SENTINEL } from "../gate.cjs";
import * as fixtures from "./index";

/**
 * The preview's fixtures are fictional by construction (ADR 0150 D5): reserved-domain addresses,
 * documentation IP ranges, no name field, and the sentinel on every row. The two date exports are
 * the fixed clock and a value derived from it, not rows.
 */
const NOT_ROWS = new Set(["FIXTURE_NOW", "PREVIEW_DELETION_EARLIEST"]);
const exported = Object.entries(fixtures).filter(([name]) => !NOT_ROWS.has(name));

function strings(value: unknown, acc: string[] = []): string[] {
  if (typeof value === "string") acc.push(value);
  else if (Array.isArray(value)) value.forEach((item) => strings(item, acc));
  else if (value !== null && typeof value === "object") Object.values(value).forEach((item) => strings(item, acc));
  return acc;
}

function keys(value: unknown, acc: string[] = []): string[] {
  if (Array.isArray(value)) value.forEach((item) => keys(item, acc));
  else if (value !== null && typeof value === "object") {
    for (const [key, item] of Object.entries(value)) {
      acc.push(key);
      keys(item, acc);
    }
  }
  return acc;
}

const allStrings = strings(exported.map(([, value]) => value));

// RFC 5737 (IPv4) and RFC 3849 (IPv6) documentation ranges.
const DOCUMENTATION_IP = /^(192\.0\.2|198\.51\.100|203\.0\.113)\.\d{1,3}$|^2001:db8:/i;
const LOOKS_LIKE_IP = /^\d{1,3}(\.\d{1,3}){3}$|^[0-9a-f]{0,4}(:[0-9a-f]{0,4}){2,7}$/i;

describe("the admin preview's fixtures (ADR 0150 D5)", () => {
  it("exports rows to check", () => {
    expect(exported.length).toBeGreaterThanOrEqual(5);
  });

  it.each(exported)("%s carries the sentinel on every row", (_name, value) => {
    const rows = Array.isArray(value) ? value : [value];
    expect(rows.length).toBeGreaterThan(0);
    for (const row of rows) expect(JSON.stringify(row)).toContain(ADMIN_PREVIEW_SENTINEL);
  });

  it("uses addresses on the sentinel's reserved domain only", () => {
    const addresses = allStrings.filter((value) => /@/.test(value));
    expect(addresses.length).toBeGreaterThan(0);
    expect(addresses.filter((value) => !value.endsWith(`@${ADMIN_PREVIEW_SENTINEL}`))).toEqual([]);
  });

  it("uses documentation IP ranges only", () => {
    const ips = allStrings.filter((value) => LOOKS_LIKE_IP.test(value));
    expect(ips.length).toBeGreaterThan(0);
    expect(ips.filter((value) => !DOCUMENTATION_IP.test(value))).toEqual([]);
  });

  it("has no name field, because an account stores none (ADR 0150 D3)", () => {
    expect(keys(exported.map(([, value]) => value)).filter((key) => /name/i.test(key))).toEqual([]);
  });

  it("names no consumer mail domain and none of the handoff's infrastructure", () => {
    expect(
      allStrings.filter((value) => /gmail|hotmail|outlook|live\.se|icloud|telia|yahoo|netcup|strato|n[uü]rnberg/i.test(value)),
    ).toEqual([]);
  });
});
