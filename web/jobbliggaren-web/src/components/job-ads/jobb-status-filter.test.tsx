import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { JobbStatusFilter } from "./jobb-status-filter";

// #383 (CTO-bind cto-7f3a9c2e1b4d8a6f) — status-filtret: tre fristående
// kryssrutor (Sparade/Ansökta/Dölj ansökta). Sparade är oberoende; Ansökta och
// Dölj ansökta är ömsesidigt uteslutande (att slå på den ena slår av den andra).

const onChange = vi.fn();

beforeEach(() => {
  onChange.mockClear();
});

function renderFilter(over: {
  savedOnly?: boolean;
  appliedOnly?: boolean;
  hideApplied?: boolean;
}) {
  return render(
    <JobbStatusFilter
      savedOnly={over.savedOnly ?? false}
      appliedOnly={over.appliedOnly ?? false}
      hideApplied={over.hideApplied ?? false}
      onChange={onChange}
    />,
  );
}

describe("JobbStatusFilter — status-kryssrutor (#383)", () => {
  it("renderar tre kryssrutor (Sparade/Ansökta/Dölj ansökta) i en grupp", () => {
    renderFilter({});
    expect(screen.getByRole("checkbox", { name: "Sparade" })).toBeInTheDocument();
    expect(screen.getByRole("checkbox", { name: "Ansökta" })).toBeInTheDocument();
    expect(
      screen.getByRole("checkbox", { name: "Dölj ansökta" }),
    ).toBeInTheDocument();
    // Gruppen får sitt tillgängliga namn från den (sr-only) "Status"-labeln.
    expect(screen.getByRole("group", { name: "Status" })).toBeInTheDocument();
  });

  // #408 kriterium 4 — kontrollen slutar låna grad-filtrets .jp-gradefilter-rytm;
  // den bor nu i popover-/panel-idiomet (.jp-panel__group + .jp-checkitem).
  it("använder INTE .jp-gradefilter längre (panel-idiom, #408)", () => {
    const { container } = renderFilter({});
    expect(container.querySelector(".jp-gradefilter")).toBeNull();
    expect(container.querySelector(".jp-gradefilter__grades")).toBeNull();
    // Gruppen bär panel-token; raderna bär ren .jp-checkitem (ej __grade).
    expect(container.querySelector(".jp-panel__group")).not.toBeNull();
    expect(container.querySelector(".jp-checkitem")).not.toBeNull();
  });

  it("speglar aria-checked ur props:arna (ingen egen state)", () => {
    renderFilter({ savedOnly: true, appliedOnly: false, hideApplied: false });
    expect(screen.getByRole("checkbox", { name: "Sparade" })).toHaveAttribute(
      "aria-checked",
      "true",
    );
    expect(screen.getByRole("checkbox", { name: "Ansökta" })).toHaveAttribute(
      "aria-checked",
      "false",
    );
  });

  it("klick på Sparade rapporterar savedOnly=true (orört appliedOnly/hideApplied)", async () => {
    const user = userEvent.setup();
    renderFilter({});
    await user.click(screen.getByRole("checkbox", { name: "Sparade" }));
    expect(onChange).toHaveBeenCalledTimes(1);
    expect(onChange).toHaveBeenCalledWith({
      savedOnly: true,
      appliedOnly: false,
      hideApplied: false,
    });
  });

  it("klick på Sparade när redan på rapporterar savedOnly=false (av-toggle)", async () => {
    const user = userEvent.setup();
    renderFilter({ savedOnly: true });
    await user.click(screen.getByRole("checkbox", { name: "Sparade" }));
    expect(onChange).toHaveBeenCalledWith({
      savedOnly: false,
      appliedOnly: false,
      hideApplied: false,
    });
  });

  it("MUTEX: slå på Ansökta när Dölj ansökta är på → Dölj ansökta slås av", async () => {
    const user = userEvent.setup();
    renderFilter({ hideApplied: true });
    await user.click(screen.getByRole("checkbox", { name: "Ansökta" }));
    expect(onChange).toHaveBeenCalledWith({
      savedOnly: false,
      appliedOnly: true,
      hideApplied: false,
    });
  });

  it("MUTEX: slå på Dölj ansökta när Ansökta är på → Ansökta slås av", async () => {
    const user = userEvent.setup();
    renderFilter({ appliedOnly: true });
    await user.click(screen.getByRole("checkbox", { name: "Dölj ansökta" }));
    expect(onChange).toHaveBeenCalledWith({
      savedOnly: false,
      appliedOnly: false,
      hideApplied: true,
    });
  });

  it("Sparade + Dölj ansökta är giltigt (ingen mutex mot Sparade)", async () => {
    const user = userEvent.setup();
    renderFilter({ savedOnly: true });
    await user.click(screen.getByRole("checkbox", { name: "Dölj ansökta" }));
    expect(onChange).toHaveBeenCalledWith({
      savedOnly: true,
      appliedOnly: false,
      hideApplied: true,
    });
  });

  it("tangentbord: Space aktiverar en kryssruta (a11y §5)", async () => {
    const user = userEvent.setup();
    renderFilter({});
    const saved = screen.getByRole("checkbox", { name: "Sparade" });
    saved.focus();
    await user.keyboard(" ");
    expect(onChange).toHaveBeenCalledWith({
      savedOnly: true,
      appliedOnly: false,
      hideApplied: false,
    });
  });
});
