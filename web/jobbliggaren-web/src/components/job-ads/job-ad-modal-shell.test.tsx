import { describe, it, expect, vi } from "vitest";
import { StrictMode } from "react";
import Link from "next/link";
import { render, screen } from "@testing-library/react";
import { JobAdModalShell } from "./job-ad-modal-shell";

vi.mock("next/navigation", () => ({
  useRouter: () => ({ back: vi.fn() }),
}));

describe("JobAdModalShell", () => {
  it("names the dialog by its title", () => {
    render(
      <JobAdModalShell title="Systemutvecklare" company="Acme AB" meta={null}>
        <div className="jp-modal__body">x</div>
      </JobAdModalShell>
    );
    expect(screen.getByRole("dialog", { name: "Systemutvecklare" })).toHaveAttribute(
      "aria-modal",
      "true"
    );
  });

  // #1828 (design-reviewer B2): the dialog carries no description — it pointed at the whole
  // ad text, which a screen reader then read as one flat string before anything else.
  it("renders the header's date line under the company and follows the viewport (#1963)", () => {
    render(
      <JobAdModalShell
        title="Systemutvecklare"
        company="Acme AB"
        meta={<div className="jp-modal__meta">Sista ansökningsdag 29 okt. 2026</div>}
      >
        <div className="jp-modal__body">x</div>
      </JobAdModalShell>
    );
    const dialog = screen.getByRole("dialog", { name: "Systemutvecklare" });
    expect(dialog.querySelector(".jp-modal__head .jp-modal__meta")).toHaveTextContent(
      "Sista ansökningsdag 29 okt. 2026"
    );
    expect(dialog).toHaveClass("jp-modal--sheet");
    expect(dialog.parentElement).toHaveClass("jp-modal-scrim--sheet");
  });

  it("sets NO aria-describedby", () => {
    render(
      <JobAdModalShell title="Systemutvecklare" company="Acme AB" meta={null}>
        <div className="jp-modal__body">x</div>
      </JobAdModalShell>
    );
    expect(screen.getByRole("dialog")).not.toHaveAttribute("aria-describedby");
  });
});

describe("JobAdModalShell focus lifecycle", () => {
  it("retains the opener through Strict Mode effect replay", () => {
    render(<Link href="/jobb/test-ad">Open ad</Link>);
    const opener = screen.getByRole("link", { name: "Open ad" });
    opener.focus();
    const modal = render(
      <StrictMode>
        <JobAdModalShell title="Systemutvecklare" company="Exempelbolaget" meta={null}>
          <div className="jp-modal__body">Evidence</div>
        </JobAdModalShell>
      </StrictMode>
    );
    expect(screen.getByRole("button", { name: "Stäng" })).toHaveFocus();
    modal.unmount();
    expect(opener).toHaveFocus();
  });

  it("does not focus an opener removed by navigation", () => {
    const list = render(<Link href="/jobb/test-ad">Open ad</Link>);
    const opener = screen.getByRole("link", { name: "Open ad" });
    opener.focus();
    const modal = render(
      <JobAdModalShell title="Systemutvecklare" company="Exempelbolaget" meta={null}>
        <div className="jp-modal__body">Evidence</div>
      </JobAdModalShell>
    );
    list.unmount();
    modal.unmount();
    expect(opener.isConnected).toBe(false);
    expect(opener).not.toHaveFocus();
  });
});
