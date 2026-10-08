import { deflateSync } from "node:zlib";

const SIGNATURE = Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]);

function crc32(bytes: Buffer): number {
  let crc = 0xffffffff;
  for (const byte of bytes) {
    crc ^= byte;
    for (let bit = 0; bit < 8; bit++)
      crc = (crc >>> 1) ^ ((crc & 1) === 1 ? 0xedb88320 : 0);
  }
  return (crc ^ 0xffffffff) >>> 0;
}

function chunk(type: string, bytes: Buffer): Buffer {
  const result = Buffer.alloc(bytes.length + 12);
  result.writeUInt32BE(bytes.length, 0);
  result.write(type, 4, 4, "ascii");
  bytes.copy(result, 8);
  result.writeUInt32BE(crc32(result.subarray(4, result.length - 4)), result.length - 4);
  return result;
}

// NormalizeAsync_AStaticAllowedImage_EmitsOneLosslessRgba8Png pins this successful read shape at the backend writer.
/** A fictional page drawn directly into RGBA scanlines: no identifiers or captured personal data. */
export function rgbaPng(width: number, height: number): Uint8Array<ArrayBuffer> {
  const stride = 1 + width * 4;
  const scanlines = Buffer.alloc(stride * height);
  const headerColor = [36, 76, 68] as const;
  const sidebarColor = [227, 234, 230] as const;
  const edgeColor = [133, 62, 50] as const;
  const lineColor = [93, 121, 111] as const;
  const cardColor = [240, 243, 241] as const;
  const backgroundColor = [255, 255, 255] as const;
  for (let y = 0; y < height; y++) {
    const start = y * stride;
    scanlines[start] = 0;
    for (let x = 0; x < width; x++) {
      let color: readonly [number, number, number];
      if (y < 96) color = headerColor;
      else if (x < 360) color = sidebarColor;
      else if (x >= width - 24 || y >= height - 24) color = edgeColor;
      else if (x > 420 && x < width - 120 && y > 180 && (y - 180) % 160 < 12) color = lineColor;
      else if (x > 420 && x < width - 120 && y > 180 && (y - 180) % 160 < 108) color = cardColor;
      else color = backgroundColor;
      const pixel = start + 1 + x * 4;
      scanlines[pixel] = color[0];
      scanlines[pixel + 1] = color[1];
      scanlines[pixel + 2] = color[2];
      scanlines[pixel + 3] = 255;
    }
  }
  const header = Buffer.alloc(13);
  header.writeUInt32BE(width, 0);
  header.writeUInt32BE(height, 4);
  header[8] = 8;
  header[9] = 6;
  return new Uint8Array(Buffer.concat([
    SIGNATURE, chunk("IHDR", header), chunk("IDAT", deflateSync(scanlines)), chunk("IEND", Buffer.alloc(0)),
  ]));
}
