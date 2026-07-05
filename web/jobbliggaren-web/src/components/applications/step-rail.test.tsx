import { describe, it, expect, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { StepRail } from "./step-rail";
import { PIPELINE_ORDER } from "@/lib/applications/status";
import type { ApplicationStatus, PipelineGroupDto } from "@/lib/dto/applications";

function makeGroups(
  populated: Partial<Record<ApplicationStatus, number>>,
): PipelineGroupDto[] {
  return PIPELINE_ORDER.map((status) => ({
    status,
    count: populated[status] ?? 0,
    applications: [],
  }));
}

function getRail() {
  return screen.getByRole("group", { name: "Filtrera på steg" });
}

describe("StepRail", () => {
  it("renderar alla 10 steg som knappar, även tomma (helhetsbilden)", () => {
    render(
      <StepRail
        groups={makeGroups({ Submitted: 3 })}
        statusFilter={null}
        onToggle={() => {}}
      />,
    );
    expect(within(getRail()).getAllByRole("button")).toHaveLength(10);
  });

  it("visar antal per steg", () => {
    render(
      <StepRail
        groups={makeGroups({ Submitted: 3, Ghosted: 1 })}
        statusFilter={null}
        onToggle={() => {}}
      />,
    );
    const submitted = within(getRail()).getByRole("button", { name: /Skickad/ });
    expect(submitted.querySelector(".jp-steprail__count")).toHaveTextContent(
      "3",
    );
  });

  it("tomma steg dimmas (data-empty=true)", () => {
    render(
      <StepRail
        groups={makeGroups({ Submitted: 2 })}
        statusFilter={null}
        onToggle={() => {}}
      />,
    );
    const draft = within(getRail()).getByRole("button", { name: /Utkast/ });
    const submitted = within(getRail()).getByRole("button", { name: /Skickad/ });
    expect(draft).toHaveAttribute("data-empty", "true");
    expect(submitted).toHaveAttribute("data-empty", "false");
  });

  it("aria-pressed speglar aktivt stegfilter", () => {
    render(
      <StepRail
        groups={makeGroups({ Submitted: 2, Rejected: 1 })}
        statusFilter="Submitted"
        onToggle={() => {}}
      />,
    );
    expect(
      within(getRail()).getByRole("button", { name: /Skickad/ }),
    ).toHaveAttribute("aria-pressed", "true");
    expect(
      within(getRail()).getByRole("button", { name: /Nekad/ }),
    ).toHaveAttribute("aria-pressed", "false");
  });

  it("klick anropar onToggle med stegets status", async () => {
    const user = userEvent.setup();
    const onToggle = vi.fn();
    render(
      <StepRail
        groups={makeGroups({ Submitted: 2 })}
        statusFilter={null}
        onToggle={onToggle}
      />,
    );
    await user.click(
      within(getRail()).getByRole("button", { name: /Skickad/ }),
    );
    expect(onToggle).toHaveBeenCalledWith("Submitted");
  });

  it("terminala steg markeras (data-terminal) och Accepterad bär skiljelinjen", () => {
    render(
      <StepRail
        groups={makeGroups({ Submitted: 1, Accepted: 1 })}
        statusFilter={null}
        onToggle={() => {}}
      />,
    );
    const submitted = within(getRail()).getByRole("button", { name: /Skickad/ });
    const accepted = within(getRail()).getByRole("button", {
      name: /Accepterad/,
    });
    expect(submitted).toHaveAttribute("data-terminal", "false");
    expect(accepted).toHaveAttribute("data-terminal", "true");
    // Skiljelinje ligger på den första terminala cellen (Accepterad).
    expect(accepted).toHaveAttribute("data-divider", "true");
    expect(submitted).toHaveAttribute("data-divider", "false");
  });
});
