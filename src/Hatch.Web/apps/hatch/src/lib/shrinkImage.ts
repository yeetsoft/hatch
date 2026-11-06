import { SHRINK_ATTEMPTS, encodeType, shrinkTarget } from './descriptionImage';

/** The file itself when it fits, else it drawn smaller onto a canvas and
    re-encoded - the DOM half of ProjectLogoField's resize, with the arithmetic
    in lib/descriptionImage.ts. Gives up after SHRINK_ATTEMPTS and returns what
    it has: the server's refusal is what the person then reads. */
export async function shrink(file: File): Promise<Blob> {
  let blob: Blob = file;
  let bitmap: ImageBitmap | null = null;
  try {
    for (let attempt = 0; attempt < SHRINK_ATTEMPTS; attempt++) {
      bitmap ??= await createImageBitmap(file);
      const target = shrinkTarget(bitmap.width, bitmap.height, blob.size);
      if (!target) return blob;
      const canvas = document.createElement('canvas');
      canvas.width = target.width;
      canvas.height = target.height;
      const ctx = canvas.getContext('2d');
      if (!ctx) return blob;
      ctx.drawImage(bitmap, 0, 0, target.width, target.height);
      const next = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, encodeType(file.type)));
      if (!next) return blob;
      blob = next;
    }
    return blob;
  } finally {
    bitmap?.close();
  }
}
