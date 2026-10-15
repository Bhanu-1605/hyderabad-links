import type { Metadata } from "next";
import { getMe } from "@/lib/owner-api";
import { signOutAction } from "./actions";

export const metadata: Metadata = { robots: { index: false, follow: false } };
export const dynamic = "force-dynamic";

export default async function AccountLayout({ children }: { children: React.ReactNode }) {
  const me = await getMe();
  return (
    <>
      <div className="border-b border-neutral-200 bg-brand-50">
        <div className="mx-auto flex max-w-5xl items-center justify-between gap-3 px-4 py-2 text-sm">
          <span className="text-neutral-700">{me.ok ? `Signed in as ${me.data.phone}` : "Signed in"}</span>
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
