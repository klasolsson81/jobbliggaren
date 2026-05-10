import { redirect } from "next/navigation";
import { getServerSession } from "@/lib/auth/session";
import { getMyProfile } from "@/lib/api/me";
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

  const profile = await getMyProfile();

  return (
    <div className="flex flex-col gap-6">
      <h1 className="text-h1 font-medium text-text-primary">Min profil</h1>

      <Card className="max-w-lg">
        <CardHeader>
          <CardTitle>Kontoinformation</CardTitle>
        </CardHeader>
        <CardContent>
          <dl className="flex flex-col gap-4">
            <div className="flex flex-col gap-1">
              <dt className="text-body-sm text-text-secondary">Användar-id</dt>
              <dd className="text-body text-text-primary font-mono">
                {user.userId}
              </dd>
            </div>
            <div className="flex flex-col gap-1">
              <dt className="text-body-sm text-text-secondary">E-postadress</dt>
              <dd className="text-body text-text-primary">{user.email}</dd>
            </div>
            <div className="flex flex-col gap-1">
              <dt className="text-body-sm text-text-secondary">Roller</dt>
              <dd className="text-body text-text-primary">
                {user.roles && user.roles.length > 0
                  ? user.roles.join(", ")
                  : "Inga roller tilldelade"}
              </dd>
            </div>
          </dl>
        </CardContent>
      </Card>

      <Card className="max-w-lg">
        <CardHeader>
          <CardTitle>Profil</CardTitle>
        </CardHeader>
        <CardContent>
          {profile ? (
            <MeProfileForm initialProfile={profile} />
          ) : (
            <p className="text-body text-text-secondary" role="alert">
              Kunde inte hämta din profil. Försök ladda om sidan.
            </p>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
