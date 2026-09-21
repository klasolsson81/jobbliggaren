"use client";

import type { ReactNode } from "react";
import { useFocusOnMount } from "@/lib/hooks/use-focus-on-mount";

// The h1 of a login step reached by a route change, focused once when it mounts.
//
// Not the input (design-reviewer, #1738): everything that makes the step completable sits ABOVE
// the field, and a screen-reader user whose focus starts in the input has to navigate backwards to
// find it; on a narrow screen autofocus raises the keyboard over the very text that says where the
// code is. `one-time-code` autofill needs no programmatic focus: it is offered when the user
// focuses the field. A panel that mounts later in the tree takes focus after this, which is the
// order wanted when the step opens on an outcome.
export function FocusHeading({ children }: { children: ReactNode }) {
  const headingRef = useFocusOnMount<HTMLHeadingElement>();

  return (
    <h1 ref={headingRef} tabIndex={-1} className="text-h1 font-bold text-heading-1">
      {children}
    </h1>
  );
}
