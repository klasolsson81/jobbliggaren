import { describe, it, expect } from "vitest";
import { pathToElementId } from "./me-path-routing";

describe("me-path-routing > pathToElementId", () => {
  describe("known paths (one per MeProfileForm-fält)", () => {
    it.each([
      ["displayName", "me-displayName"],
      ["language", "me-language"],
      ["emailNotifications", "me-emailNotifications"],
      ["weeklySummary", "me-weeklySummary"],
    ])("mappar %s → %s", (path, expected) => {
      expect(pathToElementId(path)).toBe(expected);
    });
  });

  describe("unknown paths returnerar null", () => {
    it.each([
      ["unknownField"],
      ["displayName.nested"],
      ["language.sv"],
      [""],
      ["DISPLAYNAME"], // case-sensitive
      ["display_name"], // wrong-snake-case
    ])("returnerar null för %s", (path) => {
      expect(pathToElementId(path)).toBeNull();
    });
  });
});
