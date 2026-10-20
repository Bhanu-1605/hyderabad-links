// Shared by the media actions (server) and the upload form (client). Kept out of actions.ts, which
// is a "use server" file and may only export async functions: a type or constant exported from
// there arrives as undefined in the browser rather than failing loudly.

export type MediaFormState = {
  rev: number;   // bumped on every submit so the file input re-mounts and clears
  message?: string;
  success?: boolean;
};

export const emptyMediaFormState: MediaFormState = { rev: 0 };
