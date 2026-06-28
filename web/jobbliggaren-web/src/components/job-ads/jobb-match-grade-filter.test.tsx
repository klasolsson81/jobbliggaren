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
const onRelatedToggle = vi.fn();

beforeEach(() => {
  onChange.mockClear();
  onTurnOff.mockClear();
  onTurnOn.mockClear();
  onRelatedToggle.mockClear();
});

function renderFilter(over: {
  active: boolean;
  selected?: string[];
  // #300 PR-5 — "Visa relaterade också"-toggle:n. Default AV (paritet med
  // produktens default + den rena URL:en) så de befintliga STEG 5-testerna
  // (3 grad-kryssrutor, Related dold) förblir oförändrade.
  includeRelated?: boolean;
}) {
  return render(
    <JobbMatchGradeFilter
      active={over.active}
      selected={over.selected ?? []}
      includeRelated={over.includeRelated ?? false}
      onChange={onChange}
      onTurnOff={onTurnOff}
      onTurnOn={onTurnOn}
      onRelatedToggle={onRelatedToggle}
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

// #300 PR-5 (ADR 0084) — "Visa relaterade också"-toggle:n + Related-kryssrutan +
// state-model flow-trap (design-reviewer-flaggad: "alla visade = []" mot det
// SYNLIGA setet, ej fast längd; AV droppar Related ur valet via föräldern).
describe("JobbMatchGradeFilter — Visa relaterade också (#300 PR-5)", () => {
  it("related-toggle:n finns INTE när Matchning är av (renderas inne i PÅ-blocket)", () => {
    renderFilter({ active: false });
    expect(
      screen.queryByRole("switch", { name: "Visa relaterade också" }),
    ).toBeNull();
  });

  it("related-toggle:n visas när Matchning är PÅ (egen kontroll, default av)", () => {
    renderFilter({ active: true, includeRelated: false });
    const related = screen.getByRole("switch", {
      name: "Visa relaterade också",
    });
    expect(related).toBeInTheDocument();
    expect(related).toHaveAttribute("aria-checked", "false");
    // Master-switchen "Matchning" finns kvar separat (ej hopslagen).
    expect(
      screen.getByRole("switch", { name: "Matchning" }),
    ).toBeInTheDocument();
  });

  it("Related-kryssrutan visas ENBART när related-toggle:n är på, mellan Grund och Bra", () => {
    // Toggle av → 3 kryssrutor, ingen "Relaterat yrke".
    const { unmount } = renderFilter({ active: true, includeRelated: false });
    expect(screen.getAllByRole("checkbox")).toHaveLength(3);
    expect(screen.queryByRole("checkbox", { name: "Relaterat yrke" })).toBeNull();
    unmount();

    // Toggle på → 4 kryssrutor; Related mellan Grund och Bra (LIST_MATCH_GRADES).
    renderFilter({ active: true, includeRelated: true });
    const boxes = screen.getAllByRole("checkbox");
    expect(boxes).toHaveLength(4);
    const labels = boxes.map((b) => b.textContent);
    expect(labels).toEqual([
      "Grundmatch",
      "Relaterat yrke",
      "Bra match",
      "Stark match",
    ]);
  });

  it("växla related-toggle PÅ → onRelatedToggle(true)", async () => {
    const user = userEvent.setup();
    renderFilter({ active: true, includeRelated: false });
    await user.click(
      screen.getByRole("switch", { name: "Visa relaterade också" }),
    );
    expect(onRelatedToggle).toHaveBeenCalledWith(true);
  });

  it("växla related-toggle AV → onRelatedToggle(false) (föräldern droppar Related ur valet)", async () => {
    const user = userEvent.setup();
    renderFilter({ active: true, includeRelated: true });
    await user.click(
      screen.getByRole("switch", { name: "Visa relaterade också" }),
    );
    expect(onRelatedToggle).toHaveBeenCalledWith(false);
  });

  it("STATE-TRAP: 'alla visade = []' räknas mot det SYNLIGA setet — markera sista av FYRA normaliseras till []", async () => {
    const user = userEvent.setup();
    // Toggle på (4 synliga); valt = 3 av 4 → markera den fjärde → alla fyra
    // synliga → normaliseras till [] (ren URL), INTE en kvarvarande lista.
    renderFilter({
      active: true,
      includeRelated: true,
      selected: ["Basic", "Related", "Good"],
    });
    await user.click(screen.getByRole("checkbox", { name: "Stark match" }));
    expect(onChange).toHaveBeenCalledWith([]);
  });

  it("STATE-TRAP: med toggle PÅ + tom selected = alla FYRA ikryssade (mot synliga setet)", () => {
    renderFilter({ active: true, includeRelated: true, selected: [] });
    const boxes = screen.getAllByRole("checkbox");
    expect(boxes).toHaveLength(4);
    for (const box of boxes) {
      expect(box).toHaveAttribute("aria-checked", "true");
    }
  });

  it("STATE-TRAP: avmarkera Related från all-visad (toggle på) smalnar till de övriga tre", async () => {
    const user = userEvent.setup();
    renderFilter({ active: true, includeRelated: true, selected: [] });
    await user.click(screen.getByRole("checkbox", { name: "Relaterat yrke" }));
    expect(onChange).toHaveBeenCalledWith(["Basic", "Good", "Strong"]);
  });

  it("STATE-TRAP: med toggle AV ingår Related ALDRIG i 'alla visade' — 3 ikryssade, ej 4", () => {
    // Även om selected vore tom räknas bara de 3 synliga (Related dold).
    renderFilter({ active: true, includeRelated: false, selected: [] });
    expect(screen.getAllByRole("checkbox")).toHaveLength(3);
  });
});
