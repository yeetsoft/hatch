import { useMemo, useState } from 'react';
import { Button, PaletteBoard, PaletteConfigurator, ParameterControls, useTheme } from '@hatch/ui';
import { measurePairs, type PaletteParams } from '@hatch/ui/palette';
import { GalleryPage, GallerySection } from '../components/Gallery';
import { deriveBoth, sketchPalettes } from '../lib/paletteSketch';

/** The controls at the fixed seed, wired to local state, and the board beside them
    re-derived from that same state on every move. The rim follows the theme on screen, not either board. */
function LiveControls() {
  const { resolved } = useTheme();
  const [params, setParams] = useState<PaletteParams>(() => sketchPalettes().params);
  const { light, dark } = useMemo(() => deriveBoth(params), [params]);
  const boards = useMemo(
    () =>
      (
        [
          ['Light block', light],
          ['Dark block', dark],
        ] as const
      ).map(([label, palette]) => ({ label, palette, pairs: measurePairs(palette) })),
    [light, dark],
  );

  return (
    <div className="design-pc-live">
      <div className="design-pc-live-panel">
        <ParameterControls value={params} onChange={setParams} palette={resolved === 'dark' ? dark : light} />
      </div>
      <div className="design-pc-boards">
        {boards.map(({ label, palette, pairs }) => (
          <div key={label} className="design-pc-board-block">
            <span className="design-pc-board-block-label">{label}</span>
            <PaletteBoard palette={palette} pairs={pairs} />
          </div>
        ))}
      </div>
    </div>
  );
}

export function PaletteConfiguratorPage() {
  const { light, dark } = sketchPalettes();
  const [open, setOpen] = useState(false);

  return (
    <GalleryPage
      title="Palette configurator"
      blurb="The dialog that tunes the theme: a hue dial, a slider per remaining parameter, and a board of the colours with their contrast. The real component is opened here, and its parts follow as specimens at one fixed seed."
    >
      <GallerySection
        title="The configurator"
        note="The real <PaletteConfigurator>, live, in the theme on screen. The page behind it follows the controls as they move, as a preview only; Revert, Apply and Roll are real, and Apply turns Random on for this browser. The specimens below stay at the fixed seed whatever it does."
      >
        <div className="design-row">
          <Button onClick={() => setOpen(true)}>Tune the theme</Button>
        </div>
        <PaletteConfigurator open={open} onClose={() => setOpen(false)} />
      </GallerySection>

      <GallerySection
        title="Palette board"
        note="The board the configurator is built around, at the fixed seed: every non-fixed token the engine derives, drawn the way it is used, with the measured contrast of every guarded pair beneath it. A row that clears its floor is lit; one that does not says so."
      >
        <div className="design-pc-boards">
          {(
            [
              ['Light block', light],
              ['Dark block', dark],
            ] as const
          ).map(([label, palette]) => (
            <div key={label} className="design-pc-board-block">
              <span className="design-pc-board-block-label">{label}</span>
              <PaletteBoard palette={palette} pairs={measurePairs(palette)} />
            </div>
          ))}
        </div>
      </GallerySection>

      <GallerySection
        title="Parameter controls"
        note="A hue dial and one slider per remaining parameter, bound to the engine's table, drawn at the fixed seed and wired to local state. Every track is the parameter's full range with its roll window lit on it; a value outside the window says so. The dial's rim carries the nine accents where the engine derived them, so the dots move with spread as well as with hue. The boards re-derive from the same state."
      >
        <LiveControls />
      </GallerySection>
    </GalleryPage>
  );
}
