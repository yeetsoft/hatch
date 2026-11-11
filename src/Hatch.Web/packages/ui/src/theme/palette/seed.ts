/* The theme seed: a parameter vector written as a short string and read back.
   ---------------------------------------------------------------------------
   `hx1-` and eight base64url characters: the version, then one byte per
   parameter in PARAMETER_KEYS order, each quantised to 256 steps across its
   full range. It is the vector itself, not a PRNG seed, so a theme tuned by
   hand is as shareable as a rolled one. Six bytes are 48 bits, exactly eight
   characters, so a body has no spare bits and every vector has one spelling.
   SEED_VERSION is the one place to bump: a seed of any other version is
   refused as such, never read as if it were this one's. Base64 is written out
   by hand, since the public site imports this package and has no `Buffer`. */
import { PARAMETER_KEYS, PARAMETERS, type PaletteParams } from './params';

/** Bump when the byte layout changes; every seed written before it then refuses. */
export const SEED_VERSION = 1;

export type SeedRefusal = 'malformed' | 'length' | 'version';

export type SeedResult = { ok: true; params: PaletteParams } | { ok: false; refusal: SeedRefusal; message: string };

const ALPHABET = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_';
const BODY_LENGTH = Math.ceil((PARAMETER_KEYS.length * 4) / 3);
const STEPS = 255;

function toBase64Url(bytes: number[]): string {
  let out = '';
  for (let at = 0; at < bytes.length; at += 3) {
    const word = (bytes[at] << 16) | ((bytes[at + 1] ?? 0) << 8) | (bytes[at + 2] ?? 0);
    const chars = Math.min(bytes.length - at, 3) + 1;
    for (let c = 0; c < chars; c++) out += ALPHABET[(word >> (18 - 6 * c)) & 63];
  }
  return out;
}

function fromBase64Url(body: string): number[] {
  const bytes: number[] = [];
  for (let at = 0; at < body.length; at += 4) {
    let word = 0;
    for (let c = 0; c < 4; c++) word = (word << 6) | Math.max(ALPHABET.indexOf(body[at + c] ?? 'A'), 0);
    bytes.push((word >> 16) & 255, (word >> 8) & 255, word & 255);
  }
  return bytes.slice(0, PARAMETER_KEYS.length);
}

/** Encode and decode for one seed version; the module's own pair is `makeSeedCodec(SEED_VERSION)`. */
export function makeSeedCodec(version: number): {
  encodeSeed: (params: PaletteParams) => string;
  decodeSeed: (input: string) => SeedResult;
} {
  const prefix = `hx${version}-`;

  /** A value outside its range is clamped; a non-finite one is a caller's bug, so it throws. */
  function encodeSeed(params: PaletteParams): string {
    const bytes = PARAMETER_KEYS.map((key) => {
      const value = params[key];
      if (!Number.isFinite(value)) throw new RangeError(`Seed parameter ${key} is not a finite number: ${value}`);
      const { min, max } = PARAMETERS[key];
      return Math.round(((Math.min(Math.max(value, min), max) - min) / (max - min)) * STEPS);
    });
    return prefix + toBase64Url(bytes);
  }

  function refuse(refusal: SeedRefusal, message: string): SeedResult {
    return { ok: false, refusal, message };
  }

  /** Never throws. Only surrounding whitespace is forgiven. */
  function decodeSeed(input: string): SeedResult {
    const text = input.trim();
    const head = /^hx(\d+)-/.exec(text);
    if (!head) return refuse('malformed', 'A seed starts with "hx", a version number and a dash, like hx1-.');
    // The version is read first: a later one may change what follows it.
    if (Number(head[1]) !== version) {
      return refuse('version', `This seed is version ${head[1]}; this engine reads version ${version}.`);
    }
    const body = text.slice(head[0].length);
    if (!/^[A-Za-z0-9_-]*$/.test(body)) {
      return refuse('malformed', 'A seed holds only letters, digits, dashes and underscores after its version.');
    }
    if (body.length !== BODY_LENGTH) {
      return refuse('length', `A seed has ${BODY_LENGTH} characters after its version; this one has ${body.length}.`);
    }
    const bytes = fromBase64Url(body);
    const params = {} as PaletteParams;
    PARAMETER_KEYS.forEach((key, at) => {
      const { min, max } = PARAMETERS[key];
      params[key] = min + (bytes[at] / STEPS) * (max - min);
    });
    return { ok: true, params };
  }

  return { encodeSeed, decodeSeed };
}

export const { encodeSeed, decodeSeed } = makeSeedCodec(SEED_VERSION);
