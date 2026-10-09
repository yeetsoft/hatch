import { describe, expect, it } from 'vitest';
import {
  IMAGE_MAX_BYTES,
  encodeType,
  imageMarkdown,
  imageUrl,
  insertText,
  pastedImage,
  shrinkTarget,
} from './descriptionImage';

describe('imageMarkdown', () => {
  it('is exactly the image syntax over the API route', () => {
    expect(imageMarkdown('abc')).toBe('![image](/api/hatch/images/abc)');
    expect(imageUrl('abc')).toBe('/api/hatch/images/abc');
  });
});

describe('shrinkTarget', () => {
  it('is null at the cap and under it', () => {
    expect(shrinkTarget(4000, 3000, IMAGE_MAX_BYTES)).toBeNull();
    expect(shrinkTarget(4000, 3000, 1)).toBeNull();
  });

  it('scales both sides by sqrt(0.9 * cap / size), floored', () => {
    const eight = 8 * 1024 * 1024;
    const scale = Math.sqrt((0.9 * IMAGE_MAX_BYTES) / eight); // 0.474341…
    expect(shrinkTarget(4000, 3000, eight)).toEqual({
      width: Math.floor(4000 * scale),
      height: Math.floor(3000 * scale),
    });
    expect(shrinkTarget(4000, 3000, eight)).toEqual({ width: 1897, height: 1423 });
  });

  it('keeps the aspect ratio to within a pixel', () => {
    const t = shrinkTarget(4000, 3000, 8 * 1024 * 1024)!;
    expect(Math.abs(t.width / t.height - 4 / 3)).toBeLessThan(0.002);
  });

  it('never returns a side of zero', () => {
    expect(shrinkTarget(1, 1, 1e12)).toEqual({ width: 1, height: 1 });
    expect(shrinkTarget(2, 1000, 1e12)).toEqual({ width: 1, height: 1 });
  });
});

describe('pastedImage', () => {
  it('picks the image out of a mixed clipboard', () => {
    const png = { type: 'image/png' };
    expect(pastedImage([{ type: 'text/plain' }, png])).toBe(png);
  });

  it('refuses a type the server would refuse', () => {
    expect(pastedImage([{ type: 'image/svg+xml' }])).toBeNull();
  });

  it('is null for an empty clipboard', () => {
    expect(pastedImage([])).toBeNull();
  });
});

describe('encodeType', () => {
  it('keeps JPEG and WebP, and writes PNG for the rest', () => {
    expect(encodeType('image/jpeg')).toBe('image/jpeg');
    expect(encodeType('image/webp')).toBe('image/webp');
    expect(encodeType('image/png')).toBe('image/png');
    expect(encodeType('image/gif')).toBe('image/png');
  });
});

describe('insertText', () => {
  it('replaces a selection and puts the caret after the text', () => {
    expect(insertText('hello world', 6, 11, 'X')).toEqual({ value: 'hello X', caret: 7 });
  });

  it('inserts at a bare caret', () => {
    expect(insertText('ab', 1, 1, '!![]')).toEqual({ value: 'a!![]b', caret: 5 });
  });
});
