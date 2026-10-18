"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { updateProfile } from "@/lib/owner-api";
import type { ProfileFormState } from "./form-state";

/**
 * Saves the owner's own name and email. The API validates both again and is the only thing that
 * decides whether the save happened — this action just reports back what it said.
 *
 * On success it does two things, and both are needed. The owner's name also appears in the
 * dashboard's own bar, which lives in the layout *above* this page:
 *
 * - `revalidatePath("/account", "layout")` drops that layout from Next's client router cache.
 *   Without it the bar keeps the old name however many times the page itself re-renders.
 * - the redirect then renders layout and page together, and carries the confirmation in `?saved=1`
 *   rather than in returned state, which a redirect would discard.
 */
export async function saveProfileAction(
  previous: ProfileFormState,
  formData: FormData,
): Promise<ProfileFormState> {
  const values = {
    name: String(formData.get("name") ?? "").trim(),
    email: String(formData.get("email") ?? "").trim(),
  };

  const result = await updateProfile(values);
  if (!result.ok) {
    return { rev: previous.rev + 1, values, message: result.message, fieldErrors: result.fieldErrors };
  }

  revalidatePath("/account", "layout");

  // redirect() throws, so it must sit outside any try/catch.
  redirect("/account/profile?saved=1");
}
