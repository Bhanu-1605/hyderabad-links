"use server";

import { revalidatePath } from "next/cache";
import { setEnquiryStatus } from "@/lib/owner-api";

/**
 * Changes one enquiry's status. The API enforces ownership — an id belonging to another owner
 * answers 404 there — so this only has to report what it said.
 */
export async function setStatusAction(formData: FormData): Promise<void> {
  const id = Number(formData.get("id"));
  const status = String(formData.get("status") ?? "");
  if (!Number.isInteger(id) || id <= 0 || !status) return;

  await setEnquiryStatus(id, status);

  // The unread count appears on the dashboard too, so refresh the whole owner area.
  revalidatePath("/account", "layout");
}
