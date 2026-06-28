import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { JobbMatchGradeFilter } from "./jobb-match-grade-filter";

// issue #292 (Klas + senior-cto-advisor — ERSÄTTER STEG 5:s "av = noll grader"):
// switchen speglar `active` (matchnings-axelns huvudbrytare), INTE
// selected.length. PÅ + tom selected = "alla grader visas" (alla ikryssade).
// PÅ → AV via onTurnOff; AV → PÅ via onTurnOn. Avmarkera sista grad håller
// switchen PÅ (tom = alla visas igen).

const onChange = vi.fn();
const onTurnOff = vi.fn();
const onTurnOn = vi.fn();

beforeEach(() => {
  onChange.mockClear();
  onTurnOff.mockClear();
  onTurnOn.mockClear();
});

function renderFilter(over: { active: boolean; selected?: string[] }) {
  return render(
    <JobbMatchGradeFilter
      active={over.active}
      selected={over.selected ?? []}
      onChange={onChange}
      onTurnOff={onTurnOff}
      onTurnOn={onTurnOn}
    />,
  );
}

describe("JobbMatchGradeFilter — switch + kryssrutor (issue #292)", () => {
  it("switchen speglar active=false (aria-checked=false) — INTE selected.length", () => {
    renderFilter({ active: false });
    const sw = screen.getByRole("switch", { name: "Matchning" });
    expect(sw).toHaveAttribute("aria-checked", "false");
    // Av-läget döljer kryssrute-gruppen (inga grad-kryssrutor renderas).
    expect(screen.queryByRole("group")).toBeNull();
    expect(screen.queryByRole("checkbox")).toBeNull();
  });

  it("switchen är PÅ (aria-checked=true) när active=true ÄVEN med tom selected", () => {
    renderFilter({ active: true, selected: [] });
    const sw = screen.getByRole("switch", { name: "Matchning" });
    expect(sw).toHaveAttribute("aria-checked", "true");
  });

  it("slå PÅ switchen (active=false) → anropar onTurnOn (föräldern tar bort off)", async () => {
    const user = userEvent.setup();
    renderFilter({ active: false });
    await user.click(screen.getByRole("switch", { name: "Matchning" }));
    expect(onTurnOn).toHaveBeenCalledTimes(1);
    expect(onTurnOff).not.toHaveBeenCalled();
    expect(onChange).not.toHaveBeenCalled();
  });

  it("slå AV switchen (active=true) → anropar onTurnOff (föräldern skriver matchning=off)", async () => {
    const user = userEvent.setup();
    renderFilter({ active: true, selected: [] });
    await user.click(screen.getByRole("switch", { name: "Matchning" }));
    expect(onTurnOff).toHaveBeenCalledTimes(1);
    expect(onTurnOn).not.toHaveBeenCalled();
    expect(onChange).not.toHaveBeenCalled();
  });

  it("PÅ + tom selected = 'alla grader visas' → tre kryssrutor, ALLA ikryssade (härlett)", () => {
    renderFilter({ active: true, selected: [] });
    expect(
      screen.getByRole("group", { name: "Visa matchningsgrader" }),
    ).toBeInTheDocument();
    const boxes = screen.getAllByRole("checkbox");
    expect(boxes).toHaveLength(3);
    for (const box of boxes) {
      expect(box).toHaveAttribute("aria-checked", "true");
    }
    expect(screen.getByText("Grundmatch")).toBeInTheDocument();
    expect(screen.getByText("Bra match")).toBeInTheDocument();
    expect(screen.getByText("Stark match")).toBeInTheDocument();
  });

  it("PÅ + delmängd → bara de valda kryssrutorna ikryssade", () => {
    renderFilter({ active: true, selected: ["Good", "Strong"] });
    expect(screen.getByRole("checkbox", { name: "Grundmatch" })).toHaveAttribute(
      "aria-checked",
      "false",
    );
    expect(screen.getByRole("checkbox", { name: "Bra match" })).toHaveAttribute(
      "aria-checked",
      "true",
    );
    expect(screen.getByRole("checkbox", { name: "Stark match" })).toHaveAttribute(
      "aria-checked",
      "true",
    );
  });

  it("avmarkera en grad från all-visad (tom selected) smalnar till de övriga två", async () => {
    const user = userEvent.setup();
    // Tom selected = alla visas → avmarkera Grund → kvar Bra+Stark.
    renderFilter({ active: true, selected: [] });
    await user.click(screen.getByRole("checkbox", { name: "Grundmatch" }));
    expect(onChange).toHaveBeenCalledWith(["Good", "Strong"]);
  });

  it("avmarkera SISTA graden → tom lista (= alla visas igen), switchen förblir PÅ (issue #292)", async () => {
    const user = userEvent.setup();
    // Bara Stark vald; avmarkera → [] (alla visas igen), INTE av.
    renderFilter({ active: true, selected: ["Strong"] });
    await user.click(screen.getByRole("checkbox", { name: "Stark match" }));
    expect(onChange).toHaveBeenCalledWith([]);
    expect(onTurnOff).not.toHaveBeenCalled();
  });

  it("markera en grad till en delmängd lägger till i ordinal ordning", async () => {
    const user = userEvent.setup();
    // Endast Stark valt; lägg till Grund → ordinal [Basic, Strong].
    renderFilter({ active: true, selected: ["Strong"] });
    await user.click(screen.getByRole("checkbox", { name: "Grundmatch" }));
    expect(onChange).toHaveBeenCalledWith(["Basic", "Strong"]);
  });

  it("markera den tredje graden (delmängd → alla tre) normaliseras till [] (alla visas, ren URL)", async () => {
    const user = userEvent.setup();
    // Basic + Strong valt; markera Bra → alla tre → normaliseras till [].
    renderFilter({ active: true, selected: ["Basic", "Strong"] });
    await user.click(screen.getByRole("checkbox", { name: "Bra match" }));
    expect(onChange).toHaveBeenCalledWith([]);
  });

  it("kryssruta aktiveras med tangentbord (Space) — a11y", async () => {
    const user = userEvent.setup();
    renderFilter({ active: true, selected: [] });
    const stark = screen.getByRole("checkbox", { name: "Stark match" });
    stark.focus();
    await user.keyboard(" ");
    // Tom selected = alla visas → avmarkera Stark → kvar Grund+Bra.
    expect(onChange).toHaveBeenCalledWith(["Basic", "Good"]);
  });

  it("erbjuder ALDRIG Toppmatch (endast Grundmatch/Bra match/Stark match)", () => {
    renderFilter({ active: true, selected: ["Basic", "Good", "Strong"] });
    expect(screen.queryByText("Topp")).toBeNull();
    expect(screen.queryByText("Toppmatch")).toBeNull();
    expect(screen.queryByRole("checkbox", { name: "Topp" })).toBeNull();
  });
});
