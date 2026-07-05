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
 * a z-index strictly above every opaque host surface a dialog can open from.
 * If dialog.tsx regresses to the shadcn default (z-50) this fails.
 *
 * #630 PR 7 extends the ladder: dialogs (Logga uppföljning / Slutför och
 * skicka) now also open from INSIDE the /ansokningar detail drawer
 * (`.jp-appdrawer`, z-100 band) — the same #565 bug class, inverted host. The
 * contract is therefore pinned against the HIGHEST host band (100), not just
 * the modal scrim (80).
 *
 * The user-visible occlusion assertion (real browser hit-test) lives in the
 * Playwright spec — tests/e2e/applications.spec.ts. That spec is not wired into
 * CI yet (see the #565 CI-gap note); this in-CI vitest test is the actual gate.
 */

// `.jp-appdrawer { z-index: 100 }` (src/app/globals.css) — the highest opaque
// surface that can host a dialog (the z-80 `.jp-modal-scrim` modal is the
// other). Anything the dialog needs to sit above; the toast (200) stays above.
const APPDRAWER_Z_INDEX = 100;

/** Numeric value of a Tailwind z-index utility (`z-90` or `z-[90]`), else NaN. */
function zIndexUtility(el: Element | null): number {
  if (!el) return NaN;
  const cls = el.getAttribute("class") ?? "";
  const match = cls.match(/(?:^|\s)z-\[?(\d+)\]?(?:\s|$)/);
  return match ? Number(match[1]) : NaN;
}

describe("Dialog z-index contract (#565 / #630 PR 7)", () => {
  it("renders the overlay and content above the detail drawer band (z-index > 100)", () => {
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

    expect(zIndexUtility(overlay)).toBeGreaterThan(APPDRAWER_Z_INDEX);
    expect(zIndexUtility(content)).toBeGreaterThan(APPDRAWER_Z_INDEX);
  });
});
