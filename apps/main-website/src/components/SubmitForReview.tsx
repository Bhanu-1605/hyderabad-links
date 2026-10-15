"use client";

import { useActionState } from "react";
import { submitListingAction } from "@/app/account/actions";
import type { SubmitState } from "@/app/account/form-state";

export function SubmitForReview({ listingId }: { listingId: number }) {
  const [state, formAction, pending] = useActionState<SubmitState>(() => submitListingAction(listingId), { fieldErrors: {} });
  return (
    <form action={formAction} className="rounded-xl border border-neutral-200 bg-white p-4">
      <h2 className="font-semibold">Ready to go live?</h2>
      <p className="mt-1 text-sm text-neutral-600">
        Save your changes first: submitting sends the last saved version. We will check it, and it will appear on the site once approved.
      </p>
      {state.message ? (
        <p role="alert" className="mt-3 text-sm text-red-700">
          {state.message}
        </p>
      ) : null}
      <button type="submit" disabled={pending} className="mt-3 rounded-lg border border-brand-700 px-5 py-2.5 text-sm font-semibold text-brand-700 hover:bg-brand-50 disabled:opacity-60">
        {pending ? "Submitting..." : "Submit for review"}
      </button>
    </form>
  );
}
