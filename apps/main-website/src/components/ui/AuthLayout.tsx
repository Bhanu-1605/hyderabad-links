import type { ReactNode } from "react";

/**
 * The two-panel frame the owner screens share: the form on the left, a teal panel on the right.
 * Taken from the prototype's `_AuthLayout.cshtml`, with two deliberate differences.
 *
 * The prototype hides the site navigation and rotates three marketing slides in a carousel. This
 * site keeps its header, so a visitor who lands on sign-in by accident can still get back to the
 * directory, and the three slides are shown as a short static list instead - a carousel would need
 * a JavaScript library for decoration alone.
 */
export function AuthLayout({
  title,
  subtitle,
  children,
  aside,
}: {
  title: string;
  subtitle: ReactNode;
  children: ReactNode;
  /** The panel on the right. Hidden on small screens, where the form is all that matters. */
  aside: { heading: string; lead: string; points: string[] };
}) {
  return (
    <main className="grid min-h-[calc(100vh-126px)] lg:grid-cols-2">
      <div className="flex flex-col px-5 py-10 sm:px-10 lg:px-14">
        <div className="m-auto w-full max-w-md">
          <h1 className="text-center text-2xl font-black tracking-tight text-brand-800 sm:text-3xl">{title}</h1>
          <div className="mt-2 text-center text-muted">{subtitle}</div>
          <div className="mt-7">{children}</div>
        </div>
        <p className="pt-8 text-center text-sm text-muted">
          &copy; {new Date().getFullYear()} Hyderabad Links
        </p>
      </div>

      {/* Decoration and marketing copy only, so it is not worth a screen reader's time. */}
      <aside
        aria-hidden="true"
        className="relative hidden overflow-hidden bg-[linear-gradient(130deg,#00242c_0%,#004b5d_55%,#062a34_100%)] p-14 text-white lg:flex lg:items-center"
      >
        <div className="absolute top-[14%] right-[14%] h-20 w-20 rounded-[28px] bg-white/10" />
        <div className="absolute bottom-[16%] left-[16%] h-32 w-32 rounded-[28px] bg-white/10" />
        <div className="relative max-w-sm">
          <h2 className="text-3xl font-black">{aside.heading}</h2>
          <p className="mt-3 text-lg text-brand-100">{aside.lead}</p>
          <ul className="mt-8 grid gap-3">
            {aside.points.map((point) => (
              <li key={point} className="flex items-start gap-3 text-brand-100">
                <span className="mt-0.5 text-brand-300">&#10003;</span>
                {point}
              </li>
            ))}
          </ul>
        </div>
      </aside>
    </main>
  );
}
