import { Suspense } from "react";
import Link from "next/link";
import { LoginForm } from "@/components/forms/LoginForm";

export default function LoggaInPage() {
  return (
    <div className="flex flex-col gap-8">
      <div className="flex flex-col gap-1">
        <h1 className="text-h2 font-medium text-text-primary">Logga in</h1>
        <p className="text-body text-text-secondary">Jobbliggaren</p>
      </div>

      <Suspense fallback={null}>
        <LoginForm />
      </Suspense>

      <p className="text-sm text-text-secondary text-center">
        Inget konto?{" "}
        <Link
          href="/vantelista"
          className="text-brand-600 hover:text-brand-700 underline underline-offset-2"
        >
          Anmäl dig till väntelistan
        </Link>
      </p>
    </div>
  );
}
