import { rgbaPng } from "./png-encoder";

const content = Buffer.from(rgbaPng(4096, 2304));
if (content.length > 5 * 1024 * 1024) throw new Error("The screenshot fixture exceeds the normalized image limit.");

/** Metadata is read from the same IHDR bytes the browser decodes, never borrowed from another image. */
export const FEEDBACK_SCREENSHOT = {
  content,
  width: content.readUInt32BE(16),
  height: content.readUInt32BE(20),
} as const;
