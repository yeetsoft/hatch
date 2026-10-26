import { describe, expect, it } from 'vitest';
import { dangerousDraft, dangerousFact, dangerousRequest, dangerousTitle } from './dangerous';

describe('dangerousDraft', () => {
  it('shows no reason as a blank box', () => {
    expect(dangerousDraft(null)).toBe('');
  });

  it('shows the own reason as given', () => {
    expect(dangerousDraft('load-tests the API')).toBe('load-tests the API');
  });
});

describe('dangerousRequest', () => {
  it('a blank field clears the flag', () => {
    expect(dangerousRequest('')).toBe('');
  });

  it('a whitespace-only field clears the flag', () => {
    expect(dangerousRequest('   ')).toBe('');
  });

  it('trims the field for the wire', () => {
    expect(dangerousRequest('  load-tests the API  ')).toBe('load-tests the API');
  });
});

describe('dangerousTitle', () => {
  it('names the reason after "Dangerous - "', () => {
    expect(dangerousTitle('load-tests the API')).toBe('Dangerous - load-tests the API');
  });
});

describe('dangerousFact', () => {
  it('reads the own reason with no inheritance clause', () => {
    expect(dangerousFact('load-tests the API', null)).toBe('load-tests the API');
  });

  it('names the ancestor it is inherited from', () => {
    expect(dangerousFact('load-tests the API', 'HA-1')).toBe('load-tests the API · inherited from HA-1');
  });
});
