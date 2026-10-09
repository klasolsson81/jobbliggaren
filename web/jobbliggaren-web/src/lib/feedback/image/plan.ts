/**
 * The screenshot's bounds in the browser (#1979 PR3, ADR 0156 PR3 amendment). The server re-encodes every
 * image as lossless 8-bit RGBA PNG and refuses one over 5 MiB, and Caddy gives the whole upload 10 seconds,
 * so the browser sends a PNG it has drawn itself, small enough for both. Drawing it also drops every piece
 * of metadata, the location included, before anything leaves the device.
 */

/** A file larger than this is refused before it is read. */
export const MAX_INPUT_BYTES = 25 * 1024 * 1024;
/** An image with more pixels than this, by its header, is refused before it is decoded. */
export const MAX_INPUT_PIXELS = 50_000_000;
/** The first drawing fits inside both of these, never scaled up. */
export const MAX_LONG_EDGE = 2_560;
export const MAX_PIXELS = 4_000_000;
/** The PNG the browser sends is at most this; at 2 Mbit/s it uploads in about 6 seconds. */
export const TARGET_BYTES = 1.5 * 1024 * 1024;
/** Each redraw that is still over target shrinks both sides by this, at most this many times… */
export const SCALE_STEP = 0.75;
export const MAX_REDRAWS = 6;
/** …and never below this long edge: past it the image no longer shows what the user meant. */
export const MIN_LONG_EDGE = 720;

/** How many leading bytes are read to tell the format and, where it is cheap, the dimensions. */
export const HEAD_BYTES = 64 * 1024;

export type ImageFormat = "png" | "jpeg" | "webp";
export type Size = { readonly width: number; readonly height: number };

/** PNG, JPEG or WebP by their first bytes; anything else is not accepted. */
export function sniffFormat(head: Uint8Array): ImageFormat | null {
  const at = (signature: readonly number[], offset = 0) => signature.every((byte, index) => head[offset + index] === byte);
  if (at([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a])) return "png";
  if (at([0xff, 0xd8, 0xff])) return "jpeg";
  if (at([0x52, 0x49, 0x46, 0x46]) && at([0x57, 0x45, 0x42, 0x50], 8)) return "webp";
  return null;
}

const u16be = (b: Uint8Array, i: number) => (b[i]! << 8) | b[i + 1]!;
const u32be = (b: Uint8Array, i: number) => ((b[i]! << 24) >>> 0) + (b[i + 1]! << 16) + (b[i + 2]! << 8) + b[i + 3]!;
const u16le = (b: Uint8Array, i: number) => b[i]! | (b[i + 1]! << 8);
const u24le = (b: Uint8Array, i: number) => b[i]! | (b[i + 1]! << 8) | (b[i + 2]! << 16);

/**
 * The dimensions a header states, or null when the head does not reach them. Only used to refuse an
 * obviously huge image before decoding it; a decode still decides what the image really is.
 */
export function headerSize(format: ImageFormat, head: Uint8Array): Size | null {
  if (format === "png") {
    // IHDR is the first chunk: width and height at bytes 16 and 20.
    return head.length >= 24 ? { width: u32be(head, 16), height: u32be(head, 20) } : null;
  }
  if (format === "webp") {
    if (head.length < 30) return null;
    const chunk = String.fromCharCode(head[12]!, head[13]!, head[14]!, head[15]!);
    if (chunk === "VP8X") return { width: u24le(head, 24) + 1, height: u24le(head, 27) + 1 };
    if (chunk === "VP8L") {
      const bits = head[21]! | (head[22]! << 8) | (head[23]! << 16) | (head[24]! << 24);
      return { width: (bits & 0x3fff) + 1, height: ((bits >>> 14) & 0x3fff) + 1 };
    }
    if (chunk === "VP8 ") return { width: u16le(head, 26) & 0x3fff, height: u16le(head, 28) & 0x3fff };
    return null;
  }
  // JPEG: walk the markers to a start-of-frame segment.
  let offset = 2;
  while (offset + 9 < head.length) {
    if (head[offset] !== 0xff) return null;
    const marker = head[offset + 1]!;
    if (marker === 0xd8 || (marker >= 0xd0 && marker <= 0xd7) || marker === 0x01) {
      offset += 2;
      continue;
    }
    const length = u16be(head, offset + 2);
    const isStartOfFrame = marker >= 0xc0 && marker <= 0xcf && marker !== 0xc4 && marker !== 0xc8 && marker !== 0xcc;
    if (isStartOfFrame) return { width: u16be(head, offset + 7), height: u16be(head, offset + 5) };
    offset += 2 + length;
  }
  return null;
}

/** The first drawing: the decoded size, shrunk to fit the long edge and the pixel cap, never enlarged. */
export function initialSize(decoded: Size): Size {
  const byEdge = MAX_LONG_EDGE / Math.max(decoded.width, decoded.height);
  const byPixels = Math.sqrt(MAX_PIXELS / (decoded.width * decoded.height));
  const scale = Math.min(1, byEdge, byPixels);
  return { width: Math.max(1, Math.floor(decoded.width * scale)), height: Math.max(1, Math.floor(decoded.height * scale)) };
}

/** The next, smaller drawing, or null when it would go below the floor. */
export function nextSize(size: Size): Size | null {
  const next = { width: Math.floor(size.width * SCALE_STEP), height: Math.floor(size.height * SCALE_STEP) };
  return Math.max(next.width, next.height) < MIN_LONG_EDGE || next.width < 1 || next.height < 1 ? null : next;
}
