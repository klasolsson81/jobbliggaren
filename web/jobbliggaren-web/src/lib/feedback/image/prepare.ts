import {
  HEAD_BYTES,
  MAX_INPUT_BYTES,
  MAX_INPUT_PIXELS,
  MAX_REDRAWS,
  TARGET_BYTES,
  headerSize,
  initialSize,
  nextSize,
  sniffFormat,
  type Size,
} from "./plan";

/** A decoded image the codec can draw from; `release` frees what decoding held. */
export type DecodedImage = Size & { readonly release: () => void };

/**
 * The browser's image work behind a port, so the plan can be tested without a canvas. `decode` applies the
 * image's own orientation; `encodePng` draws it at the given size and encodes a PNG, which carries none of
 * the original's metadata.
 */
export type ScreenshotCodec<T extends DecodedImage = DecodedImage> = {
  readonly decode: (file: Blob) => Promise<T>;
  readonly encodePng: (image: T, size: Size) => Promise<Blob>;
};

export type PreparedScreenshot =
  | { readonly kind: "ready"; readonly blob: Blob; readonly width: number; readonly height: number }
  | { readonly kind: "refused"; readonly reason: "type" | "unreadable" | "tooLarge" | "tooDetailed" };

/**
 * Turns a picked or pasted file into the PNG that is sent, or a refusal. There is no fallback to the
 * original file: the redraw is what keeps metadata such as a location on the device, so a file the
 * browser cannot redraw is refused rather than sent as it came.
 */
export async function prepareScreenshot<T extends DecodedImage>(
  file: Blob,
  codec: ScreenshotCodec<T>,
): Promise<PreparedScreenshot> {
  if (file.size === 0) return { kind: "refused", reason: "unreadable" };
  if (file.size > MAX_INPUT_BYTES) return { kind: "refused", reason: "tooLarge" };

  const head = new Uint8Array(await file.slice(0, HEAD_BYTES).arrayBuffer());
  const format = sniffFormat(head);
  if (format === null) return { kind: "refused", reason: "type" };
  const stated = headerSize(format, head);
  if (stated !== null && stated.width * stated.height > MAX_INPUT_PIXELS) return { kind: "refused", reason: "tooLarge" };

  let image: T;
  try {
    image = await codec.decode(file);
  } catch {
    return { kind: "refused", reason: "unreadable" };
  }
  try {
    if (image.width * image.height > MAX_INPUT_PIXELS) return { kind: "refused", reason: "tooLarge" };
    let size: Size | null = initialSize(image);
    for (let draw = 0; size !== null && draw <= MAX_REDRAWS; draw++) {
      const blob = await codec.encodePng(image, size);
      if (blob.size <= TARGET_BYTES) return { kind: "ready", blob, width: size.width, height: size.height };
      size = nextSize(size);
    }
    return { kind: "refused", reason: "tooDetailed" };
  } catch {
    return { kind: "refused", reason: "unreadable" };
  } finally {
    image.release();
  }
}
