// Client-side receipt preprocessing before upload (docs/08-frontend-design.md#uploads).
// Always re-encodes to JPEG: kills HEIC before it reaches the server and strips
// EXIF as a side effect. Downscales so the longest edge is <= MAX_EDGE.

const MAX_EDGE = 2000
const JPEG_QUALITY = 0.85

/** Decode, downscale to <= 2000px on the longest edge, re-encode JPEG 0.85. */
export async function preprocess(file: File): Promise<Blob> {
  // `from-image` bakes the EXIF orientation into the pixels; re-encoding then
  // strips the tag, so the receipt can't reach OCR rotated.
  const bitmap = await createImageBitmap(file, {
    imageOrientation: 'from-image',
  })
  try {
    const scale = Math.min(1, MAX_EDGE / Math.max(bitmap.width, bitmap.height))
    const width = Math.round(bitmap.width * scale)
    const height = Math.round(bitmap.height * scale)

    const canvas = document.createElement('canvas')
    canvas.width = width
    canvas.height = height

    const ctx = canvas.getContext('2d')
    if (!ctx) {
      throw new Error('could not get a 2d canvas context')
    }
    ctx.drawImage(bitmap, 0, 0, width, height)

    return await new Promise<Blob>((resolve, reject) => {
      canvas.toBlob(
        (blob) =>
          blob
            ? resolve(blob)
            : reject(new Error('canvas failed to encode the image')),
        'image/jpeg',
        JPEG_QUALITY,
      )
    })
  } finally {
    bitmap.close()
  }
}
