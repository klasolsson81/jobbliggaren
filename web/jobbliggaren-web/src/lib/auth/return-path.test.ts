import { describe, expect, it } from "vitest";
import { returnPathFor } from "./return-path";

const at = (path: string) => returnPathFor(new URL(path, "https://app.test"));

describe("returnPathFor", () => {
  it.each([
    ["/jobb/3f2504e0-4f89-41d3-9a0c-0305e82c3301?relaterade=on", "/jobb/3f2504e0-4f89-41d3-9a0c-0305e82c3301"],
    ["/mina-sidor/notiser", "/mina-sidor/notiser"],
    ["/foretag/sok?namn=5560000000", "/foretag/sok"],
    ["/admin/feedback?id=3F2504E0-4F89-41D3-9A0C-0305E82C3301&status=ny", "/admin/feedback?id=3f2504e0-4f89-41d3-9a0c-0305e82c3301"],
  ])("returns %s to %s, the path the login next would carry", (path, expected) => {
    expect(at(path)).toBe(expected);
  });

  it.each(["/", "/cv-granskning", "/logga-in", "/matchning", "/administration"])("has no return path on %s", (path) => {
    expect(at(path)).toBeNull();
  });
});
