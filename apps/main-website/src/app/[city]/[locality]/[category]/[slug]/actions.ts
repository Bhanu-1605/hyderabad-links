"use server";

import { sendEnquiry } from "@/lib/api";
import type { EnquiryFormState } from "./enquiry-state";

/**
 * Sends a customer enquiry to a business.
 *
 * No redirect: the confirmation replaces the form in place, so the person keeps their place on the
 * business's page. The API validates everything again and is the only thing that decides whether
 * the enquiry was recorded.
 */
export async function sendEnquiryAction(
  _previous: EnquiryFormState,
  formData: FormData,
): Promise<EnquiryFormState> {
  const read = (field: string) => String(formData.get(field) ?? "").trim();
  const values = {
    name: read("name"),
    phone: read("phone"),
    email: read("email"),
    message: read("message"),
  };

  const result = await sendEnquiry(
    { city: read("city"), locality: read("locality"), slug: read("slug") },
    values,
  );

  if (!result.ok) {
    return { values, message: result.message, fieldErrors: result.fieldErrors };
  }

  return { values, fieldErrors: {}, sent: true };
}
