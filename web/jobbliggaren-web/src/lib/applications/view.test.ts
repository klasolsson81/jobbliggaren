import { describe, it, expect } from "vitest";
import {
  APPLICATIONS_VIEWS,
  DEFAULT_APPLICATIONS_VIEW,
  isApplicationsView,
} from "./view";

// Guarden är den enda validerings-logiken i cookie-plumbingen: läsaren
// (`readApplicationsView`) faller tillbaka på DEFAULT vid !guard, settern
// (`setApplicationsViewAction`) no-op:ar vid !guard. Pinnar branch-logiken utan
// next/headers-mock (de två wrappers är tunna cookies()-anrop).
describe("applications view SSOT + guard", () => {
  it("valid-set = lista, tavla, tabell (ADR 0092 D1; Tabell tillkom PR 10)", () => {
    expect([...APPLICATIONS_VIEWS]).toEqual(["lista", "tavla", "tabell"]);
  });

  it("default = lista (ADR 0092 D1)", () => {
    expect(DEFAULT_APPLICATIONS_VIEW).toBe("lista");
  });

  it("isApplicationsView accepterar de giltiga vyerna", () => {
    expect(isApplicationsView("lista")).toBe(true);
    expect(isApplicationsView("tavla")).toBe(true);
    expect(isApplicationsView("tabell")).toBe(true);
  });

  it("isApplicationsView avvisar okänt/frånvarande värde (fallback → DEFAULT)", () => {
    // "board"/"table" är prototypens localStorage-värden (aldrig giltiga
    // cookie-värden), och undefined/null/"" = aldrig-satt cookie.
    expect(isApplicationsView("board")).toBe(false);
    expect(isApplicationsView("table")).toBe(false);
    expect(isApplicationsView(undefined)).toBe(false);
    expect(isApplicationsView(null)).toBe(false);
    expect(isApplicationsView("")).toBe(false);
  });
});
