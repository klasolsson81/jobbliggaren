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
