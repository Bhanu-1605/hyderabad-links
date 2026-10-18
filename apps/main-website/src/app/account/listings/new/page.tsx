import type { Metadata } from "next";
import Link from "next/link";
import { ListingForm } from "@/components/ListingForm";
import { getCategories, getLocalities } from "@/lib/api";
import { DEFAULT_CITY } from "@/lib/brand";
import { saveListingAction } from "../../actions";

export const metadata: Metadata = { title: "Add your business" };

export default async function NewListingPage() {
  const [categories, localities] = await Promise.all([getCategories(), getLocalities(DEFAULT_CITY)]);

  return (
    <main className="mx-auto max-w-3xl px-5 py-10 sm:px-8">
      <Link href="/account/listings" className="text-sm font-bold text-brand-700 hover:underline">
        <span aria-hidden="true">&larr;</span> Back to my listings
      </Link>

      <div className="mt-4 border-b border-line pb-5">
        <p className="text-[11px] font-black tracking-[0.22em] text-muted">BUSINESS OWNER</p>
        <h1 className="mt-1 text-3xl font-black tracking-tight text-brand-800">Add your business</h1>
        <p className="mt-2 text-muted">
          Start with the basics. It stays private as a draft until you submit it for review.
        </p>
      </div>

      <div className="mt-6">
        <ListingForm
          action={saveListingAction.bind(null, null)}
          categories={categories}
          localities={localities ?? []}
          initialValues={{}}
          submitLabel="Save draft"
        />
      </div>
    </main>
  );
}
