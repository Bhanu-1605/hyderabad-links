import type { Metadata, Viewport } from "next";
import { BRAND_NAME } from "@/lib/brand";
import "./globals.css";

export const metadata: Metadata = {
  title: { default: `${BRAND_NAME} Admin`, template: `%s | ${BRAND_NAME} Admin` },
  robots: { index: false, follow: false, nocache: true },
};

export const viewport: Viewport = { width: "device-width", initialScale: 1 };

// Read APP_ENV at request time so the same build can run in staging and production.
export const dynamic = "force-dynamic";

export default function RootLayout({ children }: { children: React.ReactNode }) {
  const environment = process.env.APP_ENV ?? "LOCAL";
  return (
    <html lang="en">
      <body className="min-h-screen bg-neutral-50 text-neutral-900 antialiased">
        <div className="flex items-center justify-between bg-neutral-800 px-4 py-2 text-xs font-semibold uppercase tracking-wider text-white">
          <span>ADMIN</span>
          <span>{environment}</span>
        </div>
        {children}
      </body>
    </html>
  );
}
