// Regenerates public/icons/ from the 240x240 logo at the repository root.
// Run with `npm run icons` (apps/hatch). Never invoked by a build - only a
// person regenerating icons runs this, so the build needs no image tooling.
import { readFileSync } from 'node:fs';
import { mkdir } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import sharp from 'sharp';

const __dirname = dirname(fileURLToPath(import.meta.url));
const logoPath = resolve(__dirname, '../../../../../hatch-logo.png');
const tokensPath = resolve(__dirname, '../../../packages/ui/src/tokens.css');
const outDir = resolve(__dirname, '../public/icons');

function readBgToken() {
  const css = readFileSync(tokensPath, 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');
  const root = /:root\s*\{([^}]*)\}/.exec(css);
  if (!root) throw new Error(`No :root block in ${tokensPath}`);
  const bg = /--bg:\s*([^;]+);/.exec(root[1]);
  if (!bg) throw new Error(`--bg not declared on :root in ${tokensPath}`);
  return bg[1].trim();
}

async function main() {
  await mkdir(outDir, { recursive: true });
  const logo = readFileSync(logoPath);
  const bg = readBgToken();

  // Downscaled from the 240x240 source - no fidelity loss.
  await sharp(logo).resize(192, 192).png().toFile(resolve(outDir, 'icon-192.png'));

  // Upscaled from the 240x240 source, since 512 is larger than the logo
  // itself - the one output here that loses fidelity.
  await sharp(logo).resize(512, 512).png().toFile(resolve(outDir, 'icon-512.png'));

  // The 240x240 logo composed at its native size, centred, on a 512x512
  // canvas filled with --bg. 240/512 = 46.9%, inside the maskable spec's
  // centre-80% safe zone, so this one needs no upscaling.
  await sharp({
    create: { width: 512, height: 512, channels: 4, background: bg },
  })
    .composite([{ input: logo, left: 136, top: 136 }])
    .png()
    .toFile(resolve(outDir, 'icon-512-maskable.png'));

  // Downscaled from the 240x240 source - no fidelity loss.
  await sharp(logo).resize(180, 180).png().toFile(resolve(outDir, 'apple-touch-icon.png'));
}

main();
