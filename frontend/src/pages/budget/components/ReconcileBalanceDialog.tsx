import {useState} from 'react'
import {Controller, useForm} from 'react-hook-form'
import {Button, Color, Variant} from '@syncfusion/react-buttons'
import {NumericTextBox} from '@syncfusion/react-inputs'
import {useCorrectAccountBalanceMutation} from '../../../shared/api/api'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'
import {getViewerTimeZone} from '../../../shared/utils/timeZone'
import {formatTHB} from '../BudgetPage.hooks'

interface FormValues { actualBalance: number | null }

/**
 * Reconcile dialog — user enters the true bank-side balance. Posts to
 * correct_account_balance's REST twin with confirmed=true: the dialog
 * itself IS the confirmation (menunest-182) — it already shows the
 * numbers and requires a press, so it skips the refuse-then-confirm gate
 * that guards the MCP tool instead.
 */
export function ReconcileBalanceDialog({
  accountId,
  trackedBalance,
  onClose,
}: {
  accountId: string
  trackedBalance: number
  onClose: () => void
}) {
  const [correctBalance, {isLoading}] = useCorrectAccountBalanceMutation()
  const [err, setErr] = useState<string | null>(null)
  const {control, handleSubmit, watch, formState} = useForm<FormValues>({
    defaultValues: {actualBalance: trackedBalance},
  })
  const actual = watch('actualBalance')
  const diff = actual == null ? 0 : Number(actual) - trackedBalance

  const onSubmit = handleSubmit(async values => {
    setErr(null)
    if (values.actualBalance == null) { setErr('Enter the actual balance.'); return }
    if (diff === 0) { onClose(); return }
    try {
      await correctBalance({
        accountId,
        actualBalance: values.actualBalance,
        confirmed: true,
        date: null,
        notes: 'Manual balance fix',
        timeZoneId: getViewerTimeZone(),
      }).unwrap()
      onClose()
    } catch (e) {
      setErr(getErrorMessage(e))
    }
  })

  return (
    <div
      className="budget-modal-overlay"
      onClick={(e) => { if (e.target === e.currentTarget) onClose() }}
      data-testid="bdg-reconcile-dialog"
    >
      <form className="budget-modal" onSubmit={onSubmit} noValidate>
        <h3>Reconcile balance</h3>
        <div className="subtitle">
          Enter what your bank actually shows. We'll post a single adjustment transaction to make our running balance match.
        </div>

        <div className="budget-modal-field">
          <div className="budget-modal-label">Tracked here</div>
          <div style={{fontSize: 15, fontWeight: 700}}>{formatTHB(trackedBalance)}</div>
        </div>

        <div className="budget-modal-field">
          <div className="budget-modal-label">Actual balance (bank)</div>
          <Controller
            control={control}
            name="actualBalance"
            rules={{validate: v => v != null || 'Required.'}}
            render={({field}) => (
              <NumericTextBox
                value={field.value ?? null}
                onChange={e => field.onChange((e.value as number | null) ?? null)}
              />
            )}
          />
          {formState.errors.actualBalance && (
            <p className="field-error">{formState.errors.actualBalance.message}</p>
          )}
        </div>

        <div className="budget-modal-field">
          <div className="budget-modal-label">Adjustment</div>
          <div style={{fontSize: 14, color: diff === 0 ? 'var(--text-muted)' : diff > 0 ? 'var(--green)' : 'var(--red)'}}>
            {diff > 0 ? '+' : ''}{formatTHB(diff)}
            {diff !== 0 && <span style={{fontSize: 11, color: 'var(--text-muted)', marginLeft: 8}}>
              · creates "Manual balance fix" transaction
            </span>}
          </div>
        </div>

        {err && <p className="field-error">{err}</p>}

        <div className="budget-modal-footer">
          <Button type="button" variant={Variant.Outlined} color={Color.Secondary} onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" variant={Variant.Filled} color={Color.Primary} disabled={isLoading}>
            {isLoading ? '…' : diff === 0 ? 'No change' : 'Save adjustment'}
          </Button>
        </div>
      </form>
    </div>
  )
}
