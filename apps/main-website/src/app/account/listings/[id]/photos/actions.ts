"use server";

import { revalidatePath } from "next/cache";
import { deleteListingMedia, moveListingMedia, uploadListingMedia } from "@/lib/owner-api";
import type { MediaFormState } from "./form-state";

const KINDS = ["logo", "photo", "video"] as const;
type Kind = (typeof KINDS)[number];

/** Uploads one file. Bind the listing id and the kind from the page. */
export async function uploadMediaAction(
  listingId: number,
  kind: Kind,
  previous: MediaFormState,
  formData: FormData,
): Promise<MediaFormState> {
  const rev = previous.rev + 1;
  const file = formData.get("file");

  if (!(file instanceof File) || file.size === 0) {
    return { rev, message: "Choose a file first." };
  }

  const result = await uploadListingMedia(listingId, kind, file);
  if (!result.ok) {
    // The API's own wording is shown: it is the side that knows the plan's allowance and what the
    // file actually turned out to be.
    return { rev, message: result.message };
  }

  revalidatePath(`/account/listings/${listingId}/photos`);
  return { rev, success: true, message: kind === "video" ? "Video added." : "Added." };
}

export async function deleteMediaAction(listingId: number, mediaId: number): Promise<MediaFormState> {
  const result = await deleteListingMedia(listingId, mediaId);
  if (!result.ok) {
    return { rev: 0, message: result.message };
  }

  revalidatePath(`/account/listings/${listingId}/photos`);
  return { rev: 0, success: true };
}

/** Moves one photo within its own kind. */
export async function moveMediaAction(
  listingId: number,
  mediaId: number,
  direction: "up" | "down",
): Promise<MediaFormState> {
  const result = await moveListingMedia(listingId, mediaId, direction);
  if (!result.ok) {
    return { rev: 0, message: result.message };
  }

  revalidatePath(`/account/listings/${listingId}/photos`);
  return { rev: 0, success: true };
}
