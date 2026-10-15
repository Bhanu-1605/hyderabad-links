"use client";

import { useActionState } from "react";
import { loginAction } from "./actions";
import { initialLoginState } from "./state";

const inputClass =
  "w-full rounded-lg border border-neutral-300 px-3 py-2.5 text-base outline-none focus:border-brand-700 focus:ring-2 focus:ring-brand-100";
const primaryButton =
  "w-full rounded-lg bg-brand-700 px-5 py-2.5 text-base font-semibold text-white hover:bg-brand-900 disabled:opacity-60";
const linkButton = "font-medium text-brand-700 underline-offset-2 hover:underline disabled:opacity-60";

export function LoginForm({ next, devHint }: { next: string; devHint: boolean }) {
  const [state, formAction, pending] = useActionState(loginAction, initialLoginState);
  const onCodeStep = state.step === "code";

  return (
    <form action={formAction} className="rounded-2xl border border-neutral-200 bg-white p-5 shadow-sm">
      <input type="hidden" name="next" value={next} />

      {onCodeStep ? (
        <>
          <input type="hidden" name="phone" value={state.phone} />
          <p className="text-sm text-neutral-600">
            Code sent to <span className="font-medium text-neutral-900">+91 {state.phone}</span>
          </p>
          <label htmlFor="code" className="mt-4 block text-sm font-medium">
            6-digit code
          </label>
          <input
            id="code"
            name="code"
            inputMode="numeric"
            autoComplete="one-time-code"
            maxLength={6}
            autoFocus
            required
            className={`${inputClass} mt-1 tracking-[0.4em]`}
          />
          {devHint ? (
            <p className="mt-2 rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-900">
              Local development: no SMS is sent. Look for &quot;login code&quot; in the window running the API.
            </p>
          ) : null}
          <button type="submit" name="intent" value="verify" disabled={pending} className={`${primaryButton} mt-4`}>
            {pending ? "Checking..." : "Sign in"}
          </button>
          <div className="mt-3 flex flex-wrap gap-4 text-sm">
            <button type="submit" name="intent" value="request" formNoValidate disabled={pending} className={linkButton}>
              Send a new code
            </button>
            <button type="submit" name="intent" value="change" formNoValidate disabled={pending} className={linkButton}>
              Use a different number
            </button>
          </div>
        </>
      ) : (
        <>
          <label htmlFor="phone-input" className="block text-sm font-medium">
            Mobile number
          </label>
          <div className="mt-1 flex gap-2">
            <span className="flex items-center rounded-lg border border-neutral-300 bg-neutral-50 px-3 text-base text-neutral-600">+91</span>
            <input
              id="phone-input"
              name="phone"
              type="tel"
              inputMode="numeric"
              autoComplete="tel-national"
              defaultValue={state.phone}
              placeholder="98765 43210"
              autoFocus
              required
              className={inputClass}
            />
          </div>
          <button type="submit" name="intent" value="request" disabled={pending} className={`${primaryButton} mt-4`}>
            {pending ? "Sending..." : "Send code"}
          </button>
        </>
      )}

      {state.notice && !state.error ? <p className="mt-3 text-sm text-neutral-600">{state.notice}</p> : null}
      {state.error ? (
        <p role="alert" className="mt-3 text-sm text-red-700">
          {state.error}
        </p>
      ) : null}
    </form>
  );
}
