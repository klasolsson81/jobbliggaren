import { describe, expect, it } from "vitest";
import { comparableAddress } from "./comparable-address";

// "björn" spelled with a precomposed ö (U+00F6) and with o + a combining diaeresis (U+0308): the
// same address to a reader, two different strings to a byte comparison.
const PRECOMPOSED = `bj${String.fromCodePoint(0xf6)}rn@exempel.se`;
const DECOMPOSED = `bjo${String.fromCodePoint(0x308)}rn@exempel.se`;

describe("comparableAddress", () => {
  it("treats the two Unicode spellings of one address as the same", () => {
    expect(PRECOMPOSED).not.toBe(DECOMPOSED);
    expect(comparableAddress(PRECOMPOSED)).toBe(comparableAddress(DECOMPOSED));
  });

  it("ignores case and surrounding whitespace", () => {
    expect(comparableAddress("  Klas.Olsson@Exempel.SE ")).toBe("klas.olsson@exempel.se");
  });

  it("still tells two different addresses apart", () => {
    expect(comparableAddress("bjorn@exempel.se")).not.toBe(comparableAddress(PRECOMPOSED));
  });
});
