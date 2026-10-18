"use server";

import { redirect } from "next/navigation";
import { onboard } from "@/lib/owner-api";
import type { OnboardingState } from "./state";

/**
 * Registers the signed-in owner. The API does the real validation and decides whether anything was
 * created, so this action only collects the fields and passes failures back to the form.
 */
export async function onboardAction(_previous: OnboardingState, formData: FormData): Promise<OnboardingState> {
  const read = (field: string) => String(formData.get(field) ?? "").trim();
  const values = {
    name: read("name"),
    businessName: read("businessName"),
    email: read("email"),
    localitySlug: read("localitySlug"),
    categorySlug: read("categorySlug"),
  };

  // The two dropdowns have no blank-but-valid state, so catch an unanswered one here rather than
  // sending it to the API only to be told "Unknown area".
  const fieldErrors: Record<string, string> = {};
  if (!values.localitySlug) fieldErrors.localitySlug = "Choose the area your business is in.";
  if (!values.categorySlug) fieldErrors.categorySlug = "Choose what your business does.";
  if (Object.keys(fieldErrors).length > 0) {
    return { values, message: "Please fix the fields marked below.", fieldErrors };
  }

  const result = await onboard(values);
  if (!result.ok) {
    return { values, message: result.message, fieldErrors: result.fieldErrors };
  }

  // redirect() throws, so it must sit outside any try/catch.
  redirect("/account/listings?welcome=1");
}
