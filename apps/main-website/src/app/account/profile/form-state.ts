// Shared by the profile form (client) and its server action, which may only export async functions.

export type ProfileFormState = {
  /** Bumped on every submit so the form re-mounts with whatever came back. */
  rev: number;
  values: { name: string; email: string };
  /** Only ever a failure: a successful save redirects, so the page shows the confirmation. */
  message?: string;
  fieldErrors: Record<string, string>;
};

export function initialProfileState(name: string, email: string): ProfileFormState {
  return { rev: 0, values: { name, email }, fieldErrors: {} };
}
