import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { JobAdModalShell } from "./job-ad-modal-shell";

vi.mock("next/navigation", () => ({
  useRouter: () => ({ back: vi.fn() }),
}));

describe("JobAdModalShell", () => {
  it("names the dialog by its title", () => {
    render(
      <JobAdModalShell title="Systemutvecklare" company="Acme AB">
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
  it("sets NO aria-describedby", () => {
    render(
      <JobAdModalShell title="Systemutvecklare" company="Acme AB">
        <div className="jp-modal__body">x</div>
      </JobAdModalShell>
    );
    expect(screen.getByRole("dialog")).not.toHaveAttribute("aria-describedby");
  });
});
