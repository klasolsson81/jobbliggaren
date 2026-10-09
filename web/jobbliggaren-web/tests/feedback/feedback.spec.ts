import { expect, test, type Page } from "@playwright/test";
import {
  APP_ORIGIN,
  HARNESS_APP_VERSION,
  SESSION_COOKIE,
  SESSION_ID,
  startHarness,
  type FeedbackAnswer,
  type Harness,
} from "./servers";

/**
 * The feedback row and the footer's dialog (#1979 PR3) in a real browser against the production build: the
 * submission travels through the BFF route to a fixture backend that records what arrived, and the
 * screenshot is decoded and redrawn by Chromium's own codec, which the vitest suites replace with a fake.
 */

let harness: Harness;

test.beforeAll(async () => {
  harness = await startHarness();
});

test.afterAll(async () => {
  await harness.stop();
});

test.beforeEach(async ({ context, page }) => {
  harness.reset();
  harness.feedback.open = true;
  await page.setViewportSize({ width: 1280, height: 900 });
  await context.addCookies([
    { name: SESSION_COOKIE, value: SESSION_ID, url: APP_ORIGIN, secure: true, httpOnly: true, sameSite: "Strict" },
  ]);
});

test.afterEach(() => {
  expect(harness.misses, "the pages read a backend route the harness does not answer").toEqual([]);
});

const PAGE = "/oversikt";
const QUESTION = "Hur fungerar den här sidan för dig?";
const RECEIPT = "Tack. Din feedback är sparad.";
const CONSENT = "Skicka med skärm- och fönsterstorlek, pixeltäthet, enhetstyp, operativsystem och webbläsare";
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;

const row = (page: Page) => page.locator("section.jp-feedback");
const footerButton = (page: Page) => page.getByRole("button", { name: "Lämna feedback om sidan" });

async function rate(page: Page, rating: number) {
  await row(page).getByRole("radio", { name: `${rating} av 5` }).check();
}

async function send(page: Page) {
  await row(page).getByRole("button", { name: /^Skicka/ }).click();
}

/** An image made by the browser's own encoder, returned as bytes for the file chooser. */
async function canvasImage(
  page: Page,
  { width, height, type, noise }: { width: number; height: number; type: string; noise: boolean }
): Promise<Buffer> {
  const base64 = await page.evaluate(
    async ({ width, height, type, noise }) => {
      const canvas = new OffscreenCanvas(width, height);
      const context = canvas.getContext("2d")!;
      if (noise) {
        const pixels = context.createImageData(width, height);
        for (let i = 0; i < pixels.data.length; i += 4) {
          pixels.data[i] = Math.random() * 256;
          pixels.data[i + 1] = Math.random() * 256;
          pixels.data[i + 2] = Math.random() * 256;
          pixels.data[i + 3] = 255;
        }
        context.putImageData(pixels, 0, 0);
      } else {
        context.fillStyle = "#b00020";
        context.fillRect(0, 0, width / 2, height);
        context.fillStyle = "#0b2a1e";
        context.fillRect(width / 2, 0, width / 2, height);
      }
      const blob = await canvas.convertToBlob({ type, quality: 0.9 });
      const bytes = new Uint8Array(await blob.arrayBuffer());
      let binary = "";
      for (let i = 0; i < bytes.length; i += 0x8000) binary += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
      return btoa(binary);
    },
    { width, height, type, noise }
  );
  return Buffer.from(base64, "base64");
}

/** A JPEG with an EXIF APP1 segment that says "rotate 90° clockwise" (orientation 6), inserted after SOI. */
function withExifOrientation6(jpeg: Buffer): Buffer {
  const tiff = Buffer.from([
    0x4d, 0x4d, 0x00, 0x2a, 0x00, 0x00, 0x00, 0x08, // big-endian TIFF, IFD0 at offset 8
    0x00, 0x01, // one entry
    0x01, 0x12, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01, 0x00, 0x06, 0x00, 0x00, // Orientation, SHORT, 1, value 6
    0x00, 0x00, 0x00, 0x00, // no next IFD
  ]);
  const payload = Buffer.concat([Buffer.from("Exif\0\0", "binary"), tiff]);
  const length = payload.length + 2;
  const app1 = Buffer.concat([Buffer.from([0xff, 0xe1, length >> 8, length & 0xff]), payload]);
  return Buffer.concat([jpeg.subarray(0, 2), app1, jpeg.subarray(2)]);
}

