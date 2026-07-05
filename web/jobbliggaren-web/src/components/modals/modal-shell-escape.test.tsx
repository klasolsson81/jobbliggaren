import { describe, it, expect, vi, beforeEach } from "vitest";
import { render } from "@testing-library/react";
import type { ReactNode } from "react";

import { ApplicationModalShell } from "@/components/applications/application-modal-shell";
import { ApplicationDrawerShell } from "@/components/applications/application-drawer-shell";
import { JobAdModalShell } from "@/components/job-ads/job-ad-modal-shell";
import { RouteModalShell } from "@/components/modals/route-modal-shell";

/**
 * #565 regression guard — cross-shell Escape contract (BEHAVIOURAL).
 *
 * All three modal shells run a document-level, bubble-phase keydown listener
 * that closes the modal (router.back) on Escape. When a nested Radix layer (the
 * destructive-status confirm Dialog, "Återta ansökan", a Select, or a CV form's
 * Dialog) is open, Radix handles Escape in the CAPTURE phase and calls
 * preventDefault() first — so a bare Escape used to close the WHOLE modal
 * instead of just the inner layer (data loss for the CV forms). The shells now
 * bail out when `e.defaultPrevented` is already set.
 *
 * Unlike the z-index occlusion, this is plain event logic with no paint
 * dependency, so it is a genuine in-CI behavioural test (not a proxy). The
 * capture-phase preventDefault below faithfully mimics Radix's highest-layer
 * handler; dispatching from document.body guarantees the document capture
 * listener runs before the shell's document bubble listener.
 */

const backMock = vi.fn();

vi.mock("next/navigation", async (importOriginal) => {
  const actual = await importOriginal<typeof import("next/navigation")>();
  return {
    ...actual,
    useRouter: () => ({
      back: backMock,
      push: vi.fn(),
      replace: vi.fn(),
      forward: vi.fn(),
      prefetch: vi.fn(),
      refresh: vi.fn(),
    }),
  };
});

interface ShellCase {
  name: string;
  render: (body: ReactNode) => void;
}

const shells: ShellCase[] = [
  {
    name: "ApplicationModalShell",
    render: (body) =>
      void render(
        <ApplicationModalShell title="Titel" subtitle="Undertitel">
          {body}
        </ApplicationModalShell>,
      ),
  },
  {
    // #630 PR 6 drawer inherits the same #565 Escape contract.
    name: "ApplicationDrawerShell",
    render: (body) =>
      void render(
        <ApplicationDrawerShell title="Titel" subtitle="Undertitel">
          {body}
        </ApplicationDrawerShell>,
      ),
  },
  {
    name: "JobAdModalShell",
    render: (body) =>
      void render(
        <JobAdModalShell title="Titel" company="Företag">
          {body}
        </JobAdModalShell>,
      ),
  },
  {
    name: "RouteModalShell",
    render: (body) =>
      void render(<RouteModalShell title="Titel">{body}</RouteModalShell>),
  },
];

function pressEscape() {
  document.body.dispatchEvent(
    new KeyboardEvent("keydown", {
      key: "Escape",
      bubbles: true,
      cancelable: true,
    }),
  );
}

describe.each(shells)("$name Escape contract (#565)", ({ render: renderShell }) => {
  beforeEach(() => {
    backMock.mockClear();
  });

  it("closes the modal on a plain Escape (router.back)", () => {
    renderShell(<button type="button">innehåll</button>);

    pressEscape();

    expect(backMock).toHaveBeenCalledTimes(1);
  });

  it("does NOT close the modal when a nested layer already handled Escape (defaultPrevented)", () => {
    renderShell(<button type="button">innehåll</button>);

    // Mimic Radix's highest-layer handler: preventDefault in the CAPTURE phase,
    // before the shell's bubble-phase listener sees the event.
    const capturePreventDefault = (event: KeyboardEvent) => {
      if (event.key === "Escape") event.preventDefault();
    };
    document.addEventListener("keydown", capturePreventDefault, { capture: true });

    pressEscape();

    document.removeEventListener("keydown", capturePreventDefault, {
      capture: true,
    });

    expect(backMock).not.toHaveBeenCalled();
  });
});
