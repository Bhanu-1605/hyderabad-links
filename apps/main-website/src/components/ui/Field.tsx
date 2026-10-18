import type { ReactNode } from "react";

/** Every text input, select and textarea on the site looks like this. */
export const fieldClass =
  "w-full rounded-lg border border-line bg-white px-3 py-2.5 text-base text-ink outline-none focus:border-brand-700 focus:ring-2 focus:ring-brand-100 aria-[invalid]:border-red-400";

/**
 * Label, control, help text and error message in the order a screen reader should meet them.
 *
 * The caller still writes its own `<input>` or `<select>`, because every form needs different
 * attributes; this only takes care of wiring the ids together, which is the part that is easy to
 * get subtly wrong. Pass `describedBy` to the control: it is the help text's id, or the error's
 * when there is one.
 */
export function Field({
  id,
  label,
  help,
  error,
  children,
}: {
  id: string;
  label: string;
  help?: ReactNode;
  error?: string;
  children: ReactNode;
}) {
  return (
    <div>
      <label htmlFor={id} className="block text-xs font-black tracking-[0.12em] text-muted uppercase">
        {label}
      </label>
      <div className="mt-1.5">{children}</div>
      {error ? (
        <p id={`${id}-error`} className="mt-1.5 text-sm font-medium text-red-700">
          {error}
        </p>
      ) : help ? (
        <p id={`${id}-help`} className="mt-1.5 text-sm text-muted">
          {help}
        </p>
      ) : null}
    </div>
  );
}

/** The id to put in a control's `aria-describedby`, or undefined when there is nothing to point at. */
export function describedBy(id: string, hasError: boolean, hasHelp: boolean): string | undefined {
  if (hasError) return `${id}-error`;
  if (hasHelp) return `${id}-help`;
  return undefined;
}
