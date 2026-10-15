export function formatDistance(metres: number): string {
  return metres < 1000 ? `${metres} m` : `${(metres / 1000).toFixed(1)} km`;
}

export const telHref = (phone: string) => `tel:${phone}`;
export const whatsappHref = (phone: string) => `https://wa.me/${phone.replace(/\D/g, "")}`;
export const mapsHref = (lat: number, lng: number) =>
  `https://www.google.com/maps/search/?api=1&query=${lat},${lng}`;

/** Absolute URL for canonical links and structured data. SITE_URL is read at runtime on the server. */
export function absoluteUrl(path: string): string {
  const base = (process.env.SITE_URL ?? "http://localhost:3000").replace(/\/$/, "");
  return `${base}${path}`;
}
