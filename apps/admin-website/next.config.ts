import path from "node:path";
import type { NextConfig } from "next";

const monorepoRoot = path.join(__dirname, "../../");

const securityHeaders = [
  { key: "X-Content-Type-Options", value: "nosniff" },
  { key: "X-Frame-Options", value: "DENY" },
  { key: "Referrer-Policy", value: "strict-origin-when-cross-origin" },
  { key: "Permissions-Policy", value: "camera=(), microphone=(), geolocation=(self)" },
  { key: "Strict-Transport-Security", value: "max-age=31536000; includeSubDomains" },
      { key: "X-Robots-Tag", value: "noindex, nofollow, noarchive" },
].filter(Boolean);

const nextConfig: NextConfig = {
  output: "standalone", // small, self-contained build for Azure App Service
  outputFileTracingRoot: monorepoRoot,
  turbopack: { root: monorepoRoot },
  poweredByHeader: false,
  async headers() {
    return [{ source: "/:path*", headers: securityHeaders }];
  },
};

export default nextConfig;
