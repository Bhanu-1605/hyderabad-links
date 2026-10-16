"use client";

import { useActionState } from "react";
import { loginAction } from "./actions";
import { initialLoginState } from "./state";

const inputClass =
  "w-full rounded-lg border border-neutral-300 px-3 py-2.5 text-base outline-none focus:border-brand-700 focus:ring-2 focus:ring-brand-100";
const primaryButton =
  "w-full rounded-lg bg-brand-700 px-5 py-2.5 text-base font-semibold text-white hover:bg-brand-900 disabled:opacity-60";

export function LoginForm({ next }: { next: string }) {
  const [state, formAction, pending] = useActionState(loginAction, initialLoginState);

  return (
    <form action={formAction} className="rounded-2xl border border-neutral-200 bg-white p-5 shadow-sm">
      <input type="hidden" name="next" value={next} />

      <label htmlFor="email" className="block text-sm font-medium">
        Email address
      </label>
      <input
        id="email"
        name="email"
        type="email"
        autoComplete="username"
        defaultValue={state.email}
        autoFocus
        required
        className={`${inputClass} mt-1`}
      />

      <label htmlFor="password" className="mt-4 block text-sm font-medium">
        Password
      </label>
      <input
        id="password"
        name="password"
        type="password"
        autoComplete="current-password"
        required
        className={`${inputClass} mt-1`}
      />

      <button type="submit" disabled={pending} className={`${primaryButton} mt-5`}>
        {pending ? "Signing in..." : "Sign in"}
      </button>

      {state.error ? (
        <p role="alert" className="mt-3 text-sm text-red-700">
          {state.error}
        </p>
      ) : null}
    </form>
  );
}
