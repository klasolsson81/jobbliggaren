import { describe, expect, it } from "vitest";
import { checkNewAddress } from "./new-address";

const CURRENT = "anna@exempel.se";
const TAB = String.fromCharCode(9);
const ZERO_WIDTH_SPACE = String.fromCharCode(0x200b);
const LONE_SURROGATE = String.fromCharCode(0xd800);

describe("checkNewAddress", () => {
  it("admits an address the backend takes, trimmed, and IDN local parts with it", () => {
    expect(checkNewAddress("  ny@exempel.se  ", CURRENT)).toEqual({ ok: true, address: "ny@exempel.se" });
    // #1781: the backend stores what StorableAddress admits, and this local part is one of them.
    expect(checkNewAddress("björn@exempel.se", CURRENT)).toEqual({
      ok: true,
      address: "björn@exempel.se",
    });
  });

  it("refuses an empty field as required, before anything else is judged", () => {
    expect(checkNewAddress("   ", CURRENT)).toEqual({ ok: false, reason: "required" });
  });

  it.each([
    ["no @", "ny.exempel.se"],
    ["@ first", "@exempel.se"],
    ["@ last", "ny@"],
    ["two @", "ny@x@exempel.se"],
    ["over 256", `${"a".repeat(250)}@exempel.se`],
  ])("refuses what the backend's validator refuses: %s", (_label, input) => {
    expect(checkNewAddress(input, CURRENT)).toEqual({ ok: false, reason: "invalid" });
  });

  it.each([
    ["a tab", `ny${TAB}x@exempel.se`],
    ["a space", "ny x@exempel.se"],
    ["a format character", `ny${ZERO_WIDTH_SPACE}@exempel.se`],
    ["a lone surrogate", `ny${LONE_SURROGATE}@exempel.se`],
    ["an astral character, a surrogate pair", `ny${String.fromCodePoint(0x1f600)}@exempel.se`],
  ])("refuses what StorableAddress refuses after a code is spent: %s", (_label, input) => {
    expect(checkNewAddress(input, CURRENT)).toEqual({ ok: false, reason: "invalid" });
  });

  it("admits exactly 256 characters, the backend's bound", () => {
    const address = `${"a".repeat(245)}@exempel.se`;
    expect(address).toHaveLength(256);
    expect(checkNewAddress(address, CURRENT)).toEqual({ ok: true, address });
  });

  it("refuses the account's own address, in the one comparison form (case, spaces, NFC)", () => {
    expect(checkNewAddress(" ANNA@exempel.SE ", CURRENT)).toEqual({ ok: false, reason: "same" });
    const composed = "ö@exempel.se";
    const decomposed = composed.normalize("NFD");
    expect(decomposed).not.toBe(composed);
    expect(checkNewAddress(decomposed, composed)).toEqual({ ok: false, reason: "same" });
  });
});
