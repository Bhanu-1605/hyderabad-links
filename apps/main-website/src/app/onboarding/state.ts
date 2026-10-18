// Shared by the onboarding form (client) and its server action. Kept out of the "use server" file,
// which may only export async functions.

export type OnboardingState = {
  /** What was typed, sent back so a rejected form keeps the answers. */
  values: Record<string, string>;
  message?: string;
  fieldErrors: Record<string, string>;
};

export const emptyOnboardingState: OnboardingState = { values: {}, fieldErrors: {} };
