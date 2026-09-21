import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import { changeEmail } from "@/lib/auth/challenge-actions";

// "Byt e-postadress" is a submit that LOOKS like a text link, not a link. A GET cannot clear a
// cookie, so a link would leave the address the user just disowned on the device for the rest of
// its fifteen minutes. It also changes state, so a button is the honest element in the
// accessibility tree, and a `<form action>` works without JavaScript.
//
// The classes put the delivered auth text-link form (`confirm-account.tsx`) on the `link` variant:
// the underline always on, never hover-only; `h-auto px-0` so it reads as inline text; and
// `py-2 -my-2` for a 44 px hit area without a taller visual box.
export function ChangeEmailButton() {
  const t = useTranslations("pages");

  return (
    <form action={changeEmail}>
      <Button
        type="submit"
        variant="link"
        size="sm"
        className="-my-2 h-auto px-0 py-2 text-brand-700 underline underline-offset-2"
      >
        {t("auth.passwordless.code.changeEmail")}
      </Button>
    </form>
  );
}
