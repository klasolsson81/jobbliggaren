import { Suspense } from "react";
import Link from "next/link";
import { RegisterForm } from "@/components/forms/RegisterForm";

export default function RegistreraPage() {
  return (
    <div className="flex flex-col gap-8">
      <div className="flex flex-col gap-1">
        <h1 className="text-h2 font-medium text-text-primary">Skapa konto</h1>
        <p className="text-body text-text-secondary">JobbPilot</p>
      </div>

      <Suspense fallback={null}>
        <RegisterForm />
      </Suspense>

      <p className="text-sm text-text-secondary text-center">
        Har du redan ett konto?{" "}
        <Link
          href="/logga-in"
          className="text-brand-600 hover:text-brand-700 underline underline-offset-2"
        >
          Logga in
        </Link>
      </p>
    </div>
  );
}
