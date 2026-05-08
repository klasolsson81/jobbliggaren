import { type Page } from "@playwright/test";

const TEST_PASSWORD = "E2eTestPass123!";

function testEmail(runId: number): string {
  return `test-e2e-${runId}@jobbpilot.se`;
}

export async function loginAs(page: Page, runId: number): Promise<void> {
  await page.goto("/logga-in");
  await page.getByLabel("E-postadress").fill(testEmail(runId));
  await page.getByLabel("Lösenord").fill(TEST_PASSWORD);
  await page.getByRole("button", { name: "Logga in" }).click();
  await page.waitForURL("**/mig");
}

export async function ensureTestUser(baseURL: string, runId: number): Promise<void> {
  const res = await fetch(`${baseURL}/api/v1/auth/register`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ email: testEmail(runId), password: TEST_PASSWORD, displayName: "E2E Testare" }),
  });
  if (!res.ok && res.status !== 409) {
    if (res.status === 400) {
      const body = await res.json().catch(() => ({}));
      if (!String(body?.title ?? "").includes("Duplicate")) {
        throw new Error(`Failed to create test user: ${res.status} ${JSON.stringify(body)}`);
      }
    } else {
      throw new Error(`Failed to create test user: ${res.status}`);
    }
  }
}
