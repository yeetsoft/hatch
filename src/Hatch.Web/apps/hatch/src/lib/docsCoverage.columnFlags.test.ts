import { describe, expect, it } from 'vitest';
import page from '../../docs/the-board-and-its-columns.md?raw';
import { COLUMN_FLAGS } from './columnFlags';

/** The terms the page sets in bold: `**Done column**`, not prose saying "deferred". */
const bold = new Set([...page.matchAll(/\*\*([^*\n]+)\*\*/g)].map((m) => m[1]));

describe('the Statuses flags', () => {
  it.each(COLUMN_FLAGS.map((flag) => flag.label))('the-board-and-its-columns names %s', (label) => {
    expect(
      bold.has(label),
      `the-board-and-its-columns.md does not name the Statuses flag "${label}": add it, in bold, to ## Column flags`,
    ).toBe(true);
  });
});
