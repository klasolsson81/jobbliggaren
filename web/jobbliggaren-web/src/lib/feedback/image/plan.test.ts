import { describe, expect, it } from "vitest";
import { rgbaPng } from "../../../../tests/admin/png-encoder";
import { MAX_LONG_EDGE, MAX_PIXELS, MIN_LONG_EDGE, headerSize, initialSize, nextSize, sniffFormat } from "./plan";

function webpVp8x(width: number, height: number): Uint8Array {
  const bytes = new Uint8Array(30);
  bytes.set([0x52, 0x49, 0x46, 0x46], 0);
  bytes.set([0x57, 0x45, 0x42, 0x50], 8);
  bytes.set([0x56, 0x50, 0x38, 0x58], 12); // "VP8X"
  const w = width - 1;
  const h = height - 1;
  bytes.set([w & 0xff, (w >> 8) & 0xff, (w >> 16) & 0xff], 24);
  bytes.set([h & 0xff, (h >> 8) & 0xff, (h >> 16) & 0xff], 27);
  return bytes;
}

function jpegWithSof(width: number, height: number): Uint8Array {
  // SOI, an APP0 segment of 16 bytes, then SOF0 with height and width.
  const app0 = [0xff, 0xe0, 0x00, 0x10, ...new Array(14).fill(0)];
  const sof0 = [0xff, 0xc0, 0x00, 0x11, 0x08, height >> 8, height & 0xff, width >> 8, width & 0xff, 0x03];
  return new Uint8Array([0xff, 0xd8, ...app0, ...sof0, ...new Array(8).fill(0)]);
}

describe("sniffFormat", () => {
  it("knows PNG, JPEG and WebP by their first bytes", () => {
    expect(sniffFormat(rgbaPng(2, 3))).toBe("png");
    expect(sniffFormat(jpegWithSof(10, 10))).toBe("jpeg");
    expect(sniffFormat(webpVp8x(10, 10))).toBe("webp");
  });

  it.each([
    ["a PDF", new TextEncoder().encode("%PDF-1.4")],
    ["a GIF", new TextEncoder().encode("GIF89a")],
    ["an SVG", new TextEncoder().encode("<svg xmlns=")],
    ["nothing", new Uint8Array([])],
  ])("refuses %s", (_, head) => {
    expect(sniffFormat(head)).toBeNull();
  });
});

describe("headerSize", () => {
  it("reads PNG, JPEG and WebP dimensions", () => {
    expect(headerSize("png", rgbaPng(2, 3))).toEqual({ width: 2, height: 3 });
    expect(headerSize("jpeg", jpegWithSof(4000, 3000))).toEqual({ width: 4000, height: 3000 });
    expect(headerSize("webp", webpVp8x(10_000, 9_000))).toEqual({ width: 10_000, height: 9_000 });
  });

  it("gives up rather than guessing on a short head", () => {
    expect(headerSize("png", rgbaPng(2, 3).slice(0, 12))).toBeNull();
    expect(headerSize("webp", webpVp8x(10, 10).slice(0, 20))).toBeNull();
  });
});

describe("initialSize", () => {
  it("never enlarges a small screenshot", () => {
    expect(initialSize({ width: 1170, height: 2532 })).toEqual({ width: 1170, height: 2532 });
  });

  it("fits the long edge", () => {
    const size = initialSize({ width: 5120, height: 1440 });
    expect(Math.max(size.width, size.height)).toBeLessThanOrEqual(MAX_LONG_EDGE);
  });

  it("fits the pixel cap", () => {
    const size = initialSize({ width: 2560, height: 2560 });
    expect(size.width * size.height).toBeLessThanOrEqual(MAX_PIXELS);
  });
});

describe("nextSize", () => {
  it("shrinks both sides by the step", () => {
    expect(nextSize({ width: 2000, height: 1000 })).toEqual({ width: 1500, height: 750 });
  });

  it("stops at the floor", () => {
    expect(nextSize({ width: Math.ceil(MIN_LONG_EDGE / 0.75) - 2, height: 100 })).toBeNull();
  });
});
