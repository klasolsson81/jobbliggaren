import { describe, expect, it } from "vitest";
import sv from "../../../messages/sv";
import en from "../../../messages/en";

/**
 * ADR 0144 Decision 4 (and its amendments): the legally bound catalogue strings, pinned
 * whole in both catalogues. DESIGN.md §8 rule 7 lets security-auditor alone shorten or
 * move them.
 * `row` is the Decision 4 row.
 */
const BOUND: ReadonlyArray<{ row: string; key: string; sv: string; en: string }> = [
  { row: "1", key: "pages.auth.passwordless.entry.privacyHint", sv: "Så behandlar vi din e-postadress: <privacy>integritetspolicyn</privacy>.", en: "How we process your email address: <privacy>the privacy policy</privacy>." },
  { row: "2", key: "pages.auth.passwordless.persistence", sv: "Du förblir inloggad på den här enheten i upp till 180 dagar. Logga ut finns på varje inloggad sida.", en: "You stay logged in on this device for up to 180 days. Log out is on every logged-in page." },
  { row: "3", key: "pages.auth.passwordless.code.resting", sv: "Finns det ett mejl från Jobbliggaren följer du instruktionerna i det. Innehåller mejlet en kod skriver du in den här. Koden gäller i 15 minuter.", en: "If there is an email from Jobbliggaren, follow the instructions in it. If the email contains a code, enter it here. The code is valid for 15 minutes." },
  { row: "3", key: "pages.auth.passwordless.code.resend.receipt", sv: "Vi har tagit emot din begäran. Kontrollera inkorgen och skräpposten. Skriv in koden från det senaste mejlet.", en: "We have received your request. Check your inbox and your spam folder. Enter the code from the most recent email." },
  { row: "3", key: "pages.auth.passwordless.code.expired", sv: "Koden går inte att använda längre. Skicka en ny kod och försök igen.", en: "That code can no longer be used. Send a new code and try again." },
  { row: "3", key: "pages.auth.passwordless.code.burned", sv: "Du har skrivit fel kod tre gånger, så koden går inte att använda längre. Innehåller mejlet en inloggningslänk kan du använda den i stället, annars skickar du en ny kod.", en: "You have entered the wrong code three times, so the code can no longer be used. If the email contains a login link you can use that instead, otherwise send a new code." },
  { row: "4", key: "pages.auth.passwordless.consent.termsLabel", sv: "Jag godkänner <terms>användarvillkoren</terms>.", en: "I accept the <terms>terms of use</terms>." },
  { row: "4", key: "pages.auth.passwordless.consent.privacySibling", sv: "Vi behandlar dina uppgifter enligt <privacy>integritetspolicyn</privacy>.", en: "We process your data in accordance with the <privacy>privacy policy</privacy>." },
  { row: "5", key: "pages.auth.passwordless.link.alreadyLoggedIn.body", sv: "Fortsätter du ersätts den aktiva inloggningen av inloggningen som länken gäller, och allt du gör efteråt hamnar på det kontot.", en: "If you continue, the active login is replaced by the login the link is for, and everything you do afterwards belongs to that account." },
  { row: "7", key: "settings.backgroundMatch.intro", sv: "När det här är på matchar vi nya annonser mot din profil varje natt. Toppmatchningar skickar vi direkt via e-post. Starka matchningar samlas i en sammanfattning som du får via e-post enligt vald takt. Bra matchningar visas i din matchningslista utan e-post. Du kan när som helst stänga av detta.", en: "When this is on, we match new job ads against your profile every night. Top matches are emailed to you right away. Strong matches are collected into a summary you receive by email at the chosen rate. Good matches appear in your match list without email. You can turn this off at any time." },
  { row: "7", key: "settings.backgroundMatch.toggleDescription", sv: "Avstängt som standard. Slår du på det samtycker du till att vi matchar nya annonser mot din profil och skickar notiser via e-post.", en: "Off by default. Turning it on means you consent to us matching new job ads against your profile and sending notifications by email." },
  { row: "8", key: "settings.followedCompanyNotifications.toggleDescription", sv: "Avstängt som standard. Slår du på det samtycker du till att vi mejlar dig nya annonser från företagen du följer. Du kan dra tillbaka samtycket när som helst genom att stänga av det här.", en: "Off by default. Turning it on means you consent to us emailing you new ads from the companies you follow. You can withdraw your consent at any time by turning this off." },
  { row: "8", key: "settings.followedCompanyNotifications.intro", sv: "Nya annonser från företag du följer visas alltid i appen, oavsett vad du väljer här. På Översikt ser du hur många som är nya. Här väljer du om vi också ska mejla dem till dig.", en: "New ads from companies you follow always appear in the app, whatever you choose here. On Overview you see how many are new. Here you choose whether we should also email them to you." },
  { row: "9", key: "jobads.applicationHistory.incompleteNote", sv: "Sammanställningen kan vara ofullständig. Antalen kan vara för låga, och en arbetsgivare kan saknas helt i listan. En ansökan räknas med bara när vi har arbetsgivarens identitet från annonsen, och den saknas för en del ansökningar.", en: "The compilation may be incomplete. The numbers can be too low, and an employer can be missing from the list entirely. An application is only counted when we have the employer's identity from the ad, and for some applications we do not." },
  { row: "9", key: "jobads.applicationHistory.applicationCount", sv: "Minst {count, plural, one {# skickad ansökan} other {# skickade ansökningar}}", en: "At least {count, plural, one {# submitted application} other {# submitted applications}}" },
  { row: "9", key: "jobads.ui.card.previousApplications", sv: "Du har minst {count, plural, one {# tidigare ansökan} other {# tidigare ansökningar}} till detta företag", en: "You have at least {count, plural, one {# previous application} other {# previous applications}} to this employer" },
  { row: "9", key: "jobads.ui.detail.previousApplications", sv: "Du har minst {count, plural, one {# tidigare ansökan} other {# tidigare ansökningar}} till detta företag. Sammanställningen kan vara ofullständig.", en: "You have at least {count, plural, one {# previous application} other {# previous applications}} to this employer. The compilation may be incomplete." },
  { row: "10", key: "jobads.ui.detail.recruiterNoticeLink", sv: "Är du kontaktperson i annonsen? Läs hur vi behandlar kontaktuppgifter.", en: "Are you a contact person in the ad? Read how we process contact details." },
  { row: "10", key: "applications.ui.preservedAd.recruiterNoticeLink", sv: "Är du kontaktperson i annonsen? Läs hur vi behandlar kontaktuppgifter.", en: "Are you a contact person in the ad? Read how we process contact details." },
  { row: "11", key: "pages.sokningar.lede", sv: "Vi sparar dina 20 senaste sökningar, inklusive söktexten du skrev. Du kan läsa mer i integritetspolicyn.", en: "We keep your 20 most recent searches, including the search term you typed. You can read more in the privacy policy." },
  { row: "12", key: "settings.account.delete.description", sv: "Ditt konto och allt du har sparat raderas, och du loggas ut på alla enheter. I 30 dagar kan du få kontot återställt genom att mejla kontakt@jobbliggaren.se. Därefter raderas det slutgiltigt och går inte att få tillbaka.", en: "Your account and everything you have saved are deleted, and you are logged out on all devices. For 30 days you can have the account restored by emailing kontakt@jobbliggaren.se. After that it is deleted for good and cannot be recovered." },
  { row: "12", key: "settings.account.delete.mailOff", sv: "E-postutskick är inte aktiverat just nu, så vi kan inte skicka någon kod. Vill du radera ditt konto kan du mejla <mail>kontakt@jobbliggaren.se</mail>.", en: "Email delivery is not enabled right now, so we cannot send a code. If you want to delete your account, email <mail>kontakt@jobbliggaren.se</mail>." },
  { row: "12", key: "settings.account.delete.contactRoute", sv: "Du kan också mejla <mail>kontakt@jobbliggaren.se</mail> och be oss radera kontot.", en: "You can also email <mail>kontakt@jobbliggaren.se</mail> and ask us to delete the account." },
  { row: "12", key: "pages.auth.passwordless.notice.accountDeleted.body", sv: "Du är utloggad på alla enheter. I 30 dagar kan du få kontot återställt genom att mejla <mail>kontakt@jobbliggaren.se</mail>.", en: "You are logged out on all devices. For 30 days you can have the account restored by emailing <mail>kontakt@jobbliggaren.se</mail>." },
  { row: "12", key: "pages.auth.passwordless.outcome.pendingDeletion.title", sv: "Ditt konto raderas permanent {date}.", en: "Your account is permanently deleted on {date}." },
  { row: "12", key: "pages.auth.passwordless.outcome.pendingDeletion.body", sv: "Fram till dess kan du få det återställt genom att mejla <mail>kontakt@jobbliggaren.se</mail>.", en: "Until then you can have it restored by emailing <mail>kontakt@jobbliggaren.se</mail>." },
  { row: "17", key: "pages.foretag.criteria.browse.source", sv: "Källa: SCB, egen bearbetning.", en: "Source: Statistics Sweden (SCB), own processing." },
  { row: "17", key: "pages.foretag.sok.source", sv: "Källa: SCB, egen bearbetning.", en: "Source: SCB, own processing." },
  { row: "17", key: "pages.foretag.criteria.ads.source", sv: "Källa: annonserna från Arbetsförmedlingens Platsbanken, urvalet från SCB:s företagsregister. Egen bearbetning.", en: "Source: the ads from the Swedish Public Employment Service's Platsbanken, the selection from Statistics Sweden's business register. Own processing." },
  { row: "6", key: "resumes.consent.title", sv: "Filen innehåller ett personnummer", en: "The file contains a personal identity number" },
  { row: "6", key: "resumes.consent.finding", sv: "Vi hittade {count, plural, one {ett personnummer} other {# personnummer}} i filen du laddade upp. Ett personnummer behöver inte finnas i ett CV och bör tas bort innan du använder innehållet.", en: "We found {count, plural, one {a personal identity number} other {# personal identity numbers}} in the file you uploaded. A personal identity number does not need to be in a CV and should be removed before you use the contents." },
  { row: "6", key: "resumes.consent.storeExplainer", sv: "Vill du spara originalfilen ändå? Den lagras krypterat och bara du kan öppna den. Att spara filen gör den inte till ditt CV. För att använda innehållet behöver du först ta bort personnumret ur filen och ladda upp den igen.", en: "Do you want to save the original file anyway? It is stored encrypted and only you can open it. Saving the file does not make it your CV. To use the contents you first need to remove the personal identity number from the file and upload it again." },
  { row: "6", key: "resumes.consent.save", sv: "Spara filen ändå", en: "Save the file anyway" },
  { row: "6", key: "resumes.consent.decline", sv: "Spara inte filen", en: "Do not save the file" },
  { row: "6", key: "resumes.consent.saving", sv: "Sparar filen…", en: "Saving the file…" },
  { row: "6", key: "resumes.consent.privacyLink", sv: "Så hanterar vi personnummer", en: "How we handle personal identity numbers" },
];

function read(catalogue: unknown, key: string): unknown {
  return key
    .split(".")
    .reduce<unknown>(
      (node, part) =>
        node !== null && typeof node === "object"
          ? (node as Record<string, unknown>)[part]
          : undefined,
      catalogue,
    );
}

function leafKeys(node: unknown, prefix: string): string[] {
  if (node === null || typeof node !== "object") return [prefix];
  return Object.entries(node as Record<string, unknown>).flatMap(([k, v]) =>
    leafKeys(v, `${prefix}.${k}`),
  );
}

describe("legally bound copy (ADR 0144 Decision 4)", () => {
  it.each(BOUND)("row $row: $key stands whole in sv and en", ({ key, sv: s, en: e }) => {
    expect(read(sv, key)).toBe(s);
    expect(read(en, key)).toBe(e);
  });

  it("row 6: resumes.consent has exactly the pinned keys", () => {
    const pinned = BOUND.filter((b) => b.row === "6").map((b) => b.key).sort();
    expect(leafKeys(read(sv, "resumes.consent"), "resumes.consent").sort()).toEqual(pinned);
    expect(leafKeys(read(en, "resumes.consent"), "resumes.consent").sort()).toEqual(pinned);
  });
});
