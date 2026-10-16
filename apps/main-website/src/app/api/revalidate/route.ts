import { revalidatePath } from "next/cache";
import { NextResponse, type NextRequest } from "next/server";

// Lets the admin site refresh a public page the moment a listing is approved, instead of waiting for
// the 60-second cache window in src/lib/api.ts to expire. Only the admin site knows the shared token,
// which is read at runtime: with no token configured this route does nothing at all, and the site
// falls back to its ordinary timed revalidation.
//
// It is not an open endpoint: it takes a list of paths, refuses anything that is not a plain path on
// this site, and never returns any data.

const MAX_PATHS = 10;

function isSafePath(value: unknown): value is string {
  return (
    typeof value === "string" &&
    value.startsWith("/") &&
    !value.startsWith("//") &&
    !value.includes("\\") &&
    !value.includes("..") &&
    value.length <= 300
  );
}

export async function POST(request: NextRequest) {
  const expected = process.env.REVALIDATE_TOKEN;
  if (!expected) {
    return new NextResponse(null, { status: 503 });
  }

  // Same answer for a missing and a wrong token, so this cannot be used to probe for the feature.
  if (request.headers.get("x-revalidate-token") !== expected) {
    return new NextResponse(null, { status: 401 });
  }

  let paths: unknown;
  try {
    ({ paths } = (await request.json()) as { paths?: unknown });
  } catch {
    return new NextResponse(null, { status: 400 });
  }

  if (!Array.isArray(paths) || paths.length === 0 || paths.length > MAX_PATHS || !paths.every(isSafePath)) {
    return new NextResponse(null, { status: 400 });
  }

  for (const path of paths) {
    revalidatePath(path);
  }

  return new NextResponse(null, { status: 204 });
}
