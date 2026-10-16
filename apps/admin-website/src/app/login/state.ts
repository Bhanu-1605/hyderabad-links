// Shared by the sign-in form (client) and its server action. Kept out of the "use server" file.

export type LoginState = {
  /** Kept so the field is not cleared when the password was wrong. The password is never kept. */
  email: string;
  error?: string;
};

export const initialLoginState: LoginState = { email: "" };
