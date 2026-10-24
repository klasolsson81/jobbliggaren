import { describe, expect, it } from "vitest";
import sv from "../../../messages/sv/content-legal.json";
import en from "../../../messages/en/content-legal.json";

const SIGNED: ReadonlyArray<{ key: string; sv: string; en: string }> = [
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

// Signature 4 (security-auditor, ADR 0175 D12, #1663 PR 2): following companies, Branschbevakningar and their
// retention, the consent section's scope, and the withdrawal paragraph that replaces signature 2's.
const SIGNED_COMPANY_WATCHES: ReadonlyArray<{ key: string; sv: string; en: string }> = [
  {
    key: "privacy.sections.2.list.6",
    sv: "Företag du följer. Följer du ett företag sparar vi vilket företag det är och de val du gör i filtret för det, till exempel Endast annonser som matchar min profil eller vilka orter annonserna ska gälla. Varje natt går vi igenom nya annonser från företagen du följer och sparar en träff för varje ny annons som passar de orter du eventuellt har valt. Antalet nya annonser visas för dig på Översikt, och träffarna på sidan Nya annonser från bevakade företag och under Notiser. Vi jämför annonserna med dina matchningsönskemål, och resultatet sparas inte. Vi sparar också vad du har sett av dem, så att samma annons inte visas som ny igen. Det här ingår i tjänsten och kräver inget samtycke. E-post om företag du följer skickas bara med ditt samtycke, se avsnittet Bevakningsnotiser i bakgrunden.",
    en: "Companies you follow. If you follow a company, we store which company it is and the choices you make in its filter, for example Only ads that match my profile or which locations the ads should cover. Every night we go through new ads from the companies you follow and store a hit for each new ad that fits any locations you have chosen. The number of new ads is shown to you on the Overview, and the hits on the page New ads from followed companies and under Notifications. We compare the ads with your matching preferences, and the result is not stored. We also store what you have seen of them, so that the same ad is not shown as new again. This is part of the service and requires no consent. Emails about companies you follow are only sent with your consent, see the section Background watch notifications.",
  },
  {
    key: "privacy.sections.2.list.7",
    sv: "Branschbevakningar. Skapar du en branschbevakning sparar vi de branscher och kommuner du väljer och det namn du eventuellt ger den. Vi använder dem för att visa företag i SCB:s företagsregister som har sitt säte i de kommunerna och hör till de branscherna, och annonserna från dem. Varje natt går vi igenom nya annonser från de företagen och sparar en träff för varje ny annons, oavsett om den matchar din profil. Antalet nya annonser visas på Översikt, och annonserna visas på bevakningens sida med nya annonser. Vilka av dem som matchar dina matchningsönskemål räknas fram när du öppnar sidan och sparas inte. Vi sparar också hur långt du har sett, när du senast ändrade bevakningens branscher eller kommuner och hur långt den nattliga genomgången har kommit, så att bara det som är nytt för dig räknas. Matchar en bevakning fler företag än vi kan gå igenom sparar vi inga nya träffar för den. Det här ingår i tjänsten och kräver inget samtycke. Vi mejlar inte om branschbevakningar, och inga uppgifter om dig lämnas till SCB.",
    en: "Industry watches. If you create an industry watch, we store the industries and municipalities you choose and any name you give it. We use them to show companies in Statistics Sweden's business register that have their registered office in those municipalities and belong to those industries, and the ads from them. Every night we go through new ads from those companies and store a hit for each new ad, whether or not it matches your profile. The number of new ads is shown on the Overview, and the ads are shown on the watch's page of new ads. Which of them match your matching preferences is worked out when you open the page and is not stored. We also store how far you have seen, when you last changed the watch's industries or municipalities and how far the nightly run has got, so that only what is new to you is counted. If a watch matches more companies than we can go through, we store no new hits for it. This is part of the service and requires no consent. We do not email about industry watches, and no data about you is passed to Statistics Sweden.",
  },
  {
    key: "privacy.sections.6.paragraphs.0",
    sv: "Om du vill kan du slå på notiser om nya jobb som matchar dina önskemål och, som ett eget val, e-postnotiser om nya annonser från företag du följer. Behandlingen i det här avsnittet sker bara för det du själv aktivt slår på. Den rättsliga grunden är ditt samtycke (artikel 6.1 a). Båda är avstängda som standard. Slår du på något av dem sparar vi en uppgift om att och när du lämnade ditt samtycke till just det, så att vi kan visa att behandlingen är laglig. Nya annonser från företag du följer och från dina branschbevakningar visas i appen utan samtycke, eftersom det ingår i tjänsten. Det beskrivs i avsnittet Konto, profil och ansökningar.",
    en: "If you want, you can turn on notifications about new jobs that match your preferences and, as a separate choice, email notifications about new ads from companies you follow. The processing in this section only takes place for what you actively turn on yourself. The legal basis is your consent (Article 6(1)(a)). Both are off by default. If you turn either of them on, we store a record that you gave your consent to it and when, so that we can demonstrate that the processing is lawful. New ads from companies you follow and from your industry watches are shown in the app without consent, because that is part of the service. This is described in the section Account, profile and applications.",
  },
  {
    key: "privacy.sections.6.paragraphs.2",
    sv: "Du kan när som helst återkalla ett samtycke genom att stänga av det i dina inställningar. Återkallar du samtycket till matchning slutar vi att i bakgrunden matcha nya annonser mot din profil och att mejla dig matchningar. Återkallar du samtycket till mejl om företag du följer slutar vi mejla dem till dig. Så länge det samtycket gäller och du har valt Endast annonser som matchar min profil för ett företag, jämför vi företagets nya annonser med din profil när mejlet skapas, även om du har återkallat samtycket till matchning. Stänger du i stället av alla e-postnotiser, eller bara mejlen om toppmatchningar eller sammanfattningen, slutar vi skicka de mejlen, men dina samtycken står kvar och matchningen fortsätter i appen. Att du återkallar ett samtycke påverkar inte att behandlingen var laglig innan du återkallade det.",
    en: "You can withdraw a consent at any time by turning it off in your settings. If you withdraw your consent to matching, we stop matching new job ads against your profile in the background and emailing you matches. If you withdraw your consent to emails about companies you follow, we stop emailing them to you. As long as that consent applies and you have chosen Only ads that match my profile for a company, we compare the company's new ads with your profile when the email is created, even if you have withdrawn your consent to matching. If you instead turn off all email notifications, or only the emails about top matches or the summary, we stop sending those emails, but your consents remain and matching continues in the app. Withdrawing a consent does not affect the lawfulness of the processing before you withdrew it.",
  },
  {
    key: "privacy.sections.9.list.10",
    sv: "Företag du följer. Vilka företag du följer och träffarna på deras annonser sparas så länge ditt konto finns. Slutar du följa ett företag visas varken företaget eller dess träffar längre, men uppgifterna finns kvar tills ditt konto raderas enligt punkterna ovan.",
    en: "Companies you follow. Which companies you follow and the hits on their ads are kept for as long as your account exists. If you stop following a company, neither the company nor its hits are shown any longer, but the data remains until your account is deleted in accordance with the points above.",
  },
  {
    key: "privacy.sections.9.list.11",
    sv: "Branschbevakningar. En branschbevakning sparas tills du tar bort den eller tills ditt konto raderas enligt punkterna ovan. Tar du bort den raderas den direkt, tillsammans med dess träffar och de företag vi har räknat fram för den. En träff rensas automatiskt vid den nattliga genomgången efter att annonsen har slutat vara aktiv, och den visas inte någonstans efter att annonsen har slutat vara aktiv.",
    en: "Industry watches. An industry watch is kept until you remove it or until your account is deleted in accordance with the points above. If you remove it, it is deleted immediately, together with its hits and the companies we worked out for it. A hit is removed automatically in the nightly run after the ad has stopped being active, and it is not shown anywhere once the ad has stopped being active.",
  },
];

// Signature 5 (security-auditor, #1981 PR 4 plan ruling 8, 2026-10-24): the DMARC aggregate reports dmarc@ receives,
// published under Art. 14(5)(b). Her B1/B2 sentences are not here yet; they need her re-sign when they ship.
const SIGNED_DMARC_REPORTS: ReadonlyArray<{ key: string; sv: string; en: string }> = [
  {
    key: "privacy.sections.3.list.10",
    sv: "Rapporter om e-post som skickas i vårt namn. E-postleverantörer som tar emot mejl där jobbliggaren.se står som avsändare, till exempel Google, skickar oss sammanställningar över vilka servrar som har skickat sådana mejl och om mejlen klarade kontrollen av att de verkligen kom från oss. Sammanställningarna innehåller de avsändande servrarnas IP-adresser, antal mejl och kontrollernas resultat, men inte mejlens innehåll eller mottagare. En IP-adress kan ibland höra till en enskild person, till exempel den som skickar e-post från en egen server. Rapporterna tas emot i vår brevlåda dmarc@jobbliggaren.se. Vårt berättigade intresse är att upptäcka om någon skickar e-post i vårt namn utan att få göra det, och att våra egna mejl inte stoppas som förfalskade. Uppgifterna kommer från de leverantörer som skickar rapporterna, inte från den som skickade mejlet. Rapporterna innehåller inga kontaktuppgifter till dem det gäller, så vi kan inte informera dem direkt och beskriver därför behandlingen här. Vill du utöva dina rättigheter för en sådan uppgift behöver du ange vilken IP-adress det gäller.",
    en: "Reports on email sent in our name. Email providers that receive emails where jobbliggaren.se appears as the sender, for example Google, send us summaries of which servers have sent such emails and whether the emails passed the check that they really came from us. The summaries contain the sending servers' IP addresses, the number of emails and the results of the checks, but not the content or the recipients of the emails. An IP address can sometimes belong to an individual, for example someone who sends email from their own server. The reports are received in our mailbox dmarc@jobbliggaren.se. Our legitimate interest is to detect whether anyone sends email in our name without being allowed to, and to keep our own emails from being stopped as forged. The data comes from the providers that send the reports, not from the person who sent the email. The reports contain no contact details for the people concerned, so we cannot inform them directly and therefore describe the processing here. If you want to exercise your rights regarding such data, you need to state which IP address it concerns.",
  },
  {
    key: "privacy.sections.7.paragraphs.6",
    sv: "STRATO GmbH (leverantör för inkommande e-post, Tyskland) tar emot och lagrar den e-post du skickar till vår kontaktadress kontakt@jobbliggaren.se, till exempel när du vill utöva dina rättigheter. De uppgifter som lämnas är din e-postadress och det du själv skriver i meddelandet. Vi behåller sådan korrespondens i tolv månader efter att ärendet har avslutats. Om posten aldrig blir ett avslutat ärende behåller vi den i tolv månader från det att den kom in. STRATO tar också emot och lagrar de rapporter om e-post som skickas i vårt namn som andra e-postleverantörer skickar till dmarc@jobbliggaren.se, se avsnittet Säkerhet, drift och produktfunktioner.",
    en: "STRATO GmbH (provider for incoming email, Germany) receives and stores the email you send to our contact address kontakt@jobbliggaren.se, for example when you want to exercise your rights. The data disclosed is your email address and what you write in the message yourself. We keep such correspondence for twelve months after the matter has been closed. If the message never becomes a closed matter, we keep it for twelve months from the day it arrived. STRATO also receives and stores the reports on email sent in our name that other email providers send to dmarc@jobbliggaren.se, see the section Security, operations and product features.",
  },
  {
    key: "privacy.sections.9.list.13",
    sv: "Rapporter om e-post som skickas i vårt namn. Rapporterna sparas i tolv månader från det att de kom in och raderas därefter.",
    en: "Reports on email sent in our name. The reports are kept for twelve months from when they arrived and are then deleted.",
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

describe("content-legal company watches and Branschbevakningar (ADR 0175, security-auditor signature 4)", () => {
  it.each(SIGNED_COMPANY_WATCHES)("$key stands whole in sv and en", ({ key, sv: s, en: e }) => {
    expect(read(sv, key)).toBe(s);
    expect(read(en, key)).toBe(e);
  });
});

describe("content-legal DMARC aggregate reports (#1981, security-auditor signature 5)", () => {
  it.each(SIGNED_DMARC_REPORTS)("$key stands whole in sv and en", ({ key, sv: s, en: e }) => {
    expect(read(sv, key)).toBe(s);
    expect(read(en, key)).toBe(e);
  });
});
