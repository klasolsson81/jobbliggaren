import { describe, expect, it } from "vitest";
import sv from "../../../messages/sv/content-legal.json";
import en from "../../../messages/en/content-legal.json";

const SIGNED: ReadonlyArray<{ key: string; sv: string; en: string }> = [
  {
    key: "privacy.sections.6.paragraphs.2",
    sv: "Du kan när som helst återkalla ett samtycke genom att stänga av det i dina inställningar. Återkallar du samtycket till matchning slutar vi matcha nya annonser mot din profil och mejla dig matchningar. Återkallar du samtycket till mejl om företag du följer slutar vi mejla dem till dig. Stänger du i stället av alla e-postnotiser, eller bara mejlen om toppmatchningar eller sammanfattningen, slutar vi skicka de mejlen, men dina samtycken står kvar och matchningen fortsätter i appen. Att du återkallar ett samtycke påverkar inte att behandlingen var laglig innan du återkallade det.",
    en: "You can withdraw a consent at any time by turning it off in your settings. If you withdraw your consent to matching, we stop matching new job ads against your profile and emailing you matches. If you withdraw your consent to emails about companies you follow, we stop emailing them to you. If you instead turn off all email notifications, or only the emails about top matches or the summary, we stop sending those emails, but your consents remain and matching continues in the app. Withdrawing a consent does not affect the lawfulness of the processing before you withdrew it.",
  },
  {
    key: "privacy.sections.10.list.6",
    sv: "Återkalla samtycke. Där behandlingen grundas på ditt samtycke kan du återkalla samtycket när som helst, utan att det påverkar lagligheten av behandlingen dessförinnan. Samtycket till matchning och samtycket till mejl om företag du följer återkallar du genom att stänga av dem i dina inställningar. Har du skickat med teknisk information i en feedback kontaktar du oss på e-postadressen ovan, så raderar vi den.",
    en: "Withdraw consent. Where the processing is based on your consent, you can withdraw your consent at any time, without it affecting the lawfulness of the processing carried out beforehand. You withdraw your consent to matching and your consent to emails about companies you follow by turning them off in your settings. If you have included technical information with feedback, contact us at the email address above and we will delete it.",
  },
];

