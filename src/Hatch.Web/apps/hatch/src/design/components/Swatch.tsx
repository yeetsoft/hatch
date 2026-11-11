import { TokenName, TokenValue } from './Gallery';

/**
 * One color token: the fill, its name, the value the browser resolved, and
 * what it is for.
 *
 * The chip is painted over --card with a hairline, not over the page, because
 * several of these tokens are translucent washes (--primary-bg and its
 * siblings) and a wash shown against the wrong backdrop is shown as the wrong
 * color. --card is where those washes are actually used.
 */
export function Swatch({ token, value, use }: { token: string; value?: string; use?: string }) {
  return (
    <div className="design-swatch">
      <div className="design-swatch-chip">
        <div className="design-swatch-fill" style={{ background: `var(${token})` }} />
      </div>
      <div className="design-swatch-meta">
        <TokenName name={token} />
        <TokenValue value={value} />
        {use ? <span className="design-swatch-use">{use}</span> : null}
      </div>
    </div>
  );
}
