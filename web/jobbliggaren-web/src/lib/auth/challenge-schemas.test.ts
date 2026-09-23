import { describe, expect, it } from "vitest";
import {
  acceptTermsInputSchema,
  codeInputSchema,
  emailInputSchema,
  linkTokenInputSchema,
} from "./challenge-schemas";

describe("emailInputSchema", () => {
  it("trims, and decides nothing else about what an address is", () => {
    expect(emailInputSchema.parse("  anna@example.com ")).toBe("anna@example.com");
  });

  // The backend admits these since #1781. The HTML email production and Zod's default email
  // pattern refuse both, so a stricter rule here would be a narrower definition in front of it.
  it.each(["björn@example.se", "o'brien@example.com"])("lets %s through to the backend", (address) => {
    expect(emailInputSchema.safeParse(address).success).toBe(true);
  });

  it.each(["", "   ", "a".repeat(257)])("refuses %j", (value) => {
    expect(emailInputSchema.safeParse(value).success).toBe(false);
  });
});

describe("codeInputSchema", () => {
  it("accepts six digits, trimmed", () => {
    expect(codeInputSchema.parse(" 012345 ")).toBe("012345");
  });

  it.each(["12345", "1234567", "12345a", "12 345", "", "１２３４５６"])("refuses %j", (value) => {
    expect(codeInputSchema.safeParse(value).success).toBe(false);
  });
});

describe("linkTokenInputSchema", () => {
  it("accepts a token up to the backend validator's 128 characters", () => {
    expect(linkTokenInputSchema.safeParse("x".repeat(128)).success).toBe(true);
  });

  it.each(["", "   ", "x".repeat(129)])("refuses %j", (value) => {
    expect(linkTokenInputSchema.safeParse(value).success).toBe(false);
  });
});

describe("acceptTermsInputSchema", () => {
  it("accepts only what a ticked native checkbox posts", () => {
    expect(acceptTermsInputSchema.safeParse("on").success).toBe(true);
  });

  it.each([null, undefined, "", "true", "off"])("refuses %j", (value) => {
    expect(acceptTermsInputSchema.safeParse(value).success).toBe(false);
  });
});
