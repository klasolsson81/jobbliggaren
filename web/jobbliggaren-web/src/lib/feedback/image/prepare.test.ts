import { describe, expect, it, vi } from "vitest";
import { rgbaPng } from "../../../../tests/admin/png-encoder";
import { MAX_INPUT_BYTES, MAX_REDRAWS, TARGET_BYTES, type Size } from "./plan";
import { prepareScreenshot, type DecodedImage, type ScreenshotCodec } from "./prepare";

/**
 * A codec that decodes to a configured size and encodes a blob whose size the test decides, so the plan
 * is measured without a canvas. The real codec runs in the feedback Playwright harness.
 */
function fakeCodec(options: { decoded?: Size; encodeBytes?: (size: Size, draw: number) => number; decodeFails?: boolean } = {}) {
  const release = vi.fn();
  const draws: Size[] = [];
  const codec: ScreenshotCodec = {
    decode: vi.fn(async (): Promise<DecodedImage> => {
      if (options.decodeFails) throw new Error("decode");
      return { ...(options.decoded ?? { width: 1920, height: 1080 }), release };
    }),
    encodePng: vi.fn(async (_image: DecodedImage, size: Size) => {
      draws.push(size);
      const bytes = options.encodeBytes?.(size, draws.length - 1) ?? 1000;
      return new Blob([new Uint8Array(bytes)], { type: "image/png" });
    }),
  };
  return { codec, release, draws };
}

const png = () => new Blob([rgbaPng(2, 3)], { type: "image/png" });

describe("prepareScreenshot", () => {
  it("sends the first drawing when it is within target", async () => {
    const { codec, release, draws } = fakeCodec();
    const prepared = await prepareScreenshot(png(), codec);
    expect(prepared).toMatchObject({ kind: "ready", width: 1920, height: 1080 });
    expect(draws).toHaveLength(1);
    expect(release).toHaveBeenCalledTimes(1);
  });

  it("redraws smaller until the PNG fits", async () => {
    const { codec, draws } = fakeCodec({ encodeBytes: (_, draw) => (draw < 2 ? TARGET_BYTES + 1 : TARGET_BYTES) });
    expect(await prepareScreenshot(png(), codec)).toMatchObject({ kind: "ready", width: 1080, height: 607 });
    expect(draws.map((size) => size.width)).toEqual([1920, 1440, 1080]);
  });

  it("refuses an image too detailed to fit, without sending the original", async () => {
    const { codec, release, draws } = fakeCodec({ decoded: { width: 2560, height: 1440 }, encodeBytes: () => TARGET_BYTES + 1 });
    expect(await prepareScreenshot(png(), codec)).toEqual({ kind: "refused", reason: "tooDetailed" });
    expect(draws.length).toBeLessThanOrEqual(MAX_REDRAWS + 1);
    expect(release).toHaveBeenCalledTimes(1);
  });

  it("refuses a file that is not PNG, JPEG or WebP before decoding", async () => {
    const { codec } = fakeCodec();
    expect(await prepareScreenshot(new Blob(["%PDF-1.4 x"]), codec)).toEqual({ kind: "refused", reason: "type" });
    expect(codec.decode).not.toHaveBeenCalled();
  });

  it("refuses a file over the input cap before reading it", async () => {
    const { codec } = fakeCodec();
    const huge = { size: MAX_INPUT_BYTES + 1, slice: vi.fn() } as unknown as Blob;
    expect(await prepareScreenshot(huge, codec)).toEqual({ kind: "refused", reason: "tooLarge" });
    expect(huge.slice).not.toHaveBeenCalled();
  });

  it("refuses an image whose header states too many pixels, before decoding", async () => {
    const { codec } = fakeCodec();
    const head = rgbaPng(2, 3);
    new DataView(head.buffer, head.byteOffset).setUint32(16, 10_000);
    new DataView(head.buffer, head.byteOffset).setUint32(20, 10_000);
    expect(await prepareScreenshot(new Blob([head]), codec)).toEqual({ kind: "refused", reason: "tooLarge" });
    expect(codec.decode).not.toHaveBeenCalled();
  });

  it("refuses an image the browser cannot decode", async () => {
    const { codec } = fakeCodec({ decodeFails: true });
    expect(await prepareScreenshot(png(), codec)).toEqual({ kind: "refused", reason: "unreadable" });
  });

  it("refuses an empty file", async () => {
    const { codec } = fakeCodec();
    expect(await prepareScreenshot(new Blob([]), codec)).toEqual({ kind: "refused", reason: "unreadable" });
  });
});
