import type { ReactNode } from "react";

/**
 * The dashed panel shown when a page has nothing to list, and the matching panel for a failure.
 * Both are here so an empty search and an empty category page cannot drift apart.
 */
export function EmptyState({
  mark,
  title,
  children,
  action,
  tone = "empty",
}: {
  mark: string;
  title: string;
  children?: ReactNode;
  action?: ReactNode;
  tone?: "empty" | "error";
}) {
  const isError = tone === "error";
  return (
    <div
      role={isError ? "alert" : undefined}
      className={`rounded-2xl border border-dashed px-5 py-12 text-center ${
        isError ? "border-red-300 bg-red-50" : "border-brand-200 bg-brand-50"
      }`}
    >
      <span aria-hidden="true" className={`text-4xl ${isError ? "text-red-700" : "text-brand-700"}`}>
        {mark}
      </span>
      <h2 className={`mt-2 text-xl font-black sm:text-2xl ${isError ? "text-red-900" : "text-brand-800"}`}>{title}</h2>
      {children ? (
        <div className={`mx-auto mt-2 max-w-xl text-sm ${isError ? "text-red-800" : "text-muted"}`}>{children}</div>
      ) : null}
      {action ? <div className="mt-6">{action}</div> : null}
    </div>
  );
}
