/* What each colour token is, to the palette engine.
   ---------------------------------------------------------------------------
   Every colour-valued token in tokens.css has exactly one role, and
   paletteRoles.test.ts fails - naming the token - when the file and this table
   disagree in either direction.

   neutral  the paper and its ink: lightness stays where it was authored, the
            paper tint sets chroma and hue.
   accent   hue rotated and spread, chroma scaled, lightness lifted, then
            gamut-clipped by reducing chroma.
   derived  regenerated from an accent (or an ink) so the relationship the
            designer authored survives.
   fixed    exactly as authored: the console, the letterbox, the series ramp
            (CVD-validated, "not a value to improvise on"), shadow and overlay. */

export type Role = 'neutral' | 'accent' | 'derived' | 'fixed';

export const NEUTRALS = ['--bg', '--card', '--ink', '--muted', '--line', '--line-strong'] as const;

/** The three accents that must stay mutually distinct in hue, in the order the guard relaxes them. */
export const SEMANTIC = ['--danger', '--success', '--warn'] as const;

/** The accents with a wash and a text form: `--<name>`, `--<name>-bg`, `--<name>-ink`. */
export const FAMILIES = ['primary', 'danger', 'success', 'warn'] as const;

const FLOW_KINDS = ['turn', 'family', 'person', 'other'] as const;

/** `--success-fill` takes `--success`'s final hue plus its authored hue offset; it is no family of its own. */
export const SUCCESS_FILL = '--success-fill';

export const CHROME = '--chrome';
export const CHROME_END = '--chrome-end';
export const CHROME_GLOW = '--chrome-glow';

/** Accent fills that take the hue transform directly, `--success-fill` excepted. */
export const ACCENTS = [
  '--primary',
  ...SEMANTIC,
  CHROME,
  CHROME_END,
  CHROME_GLOW,
  ...FLOW_KINDS.map((kind) => `--flow-${kind}`),
] as const;

/** Every fill an ink is laid on, with the ink: the pairs the fill guard serves. */
export const INK_ON: readonly (readonly [fill: string, ink: string])[] = [
  ['--primary', '--on-accent'],
  ['--danger', '--on-accent'],
  ['--success', '--on-accent'],
  [SUCCESS_FILL, '--on-accent'],
  ['--warn', '--on-accent'],
  [CHROME, '--on-chrome'],
  [CHROME_END, '--on-chrome'],
  ...FLOW_KINDS.map((kind) => [`--flow-${kind}`, `--on-flow-${kind}`] as const),
];

/** Translucent tokens, each the final colour of its base at the authored alpha. */
export const WASH_OF: Readonly<Record<string, string>> = {
  ...Object.fromEntries(FAMILIES.map((name) => [`--${name}-bg`, `--${name}`])),
  '--on-accent-wash': '--on-accent',
  '--on-chrome-wash': '--on-chrome',
};

/** The text form of each family: authored lightness, chroma offset from its accent. */
export const INK_OF: Readonly<Record<string, string>> = Object.fromEntries(
  FAMILIES.map((name) => [`--${name}-ink`, `--${name}`]),
);

const FIXED = [
  '--console',
  '--console-line',
  '--on-console',
  '--console-dim',
  '--console-glow',
  '--letterbox',
  '--shadow',
  '--overlay',
  ...Array.from({ length: 8 }, (_, at) => `--series-${at + 1}`),
];

const DERIVED = [
  ...Object.keys(WASH_OF),
  ...Object.keys(INK_OF),
  ...new Set(INK_ON.map(([, ink]) => ink)),
];

const table: [string, Role][] = [
  ...NEUTRALS.map((token): [string, Role] => [token, 'neutral']),
  ...[...ACCENTS, SUCCESS_FILL].map((token): [string, Role] => [token, 'accent']),
  ...DERIVED.map((token): [string, Role] => [token, 'derived']),
  ...FIXED.map((token): [string, Role] => [token, 'fixed']),
];

export const ROLES: Readonly<Record<string, Role>> = Object.fromEntries(table);
