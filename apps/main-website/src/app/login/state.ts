// Shared by the login form (client) and its server action. Kept out of the "use server" file.

export type LoginState = {
  step: "phone" | "code";
  phone: string;
  error?: string;
  notice?: string;
};

export const initialLoginState: LoginState = { step: "phone", phone: "" };
