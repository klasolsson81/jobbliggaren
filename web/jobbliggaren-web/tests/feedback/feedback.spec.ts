import { expect, test, type Locator, type Page } from "@playwright/test";
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
 * The feedback row and the feedback dialogs (#1979 PR3) in a real browser against the production build: the
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

// A page the job-modal harness answers every read of.
const PAGE = "/sparade";
const QUESTION = "Hur fungerar sidan Sparade annonser för dig?";
const RATED = "Tack för ditt betyg.";
const RECEIPT = "Tack. Din feedback är sparad.";
const CONSENT = "Skicka med teknisk information om skärm, enhet och webbläsare";
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;

const row = (page: Page) => page.locator("section.jp-feedback");
// The visible line under the stars; the row's live region repeats it for a screen reader.
const message = (page: Page) => row(page).locator(".jp-feedback__message");
const footerButton = (page: Page) => page.getByRole("button", { name: "Lämna feedback", exact: true });
// The row's "Lämna mer feedback" opens the page's dialog; the footer opens general feedback.
const pageDialog = (page: Page) => page.getByRole("dialog", { name: "Feedback om sidan Sparade annonser" });
const generalDialog = (page: Page) => page.getByRole("dialog", { name: "Feedback om Jobbliggaren" });

/** The row's star is a button: pressing it is the answer. */
async function rate(scope: Locator, rating: number) {
  await scope.getByRole("button", { name: `${rating} av 5` }).click();
}

/** The dialog's star is a radio, visually hidden inside its label, which is what a user clicks. */
async function rateInForm(scope: Locator, rating: number) {
  await scope.getByRole("radio", { name: `${rating} av 5` }).locator("xpath=..").click();
}

const filled = (scope: Locator, rating: number) =>
  scope.getByRole("button", { name: `${rating} av 5` }).locator("svg");

/** Opens the whole form for general feedback from the footer. */
async function openDialog(page: Page): Promise<Locator> {
  await footerButton(page).click();
  return generalDialog(page);
}

async function send(scope: Locator) {
  await scope.getByRole("button", { name: /^Skicka/ }).click();
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

async function attach(scope: Locator, name: string, mimeType: string, buffer: Buffer) {
  await scope.locator('input[type="file"]').setInputFiles({ name, mimeType, buffer });
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

test("the footer's feedback control is the one filled button on the deep-green footer, not a link", async ({ page }) => {
  await page.goto(PAGE);

  const button = footerButton(page);
  await expect(button).toBeVisible();
  await expect(button).toHaveCSS("background-color", "rgb(255, 255, 255)");
  await expect(button).toHaveCSS("color", "rgb(11, 42, 30)");
  expect((await button.boundingBox())!.height).toBeGreaterThanOrEqual(44);
  await expect(button.locator("svg")).toHaveAttribute("aria-hidden", "true");
});

test("a star in the row sends the rating alone at once, and the confirmation's close removes the row", async ({ page }) => {
  await page.goto(PAGE);
  await rate(row(page), 4);

  const confirmation = row(page).getByText(RATED);
  await expect(confirmation).toBeVisible();
  await expect(confirmation).toBeFocused();
  await expect(row(page).getByRole("group", { name: QUESTION })).toHaveCount(0);

  expect(harness.feedbackReceipts).toHaveLength(1);
  const { payload, screenshot } = harness.feedbackReceipts[0]!;
  expect(payload).toMatchObject({ page: "saved-ads", rating: 4, appVersion: HARNESS_APP_VERSION });
  expect(String(payload.submissionKey)).toMatch(UUID);
  expect(payload).not.toHaveProperty("comment");
  expect(payload).not.toHaveProperty("client");
  expect(payload).not.toHaveProperty("renderedVersion");
  expect(screenshot).toBeNull();

  await row(page).getByRole("button", { name: "Stäng" }).click();

  await expect(row(page)).toHaveCount(0);
  await expect(footerButton(page)).toBeVisible();
  expect(harness.feedbackReceipts).toHaveLength(1);
  // Focus left with the row, to the next stop in reading order, instead of dropping to <body>.
  await expect(page.locator("footer").locator("a[href], button").first()).toBeFocused();
});

test("the stars take Tab and fill under focus, and Enter sends the star focus is on", async ({ page }) => {
  await page.goto(PAGE);
  await row(page).getByRole("button", { name: "1 av 5" }).focus();

  await page.keyboard.press("Tab");

  await expect(filled(row(page), 1)).toHaveAttribute("fill", "currentColor");
  await expect(filled(row(page), 2)).toHaveAttribute("fill", "currentColor");
  await expect(filled(row(page), 3)).toHaveAttribute("fill", "none");
  expect(harness.feedbackReceipts).toHaveLength(0);

  await page.keyboard.press("Enter");

  await expect(row(page).getByText(RATED)).toBeVisible();
  expect(harness.feedbackReceipts).toHaveLength(1);
  expect(harness.feedbackReceipts[0]!.payload).toMatchObject({ page: "saved-ads", rating: 2 });
});

test("Lämna mer feedback opens the form with the rating chosen; a comment and the ticked box arrive as a second submission", async ({ page }) => {
  await page.goto(PAGE);
  await rate(row(page), 4);
  await expect(row(page).getByText(RATED)).toBeVisible();

  await row(page).getByRole("button", { name: "Lämna mer feedback" }).click();
  await expect(pageDialog(page).getByRole("group", { name: QUESTION })).toBeVisible();
  await expect(pageDialog(page).getByRole("radio", { name: "4 av 5" })).toBeChecked();
  // The rating is saved already; the form that carries it cannot take it back.
  await expect(pageDialog(page).getByRole("button", { name: "Ta bort betyget" })).toHaveCount(0);
  await pageDialog(page).getByLabel("Kommentar (valfri)").fill("Översikten är tydlig.");
  await pageDialog(page).getByRole("checkbox", { name: CONSENT }).check();
  await send(pageDialog(page));

  const receipt = pageDialog(page).getByText(RECEIPT);
  await expect(receipt).toBeVisible();
  await expect(receipt).toBeFocused();

  expect(harness.feedbackReceipts).toHaveLength(2);
  const [first, second] = harness.feedbackReceipts;
  expect(second!.payload).toMatchObject({
    page: "saved-ads",
    rating: 4,
    comment: "Översikten är tydlig.",
    appVersion: HARNESS_APP_VERSION,
  });
  expect(String(second!.payload.submissionKey)).toMatch(UUID);
  expect(second!.payload.submissionKey).not.toBe(first!.payload.submissionKey);
  expect(second!.payload).not.toHaveProperty("renderedVersion");
  const client = second!.payload.client as Record<string, unknown>;
  expect(Object.keys(client).sort()).toEqual(
    expect.arrayContaining(["viewportWidth", "viewportHeight", "screenWidth", "screenHeight", "pixelRatio"])
  );
  expect(client).not.toHaveProperty("theme");
  expect(second!.screenshot).toBeNull();

  await page.keyboard.press("Escape");
  await expect(row(page).getByRole("button", { name: "Lämna mer feedback" })).toBeFocused();
  await expect(row(page).getByText(RATED)).toBeVisible();
});

test("without the box, no device context leaves the browser", async ({ page }) => {
  await page.goto(PAGE);
  const form = await openDialog(page);
  await rateInForm(form, 2);
  await form.getByLabel("Kommentar (valfri)").fill("Utan rutan.");
  await send(form);

  await expect(form.getByText(RECEIPT)).toBeVisible();
  expect(harness.feedbackReceipts[0]!.payload).toMatchObject({ page: "general", rating: 2 });
  expect(harness.feedbackReceipts[0]!.payload).not.toHaveProperty("client");
});

test("an answered page asks no more, and the footer still offers the dialog", async ({ page }) => {
  await page.goto(PAGE);
  await rate(row(page), 5);
  await expect(row(page).getByText(RATED)).toBeVisible();

  await page.reload();

  await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
  await expect(page.getByRole("group", { name: QUESTION })).toHaveCount(0);
  await expect(footerButton(page)).toBeVisible();
});

test("an unknown answer offers to send the rating again under the same key, which is saved once", async ({ page }) => {
  harness.feedback.answer = "unknown";
  await page.goto(PAGE);
  await rate(row(page), 3);

  await expect(message(page)).toHaveText("Vi vet inte om din feedback sparades. Skicka igen, så sparas den bara en gång.");
  harness.feedback.answer = "saved";
  await row(page).getByRole("button", { name: "Skicka igen" }).click();

  await expect(row(page).getByText(RATED)).toBeVisible();
  const [first, second] = harness.feedbackReceipts;
  expect(harness.feedbackReceipts).toHaveLength(2);
  expect(second!.payload.submissionKey).toBe(first!.payload.submissionKey);
  expect(second!.payload.rating).toBe(3);
});

const REFUSALS: ReadonlyArray<[FeedbackAnswer, string]> = [
  ["closed", "Det går inte att skicka feedback just nu."],
  ["busy", "Det går inte att skicka just nu. Försök igen om en stund."],
  ["rateLimited", "Du har skickat många svar på kort tid. Försök igen om 9 minuter."],
];

for (const [answer, text] of REFUSALS) {
  test(`the backend's ${answer} answer is told under the row's stars, and the rating stays`, async ({ page }) => {
    harness.feedback.answer = answer;
    await page.goto(PAGE);
    await rate(row(page), 3);

    await expect(message(page)).toHaveText(text);
    // Neither the pointer nor focus is on a star any more, so the fill is the kept rating.
    await page.mouse.move(0, 0);
    await page.evaluate(() => (document.activeElement as HTMLElement).blur());
    await expect(filled(row(page), 3)).toHaveAttribute("fill", "currentColor");
    await expect(filled(row(page), 4)).toHaveAttribute("fill", "none");
    await expect(row(page).getByText(RATED)).toHaveCount(0);
  });
}

test("choosing the star again after a refusal sends it again", async ({ page }) => {
  harness.feedback.answer = "busy";
  await page.goto(PAGE);
  await rate(row(page), 2);
  await expect(message(page)).toHaveText("Det går inte att skicka just nu. Försök igen om en stund.");

  harness.feedback.answer = "saved";
  await rate(row(page), 2);

  await expect(row(page).getByText(RATED)).toBeVisible();
  expect(harness.feedbackReceipts).toHaveLength(2);
  expect(harness.feedbackReceipts[1]!.payload.submissionKey).toBe(harness.feedbackReceipts[0]!.payload.submissionKey);
});

test("a large detailed PNG is redrawn smaller before it is sent", async ({ page }) => {
  await page.goto(PAGE);
  const png = await canvasImage(page, { width: 3000, height: 2000, type: "image/png", noise: true });
  const form = await openDialog(page);
  await rateInForm(form, 4);
  await attach(form, "skarmbild.png", "image/png", png);

  await expect(form.getByRole("img", { name: "Skärmbilden du har valt" })).toBeVisible({ timeout: 30_000 });
  await send(form);
  await expect(form.getByText(RECEIPT)).toBeVisible();

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
  const form = await openDialog(page);
  await rateInForm(form, 4);
  await attach(form, "foto.jpg", "image/jpeg", jpeg);

  await expect(form.getByRole("img", { name: "Skärmbilden du har valt" })).toBeVisible();
  await send(form);
  await expect(form.getByText(RECEIPT)).toBeVisible();

  expect(harness.feedbackReceipts[0]!.screenshot).toMatchObject({ png: true, width: 200, height: 400 });
});

test("a WebP is accepted and sent as a PNG", async ({ page }) => {
  await page.goto(PAGE);
  const webp = await canvasImage(page, { width: 300, height: 150, type: "image/webp", noise: false });
  const form = await openDialog(page);
  await rateInForm(form, 4);
  await attach(form, "bild.webp", "image/webp", webp);

  await expect(form.getByRole("img", { name: "Skärmbilden du har valt" })).toBeVisible();
  await send(form);
  await expect(form.getByText(RECEIPT)).toBeVisible();

  expect(harness.feedbackReceipts[0]!.screenshot).toMatchObject({ png: true, width: 300, height: 150 });
});

test("a file that is not an image is refused, and the rating still goes without it", async ({ page }) => {
  await page.goto(PAGE);
  const form = await openDialog(page);
  await rateInForm(form, 2);
  await attach(form, "inte-en-bild.png", "image/png", Buffer.from("inte en bild"));

  await expect(form.locator(".jp-feedback__note")).toHaveText("Bilden måste vara en PNG-, JPEG- eller WebP-fil.");
  await send(form);
  await expect(form.getByText(RECEIPT)).toBeVisible();

  expect(harness.feedbackReceipts[0]!.screenshot).toBeNull();
});

test("the footer's general dialog keeps its draft over close and reopen, and sends a comment alone", async ({ page }) => {
  await page.goto(PAGE);
  await footerButton(page).click();
  await expect(generalDialog(page).getByRole("group", { name: "Hur fungerar Jobbliggaren för dig?" })).toBeVisible();
  await generalDialog(page).getByLabel("Kommentar (valfri)").fill("Sidfoten fungerar.");
  await page.keyboard.press("Escape");
  await expect(generalDialog(page)).toHaveCount(0);

  await footerButton(page).click();
  await expect(generalDialog(page).getByLabel("Kommentar (valfri)")).toHaveValue("Sidfoten fungerar.");
  await generalDialog(page).getByRole("button", { name: "Skicka feedback" }).click();

  await expect(generalDialog(page).getByText(RECEIPT)).toBeVisible();
  expect(harness.feedbackReceipts[0]!.payload).toMatchObject({ page: "general", comment: "Sidfoten fungerar." });
  expect(harness.feedbackReceipts[0]!.payload).not.toHaveProperty("rating");

  // General feedback does not answer the page: its row still asks.
  await page.keyboard.press("Escape");
  await expect(row(page).getByRole("group", { name: QUESTION })).toBeVisible();
});
