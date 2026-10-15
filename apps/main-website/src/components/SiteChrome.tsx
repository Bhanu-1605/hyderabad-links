import Link from "next/link";
import { BRAND_NAME } from "@/lib/brand";

export function SiteHeader() {
  return (
    <header className="border-b border-neutral-200 bg-white">
      <div className="mx-auto flex max-w-5xl items-center justify-between px-4 py-3">
        <Link href="/" className="text-lg font-bold tracking-tight text-brand-700">
          {BRAND_NAME}
        </Link>
        <Link href="/account/listings" className="text-sm font-medium text-brand-700 hover:underline">
          List your business
        </Link>
      </div>
    </header>
  );
}

export function SiteFooter() {
  return (
    <footer className="mt-16 border-t border-neutral-200 bg-neutral-50">
      <div className="mx-auto max-w-5xl px-4 py-6 text-sm text-neutral-500">
        &copy; {new Date().getFullYear()} {BRAND_NAME}. Hyderabad&apos;s local business directory.
      </div>
    </footer>
  );
}
