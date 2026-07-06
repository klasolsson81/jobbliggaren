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
  it("valid-set = lista, tavla (Tabell införs i PR 10)", () => {
    expect([...APPLICATIONS_VIEWS]).toEqual(["lista", "tavla"]);
  });

  it("default = lista (ADR 0092 D1)", () => {
    expect(DEFAULT_APPLICATIONS_VIEW).toBe("lista");
  });

  it("isApplicationsView accepterar de giltiga vyerna", () => {
    expect(isApplicationsView("lista")).toBe(true);
    expect(isApplicationsView("tavla")).toBe(true);
  });

  it("isApplicationsView avvisar okänt/frånvarande värde (fallback → DEFAULT)", () => {
    // "tabell" är ännu inte giltig (PR 10), "board" är prototypens localStorage-
    // värde, och undefined/null/"" = aldrig-satt cookie.
    expect(isApplicationsView("tabell")).toBe(false);
    expect(isApplicationsView("board")).toBe(false);
    expect(isApplicationsView(undefined)).toBe(false);
    expect(isApplicationsView(null)).toBe(false);
    expect(isApplicationsView("")).toBe(false);
  });
});
