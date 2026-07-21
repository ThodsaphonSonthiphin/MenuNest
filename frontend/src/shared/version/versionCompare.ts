export function commitOf(version: string): string {
  const i = version.indexOf('+')
  return i >= 0 ? version.slice(i + 1) : version
}

export function inSync(appCommit: string, apiCommit: string | undefined | null): boolean {
  return !!apiCommit && appCommit.length > 0 && appCommit === apiCommit
}
