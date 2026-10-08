import { expect, test, type BrowserContext, type Locator, type Page } from "@playwright/test";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { createRequire } from "node:module";
import { join, win32 } from "node:path";
import { LOCALE_COOKIE, type Locale } from "../../src/i18n/routing";
import { LOGIN_FLOW_COOKIE_NAME } from "../../src/lib/auth/cookie-names";
import { decodeLoginFlow, encodeLoginFlow, type LoginFlow } from "../../src/lib/auth/login-flow";
import { MEMBER } from "./fixtures";
import { APP_ORIGIN, SESSION_COOKIE, SESSION_ID, startHarness, type Harness } from "./servers";

let harness: Harness;
const cliRequire = createRequire(require.resolve("@lhci/cli/package.json"));
const lighthouseRequire = createRequire(cliRequire.resolve("lighthouse"));
const axePath = lighthouseRequire.resolve("axe-core/axe.min.js");
type AxeResult = { violations: { id: string; impact: string | null }[] };

const COPY = {
  sv: {
    trigger: "Radera konto",
    confirmation: "Radera ditt konto",
    email: "Skriv din e-postadress för att bekräfta",
    scheduled: "Radering av ditt konto schemaläggs",
    immediateBlock: "du loggas ut på alla enheter direkt",
    retained: "Dina sparade uppgifter finns kvar i 30 dagar.",
    permanentJob: "Därefter rensas de permanent av den dagliga raderingskörningen.",
    noRestore: "Du kan inte återställa kontot eller avbryta raderingen.",
    pendingTitle: "Ditt konto raderas permanent tidigast 19 okt. 2026.",
    noticeTitle: "Radering av ditt konto är schemalagd",
    noticeBlock: "Du är utloggad på alla enheter.",
    noticeJob: "rensas sedan permanent av den dagliga raderingskörningen.",
    loginEmail: "E-postadress",
    retentionHeading: "Hur länge vi sparar uppgifter",
    retentionBlock: "När du begär radering av ditt konto spärras åtkomsten direkt.",
    retentionGrace: "Uppgifterna finns kvar under en respit på 30 dagar.",
    retentionJob: "Därefter rensar den dagliga raderingskörningen kontot och dess uppgifter permanent",
    securityHeading: "Säkerhet",
    securityKey: "tas din krypteringsnyckel bort ur den aktiva databasen.",
    securityBackups: "Äldre säkerhetskopior kan innehålla uppgifter och nycklar tills kopiorna gallras enligt sina separata lagringstider.",
    securitySeparate: "Kontoraderingen tar inte bort säkerhetskopior omedelbart.",
  },
  en: {
    trigger: "Delete account",
    confirmation: "Delete your account",
    email: "Enter your email address to confirm",
    scheduled: "Deletion of your account is scheduled",
    immediateBlock: "you are logged out on all devices immediately",
    retained: "Your saved data remains for 30 days.",
    permanentJob: "After that, the daily deletion job removes it permanently.",
    noRestore: "You cannot restore the account or cancel deletion.",
    pendingTitle: "Your account will be permanently deleted no earlier than Oct 19, 2026.",
    noticeTitle: "Deletion of your account is scheduled",
    noticeBlock: "You are logged out on all devices.",
    noticeJob: "then removed permanently by the daily deletion job.",
    loginEmail: "Email address",
    retentionHeading: "How long we keep data",
    retentionBlock: "When you request deletion of your account, access is blocked immediately.",
    retentionGrace: "The data remains during a 30-day grace period.",
    retentionJob: "After that, the daily deletion job permanently removes the account and its data",
    securityHeading: "Security",
    securityKey: "your encryption key is removed from the active database.",
    securityBackups: "Older backups may contain data and keys until those backups expire under their separate retention periods.",
    securitySeparate: "Account deletion does not remove backups immediately.",
  },
} as const;

function outputDirectory() {
  const supplied = process.env.ADMIN_DELETION_SCREENSHOT_DIR;
  if (!supplied) return null;
  const directory = win32.resolve(supplied);
  const repository = win32.resolve(__dirname, "../../../..").toLowerCase();
  if (!win32.isAbsolute(supplied) || !/^c:\\tmp\\/i.test(directory)
    || directory.toLowerCase() === repository || directory.toLowerCase().startsWith(`${repository}\\`))
    throw new Error("Deletion copy screenshots require an absolute external C:/tmp directory.");
  mkdirSync(directory, { recursive: true });
  return directory;
}

