import type { DecodedImage, ScreenshotCodec } from "./prepare";
import type { Size } from "./plan";

/**
 * The real codec: the browser decodes the image with its own orientation applied, draws it on a canvas and
 * encodes a PNG. Exercised by the feedback Playwright harness; unit tests use a fake behind the same port.
 */

async function decodeToSource(file: Blob): Promise<{ source: CanvasImageSource; size: Size; release: () => void }> {
  if (typeof createImageBitmap === "function") {
    try {
      const bitmap = await createImageBitmap(file, { imageOrientation: "from-image" });
      return { source: bitmap, size: { width: bitmap.width, height: bitmap.height }, release: () => bitmap.close() };
    } catch {
      // Some browsers refuse the options bag; the element path below applies orientation by default.
    }
  }
  const url = URL.createObjectURL(file);
  const element = new Image();
  element.src = url;
  try {
    await element.decode();
  } catch (error) {
    URL.revokeObjectURL(url);
    throw error;
  }
  return {
    source: element,
    size: { width: element.naturalWidth, height: element.naturalHeight },
    release: () => URL.revokeObjectURL(url),
  };
}

type Decoded = DecodedImage & { readonly source: CanvasImageSource };

function canvasOf(size: Size): { context: CanvasRenderingContext2D | OffscreenCanvasRenderingContext2D; toPng: () => Promise<Blob> } {
  if (typeof OffscreenCanvas === "function") {
    const canvas = new OffscreenCanvas(size.width, size.height);
    const context = canvas.getContext("2d");
    if (context === null) throw new Error("No 2d context.");
    return { context, toPng: () => canvas.convertToBlob({ type: "image/png" }) };
  }
  const canvas = document.createElement("canvas");
  canvas.width = size.width;
  canvas.height = size.height;
  const context = canvas.getContext("2d");
  if (context === null) throw new Error("No 2d context.");
  return {
    context,
    toPng: () =>
      new Promise<Blob>((resolve, reject) =>
        canvas.toBlob((blob) => (blob === null ? reject(new Error("Encoding failed.")) : resolve(blob)), "image/png"),
      ),
  };
}

export const browserCodec: ScreenshotCodec<Decoded> = {
  async decode(file) {
    const { source, size, release } = await decodeToSource(file);
    return { ...size, release, source };
  },
  async encodePng(image, size) {
    const { context, toPng } = canvasOf(size);
    context.imageSmoothingEnabled = true;
    context.imageSmoothingQuality = "high";
    context.drawImage(image.source, 0, 0, size.width, size.height);
    return toPng();
  },
};
