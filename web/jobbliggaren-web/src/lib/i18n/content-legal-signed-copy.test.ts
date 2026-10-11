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