async function attach(page: Page, name: string, mimeType: string, buffer: Buffer) {
  await row(page).locator('input[type="file"]').setInputFiles({ name, mimeType, buffer });
}

test("closed: the page shows no row and the footer no button", async ({ page }) => {
  harness.feedback.open = false;

  await page.goto(PAGE);

  await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
  await expect(page.getByRole("group", { name: QUESTION })).toHaveCount(0);
  await expect(footerButton(page)).toHaveCount(0);
  expect(harness.requests).toContain("GET /api/v1/me/feedback/prompt-state");
  await expect(page.locator(".jp-foot__links li:empty")).toHaveCount(0);
});

test("a rating, a comment and the ticked box arrive as one stamped submission", async ({ page }) => {
  await page.goto(PAGE);
  await rate(page, 4);
  await row(page).getByLabel("Kommentar (valfri)").fill("Översikten är tydlig.");
  await row(page).getByRole("checkbox", { name: CONSENT }).check();
  await send(page);

  const receipt = row(page).getByText(RECEIPT);
  await expect(receipt).toBeVisible();
  await expect(receipt).toBeFocused();

  expect(harness.feedbackReceipts).toHaveLength(1);
  const { payload, screenshot } = harness.feedbackReceipts[0]!;
  expect(payload).toMatchObject({
    page: "overview",
    rating: 4,
    comment: "Översikten är tydlig.",
    appVersion: HARNESS_APP_VERSION,
  });
  expect(String(payload.submissionKey)).toMatch(UUID);
  expect(payload).not.toHaveProperty("renderedVersion");
  const client = payload.client as Record<string, unknown>;
  expect(Object.keys(client).sort()).toEqual(
    expect.arrayContaining(["viewportWidth", "viewportHeight", "screenWidth", "screenHeight", "pixelRatio"])
  );
  expect(client).not.toHaveProperty("theme");
  expect(screenshot).toBeNull();
});

test("without the box, no device context leaves the browser", async ({ page }) => {
  await page.goto(PAGE);
  await rate(page, 2);
  await send(page);

  await expect(row(page).getByText(RECEIPT)).toBeVisible();
  expect(harness.feedbackReceipts[0]!.payload).not.toHaveProperty("client");
});

test("an answered page asks no more, and the footer still offers the dialog", async ({ page }) => {
  await page.goto(PAGE);
  await rate(page, 5);
  await send(page);
  await expect(row(page).getByText(RECEIPT)).toBeVisible();

  await page.reload();

  await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
  await expect(page.getByRole("group", { name: QUESTION })).toHaveCount(0);
  await expect(footerButton(page)).toBeVisible();
});

test("an unknown answer offers to send again under the same key, which is saved once", async ({ page }) => {
  harness.feedback.answer = "unknown";
  await page.goto(PAGE);
  await rate(page, 3);
  await send(page);

  await expect(row(page).getByText("Vi vet inte om din feedback sparades. Skicka igen, så sparas den bara en gång.")).toBeVisible();
  harness.feedback.answer = "saved";
  await row(page).getByRole("button", { name: "Skicka igen" }).click();

  await expect(row(page).getByText(RECEIPT)).toBeVisible();
  const [first, second] = harness.feedbackReceipts;
  expect(harness.feedbackReceipts).toHaveLength(2);
  expect(second!.payload.submissionKey).toBe(first!.payload.submissionKey);
});

const REFUSALS: ReadonlyArray<[FeedbackAnswer, string]> = [
  ["closed", "Det går inte att skicka feedback just nu."],
  ["busy", "Det går inte att skicka just nu. Försök igen om en stund."],
  ["rateLimited", "Du har skickat många svar på kort tid. Försök igen om 9 minuter."],
  ["tooLarge", "Bilden är för stor. Den får vara högst 5 MB."],
  ["empty", "Välj ett betyg eller skriv en kommentar."],
];

