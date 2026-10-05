import { useId } from 'react';
import { ProjectMark, PROJECT_ICONS } from '@hatch/ui';

/**
 * Same radio-group pattern as `@hatch/ui`'s `ThemeSwitch` - a real
 * `<input type="radio">` per option, visually hidden, so arrow-key/tab
 * navigation and the screen-reader grouping come from the browser rather
 * than a hand-rolled key handler. The visible thing beside each input is a
 * `ProjectMark` rather than `ThemeSwitch`'s `<span>`.
 *
 * Only the selected mark is drawn on `color` - every other option sits on
 * `--muted` (passing it `color={null}`), which is what keeps the full set
 * from reading as identically-tinted swatches.
 */
export function ProjectIconPicker({
  value,
  color,
  onChange,
}: {
  value: string | null;
  color: string | null;
  onChange: (slug: string | null) => void;
}) {
  const name = useId();
  const options: { slug: string | null; title: string }[] = [
    { slug: null, title: 'None' },
    ...PROJECT_ICONS.map(({ slug, title }) => ({ slug, title })),
  ];

  return (
    <fieldset className="hatch-icon-picker">
      <legend className="hatch-icon-picker__legend">Icon</legend>
      {options.map((option) => (
        <label key={option.slug ?? 'none'} className="hatch-icon-picker__option">
          <input
            type="radio"
            name={name}
            checked={value === option.slug}
            onChange={() => onChange(option.slug)}
          />
          {option.slug === null ? (
            <span className="hatch-icon-picker__none">None</span>
          ) : (
            <ProjectMark
              size="md"
              letters=""
              icon={option.slug}
              color={option.slug === value ? color : null}
              title={option.title}
            />
          )}
        </label>
      ))}
    </fieldset>
  );
}
