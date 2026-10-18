import type { Metadata } from "next";
import Link from "next/link";
import { EmptyState } from "@/components/ui/EmptyState";
import { getMe } from "@/lib/owner-api";
import { signOutAction } from "../actions";

export const metadata: Metadata = { title: "Account settings" };

/**
 * The account itself, as distinct from the business: how you sign in, and how to sign out.
 *
 * There is deliberately no password section, no email-login option and no "delete my account"
 * button. Sign-in is the verified phone number and nothing else, and the other two are not
 * implemented — offering them would be a control that does nothing.
 */
export default async function AccountSettingsPage() {
  const me = await getMe();

  return (
    <>
      <h1 className="text-2xl font-black text-ink">Account settings</h1>
      <p className="mt-1 text-muted">How you sign in to Hyderabad Links.</p>

      {!me.ok ? (
        <div className="mt-6">
          <EmptyState mark="!" title="We could not load your account" tone="error">
            <p>{me.message}</p>
          </EmptyState>
        </div>
      ) : (
        <>
          <section
            aria-labelledby="sign-in"
            className="mt-7 rounded-xl border border-line bg-white p-5 shadow-[0_8px_22px_#002a340b]"
          >
            <h2 id="sign-in" className="text-lg font-black text-ink">
              Sign-in method
            </h2>

            <dl className="mt-4 grid gap-4 sm:grid-cols-2">
              <div>
                <dt className="text-xs font-black tracking-[0.12em] text-muted uppercase">Mobile number</dt>
                <dd className="mt-1.5 font-bold text-ink">{me.data.phone}</dd>
              </div>
              <div>
                <dt className="text-xs font-black tracking-[0.12em] text-muted uppercase">Name</dt>
                <dd className="mt-1.5 font-bold text-ink">{me.data.name ?? "Not set"}</dd>
              </div>
              <div>
                <dt className="text-xs font-black tracking-[0.12em] text-muted uppercase">Email</dt>
                <dd className="mt-1.5 font-bold text-ink">{me.data.email ?? "Not set"}</dd>
              </div>
            </dl>

            <p className="mt-5 border-t border-line pt-4 text-sm text-muted">
              You sign in with a one-time code sent to your mobile number. There is no password to
              remember or lose. Your name and email can be changed on the{" "}
              <Link href="/account/profile" className="font-bold text-brand-700 hover:underline">
                business profile
              </Link>{" "}
              page.
            </p>
            <p className="mt-2 text-sm text-muted">
              Changing the mobile number itself means verifying the new one, which is not something
              you can do here yet. Contact support if you need it changed.
            </p>
          </section>

          <section
            aria-labelledby="sign-out"
            className="mt-6 rounded-xl border border-line bg-white p-5 shadow-[0_8px_22px_#002a340b]"
          >
            <h2 id="sign-out" className="text-lg font-black text-ink">
              Sign out
            </h2>
            <p className="mt-1.5 text-sm text-muted">
              This ends the session on this device. You will need a new code to sign in again.
            </p>
            <form action={signOutAction} className="mt-4">
              <button
                type="submit"
                className="cursor-pointer rounded-full border border-brand-200 bg-white px-6 py-3 text-sm font-bold text-brand-700 hover:border-brand-700 hover:bg-brand-50"
              >
                Sign out
              </button>
            </form>
          </section>
        </>
      )}
    </>
  );
}