// Signature 3 (security-auditor, ADR 0166): the paragraphs that tell a contact person the ad is shown on a public page.
// Pinned whole in both languages, so the signed text cannot drift after the signature (Art. 12).
const SIGNED_PUBLIC_PAGE: ReadonlyArray<{ key: string; sv: string; en: string }> = [
  {
    key: "privacy.sections.5.paragraphs.0",
    sv: "Jobbannonser från Platsbanken kan innehålla uppgifter om en kontaktperson hos arbetsgivaren, till exempel namn, e-postadress och telefonnummer för arbetet. Innan en annons sparas tar vi automatiskt bort e-postadresser och telefonnummer ur annonstexten. Kontaktpersonens uppgifter sparas i stället strukturerat och avskilt, utanför fritextsökningen, så att du kan följa upp en ansökan med rätt person. Borttagningen ur texten är regelbaserad och kan missa uppgifter som skrivits på ovanliga sätt eller som ligger i en bild, och den tar inte bort namn i den löpande texten. Annonserna visas också på en publik sida som sökmotorer kan indexera. Sidan visar aldrig kontaktuppgifterna, men ett namn i annonsens löpande text syns där.",
    en: "Job ads from Platsbanken can contain details of a contact person at the employer, for example a name, a work email address and a work phone number. Before an ad is stored, we automatically remove email addresses and phone numbers from the ad text. The contact person's details are instead stored in a structured, separate field outside the free-text search, so that you can follow up an application with the right person. The removal from the text is rule-based and can miss details written in unusual ways or embedded in an image, and it does not remove names in the running text. The ads are also shown on a public page that search engines can index. The page never shows the contact details, but a name in the ad's running text is visible there.",
  },
  {
    key: "recruiterNotice.intro",
    sv: "Jobbliggaren hämtar jobbannonser som öppna data från Arbetsförmedlingens Platsbanken och visar dem, både för dig som söker jobb och på en publik sida som sökmotorer kan indexera. Om du står som kontaktperson i en annons behandlar vi därför vissa uppgifter om dig, trots att du inte är användare hos oss. Den här sidan beskriver vad vi behandlar, varför, hur länge och vilka rättigheter du har.",
    en: "Jobbliggaren retrieves job ads as open data from Arbetsförmedlingen's Platsbanken and shows them, both to jobseekers and on a public page that search engines can index. If you are named as a contact person in an ad, we therefore process some data about you even though you are not a user of the service. This page describes what we process, why, for how long and which rights you have.",
  },
  {
    key: "recruiterNotice.sections.0.paragraphs.1",
    sv: "Innan annonstexten sparas tar vi automatiskt bort e-postadresser, telefonnummer och personnummer ur den löpande texten. Kontaktuppgifterna sparas i stället strukturerat och avskilt, utanför fritextsökningen. Borttagningen ur texten är regelbaserad. Den kan missa uppgifter som skrivits på ovanliga sätt eller som ligger i en bild, och den tar inte bort namn: ett namn i annonsens löpande text finns kvar i texten.",
    en: "Before the ad text is stored, we automatically remove email addresses, phone numbers and personal identity numbers from the running text. The contact details are instead stored in a structured, separate field outside the free-text search. The removal is rule-based. It can miss details written in unusual ways or embedded in an image, and it does not remove names: a name in the ad's running text stays in the text.",
  },
  {
    key: "recruiterNotice.sections.0.paragraphs.2",
    sv: "Annonsen visas också på en publik sida på jobbliggaren.se så länge annonsen är öppen. Vem som helst kan öppna sidan utan att logga in, och sökmotorer kan indexera den. Sidan visar annonsens titel, arbetsgivarens namn och hela annonstexten så som vi har sparat den. Den visar aldrig det avskilda kontaktblocket, men ett namn i annonsens löpande text syns där och kan hittas via en sökmotor. Mottagare är därför allmänheten och sökmotorerna.",
    en: "The ad is also shown on a public page at jobbliggaren.se for as long as the ad is open. Anyone can open the page without logging in, and search engines can index it. The page shows the ad's title, the employer's name and the whole ad text as we have stored it. It never shows the separate contact block, but a name in the ad's running text is visible there and can be found through a search engine. The recipients are therefore the public and search engines.",
  },
  {
    key: "recruiterNotice.sections.1.paragraphs.0",
    sv: "Den lagliga grunden är berättigat intresse (artikel 6.1 f). Intressena är två. Den som söker ett jobb behöver kunna hitta annonsen och följa upp sin egen ansökan med rätt person hos arbetsgivaren, och Jobbliggaren har ett eget intresse av att annonserna går att hitta för allmänheten. Vi har vägt de intressena mot dina rättigheter innan vi började visa annonserna publikt. Arbetsförmedlingens annonsvillkor innebär att annonsören ska ha kontaktpersonens samtycke till publiceringen och att annonsdata får visas hos andra aktörer som hämtar annonser via API:t.",
    en: "The legal basis is legitimate interest (Article 6(1)(f)). There are two interests. A jobseeker needs to be able to find the ad and follow up their own application with the right person at the employer, and Jobbliggaren has its own interest in making the ads findable by the public. We weighed those interests against your rights before we began showing the ads publicly. Arbetsförmedlingen's advertising terms require the advertiser to have the contact person's consent to the publication, and state that ad data may be shown by other actors retrieving ads via the API.",
  },
  {
    key: "recruiterNotice.sections.2.paragraphs.0",
    sv: "Kontaktuppgifterna på en annons raderas när annonsen inte längre går att söka i tjänsten, till exempel när den har arkiverats eller försvunnit ur Platsbankens flöde. Den publika sidan svarar då att annonsen inte längre är tillgänglig. Ett namn i annonsens löpande text raderas inte av det: det finns kvar i annonstexten hos oss tills du begär radering.",
    en: "The contact details on an ad are deleted when the ad can no longer be found in the service, for example when it has been archived or has left the Platsbanken feed. The public page then answers that the ad is no longer available. A name in the ad's running text is not deleted by that: it stays in the ad text with us until you request erasure.",
  },
  {
    key: "recruiterNotice.sections.3.paragraphs.1",
    sv: "Du har också rätt att begära radering (artikel 17). Eftersom ditt namn kan förekomma i annonsens löpande text raderar vi då hela vår kopia av annonsen och hindrar att den hämtas in igen. Vi ber sedan sökmotorerna ta bort annonsens adress hos oss. Vi kan inte ta bort annonsen hos Arbetsförmedlingen, som är den som har publicerat den, och inte heller kopior som sökmotorer, länkförhandsvisningar eller andra har gjort.",
    en: "You also have the right to request erasure (Article 17). Because your name can appear in the ad's running text, we then erase our entire copy of the ad and block it from being retrieved again. We then ask search engines to remove the ad's address on our site. We cannot remove the ad at Arbetsförmedlingen, which is the publisher, nor copies that search engines, link previews or others have made.",
  },
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

describe("content-legal consent-withdrawal copy (ADR 0164 D8, security-auditor signature 2)", () => {
  it.each(SIGNED)("$key stands whole in sv and en", ({ key, sv: s, en: e }) => {
    expect(read(sv, key)).toBe(s);
    expect(read(en, key)).toBe(e);
  });
});

describe("content-legal public-page notice (ADR 0166, security-auditor signature 3)", () => {
  it.each(SIGNED_PUBLIC_PAGE)("$key stands whole in sv and en", ({ key, sv: s, en: e }) => {
    expect(read(sv, key)).toBe(s);
    expect(read(en, key)).toBe(e);
  });
});
