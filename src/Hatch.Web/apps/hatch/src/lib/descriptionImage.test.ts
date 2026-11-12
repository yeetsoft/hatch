import { describe, expect, it } from 'vitest';
import {
  IMAGE_MAX_BYTES,
  appendBlock,
  attachmentMarkdown,
  attachmentUrl,
  dragHasFiles,
  encodeType,
  imageMarkdown,
  imageUrl,
  insertText,
  markdownForDrop,
  pastedImage,
  shrinkTarget,
} from './descriptionImage';
import { message } from './errors';

describe('imageMarkdown', () => {
  it('is exactly the image syntax over the API route', () => {
    expect(imageMarkdown('abc')).toBe('![image](/api/images/abc)');
    expect(imageUrl('abc')).toBe('/api/images/abc');
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

describe('dragHasFiles', () => {
  it('is true when Files is among the types', () => {
    expect(dragHasFiles(['Files'])).toBe(true);
    expect(dragHasFiles(['text/plain', 'Files'])).toBe(true);
  });

  it('is false for text, links, and nothing at all', () => {
    expect(dragHasFiles(['text/plain'])).toBe(false);
    expect(dragHasFiles(['text/uri-list'])).toBe(false);
    expect(dragHasFiles([])).toBe(false);
    expect(dragHasFiles(null)).toBe(false);
    expect(dragHasFiles(undefined)).toBe(false);
  });
});

describe('attachmentMarkdown', () => {
  it('is image syntax over the attachment route for an image type', () => {
    expect(attachmentMarkdown({ id: 'abc', fileName: 'photo.png', contentType: 'image/png' })).toBe(
      '![photo.png](/api/images/abc)',
    );
  });

  it('is link syntax for a non-image type', () => {
    expect(attachmentMarkdown({ id: 'abc', fileName: 'report.pdf', contentType: 'application/pdf' })).toBe(
      '[report.pdf](/api/attachments/abc)',
    );
    expect(attachmentUrl('abc')).toBe('/api/attachments/abc');
  });

  it('escapes the backslash before the brackets, so an escaped backslash is never doubled', () => {
    expect(attachmentMarkdown({ id: 'abc', fileName: 'a\\b[c].pdf', contentType: 'application/pdf' })).toBe(
      '[a\\\\b\\[c\\].pdf](/api/attachments/abc)',
    );
  });

  it('falls back to a word naming the kind when the file name is empty', () => {
    expect(attachmentMarkdown({ id: 'abc', fileName: '', contentType: 'image/png' })).toBe(
      '![image](/api/images/abc)',
    );
    expect(attachmentMarkdown({ id: 'abc', fileName: '', contentType: 'application/pdf' })).toBe(
      '[attachment](/api/attachments/abc)',
    );
  });
});

describe('appendBlock', () => {
  it('is the block alone when value is empty or whitespace-only', () => {
    expect(appendBlock('', 'block')).toBe('block');
    expect(appendBlock('   \n  ', 'block')).toBe('block');
  });

  it('inserts two newlines when value has no trailing newline', () => {
    expect(appendBlock('text', 'block')).toBe('text\n\nblock');
  });

  it('inserts one more newline when value ends in exactly one', () => {
    expect(appendBlock('text\n', 'block')).toBe('text\n\nblock');
  });

  it('inserts none when value already ends in a blank line', () => {
    expect(appendBlock('text\n\n', 'block')).toBe('text\n\nblock');
    expect(appendBlock('text\n\n\n', 'block')).toBe('text\n\n\nblock');
  });
});

describe('markdownForDrop', () => {
  it('uploads strictly in order and joins the markdown with a newline', async () => {
    const calls: string[] = [];
    const files = [{ name: 'a.png' }, { name: 'b.pdf' }, { name: 'c.jpg' }];
    const upload = async (f: { name: string }) => {
      calls.push(f.name);
      return { id: f.name, contentType: f.name.endsWith('.pdf') ? 'application/pdf' : 'image/jpeg' };
    };
    const result = await markdownForDrop(files, upload);
    expect(calls).toEqual(['a.png', 'b.pdf', 'c.jpg']);
    expect(result).toBe(
      '![a.png](/api/images/a.png)\n[b.pdf](/api/attachments/b.pdf)\n![c.jpg](/api/images/c.jpg)',
    );
  });

  it('stops at the first rejection and never calls upload for later files', async () => {
    const calls: string[] = [];
    const files = [{ name: 'a.png' }, { name: 'b.pdf' }, { name: 'c.jpg' }];
    const upload = async (f: { name: string }) => {
      calls.push(f.name);
      if (f.name === 'b.pdf') throw new Error('boom');
      return { id: f.name, contentType: 'image/jpeg' };
    };
    await expect(markdownForDrop(files, upload)).rejects.toThrow('b.pdf: boom');
    expect(calls).toEqual(['a.png', 'b.pdf']);
  });

  it('is the bare message for a single-file drop', async () => {
    const files = [{ name: 'a.png' }];
    const err = new Error('boom');
    const upload = async () => {
      throw err;
    };
    await expect(markdownForDrop(files, upload)).rejects.toThrow(message(err));
  });

  it('names the failing file for a multi-file drop', async () => {
    const files = [{ name: 'a.png' }, { name: 'b.pdf' }];
    const err = new Error('boom');
    const upload = async (f: { name: string }) => {
      if (f.name === 'b.pdf') throw err;
      return { id: f.name, contentType: 'image/jpeg' };
    };
    await expect(markdownForDrop(files, upload)).rejects.toThrow(`b.pdf: ${message(err)}`);
  });
});
