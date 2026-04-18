# JobbPilot — Composition Examples

Full JSX templates for common Fas 1 flows. Copy and adapt — these are not
generated boilerplate, they are JobbPilot-specific starting points that
already apply the correct tokens, patterns, and Swedish copy conventions.

---

## 1. Login form with error handling

```tsx
// app/logga-in/page.tsx
import { useForm } from "react-hook-form"
import { zodResolver } from "@hookform/resolvers/zod"
import { z } from "zod"
import { Form, FormField, FormItem, FormLabel, FormControl, FormMessage } from "@/components/ui/form"
import { Input } from "@/components/ui/input"
import { Button } from "@/components/ui/button"
import { Alert, AlertDescription } from "@/components/ui/alert"

const schema = z.object({
  email: z.string().email("Ange en giltig e-postadress."),
  password: z.string().min(1, "Ange ditt lösenord."),
})

export default function LoginPage() {
  const form = useForm<z.infer<typeof schema>>({
    resolver: zodResolver(schema),
  })
  const [serverError, setServerError] = useState<string | null>(null)

  async function onSubmit(values: z.infer<typeof schema>) {
    setServerError(null)
    const result = await loginAction(values)
    if (!result.ok) setServerError("Inloggningen misslyckades. Kontrollera e-post och lösenord.")
  }

  return (
    <main className="max-w-sm mx-auto py-12">
      <h1 className="text-h1 mb-6">Logga in</h1>

      {serverError && (
        <Alert variant="danger" className="mb-4">
          <AlertDescription>{serverError}</AlertDescription>
        </Alert>
      )}

      <Form {...form}>
        <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-4">
          <FormField control={form.control} name="email" render={({ field }) => (
            <FormItem>
              <FormLabel>E-post</FormLabel>
              <FormControl><Input type="email" autoComplete="email" {...field} /></FormControl>
              <FormMessage />
            </FormItem>
          )} />

          <FormField control={form.control} name="password" render={({ field }) => (
            <FormItem>
              <FormLabel>Lösenord</FormLabel>
              <FormControl><Input type="password" autoComplete="current-password" {...field} /></FormControl>
              <FormMessage />
            </FormItem>
          )} />

          <Button type="submit" className="w-full" disabled={form.formState.isSubmitting}>
            {form.formState.isSubmitting ? "Loggar in…" : "Logga in"}
          </Button>
        </form>
      </Form>
    </main>
  )
}
```

---

## 2. Application list with filter + table + pagination

```tsx
// app/ansokningar/page.tsx (Server Component)
import { Suspense } from "react"
import { Table, TableHeader, TableRow, TableHead, TableBody, TableCell } from "@/components/ui/table"
import { Badge } from "@/components/ui/badge"
import { Skeleton } from "@/components/ui/skeleton"
import { Alert, AlertTitle, AlertDescription } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import Link from "next/link"

export default async function ApplicationsPage({
  searchParams,
}: {
  searchParams: Promise<{ page?: string; status?: string }>
}) {
  const { page = "1", status } = await searchParams
  const result = await getApplications({ page: Number(page), status })

  return (
    <main>
      <div className="flex items-center justify-between mb-6">
        <h1 className="text-h1">Ansökningar</h1>
        <Button asChild>
          <Link href="/jobb">Hitta jobb</Link>
        </Button>
      </div>

      {/* Status filter (simplified) */}
      <div className="flex gap-2 mb-4">
        {["Alla", "Aktiva", "Avslutade"].map((label) => (
          <Button key={label} variant={status === label ? "secondary" : "ghost"} size="sm">
            {label}
          </Button>
        ))}
      </div>

      <Suspense fallback={<ApplicationTableSkeleton />}>
        {result.items.length === 0 ? (
          <Alert>
            <AlertTitle>Inga ansökningar</AlertTitle>
            <AlertDescription>
              Du har inga aktiva ansökningar. Hitta jobb som passar din profil under Jobb.
            </AlertDescription>
            <Button asChild variant="primary" className="mt-3">
              <Link href="/jobb">Visa jobb</Link>
            </Button>
          </Alert>
        ) : (
          <>
            <p className="text-body-sm text-text-secondary mb-2">
              Visar {result.from}–{result.to} av {result.total}
            </p>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Företag</TableHead>
                  <TableHead>Roll</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Senast uppdaterad</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {result.items.map((app) => (
                  <TableRow key={app.id} className="cursor-pointer hover:bg-surface-secondary">
                    <TableCell>{app.companyName}</TableCell>
                    <TableCell>{app.roleName}</TableCell>
                    <TableCell><Badge variant={app.statusVariant}>{app.statusLabel}</Badge></TableCell>
                    <TableCell className="text-text-secondary">{app.updatedAt}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
            {/* Pagination */}
            <div className="flex items-center justify-between mt-4">
              <Button variant="ghost" size="sm" disabled={!result.hasPrev} asChild>
                <Link href={`?page=${Number(page) - 1}`}>Föregående</Link>
              </Button>
              <span className="text-body-sm text-text-secondary">Sida {page} av {result.totalPages}</span>
              <Button variant="ghost" size="sm" disabled={!result.hasNext} asChild>
                <Link href={`?page=${Number(page) + 1}`}>Nästa</Link>
              </Button>
            </div>
          </>
        )}
      </Suspense>
    </main>
  )
}

function ApplicationTableSkeleton() {
  return (
    <div className="space-y-2">
      {Array.from({ length: 5 }).map((_, i) => (
        <Skeleton key={i} className="h-11 w-full rounded-md" />
      ))}
    </div>
  )
}
```

