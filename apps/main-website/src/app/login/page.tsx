import type { Metadata } from "next";
import { safeNextPath } from "@/lib/session";
import { LoginForm } from "./LoginForm";

export const metadata: Metadata = { title: "Sign in", robots: { index: false, follow: false } };
export const dynamic = "force-dynamic";

type Props = { searchParams: Promise<{ next?: string }> };

export default async function LoginPage({ searchParams }: Props) {
  const { next } = await searchParams;
  return (
    <main className="mx-auto max-w-md px-4 pt-10">
      <h1 className="text-2xl font-bold">Sign in to manage your business</h1>
      <p className="mt-2 text-neutral-600">
        Enter your mobile number and we will text you a code. New here? The same steps create your account.
      </p>
      <div className="mt-6">
        <LoginForm next={safeNextPath(next)} devHint={(process.env.APP_ENV ?? "LOCAL") === "LOCAL"} />
      </div>
    </main>
  );
}
