import { describe, expect, it } from 'vitest'
import { siteEnv } from './siteEnv'

describe('base', () => {
  it.each([
    ['unset', undefined, '/'],
    ['empty', '', '/'],
    ['a single slash', '/', '/'],
    ['no slashes', 'project', '/project/'],
    ['a leading slash', '/project', '/project/'],
    ['a trailing slash', 'project/', '/project/'],
    ['both slashes', '/project/', '/project/'],
  ])('%s base_path gives %s', (_name: string, base_path: string | undefined, expected: string) => {
    expect(siteEnv({ base_path }).base).toBe(expected)
  })
})

describe('canonical', () => {
  it.each([
    ['unset', undefined, undefined],
    ['empty', '', undefined],
    ['with no trailing slash', 'https://example.test/project', 'https://example.test/project/'],
    ['with a trailing slash', 'https://example.test/project/', 'https://example.test/project/'],
  ])('%s base_url gives %s', (_name: string, base_url: string | undefined, expected: string | undefined) => {
    expect(siteEnv({ base_url }).canonical).toBe(expected)
  })
})

describe('repositoryUrl and cloneUrl', () => {
  it.each([
    ['both set', 'https://github.test', 'owner/repo', 'https://github.test/owner/repo'],
    ['both set, server with a trailing slash', 'https://github.test/', 'owner/repo', 'https://github.test/owner/repo'],
    ['only the server set', 'https://github.test', undefined, undefined],
    ['only the repository set', undefined, 'owner/repo', undefined],
    ['neither set', undefined, undefined, undefined],
    ['server empty, repository set', '', 'owner/repo', undefined],
    ['server set, repository empty', 'https://github.test', '', undefined],
  ])(
    '%s',
    (
      _name: string,
      GITHUB_SERVER_URL: string | undefined,
      GITHUB_REPOSITORY: string | undefined,
      expected: string | undefined,
    ) => {
      const result = siteEnv({ GITHUB_SERVER_URL, GITHUB_REPOSITORY })
      expect(result.repositoryUrl).toBe(expected)
      expect(result.cloneUrl).toBe(expected === undefined ? undefined : `${expected}.git`)
    },
  )
})
