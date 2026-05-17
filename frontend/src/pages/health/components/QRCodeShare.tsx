import { QRCodeGeneratorComponent } from '@syncfusion/ej2-react-barcode-generator'

/**
 * Thin wrapper around Syncfusion's `QRCodeGeneratorComponent` so the
 * Share Links page can render a QR code without leaking the vendor's
 * naming into the page code. Defaults match the design in the mock:
 * 200×200 px, SVG mode, error correction level 30 (Q), text hidden.
 *
 * The Syncfusion library is already a project dependency
 * (`@syncfusion/ej2-react-barcode-generator`); see `FamilyPage.tsx`
 * for another usage example.
 */
export interface QRCodeShareProps {
  shareUrl: string
  size?: number
}

export function QRCodeShare({ shareUrl, size = 200 }: QRCodeShareProps) {
  const sizePx = `${size}px`
  return (
    <div className="health-qr-wrapper">
      <QRCodeGeneratorComponent
        value={shareUrl}
        width={sizePx}
        height={sizePx}
        errorCorrectionLevel={30}
        displayText={{ visibility: false }}
        mode="SVG"
        margin={{ left: 4, right: 4, top: 4, bottom: 4 }}
      />
    </div>
  )
}
