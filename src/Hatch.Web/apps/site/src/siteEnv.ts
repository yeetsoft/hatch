/* The one place that reads the four variables a deploy sets and a dev
   checkout never does. `base_url`/`base_path` arrive as `configure-pages`
   step outputs mapped onto `env:` by the workflow (HA-727); `GITHUB_SERVER_URL`
   and `GITHUB_REPOSITORY` are real runner environment variables and need no
   mapping. An empty string is a workflow having mapped an unset output, so it
   is treated as unset throughout. */

export interface SiteEnv {
  base: string
  canonical: string | undefined
  repositoryUrl: string | undefined
  cloneUrl: string | undefined
}

const trimSlashes = (value: string): string => value.replace(/^\/+/, '').replace(/\/+$/, '')

const setOrUndefined = (value: string | undefined): string | undefined =>
  value === undefined || value === '' ? undefined : value

export function siteEnv(env: Record<string, string | undefined>): SiteEnv {
  const basePath = trimSlashes(setOrUndefined(env.base_path) ?? '')
  const base = basePath === '' ? '/' : `/${basePath}/`

  const baseUrl = setOrUndefined(env.base_url)
  const canonical = baseUrl === undefined ? undefined : `${baseUrl.replace(/\/+$/, '')}/`

  const serverUrl = setOrUndefined(env.GITHUB_SERVER_URL)
  const repository = setOrUndefined(env.GITHUB_REPOSITORY)
  const repositoryUrl =
    serverUrl === undefined || repository === undefined ? undefined : `${serverUrl.replace(/\/+$/, '')}/${repository}`
  const cloneUrl = repositoryUrl === undefined ? undefined : `${repositoryUrl}.git`

  return { base, canonical, repositoryUrl, cloneUrl }
}
