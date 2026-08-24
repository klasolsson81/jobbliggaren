import { test, expect } from "@playwright/test";

/**
 * Guards the one property no unit test can cross (#1488): what a LOGGED-OUT request
 * for an unmatched URL actually gets over HTTP.
 *
 * The defect: `(app)/@modal/[...catchAll]` made the `(app)` group match every
 * otherwise-unmatched path, so `(app)/layout.tsx` ran, found no session, and redirected
 * a visitor who mistyped a URL to `/logga-in`. The root `not-found.tsx` — which #1477
 * gave the public site frame — was never reached.
 *
 * Why this cannot be a vitest fitness function: the property is runtime route
 * resolution, and specifically the ORDER in which a layout and a 404 resolve. The
 * filesystem rule that closes the defect class lives in
 * `src/app/(app)/modal-slot-coverage.test.ts` and blocks CI; this spec measures that the
 * rule buys the behaviour it was written for.
 *
 * The control is load-bearing, not decoration: without it, a fix that disarmed the auth
 * gate entirely would also make the first assertion pass.
 */

const UNMATCHED = "/nagot-som-inte-finns";

test("an unmatched URL answers 404 with the public frame, not a login redirect", async ({
  request,
}) => {
  const res = await request.get(UNMATCHED, { maxRedirects: 0 });

  expect(res.status(), `${UNMATCHED} is a 404, not a 3xx`).toBe(404);
  expect(res.headers()["location"], "no redirect was issued").toBeUndefined();

  const body = await res.text();
  expect(body).toContain("Sidan finns inte");
  // The way back the root not-found carries; a bare 404 without it is the defect
  // #1477 closed.
  expect(body).toContain("Till startsidan");
});

test("a real protected route still redirects to the login form", async ({ request }) => {
  const res = await request.get("/cv", { maxRedirects: 0 });

  expect(res.status()).toBe(307);
  expect(res.headers()["location"]).toContain("/logga-in?next=%2Fcv");
});