---

## 3. CV upload with progress + toast

```tsx
// components/cv/CvUpload.tsx
"use client"
import { useState, useTransition } from "react"
import { Button } from "@/components/ui/button"
import { useToast } from "@/components/ui/use-toast"
import { Upload } from "lucide-react"

export function CvUpload() {
  const [isPending, startTransition] = useTransition()
  const { toast } = useToast()

  function handleUpload(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    if (!file) return

    startTransition(async () => {
      const result = await uploadCvAction(file)
      if (result.ok) {
        toast({
          title: "CV uppladdat",
          description: `${file.name} är nu din aktiva profil.`,
          duration: 3000,
        })
      } else {
        toast({
          title: "Uppladdning misslyckades",
          description: result.error ?? "Försök igen om en stund.",
          variant: "destructive",
          // no duration — error persists until dismissed
        })
      }
    })
  }

  return (
    <label className="cursor-pointer">
      <input
        type="file"
        accept=".pdf,.docx"
        className="sr-only"
        onChange={handleUpload}
        aria-label="Ladda upp CV (PDF eller Word)"
      />
      <Button variant="secondary" disabled={isPending} asChild>
        <span>
          <Upload className="size-4 mr-2" aria-hidden />
          {isPending ? "Laddar upp…" : "Ladda upp CV"}
        </span>
      </Button>
    </label>
  )
}
```

---

## 4. OAuth connection with consent dialog

```tsx
// components/integrations/GmailConnect.tsx
"use client"
import {
  Dialog, DialogContent, DialogTitle, DialogDescription, DialogFooter
} from "@/components/ui/dialog"
import { Button } from "@/components/ui/button"
import { useState } from "react"
import Link from "next/link"

export function GmailConnect() {
  const [open, setOpen] = useState(false)

  function handleConfirm() {
    setOpen(false)
    // Redirect to OAuth flow
    window.location.href = "/api/auth/gmail/start"
  }

  return (
    <>
      <Button variant="secondary" onClick={() => setOpen(true)}>
        Koppla Gmail
      </Button>

      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent>
          <DialogTitle>Koppla ditt Gmail-konto</DialogTitle>
          <DialogDescription asChild>
            <div className="space-y-3 text-body text-text-primary">
              <p>
                För att importera dina jobbrelaterade e-postmeddelanden
                behöver JobbPilot åtkomst till Gmail.
              </p>
              <p>Vi begär följande behörigheter:</p>
              <ul className="list-disc pl-5 space-y-1 text-body-sm">
                <li>Läsa e-post (gmail.readonly) — inte skriva eller skicka</li>
              </ul>
              <p className="text-body-sm text-text-secondary">
                Din data skickas till Anthropics API för att identifiera
                jobbrelaterade mejl. Läs{" "}
                <Link href="/integritetspolicy" className="text-brand-600 underline">
                  integritetspolicyn
                </Link>{" "}
                innan du fortsätter.
              </p>
            </div>
          </DialogDescription>
          <DialogFooter>
            <Button variant="ghost" onClick={() => setOpen(false)}>Avbryt</Button>
            <Button variant="primary" onClick={handleConfirm}>
              Fortsätt till Google
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  )
}
```

Note: this consent dialog is required by GDPR Art. 7 — do not simplify
or remove it without consulting `security-auditor`.
