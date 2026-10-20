import type { ListingMedia } from "@/lib/types";

/**
 * The photos and video a business has added.
 *
 * Plain <img> and <video> rather than next/image: the files live in Blob Storage under a host that
 * differs per environment, and the optimiser would need each host registered in next.config. The
 * API caps the file sizes instead, and `width`/`height` come from the file itself, which is what
 * stops the page jumping about as the images load.
 */
export function ListingGallery({ media, businessName }: { media: ListingMedia[]; businessName: string }) {
  const photos = media.filter((item) => item.kind === "photo");
  const videos = media.filter((item) => item.kind === "video");
  if (photos.length === 0 && videos.length === 0) return null;

  return (
    <section
      aria-labelledby="gallery-heading"
      className="mt-8 rounded-2xl border border-line bg-white p-5 shadow-[0_8px_28px_#002a3412] sm:p-7"
    >
      <h2 id="gallery-heading" className="text-xl font-black text-ink">
        Photos
      </h2>

      {photos.length > 0 ? (
        <ul className="mt-5 grid grid-cols-2 gap-4 sm:grid-cols-3">
          {photos.map((photo) => (
            <li key={photo.url}>
              {/* The alt text names the business: there is nothing stored describing each picture,
                  and an invented description would be worse than none for a screen reader. */}
              <img
                src={photo.url}
                alt={businessName}
                width={photo.width ?? undefined}
                height={photo.height ?? undefined}
                loading="lazy"
                className="aspect-square w-full rounded-xl border border-line bg-surface object-cover"
              />
            </li>
          ))}
        </ul>
      ) : null}

      {videos.map((video) => (
        <video
          key={video.url}
          src={video.url}
          controls
          preload="metadata"
          className="mt-4 aspect-video w-full rounded-xl bg-black"
        />
      ))}
    </section>
  );
}
