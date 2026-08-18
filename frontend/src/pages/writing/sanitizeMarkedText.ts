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
    // DOMPurify defaults ALLOW_DATA_ATTR and ALLOW_ARIA_ATTR to true, and
    // ALLOWED_ATTR does not close them -- a bare allow-list of ['class']
    // still lets any data-* or aria-* attribute through untouched. Both must
    // be turned off explicitly or the allow-list is wider than it looks
    // (fix round 2, #97): a smuggled aria-hidden="true" would silently pull
    // block 1 out of the accessibility tree, and data-* carries
    // attacker-chosen bytes into the DOM.
    ALLOW_DATA_ATTR: false,
    ALLOW_ARIA_ATTR: false,
    RETURN_DOM: true,
  }) as HTMLElement

  for (const element of Array.from(root.querySelectorAll('[class]'))) {
    const kept = Array.from(element.classList).filter((c) => ALLOWED_MARK_CLASSES.has(c))
    if (kept.length === 0) element.removeAttribute('class')
    else element.setAttribute('class', kept.join(' '))
  }

  return root.innerHTML
}
