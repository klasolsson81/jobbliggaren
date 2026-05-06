export default function AuthLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <div className="min-h-full flex flex-col items-center justify-center px-6 py-12 bg-surface-secondary">
      <div className="w-full max-w-sm">{children}</div>
    </div>
  );
}
