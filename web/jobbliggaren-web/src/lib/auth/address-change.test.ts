import { describe, expect, it } from "vitest";
import { checkAddressChange } from "./address-change";

// #1975 — the fields of /adressbyte before anything is sent: the form runs this in the browser and the Server Action
// runs it again, so a post without JavaScript meets the same rules.

const OK = { currentEmail: "anna@exempel.se", newEmail: "anna.ny@exempel.se", code: "482915" };

describe("checkAddressChange", () => {
  it("sends the two addresses trimmed and the code, and nothing else", () => {
    expect(checkAddressChange({ currentEmail: "  anna@exempel.se ", newEmail: " anna.ny@exempel.se", code: " 482915 " })).toEqual({
      ok: true,
      input: OK,
    });
  });

  it("names every empty field at once", () => {
    expect(checkAddressChange({ currentEmail: " ", newEmail: "", code: "" })).toEqual({
      ok: false,
      errors: { currentEmail: "required", newEmail: "required", code: "required" },
    });
  });

  it.each([
    ["no @", "anna.exempel.se"],
    ["two @", "anna@@exempel.se"],
    ["a space inside", "anna @exempel.se"],
  ])("refuses an address with %s on its own field", (_label, address) => {
    expect(checkAddressChange({ ...OK, currentEmail: address })).toEqual({ ok: false, errors: { currentEmail: "invalid" } });
    expect(checkAddressChange({ ...OK, newEmail: address })).toEqual({ ok: false, errors: { newEmail: "invalid" } });
  });

  it("admits an address the backend admits and the browser's own check would not", () => {
    expect(checkAddressChange({ ...OK, newEmail: "björn@exempel.se" })).toMatchObject({ ok: true });
  });

  it.each([
    ["in another case", "ANNA@Exempel.SE"],
    ["with spaces around it", "  anna@exempel.se  "],
    ["in another Unicode form", "anna@exempel.se".normalize("NFD")],
  ])("refuses a new address that is the current one %s, on the new address", (_label, typed) => {
    expect(checkAddressChange({ ...OK, newEmail: typed })).toEqual({ ok: false, errors: { newEmail: "same" } });
  });

  it.each(["12345", "1234567", "12a456"])("refuses the code %s as malformed, before anything is spent", (code) => {
    expect(checkAddressChange({ ...OK, code })).toEqual({ ok: false, errors: { code: "malformed" } });
  });
});
