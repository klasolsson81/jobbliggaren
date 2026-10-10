"use client";

import { useId, useState } from "react";
import { ChevronDown } from "lucide-react";

/**
 * The show/hide toggle of the preamble notice (#2083). It holds only the open state: the CV text
 * arrives as `children`, already rendered on the server, so it is never a prop of this island.
 */
export function CvPreambleDisclosure({
  showLabel,
  hideLabel,
  children,
}: {
  showLabel: string;
  hideLabel: string;
  children: React.ReactNode;
}) {
  const [open, setOpen] = useState(false);
  const panelId = useId();

  return (
    <>
      <button
        type="button"
        className="jp-btn jp-btn--sm jp-btn--info-outline jp-cvnotice__toggle"
        aria-expanded={open}
        aria-controls={panelId}
        onClick={() => setOpen((value) => !value)}
      >
        {open ? hideLabel : showLabel}
        <ChevronDown size={16} aria-hidden="true" />
      </button>
      <div id={panelId} className="jp-cvnotice__panel" hidden={!open}>
        {children}
      </div>
    </>
  );
}
