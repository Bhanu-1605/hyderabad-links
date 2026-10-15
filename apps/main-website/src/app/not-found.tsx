import Link from "next/link";

export default function NotFound() {
  return (
    <main className="mx-auto max-w-3xl px-4 pt-16 text-center">
      <h1 className="text-3xl font-bold">Page not found</h1>
      <p className="mt-2 text-neutral-600">We could not find that page.</p>
      <Link href="/" className="mt-6 inline-block rounded-lg bg-brand-700 px-5 py-2.5 font-medium text-white hover:bg-brand-900">
        Back to home
      </Link>
    </main>
  );
}
