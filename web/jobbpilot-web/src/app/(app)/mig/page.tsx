import { redirect } from "next/navigation";
import { getServerSession } from "@/lib/auth/session";
import { getMyProfile } from "@/lib/api/me";
import { assertNever, type ApiResult } from "@/lib/dto/_helpers";
import type { JobSeekerProfileDto } from "@/lib/dto/me";
import { MeProfileForm } from "@/components/me/me-profile-form";
import {
  Card,
  CardHeader,
  CardTitle,
  CardContent,
} from "@/components/ui/card";

export default async function MigPage() {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  // unauthorized hanteras tidigt så `renderProfile` får en smalare typ;
  // detail-pages använder switch-inom-pattern eftersom de saknar
  // partial-render-yta. Båda är legitima konventioner per ADR 0030.
  const profileResult = await getMyProfile();
  if (profileResult.kind === "unauthorized") redirect("/logga-in");

  return (
    <div className="flex flex-col gap-6">
      <h1 className="text-h1 font-medium text-text-primary">Min profil</h1>

      <Card className="max-w-lg">
        <CardHeader>
          <CardTitle asChild>
            <h2>Kontoinformation</h2>
          </CardTitle>
        </CardHeader>
        <CardContent>
          <dl className="flex flex-col gap-4">
            <div className="flex flex-col gap-1">
              <dt className="text-body-sm text-text-secondary">E-postadress</dt>
              <dd className="text-body text-text-primary">{user.email}</dd>
            </div>
            {user.roles && user.roles.length > 0 && (
              <div className="flex flex-col gap-1">
                <dt className="text-body-sm text-text-secondary">Roller</dt>
                <dd className="text-body text-text-primary">
                  {user.roles.join(", ")}
                </dd>
              </div>
            )}
          </dl>
        </CardContent>
      </Card>

      <Card className="max-w-lg">
        <CardHeader>
          <CardTitle asChild>
            <h2>Profil</h2>
          </CardTitle>
        </CardHeader>
        <CardContent>{renderProfile(profileResult)}</CardContent>
      </Card>
    </div>
  );
}

function renderProfile(
  result: Exclude<ApiResult<JobSeekerProfileDto>, { kind: "unauthorized" }>
) {
  switch (result.kind) {
    case "ok":
      return <MeProfileForm initialProfile={result.data} />;
    case "notFound":
      // Profil-rad finns inte ännu (nytt konto innan onboarding) — legitimt
      // tillstånd, inte fel. Egen copy enligt copy-skill empty-state-pattern.
      return (
        <p className="text-body text-text-secondary">
          Din profil är inte skapad ännu. Fyll i uppgifterna nedan för att
          komma igång.
        </p>
      );
    case "forbidden":
    case "error":
      return (
        <p className="text-body text-text-secondary">
          Profilen kunde inte hämtas just nu. Försök ladda om sidan om en
          stund.
        </p>
      );
    default:
      return assertNever(result);
  }
}
