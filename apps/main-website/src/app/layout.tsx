import type { Metadata, Viewport } from "next";
import { SiteFooter, SiteHeader } from "@/components/SiteChrome";
import { BRAND_NAME } from "@/lib/brand";
import "./globals.css";

export const metadata: Metadata = {
  title: { default: `${BRAND_NAME} | Local business directory for Hyderabad`, template: `%s | ${BRAND_NAME}` },
  description: "Find trusted local businesses and services across Hyderabad.",
};

export const viewport: Viewport = { width: "device-width", initialScale: 1 };

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en">
      <body className="min-h-screen bg-white font-sans text-ink antialiased">
        <SiteHeader />
        {children}
        <SiteFooter />
      </body>
    </html>
  );
}
