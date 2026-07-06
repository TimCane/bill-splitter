import zlib from 'node:zlib'

// The e2e stack runs the real OCR sidecar, so the two specs need images the
// sidecar reacts to predictably: a decodable one (parses to an empty Review) and
// an undecodable one (the pipeline fails over to manual entry). Neither carries
// real receipt text - OCR content is nondeterministic, so the specs never assert
// on parsed items; the host always lands at least one item by hand.

function crc32(buffer) {
  // zlib.crc32 landed in Node 22.2; fall back to a table for older runtimes.
  if (typeof zlib.crc32 === 'function') {
    return zlib.crc32(buffer) >>> 0
  }

  let crc = 0xffffffff
  for (const byte of buffer) {
    crc ^= byte
    for (let bit = 0; bit < 8; bit++) {
      crc = crc & 1 ? (crc >>> 1) ^ 0xedb88320 : crc >>> 1
    }
  }
  return (crc ^ 0xffffffff) >>> 0
}

function pngChunk(type, data) {
  const length = Buffer.alloc(4)
  length.writeUInt32BE(data.length, 0)
  const typeAndData = Buffer.concat([Buffer.from(type, 'ascii'), data])
  const crc = Buffer.alloc(4)
  crc.writeUInt32BE(crc32(typeAndData), 0)
  return Buffer.concat([length, typeAndData, crc])
}

/** A valid 1x1 RGB PNG - decodable by Pillow, so OCR succeeds with no text. */
export function decodablePng() {
  const signature = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a])

  const ihdr = Buffer.alloc(13)
  ihdr.writeUInt32BE(1, 0) // width
  ihdr.writeUInt32BE(1, 4) // height
  ihdr[8] = 8 // bit depth
  ihdr[9] = 2 // colour type: truecolour

  const raw = Buffer.from([0x00, 0xff, 0xff, 0xff]) // filter byte + one white pixel
  const idat = zlib.deflateSync(raw)

  return Buffer.concat([
    signature,
    pngChunk('IHDR', ihdr),
    pngChunk('IDAT', idat),
    pngChunk('IEND', Buffer.alloc(0)),
  ])
}

/**
 * A JPEG header that passes the backend magic-byte and dimension guards but has
 * no scan data, so the OCR sidecar cannot decode it - the session fails over to
 * manual entry (docs/06-ocr-service.md#backend-job-flow).
 */
export function undecodableJpeg() {
  return Buffer.from([0xff, 0xd8, 0xff, 0xc0, 0x00, 0x11, 0x08, 0x00, 0x64, 0x00, 0x64])
}
