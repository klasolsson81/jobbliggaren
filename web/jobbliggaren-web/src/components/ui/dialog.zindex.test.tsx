import { describe, it, expect } from "vitest";
import { render } from "@testing-library/react";

import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "./dialog";

/**
 * #565 regression guard — CONTRACT level.
 *
 * jsdom has no paint/stacking model, so the actual occlusion bug (a portalled
 * dialog painting BEHIND the z-80 `.jp-modal-scrim` of an intercepting-route
 * modal, with an opaque panel) is structurally invisible to a unit test — which
 * is exactly why the bug shipped green. This guard therefore pins the STRUCTURAL
 * contract that made the bug possible: the dialog overlay AND content must carry
 * a z-index strictly above the modal scrim. If dialog.tsx regresses to the
 * shadcn default (z-50 < 80) this fails.
 *
 * The user-visible occlusion assertion (real browser hit-test) lives in the
 * Playwright spec — tests/e2e/applications.spec.ts. That spec is not wired into
 * CI yet (see the #565 CI-gap note); this in-CI vitest test is the actual gate.
 */

// `.jp-modal-scrim { z-index: 80 }` (src/app/globals.css). The intercepting-
// route modal that hosts the confirm dialog paints at this level with an opaque
// panel — anything the dialog needs to sit above.
const SCRIM_Z_INDEX = 80;

/** Numeric value of a Tailwind z-index utility (`z-90` or `z-[90]`), else NaN. */
function zIndexUtility(el: Element | null): number {
  if (!el) return NaN;
  const cls = el.getAttribute("class") ?? "";
  const match = cls.match(/(?:^|\s)z-\[?(\d+)\]?(?:\s|$)/);
  return match ? Number(match[1]) : NaN;
}

describe("Dialog z-index contract (#565)", () => {
  it("renders the overlay and content above the modal scrim (z-index > 80)", () => {
    render(
      <Dialog open>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Titel</DialogTitle>
            <DialogDescription>Beskrivning</DialogDescription>
          </DialogHeader>
        </DialogContent>
      </Dialog>,
    );

    const overlay = document.querySelector('[data-slot="dialog-overlay"]');
    const content = document.querySelector('[data-slot="dialog-content"]');

    expect(overlay).not.toBeNull();
    expect(content).not.toBeNull();

    expect(zIndexUtility(overlay)).toBeGreaterThan(SCRIM_Z_INDEX);
    expect(zIndexUtility(content)).toBeGreaterThan(SCRIM_Z_INDEX);
  });
});
