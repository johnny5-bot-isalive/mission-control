export function buildApiUrl(apiBaseUrl: string, routePath: string): string {
  const normalizedBase = apiBaseUrl.endsWith('/')
    ? apiBaseUrl.slice(0, -1)
    : apiBaseUrl

  const normalizedRoute = routePath.startsWith('/')
    ? routePath
    : `/${routePath}`

  return `${normalizedBase}${normalizedRoute}`
}
