import type { Metadata } from "next";
import { BRAND_NAME } from "@/lib/brand";
import { safeNextPath } from "@/lib/session";
import { LoginForm } from "./LoginForm";

export const metadata: Metadata = { title: "Sign in", robots: { index: false, follow: false } };
export const dynamic = "force-dynamic";

type Props = { searchParams: Promise<{ next?: string }> };

export default async function LoginPage({ searchParams }: Props) {
  const { next } = await searchParams;
  return (
    <main className="mx-auto max-w-md px-4 pt-10">
      <h1 className="text-2xl font-bold">{BRAND_NAME} platform administration</h1>
      <p className="mt-2 text-neutral-600">
        Only accounts with platform administration access can sign in here. Everyone else uses the
        admin site.
      </p>
      <div className="mt-6">
        <LoginForm next={safeNextPath(next)} />
      </div>
    </main>
  );
}
