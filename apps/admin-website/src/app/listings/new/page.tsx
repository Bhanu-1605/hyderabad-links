import type { Metadata } from "next";
import Link from "next/link";
import { ListingForm } from "@/components/ListingForm";
import { PERMISSION_CREATE, PERMISSION_REVIEW, getAdminProfile, getCategories, getLocalities } from "@/lib/admin-api";
import { saveListingAction } from "../actions";

export const metadata: Metadata = { title: "Add a listing" };
export const dynamic = "force-dynamic";

export default async function AddListingPage() {
  const [me, categories, localities] = await Promise.all([getAdminProfile(), getCategories(), getLocalities()]);

  // The API refuses this anyway; the page only avoids showing a form that would be rejected.
  if (me.ok && !me.data.permissions.includes(PERMISSION_CREATE)) {
    return (
      <main className="mx-auto max-w-2xl px-4 pt-8">
        <h1 className="text-2xl font-bold">Add a listing</h1>
        <p className="mt-4 rounded-xl border border-neutral-200 bg-white p-4 text-sm text-neutral-600 shadow-sm">
          Your account cannot add listings. Ask a super admin for the &quot;Add listings manually&quot; permission.
        </p>
      </main>
    );
  }

  const goesLiveAtOnce = me.ok && me.data.permissions.includes(PERMISSION_REVIEW);

  return (
    <main className="mx-auto max-w-2xl px-4 pt-8 pb-12">
      <Link href="/listings" className="text-sm font-medium text-brand-700 underline-offset-2 hover:underline">
        &larr; Back to the queue
      </Link>
      <h1 className="mt-3 text-2xl font-bold">Add a listing</h1>
      <p className="mt-1 text-neutral-600">
        For a business that phoned in or walked in. {goesLiveAtOnce
          ? "It goes live as soon as you save it."
          : "It joins the review queue, because your account cannot publish listings."}
      </p>

      <div className="mt-6">
        <ListingForm
          action={saveListingAction.bind(null, null)}
          categories={categories}
          localities={localities}
          initialValues={{}}
          submitLabel={goesLiveAtOnce ? "Add and publish" : "Add for review"}
        />
      </div>
    </main>
  );
}
