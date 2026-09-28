/** The look every control in the board's filter bar shares: one rule in
    App.css, so a search box, a select, a menu trigger and a switch cannot
    drift apart. `lit` is "holding something other than its default". */
export const facetClass = (lit: boolean): string => `hatch-facet${lit ? ' hatch-facet--lit' : ''}`;