for (const [answer, text] of REFUSALS) {
  test(`the backend's ${answer} answer is told beside Send, and the draft stays`, async ({ page }) => {
    harness.feedback.answer = answer;
    await page.goto(PAGE);
    await rate(page, 1);
    await row(page).getByLabel("Kommentar (valfri)").fill("Utkastet ska stå kvar.");
    await send(page);

    await expect(row(page).getByText(text, { exact: true })).toBeVisible();
    await expect(row(page).getByLabel("Kommentar (valfri)")).toHaveValue("Utkastet ska stå kvar.");
  });
}

test("a large detailed PNG is redrawn smaller before it is sent", async ({ page }) => {
  await page.goto(PAGE);
  const png = await canvasImage(page, { width: 3000, height: 2000, type: "image/png", noise: true });
  await rate(page, 4);
  await attach(page, "skarmbild.png", "image/png", png);

  await expect(row(page).getByRole("img", { name: "Skärmbilden du har valt" })).toBeVisible({ timeout: 30_000 });
  await send(page);
  await expect(row(page).getByText(RECEIPT)).toBeVisible();

  const shot = harness.feedbackReceipts[0]!.screenshot!;
  expect(shot.png).toBe(true);
  expect(Math.max(shot.width, shot.height)).toBeLessThanOrEqual(2560);
  expect(shot.width * shot.height).toBeLessThanOrEqual(4_000_000);
  expect(shot.width).toBeLessThan(3000);
  expect(shot.bytes).toBeLessThanOrEqual(1.5 * 1024 * 1024);
});

test("a JPEG's EXIF orientation is applied, and the metadata does not travel", async ({ page }) => {
  await page.goto(PAGE);
  const jpeg = withExifOrientation6(await canvasImage(page, { width: 400, height: 200, type: "image/jpeg", noise: false }));
  await rate(page, 4);
  await attach(page, "foto.jpg", "image/jpeg", jpeg);

  await expect(row(page).getByRole("img", { name: "Skärmbilden du har valt" })).toBeVisible();
  await send(page);
  await expect(row(page).getByText(RECEIPT)).toBeVisible();

  expect(harness.feedbackReceipts[0]!.screenshot).toMatchObject({ png: true, width: 200, height: 400 });
});

test("a WebP is accepted and sent as a PNG", async ({ page }) => {
  await page.goto(PAGE);
  const webp = await canvasImage(page, { width: 300, height: 150, type: "image/webp", noise: false });
  await rate(page, 4);
  await attach(page, "bild.webp", "image/webp", webp);

  await expect(row(page).getByRole("img", { name: "Skärmbilden du har valt" })).toBeVisible();
  await send(page);
  await expect(row(page).getByText(RECEIPT)).toBeVisible();

  expect(harness.feedbackReceipts[0]!.screenshot).toMatchObject({ png: true, width: 300, height: 150 });
});

test("a file that is not an image is refused, and the rating still goes without it", async ({ page }) => {
  await page.goto(PAGE);
  await rate(page, 2);
  await attach(page, "inte-en-bild.png", "image/png", Buffer.from("inte en bild"));

  await expect(row(page).getByText("Bilden måste vara en PNG-, JPEG- eller WebP-fil.")).toBeVisible();
  await send(page);
  await expect(row(page).getByText(RECEIPT)).toBeVisible();

  expect(harness.feedbackReceipts[0]!.screenshot).toBeNull();
});

test("the footer's dialog keeps its draft over close and reopen, and sends a comment alone", async ({ page }) => {
  await page.goto(PAGE);
  await footerButton(page).click();
  const dialog = page.getByRole("dialog", { name: "Feedback om sidan" });
  await dialog.getByLabel("Kommentar (valfri)").fill("Sidfoten fungerar.");
  await page.keyboard.press("Escape");
  await expect(dialog).toHaveCount(0);

  await footerButton(page).click();
  await expect(dialog.getByLabel("Kommentar (valfri)")).toHaveValue("Sidfoten fungerar.");
  await dialog.getByRole("button", { name: "Skicka feedback" }).click();

  await expect(dialog.getByText(RECEIPT)).toBeVisible();
  expect(harness.feedbackReceipts[0]!.payload).toMatchObject({ page: "overview", comment: "Sidfoten fungerar." });
  expect(harness.feedbackReceipts[0]!.payload).not.toHaveProperty("rating");
});
