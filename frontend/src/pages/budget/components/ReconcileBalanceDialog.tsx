import {useState} from 'react'
import {Controller, useForm} from 'react-hook-form'
import {Button, Color, Variant} from '@syncfusion/react-buttons'
import {NumericTextBox} from '@syncfusion/react-inputs'
import {useCreateBudgetTransactionMutation} from '../../../shared/api/api'
import {useAppSelector} from '../../../store'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'
import {formatTHB} from '../BudgetPage.hooks'

interface FormValues { actualBalance: number | null }

function todayIso(): string {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

/**
 * Reconcile dialog — user enters the true bank-side balance.
 * We compute (actual − tracked) and post a single adjustment
 * transaction (categoryId=null, amount=diff) so the account's
 * running balance lines up with reality. No new backend.
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
  const {year, month} = useAppSelector(s => s.budget)
  const [create, {isLoading}] = useCreateBudgetTransactionMutation()
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
      await create({
        accountId,
        categoryId: null,
        amount: diff,
        date: todayIso(),
        notes: 'Manual balance fix',
        year, month,
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
