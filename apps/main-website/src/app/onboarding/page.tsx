import type { Metadata } from "next";
import { redirect } from "next/navigation";
import { AuthLayout } from "@/components/ui/AuthLayout";
import { getCategories, getLocalities } from "@/lib/api";
import { DEFAULT_CITY, DEFAULT_CITY_NAME } from "@/lib/brand";
import { getMe } from "@/lib/owner-api";
import { OnboardingForm } from "./OnboardingForm";

export const metadata: Metadata = { title: "Welcome", robots: { index: false, follow: false } };
export const dynamic = "force-dynamic";

export default async function OnboardingPage() {
  // getMe() sends the owner to sign in again if the token is missing or rejected.
  const me = await getMe();
  if (me.ok && !me.data.needsOnboarding) {
    redirect("/account/listings"); // already registered: nothing to do here
  }

  const [tree, localities] = await Promise.all([getCategories(), getLocalities(DEFAULT_CITY)]);
  const phone = me.ok ? me.data.phone : "";

  return (
    <AuthLayout
      title="Welcome to Hyderabad Links"
      subtitle={
        <>
          <p>Let&rsquo;s get your business online in {DEFAULT_CITY_NAME}.</p>
          {phone ? (
            <p className="mt-2 font-bold text-brand-700">
              <span aria-hidden="true">&#10003;</span> Mobile number verified: {phone}
            </p>
          ) : null}
        </>
      }
      aside={{
        heading: "Almost there",
        lead: "A few details and your first listing is ready to edit.",
        points: [
          "Your listing starts as a draft, so nothing goes public yet",
          "Add the address, description and contact details next",
          "Send it for review when you are happy with it",
        ],
      }}
    >
      <OnboardingForm phone={phone} categories={tree} localities={localities ?? []} />
    </AuthLayout>
  );
}
