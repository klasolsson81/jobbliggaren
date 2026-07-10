import { describe, it, expect } from "vitest";
import { readProblemTitle } from "./problem";

function responseWith(json: () => Promise<unknown>): Response {
  return { json } as unknown as Response;
}

describe("readProblemTitle (#616)", () => {
  it("returns the title from a ProblemDetails body", async () => {
    const res = responseWith(async () => ({
      title: "Auth.PwnedPassword",
      detail: "irrelevant",
      status: 400,
    }));

    await expect(readProblemTitle(res)).resolves.toBe("Auth.PwnedPassword");
  });

  it("returns null for a body without a title", async () => {
    const res = responseWith(async () => ({ errors: { Password: ["x"] } }));

    await expect(readProblemTitle(res)).resolves.toBeNull();
  });

  it("returns null for a non-string title", async () => {
    const res = responseWith(async () => ({ title: 42 }));

    await expect(readProblemTitle(res)).resolves.toBeNull();
  });

  it("returns null instead of throwing on a non-JSON body", async () => {
    const res = responseWith(async () => {
      throw new SyntaxError("Unexpected token");
    });

    await expect(readProblemTitle(res)).resolves.toBeNull();
  });
});
