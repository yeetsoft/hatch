/* sRGB <-> OKLab <-> OKLCH, hand-written, no dependency.
   ---------------------------------------------------------------------------
   The matrices are Bjorn Ottosson's (https://bottosson.github.io/posts/oklab/).
   Colours go in and come out as 8-bit hex, because Monaco, mermaid and the
   `theme-color` meta take hex and because a palette's output has to be
   inspectable. Hue is in degrees, 0 up to 360. */

export interface Oklch {
  l: number;
  c: number;
  h: number;
}

/** 8-bit channels, and alpha from 0 to 1. */
export interface Rgba {
  r: number;
  g: number;
  b: number;
  a: number;
}

type Triple = [number, number, number];

const toLinear = (v: number): number => (v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4);
const fromLinear = (v: number): number => (v <= 0.0031308 ? v * 12.92 : 1.055 * v ** (1 / 2.4) - 0.055);

const HEX = /^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i;
const RGB = /^rgba?\(\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)\s*(?:,\s*([\d.]+)\s*)?\)$/i;

/** `#rrggbb` or `rgb[a](r, g, b[, a])`; null for anything else. */
export function parseCssColor(value: string): Rgba | null {
  const text = value.trim();
  const hex = HEX.exec(text);
  if (hex) return { r: parseInt(hex[1], 16), g: parseInt(hex[2], 16), b: parseInt(hex[3], 16), a: 1 };
  const rgb = RGB.exec(text);
  if (rgb) return { r: Number(rgb[1]), g: Number(rgb[2]), b: Number(rgb[3]), a: rgb[4] === undefined ? 1 : Number(rgb[4]) };
  return null;
}

export const formatRgba = (r: number, g: number, b: number, a: number): string => `rgba(${r}, ${g}, ${b}, ${a})`;

export const formatHex = (r: number, g: number, b: number): string =>
  `#${[r, g, b].map((v) => v.toString(16).padStart(2, '0')).join('')}`;

function readHex(hex: string): Triple {
  const parsed = HEX.test(hex) ? parseCssColor(hex) : null;
  if (!parsed) throw new Error(`Not a #rrggbb colour: ${hex}`);
  return [parsed.r, parsed.g, parsed.b];
}

function linearToOklab([r, g, b]: Triple): Triple {
  const l = Math.cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
  const m = Math.cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
  const s = Math.cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
  return [
    0.2104542553 * l + 0.793617785 * m - 0.0040720468 * s,
    1.9779984951 * l - 2.428592205 * m + 0.4505937099 * s,
    0.0259040371 * l + 0.7827717662 * m - 0.808675766 * s,
  ];
}

function oklabToLinear([L, a, b]: Triple): Triple {
  const l = (L + 0.3963377774 * a + 0.2158037573 * b) ** 3;
  const m = (L - 0.1055613458 * a - 0.0638541728 * b) ** 3;
  const s = (L - 0.0894841775 * a - 1.291485548 * b) ** 3;
  return [
    4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
    -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
    -0.0041960863 * l - 0.7034186147 * m + 1.707614701 * s,
  ];
}

/** Below this a colour is grey and its hue is noise, so it reads as 0. */
const GREY = 1e-6;

export function hexToOklch(hex: string): Oklch {
  const [L, a, b] = linearToOklab(readHex(hex).map((v) => toLinear(v / 255)) as Triple);
  const c = Math.hypot(a, b);
  const h = c < GREY ? 0 : ((Math.atan2(b, a) * 180) / Math.PI + 360) % 360;
  return { l: L, c, h };
}

const linearAt = (l: number, c: number, h: number): Triple => {
  const rad = (h * Math.PI) / 180;
  return oklabToLinear([l, c * Math.cos(rad), c * Math.sin(rad)]);
};

const inGamut = (rgb: Triple): boolean => rgb.every((v) => v >= -GREY && v <= 1 + GREY);

const clamp = (v: number, lo: number, hi: number): number => Math.min(hi, Math.max(lo, v));

/**
 * The most chroma, up to `c`, that this lightness and hue can carry in sRGB.
 * Lightness and hue are never touched: a colour out of gamut loses colour, not
 * brightness, which is what keeps "dark stays dark" true under a rotation.
 */
export function clipChroma(l: number, c: number, h: number): number {
  const lightness = clamp(l, 0, 1);
  const want = Math.max(0, c);
  if (inGamut(linearAt(lightness, want, h))) return want;
  let lo = 0;
  let hi = want;
  for (let step = 0; step < 24; step += 1) {
    const mid = (lo + hi) / 2;
    if (inGamut(linearAt(lightness, mid, h))) lo = mid;
    else hi = mid;
  }
  return lo;
}

export function oklchToHex(l: number, c: number, h: number): string {
  const lightness = clamp(l, 0, 1);
  const rgb = linearAt(lightness, clipChroma(lightness, c, h), h);
  const [r, g, b] = rgb.map((v) => Math.round(fromLinear(clamp(v, 0, 1)) * 255));
  return formatHex(r, g, b);
}
