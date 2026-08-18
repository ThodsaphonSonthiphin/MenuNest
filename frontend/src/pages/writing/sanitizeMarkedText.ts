import DOMPurify from 'dompurify'

/**
 * The four marks a Correction is allowed to paint (ADR-180). Anything else in a
 * class attribute — including MenuNest's own class names — is stripped, so a
 * correction can never repaint the page with the app's styles.
 */
const ALLOWED_MARK_CLASSES = new Set(['miss', 'fix', 'hit', 'th'])

/**
 * Sanitises the AI-authored `markedText` of a Correction down to a closed
 * allow-list before it reaches the DOM.
 *
 * `markedText` is HTML written by a language model and stored verbatim — the
 * recording path does no sanitising by design — so this is the boundary. The
 * allow-list and the class filter together ARE the security contract: widening
 * either is a security change, not styling.
 */
export function sanitizeMarkedText(markedText: string): string {
  const root = DOMPurify.sanitize(markedText, {
    ALLOWED_TAGS: ['p', 'span', 'br'],
    ALLOWED_ATTR: ['class'],
    RETURN_DOM: true,
  }) as HTMLElement

  for (const element of Array.from(root.querySelectorAll('[class]'))) {
    const kept = Array.from(element.classList).filter((c) => ALLOWED_MARK_CLASSES.has(c))
    if (kept.length === 0) element.removeAttribute('class')
    else element.setAttribute('class', kept.join(' '))
  }

  return root.innerHTML
}
