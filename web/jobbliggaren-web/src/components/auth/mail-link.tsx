import type { ReactNode } from "react";

/** A text link in running copy: the brand colour and an underline, never colour alone. */
export const TEXT_LINK = "text-brand-700 underline underline-offset-2";

/**
 * Renders a message's `<mail>` chunk as a mailto link. The address has one home, the message itself:
 * the link reads it out of the chunk.
 */
export function mailLink(chunks: ReactNode) {
  const address = Array.isArray(chunks) ? chunks.join("") : String(chunks);
  return (
    <a href={`mailto:${address}`} className={TEXT_LINK}>
      {chunks}
    </a>
  );
}

/**
 * The same chunk as plain text, for copy that is read as a description (a field's hint): a link there
 * would put a tab stop between the field and the control after it (design-reviewer, #1740).
 */
export function mailText(chunks: ReactNode) {
  return chunks;
}
