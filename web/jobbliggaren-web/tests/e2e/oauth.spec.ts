import { test, expect } from "@playwright/test";

// #1744 (ADR 0142 D8). Needs an api that registers Google: e2e.yml gives it a fake client.

const AUTHORIZATION_ENDPOINT = "https://accounts.google.com/o/oauth2/v2/auth";
const START_PATH = "/api/auth/oauth/google/start";
const STATE_COOKIE = "__Host-jobbliggaren_oauth";

test.describe("Fortsätt med Google", () => {
  test("is a link outside any form, and its start binds this browser to the flow", async ({ page, context }) => {
    // A browser follows a 3xx without asking Playwright's routes, so the start's redirect to Google is
    // taken here, before the browser sees it, and answered with a stub.
    let location = "";
    await page.route(
      (url) => url.pathname === START_PATH,
      async (route) => {
        const response = await route.fetch({ maxRedirects: 0 });
        location = response.headers()["location"] ?? "";
        await route.fulfill({ status: 200, contentType: "text/html", body: "<!doctype html><title>stub</title>" });
      }
    );

    await page.goto("/logga-in?next=%2Fcv");
    const google = page.getByRole("link", { name: "Fortsätt med Google" });
    expect(await google.evaluate((el) => ({ tag: el.tagName, inForm: el.closest("form") !== null }))).toEqual({
      tag: "A",
      inForm: false,
    });

    await google.click();
    await expect.poll(() => location).not.toBe("");
    const authorization = new URL(location);
    expect(`${authorization.origin}${authorization.pathname}`).toBe(AUTHORIZATION_ENDPOINT);
    expect(authorization.searchParams.get("response_type")).toBe("code");
    expect(authorization.searchParams.get("code_challenge_method")).toBe("S256");
    expect(new URL(authorization.searchParams.get("redirect_uri") ?? "").pathname).toBe(
      "/api/auth/oauth/google/callback"
    );

    const cookie = (await context.cookies()).find((c) => c.name === STATE_COOKIE);
    expect(cookie).toMatchObject({
      value: authorization.searchParams.get("state"),
      httpOnly: true,
      secure: true,
      sameSite: "Lax",
      path: "/",
    });
    expect(cookie!.expires - Date.now() / 1000).toBeLessThanOrEqual(600);
    expect(cookie!.expires).toBeGreaterThan(Date.now() / 1000);
  });

  // Declared unreachable: no path in src renders a form to the start (provider-buttons.test.tsx pins
  // the row outside any form). This asserts only that the page degrades safely if one appears.
  test("a form never reaches the provider: the CSP refuses its redirect there", async ({ page }) => {
    await page.goto("/logga-in");
    const violated = page.evaluate(
      () =>
        new Promise<string>((resolve) => {
          document.addEventListener("securitypolicyviolation", (event) => resolve(event.violatedDirective), {
            once: true,
          });
        })
    );
    await page.evaluate((action) => {
      const form = document.createElement("form");
      form.method = "get";
      form.action = action;
      document.body.append(form);
      form.submit();
    }, START_PATH);

    expect(await violated).toBe("form-action");
    expect(new URL(page.url()).pathname).toBe("/logga-in");
  });
});
