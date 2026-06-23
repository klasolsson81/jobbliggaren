import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { JobbMatchGradeFilter } from "./jobb-match-grade-filter";

// STEG 5 (grade-filter) — beteende-kontraktet (Klas-låst produktmodell):
// switch på/av, "Av = noll grader", på → alla tre, avmarkera-alla → av.

const onChange = vi.fn();

beforeEach(() => {
  onChange.mockClear();
});

describe("JobbMatchGradeFilter — switch + kryssrutor", () => {
  it("switchen är AV (aria-checked=false) när inga grader är valda", () => {
    render(<JobbMatchGradeFilter selected={[]} onChange={onChange} />);
    const sw = screen.getByRole("switch", { name: "Matchning" });
    expect(sw).toHaveAttribute("aria-checked", "false");
    // Av-läget döljer kryssrute-gruppen (inga grad-kryssrutor renderas).
    expect(screen.queryByRole("group")).toBeNull();
    expect(screen.queryByRole("checkbox")).toBeNull();
  });

  it("slå PÅ switchen → emitterar ALLA TRE grader (ordinal ordning)", async () => {
    const user = userEvent.setup();
    render(<JobbMatchGradeFilter selected={[]} onChange={onChange} />);
    await user.click(screen.getByRole("switch", { name: "Matchning" }));
    expect(onChange).toHaveBeenCalledWith(["Basic", "Good", "Strong"]);
  });

  it("switchen är PÅ och kryssrute-gruppen visas när minst en grad är vald", () => {
    render(
      <JobbMatchGradeFilter
        selected={["Basic", "Good", "Strong"]}
        onChange={onChange}
      />,
    );
    const sw = screen.getByRole("switch", { name: "Matchning" });
    expect(sw).toHaveAttribute("aria-checked", "true");
    // Grupp-label + tre kryssrutor (Grund/Bra/Stark), alla markerade.
    expect(
      screen.getByRole("group", { name: "Visa matchningsgrader" }),
    ).toBeInTheDocument();
    const boxes = screen.getAllByRole("checkbox");
    expect(boxes).toHaveLength(3);
    for (const box of boxes) {
      expect(box).toHaveAttribute("aria-checked", "true");
    }
    expect(screen.getByText("Grund")).toBeInTheDocument();
    expect(screen.getByText("Bra")).toBeInTheDocument();
    expect(screen.getByText("Stark")).toBeInTheDocument();
  });

  it("slå AV switchen (från alla tre) → emitterar tom lista (Av = noll grader)", async () => {
    const user = userEvent.setup();
    render(
      <JobbMatchGradeFilter
        selected={["Basic", "Good", "Strong"]}
        onChange={onChange}
      />,
    );
    await user.click(screen.getByRole("switch", { name: "Matchning" }));
    expect(onChange).toHaveBeenCalledWith([]);
  });

  it("avmarkera en grad smalnar (bevarar ordinal ordning för resten)", async () => {
    const user = userEvent.setup();
    render(
      <JobbMatchGradeFilter
        selected={["Basic", "Good", "Strong"]}
        onChange={onChange}
      />,
    );
    // Avmarkera Grund (Basic) → kvar Bra+Stark (Good, Strong).
    await user.click(screen.getByRole("checkbox", { name: "Grund" }));
    expect(onChange).toHaveBeenCalledWith(["Good", "Strong"]);
  });

  it("avmarkera SISTA grad → tom lista → switchen slår av (härlett)", async () => {
    const user = userEvent.setup();
    render(<JobbMatchGradeFilter selected={["Strong"]} onChange={onChange} />);
    await user.click(screen.getByRole("checkbox", { name: "Stark" }));
    expect(onChange).toHaveBeenCalledWith([]);
  });

  it("markera en grad till en delmängd lägger till i ordinal ordning", async () => {
    const user = userEvent.setup();
    // Endast Stark valt; lägg till Grund → ordinal [Basic, Strong].
    render(<JobbMatchGradeFilter selected={["Strong"]} onChange={onChange} />);
    await user.click(screen.getByRole("checkbox", { name: "Grund" }));
    expect(onChange).toHaveBeenCalledWith(["Basic", "Strong"]);
  });

  it("kryssruta aktiveras med tangentbord (Space) — a11y", async () => {
    const user = userEvent.setup();
    render(
      <JobbMatchGradeFilter
        selected={["Basic", "Good", "Strong"]}
        onChange={onChange}
      />,
    );
    const stark = screen.getByRole("checkbox", { name: "Stark" });
    stark.focus();
    await user.keyboard(" ");
    expect(onChange).toHaveBeenCalledWith(["Basic", "Good"]);
  });

  it("erbjuder ALDRIG Toppmatch (endast Grund/Bra/Stark)", () => {
    render(
      <JobbMatchGradeFilter
        selected={["Basic", "Good", "Strong"]}
        onChange={onChange}
      />,
    );
    expect(screen.queryByText("Topp")).toBeNull();
    expect(screen.queryByText("Toppmatch")).toBeNull();
    expect(screen.queryByRole("checkbox", { name: "Topp" })).toBeNull();
  });
});
