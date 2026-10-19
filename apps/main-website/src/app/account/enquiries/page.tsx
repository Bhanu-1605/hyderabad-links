import type { Metadata } from "next";
import Link from "next/link";
import { Pagination } from "@/components/Pagination";
import { ButtonLink } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";
import { listEnquiries, type OwnerEnquiry } from "@/lib/owner-api";
import { telHref, whatsappHref } from "@/lib/format";
import { setStatusAction } from "./actions";

export const metadata: Metadata = { title: "Enquiries" };

/**
 * The owner's inbox: people who filled in the form on one of their listings.
 *
 * Everything here is private customer data, which is why the API scopes every row to listings this
 * owner is a member of. The filters are plain links, so the state lives in the URL.
 */

const FILTERS = [
  { value: "", label: "All" },
  { value: "new", label: "Unread" },
  { value: "replied", label: "Replied" },
  { value: "closed", label: "Closed" },
  { value: "spam", label: "Spam" },
] as const;

const STATUS_TONE: Record<string, string> = {
  new: "bg-brand-700 text-white",
  read: "bg-brand-50 text-brand-800",
  replied: "bg-green-100 text-green-900",
  closed: "bg-neutral-100 text-neutral-700",
  spam: "bg-red-100 text-red-900",
};

function StatusButton({ id, status, label }: { id: number; status: string; label: string }) {
  return (
    <form action={setStatusAction}>
      <input type="hidden" name="id" value={id} />
      <input type="hidden" name="status" value={status} />
      <button
        type="submit"
        className="cursor-pointer rounded-full border border-brand-200 px-3.5 py-1.5 text-xs font-bold text-brand-700 hover:border-brand-700 hover:bg-brand-50"
      >
        {label}
      </button>
    </form>
  );
}

function EnquiryCard({ enquiry }: { enquiry: OwnerEnquiry }) {
  const received = new Date(enquiry.createdAt);
  return (
    <li className="rounded-xl border border-line bg-white p-5 shadow-[0_8px_22px_#002a340b]">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="text-xs font-black tracking-[0.1em] text-muted uppercase">{enquiry.listingName}</p>
          <h3 className="mt-1 text-lg font-black text-ink">{enquiry.name}</h3>
          <p className="mt-0.5 text-sm text-muted">
            <time dateTime={enquiry.createdAt}>
              {received.toLocaleDateString("en-IN", { day: "numeric", month: "short", year: "numeric" })}
              {", "}
              {received.toLocaleTimeString("en-IN", { hour: "2-digit", minute: "2-digit" })}
            </time>
          </p>
        </div>
        <span
          className={`shrink-0 rounded-full px-3 py-1 text-xs font-extrabold ${
            STATUS_TONE[enquiry.status] ?? "bg-neutral-100 text-neutral-700"
          }`}
        >
          {enquiry.status === "new" ? "Unread" : enquiry.status}
        </span>
      </div>

      <p className="mt-3 text-ink whitespace-pre-line">{enquiry.message}</p>

      <div className="mt-4 flex flex-wrap items-center gap-2 border-t border-line pt-4">
        <a
          href={telHref(enquiry.phone)}
          className="rounded-full bg-brand-700 px-4 py-2 text-sm font-bold text-white hover:bg-brand-800"
        >
          Call {enquiry.phone}
        </a>
        <a
          href={whatsappHref(enquiry.phone)}
          target="_blank"
          rel="noopener noreferrer"
          className="rounded-full border border-brand-200 px-4 py-2 text-sm font-bold text-brand-700 hover:border-brand-700 hover:bg-brand-50"
        >
          WhatsApp
        </a>
        {enquiry.email ? (
          <a
            href={`mailto:${enquiry.email}`}
            className="rounded-full border border-brand-200 px-4 py-2 text-sm font-bold text-brand-700 hover:border-brand-700 hover:bg-brand-50"
          >
            Email
          </a>
        ) : null}

        <span className="ml-auto flex flex-wrap gap-2">
          {enquiry.status === "new" ? <StatusButton id={enquiry.id} status="read" label="Mark read" /> : null}
          {enquiry.status !== "replied" ? <StatusButton id={enquiry.id} status="replied" label="Mark replied" /> : null}
          {enquiry.status !== "closed" ? <StatusButton id={enquiry.id} status="closed" label="Close" /> : null}
          {enquiry.status !== "spam" ? <StatusButton id={enquiry.id} status="spam" label="Spam" /> : null}
        </span>
      </div>
    </li>
  );
}

type Props = { searchParams: Promise<{ status?: string; page?: string }> };

export default async function EnquiriesPage({ searchParams }: Props) {
  const { status, page } = await searchParams;
  const pageNumber = Number(page);
  const result = await listEnquiries({
    status: status || undefined,
    page: Number.isFinite(pageNumber) && pageNumber > 0 ? Math.floor(pageNumber) : 1,
  });

  return (
    <>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-black text-ink">Enquiries</h1>
        {result.ok && result.data.newCount > 0 ? (
          <p className="rounded-full bg-brand-700 px-3.5 py-1.5 text-sm font-extrabold text-white">
            {result.data.newCount} unread
          </p>
        ) : null}
      </div>
      <p className="mt-1 text-muted">People who contacted you through your listings.</p>

      {!result.ok ? (
        <div className="mt-6">
          <EmptyState mark="!" title="We could not load your enquiries" tone="error">
            <p>{result.message}</p>
          </EmptyState>
        </div>
      ) : (
        <>
          <nav aria-label="Filter enquiries" className="mt-5 flex flex-wrap gap-2">
            {FILTERS.map((filter) => {
              const active = (status ?? "") === filter.value;
              return (
                <Link
                  key={filter.value || "all"}
                  href={filter.value ? `/account/enquiries?status=${filter.value}` : "/account/enquiries"}
                  aria-current={active ? "page" : undefined}
                  className={`rounded-full px-4 py-2 text-sm font-bold ${
                    active ? "bg-brand-700 text-white" : "border border-line bg-white text-brand-800 hover:bg-brand-50"
                  }`}
                >
                  {filter.label}
                </Link>
              );
            })}
          </nav>

          {result.data.items.length === 0 ? (
            <div className="mt-5">
              <EmptyState
                mark="&#9993;"
                title={status ? "Nothing with that status" : "No enquiries yet"}
                action={status ? <ButtonLink href="/account/enquiries" tone="ghost">Show all</ButtonLink> : undefined}
              >
                <p>
                  {status
                    ? "Try another filter."
                    : "When someone fills in the contact form on one of your live listings, it will appear here."}
                </p>
              </EmptyState>
            </div>
          ) : (
            <>
              <p className="mt-5 text-sm text-muted">
                {result.data.total} {result.data.total === 1 ? "enquiry" : "enquiries"}
              </p>
              <ul className="mt-3 grid gap-3">
                {result.data.items.map((enquiry) => (
                  <EnquiryCard key={enquiry.id} enquiry={enquiry} />
                ))}
              </ul>
              <Pagination
                page={result.data.page}
                totalPages={result.data.totalPages}
                params={{ status }}
                basePath="/account/enquiries"
              />
            </>
          )}
        </>
      )}
    </>
  );
}
