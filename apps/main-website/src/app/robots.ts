import type { MetadataRoute } from "next";
import { absoluteUrl } from "@/lib/format";

/**
 * Everything behind a sign-in is disallowed, and the sitemap is named here so a crawler finds it
 * without being submitted one.
 */
export default function robots(): MetadataRoute.Robots {
  return {
    rules: [
      {
        userAgent: "*",
        allow: "/",
        disallow: ["/dashboard", "/account", "/login", "/onboarding", "/api/", "/health"],
      },
    ],
    sitemap: absoluteUrl("/sitemap.xml"),
  };
}
