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
 * #630 PR 7 extended the ladder: dialogs (Logga uppföljning / Slutför och
 * skicka) could open from INSIDE the /ansokningar detail drawer (z-100 band)
 * — the same #565 bug class, inverted host. The drawer was retired 2026-07-10
 * (ADR 0092 Livscykel-amendment), but the contract stays pinned against that
 * HISTORICAL maximum host band (100), not just the modal scrim (80) — the
 * mobile-nav `.jp-drawer` still occupies the 99/100 band and lowering the
 * floor would only invite regressions.
 *
 * The browser-level companion lives in tests/e2e/applications.spec.ts and is now
 * wired into CI (#813). Correction while we were in here: that spec used to claim
 * a real `.click()` was the occlusion hit-test. **It is not.** With a Radix modal
 * open, `pointer-events: none` is set on body AND on the scrim, so the scrim
 * cannot intercept the click no matter what it paints over — a dialog regressed to
 * z-50, painting UNDER the z-80 scrim, still clicks through green (verified by
 * mutation). #565's reachable symptom is therefore "invisible", not "unclickable",
 * and the e2e spec now asserts the PAINT order (computed z-index in a real browser,
 * real cascade) instead. That assertion does fail under the z-50 regression.
 * This vitest guard remains the in-CI contract gate; the e2e one is observe-only.
 */

// The historical maximum opaque host band (the retired detail drawer's
// z-100; the mobile-nav `.jp-drawer` still sits at 99/100). Today's dialog
// host is the z-80 `.jp-modal-scrim` modal — the floor deliberately stays at
// the historical maximum; the toast (200) stays above.
const MAX_HOST_Z_INDEX = 100;

/** Numeric value of a Tailwind z-index utility (`z-90` or `z-[90]`), else NaN. */
function zIndexUtility(el: Element | null): number {
  if (!el) return NaN;
  const cls = el.getAttribute("class") ?? "";
  const match = cls.match(/(?:^|\s)z-\[?(\d+)\]?(?:\s|$)/);
  return match ? Number(match[1]) : NaN;
}

describe("Dialog z-index contract (#565 / #630 PR 7)", () => {
  it("renders the overlay and content above the historical max host band (z-index > 100)", () => {
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

    expect(zIndexUtility(overlay)).toBeGreaterThan(MAX_HOST_Z_INDEX);
    expect(zIndexUtility(content)).toBeGreaterThan(MAX_HOST_Z_INDEX);
  });
});
