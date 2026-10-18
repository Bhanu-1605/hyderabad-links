import type { Metadata } from "next";
import { AuthLayout } from "@/components/ui/AuthLayout";
import { safeNextPath } from "@/lib/session";
import { LoginForm } from "./LoginForm";

export const metadata: Metadata = { title: "Sign in", robots: { index: false, follow: false } };
export const dynamic = "force-dynamic";

type Props = { searchParams: Promise<{ next?: string }> };

export default async function LoginPage({ searchParams }: Props) {
  const { next } = await searchParams;

  return (
    <AuthLayout
      title="Welcome back"
      subtitle="Sign in to manage your business. New here? The same steps create your account."
      aside={{
        heading: "Welcome to Hyderabad Links",
        lead: "Hyderabad's local business directory.",
        points: [
          "Be found by customers searching your area",
          "Keep your details and digital profiles in one place",
          "Free to list, and reviewed before it goes live",
        ],
      }}
    >
      <LoginForm next={safeNextPath(next)} devHint={(process.env.APP_ENV ?? "LOCAL") === "LOCAL"} />
    </AuthLayout>
  );
}
