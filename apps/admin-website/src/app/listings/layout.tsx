import type { Metadata } from "next";
import Link from "next/link";
import { getAdminProfile } from "@/lib/admin-api";
import { signOutAction } from "./actions";

export const metadata: Metadata = { robots: { index: false, follow: false } };
export const dynamic = "force-dynamic";

export default async function ModerationLayout({ children }: { children: React.ReactNode }) {
  const me = await getAdminProfile();

  return (
    <>
      <div className="border-b border-neutral-200 bg-brand-50">
        <div className="mx-auto flex max-w-5xl flex-wrap items-center justify-between gap-3 px-4 py-2 text-sm">
          <div className="flex items-center gap-4">
            <Link href="/listings" className="font-semibold text-brand-900">
              Listings
            </Link>
            <span className="text-neutral-700">
              {me.ok ? `${me.data.displayName} (${me.data.roles.join(", ") || "no role"})` : "Signed in"}
            </span>
          </div>
          <form action={signOutAction}>
            <button type="submit" className="font-medium text-brand-700 underline-offset-2 hover:underline">
              Sign out
            </button>
          </form>
        </div>
      </div>
      {children}
    </>
  );
}
