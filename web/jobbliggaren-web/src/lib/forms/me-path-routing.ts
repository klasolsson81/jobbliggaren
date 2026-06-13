/**
 * Mappar `serverError.path` från `updateMyProfileSchema` (Zod) till HTML `id`-attributet
 * på motsvarande form-kontroll i `MeProfileForm`. Används för programmatisk focus-flytt
 * vid server-validation-fel (TD-15 a11y-pattern).
 *
 * Returnerar `null` om path inte mappar mot ett känt fält — då skippas focus-flytten
 * i `useEffect`-callbacken (better than focusing the wrong element).
 *
 * Function:s domän-kunskap är `MeProfileForm`-fältuppsättningen. Extraherad till
 * separat modul per TD-46 för isolated unit-tests (komponent-tester slipper kämpa
 * mot jsdom-quirks som HTML5-constraint-validation på `type="email"`).
 */
export function pathToElementId(path: string): string | null {
  switch (path) {
    case "displayName":
      return "me-displayName";
    case "language":
      return "me-language";
    case "emailNotifications":
      return "me-emailNotifications";
    case "weeklySummary":
      return "me-weeklySummary";
    default:
      return null;
  }
}