async function inspect(page: Page, state: string, target?: Locator) {
  const directory = outputDirectory();
  if (directory !== null) {
    const options = { path: join(directory, `${state}-${page.viewportSize()?.width}.png`), animations: "disabled" as const };
    if (target) await target.screenshot(options);
    else await page.screenshot({ ...options, fullPage: true });
  }
  await page.addScriptTag({ path: axePath });
  const result = await page.evaluate(async () => {
    const axe = (window as unknown as { axe: { run: (root: Document, options: unknown) => Promise<AxeResult> } }).axe;
    return axe.run(document, { runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"] } });
  });
  expect(result.violations, state).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
}

async function keyboardReach(page: Page, target: Locator) {
  for (let count = 0; count < 80 && !await target.evaluate((node) => node === document.activeElement); count++)
    await page.keyboard.press("Tab");
  await expect(target).toBeFocused();
}

async function setLocale(context: BrowserContext, locale: Locale) {
  // setLocaleAction writes this presentation preference; it grants no access.
  await context.addCookies([{ name: LOCALE_COOKIE, value: locale, url: APP_ORIGIN,
    secure: true, httpOnly: true, sameSite: "Lax" }]);
}

async function presentFlow(context: BrowserContext, flow: LoginFlow) {
  const encoded = encodeLoginFlow(flow);
  expect(decodeLoginFlow(encoded)).toEqual(flow);
  // These unsigned presentation phases contain no session, code, grant or backend record.
  await context.addCookies([{ name: LOGIN_FLOW_COOKIE_NAME, value: encoded, url: APP_ORIGIN,
    secure: true, httpOnly: true, sameSite: "Strict" }]);
}

test.beforeAll(async () => { harness = await startHarness(); });
test.afterAll(async () => { if (harness !== undefined) await harness.stop(); });
test.beforeEach(() => harness.reset());
test.afterEach(() => { expect(harness.misses).toEqual([]); });

for (const locale of ["sv", "en"] as const) {
  const copy = COPY[locale];
  for (const width of [1280, 3440]) {
    test(`owner deletion confirmation describes scheduling and grace in ${locale} at ${width}px`, async ({ page, context }) => {
      await page.setViewportSize({ width, height: 900 });
      await setLocale(context, locale);
      harness.who = "member";
      await context.addCookies([{ name: SESSION_COOKIE, value: SESSION_ID, url: APP_ORIGIN,
        secure: true, httpOnly: true, sameSite: "Strict" }]);
      await page.goto("/mina-sidor/sekretess");
      const trigger = page.getByRole("button", { name: copy.trigger, exact: true });
      await keyboardReach(page, trigger);
      await page.keyboard.press("Enter");
      const dialog = page.getByRole("dialog", { name: copy.confirmation, exact: true });
      await expect(dialog).toBeVisible();
      for (const fact of [copy.scheduled, copy.immediateBlock, copy.retained, copy.permanentJob, copy.noRestore])
        await expect(dialog).toContainText(fact);
      const email = dialog.getByLabel(copy.email, { exact: true });
      await expect(email).toBeFocused();
      await email.fill(MEMBER.email);
      for (let count = 0; count < 8; count++) {
        await page.keyboard.press("Tab");
        expect(await dialog.evaluate((node) => node.contains(document.activeElement))).toBe(true);
      }
      await inspect(page, `owner-deletion-confirmation-${locale}`);
      await page.keyboard.press("Escape");
      await expect(dialog).toHaveCount(0);
      await expect(trigger).toBeFocused();
      expect(harness.requests.filter((path) => path.startsWith("POST /api/v1/auth/reauth"))).toEqual([]);
      expect(harness.deletionRequests).toEqual([]);
    });

    test(`pending deletion login outcome refuses restoration in ${locale} at ${width}px`, async ({ page, context }) => {
      await page.setViewportSize({ width, height: 900 });
      await setLocale(context, locale);
      // verifyCode writes this phase. challenge-actions.test.ts's terminal-outcome case pins the writer/redirect;
      // login-flow.test.ts pins its accepted encoding. Only its rendered presentation is asserted here.
      await presentFlow(context, { phase: "outcome", result: { outcome: "pendingDeletion", permanentDeletionDate: "2026-10-19" } });
      await page.goto("/logga-in/kod");
      const status = page.getByRole("main").getByRole("status").filter({
        has: page.getByRole("heading", { level: 2, name: copy.pendingTitle, exact: true }),
      });
      await expect(status).toHaveCount(1);
      await expect(status.getByRole("heading", { level: 2 })).toHaveText(copy.pendingTitle);
      await expect(status).toContainText(copy.noRestore);
      await expect(status).toBeFocused();
      const contact = status.getByRole("link", { name: "kontakt@jobbliggaren.se", exact: true });
      await expect(contact).toHaveAttribute("href", "mailto:kontakt@jobbliggaren.se");
      await page.keyboard.press("Tab");
      await expect(contact).toBeFocused();
      await expect(page.getByRole("main").getByRole("textbox")).toHaveCount(0);
      await inspect(page, `pending-deletion-login-${locale}`);
      expect(harness.requests.filter((path) => path.startsWith("POST /api/v1/auth/"))).toEqual([]);
    });

    test(`scheduled account deletion notice distinguishes cleanup in ${locale} at ${width}px`, async ({ page, context }) => {
      await page.setViewportSize({ width, height: 900 });
      await setLocale(context, locale);
      // deleteAccountAction writes this notice after its scheduling response, then ends the session and redirects.
      // me.delete-account.test.ts's notice/session/redirect test pins that producer; no deletion fact rests on this cookie.
      await presentFlow(context, { phase: "notice", notice: "accountDeleted" });
      await page.goto("/logga-in");
      const main = page.getByRole("main");
      const status = main.getByRole("status").filter({
        has: page.getByRole("heading", { level: 2, name: copy.noticeTitle, exact: true }),
      });
      await expect(status).toHaveCount(1);
      await expect(status.getByRole("heading", { level: 2 })).toHaveText(copy.noticeTitle);
      for (const fact of [copy.noticeBlock, copy.retained.replace(/\.$/, ""), copy.noticeJob, copy.noRestore])
        await expect(status).toContainText(fact);
      await expect(status).toBeFocused();
      await page.keyboard.press("Tab");
      await expect(status.getByRole("link", { name: "kontakt@jobbliggaren.se", exact: true })).toBeFocused();
      await page.keyboard.press("Tab");
      await expect(main.getByLabel(copy.loginEmail, { exact: true })).toBeFocused();
      await inspect(page, `scheduled-deletion-notice-${locale}`);
      expect(harness.requests.filter((path) => path.startsWith("POST /api/v1/auth/"))).toEqual([]);
    });

    test(`privacy retention and security distinguish grace, cleanup and backups in ${locale} at ${width}px`, async ({ page, context }) => {
      await page.setViewportSize({ width, height: 900 });
      await setLocale(context, locale);
      await page.goto("/integritet");
      await keyboardReach(page, page.getByRole("link", { name: copy.retentionHeading, exact: true }));
      await page.keyboard.press("Enter");
      const retentionHeading = page.getByRole("heading", { name: copy.retentionHeading, exact: true });
      await expect(retentionHeading).toBeFocused();
      const retention = retentionHeading.locator("..");
      for (const fact of [copy.retentionBlock, copy.retentionGrace, copy.retentionJob, copy.noRestore])
        await expect(retention).toContainText(fact);
      await inspect(page, `privacy-retention-${locale}`, retention);

      await keyboardReach(page, page.getByRole("link", { name: copy.securityHeading, exact: true }));
      await page.keyboard.press("Enter");
      const securityHeading = page.getByRole("heading", { name: copy.securityHeading, exact: true });
      await expect(securityHeading).toBeFocused();
      const security = securityHeading.locator("..");
      for (const fact of [copy.securityKey, copy.securityBackups, copy.securitySeparate])
        await expect(security).toContainText(fact);
      await inspect(page, `privacy-security-${locale}`, security);
    });
  }
}

// This optional artifact mode is required when the driving session requests rendered mail evidence.
// EmailTemplatesLoginChallengeTests exports both actual EmailTemplates.LoginChallenge(PendingDeletion) parts;
// absent files fail the configured run rather than silently replacing the producer with a hand-built body.
if (process.env.ADMIN_DELETION_SCREENSHOT_DIR) {
  for (const width of [1280, 3440]) {
    test(`actual generated pending-deletion mail gives the earliest date and refuses restore at ${width}px`, async ({ page }) => {
      const directory = outputDirectory();
      if (directory === null) throw new Error("Generated mail evidence requires its configured artifact directory.");
      const html = readFileSync(join(directory, "pending-deletion-mail.html"), "utf8");
      const plain = readFileSync(join(directory, "pending-deletion-mail.txt"), "utf8").replace(/\s+/g, " ").trim();
      await page.setViewportSize({ width, height: 900 });
      await page.setContent(html);
      await expect(page.getByRole("heading", { level: 1, name: "Ditt konto är markerat för radering", exact: true })).toBeVisible();
      const contact = page.getByRole("link", { name: "kontakt@jobbliggaren.se", exact: true });
      const content = contact.locator("xpath=ancestor::td[1]");
      for (const fact of ["Kontot raderas permanent tidigast 2026-10-19.", COPY.sv.noRestore,
        "Har du frågor kan du skriva till oss:"]) {
        expect(plain).toContain(fact);
        await expect(content).toContainText(fact);
      }
      expect(plain).not.toContain("kan du få det återställt");
      await expect(content).not.toContainText("kan du få det återställt");
      await expect(contact).toHaveAttribute("href", "mailto:kontakt@jobbliggaren.se");
      await keyboardReach(page, contact);
      await page.screenshot({ path: join(directory, `pending-deletion-generated-mail-${width}.png`),
        fullPage: true, animations: "disabled" });
      await page.addScriptTag({ path: axePath });
      const accessibility = await page.evaluate(async () => {
        const axe = (window as unknown as { axe: { run: (root: Document | Element, options: unknown) => Promise<AxeResult> } }).axe;
        const content = document.querySelector('a[href="mailto:kontakt@jobbliggaren.se"]')?.closest("td");
        if (!content) throw new Error("The generated mail must retain its production content cell.");
        const options = { runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"] } };
        return { document: await axe.run(document, options), content: await axe.run(content, options) };
      });
      // Record the whole email document separately; the changed body cell is the scoped accessibility gate.
      writeFileSync(join(directory, `pending-deletion-generated-mail-axe-${width}.json`), JSON.stringify(accessibility, null, 2));
      expect(accessibility.content.violations).toEqual([]);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
      expect(harness.requests).toEqual([]);
    });
  }
}
