import { Button, Color, Variant } from '@syncfusion/react-buttons'

interface ConfirmationMessageProps {
  onConfirm: () => void
  onReject: () => void
  disabled?: boolean
}

export function ConfirmationMessage({ onConfirm, onReject, disabled }: ConfirmationMessageProps) {
  return (
    <div className="ai-confirmation">
      <div className="ai-confirmation__buttons">
        <Button variant={Variant.Filled} color={Color.Primary} onClick={onConfirm} disabled={disabled}>
          ยืนยัน
        </Button>
        <Button variant={Variant.Outlined} color={Color.Primary} onClick={onReject} disabled={disabled}>
          ยกเลิก
        </Button>
      </div>
    </div>
  )
}
