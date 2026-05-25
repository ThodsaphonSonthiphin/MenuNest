import {useState} from 'react'
import {Controller, useForm} from 'react-hook-form'
import {Button, Color, Variant} from '@syncfusion/react-buttons'
import {NumericTextBox} from '@syncfusion/react-inputs'
import {useAppSelector} from '../../../store'
import {useSetMonthlyIncomeMutation} from '../../../shared/api/api'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'

const MONTHS = ['January','February','March','April','May','June','July','August','September','October','November','December']

interface FormValues { amount: number | null }

export function SetIncomeDialog({
  currentAmount,
  onClose,
}: {
  currentAmount: number
  onClose: () => void
}) {
  const {year, month} = useAppSelector(s => s.budget)
  const [setIncome, {isLoading}] = useSetMonthlyIncomeMutation()
  const [err, setErr] = useState<string | null>(null)
  const {control, handleSubmit, formState} = useForm<FormValues>({defaultValues: {amount: currentAmount}})

  const onSubmit = handleSubmit(async values => {
    setErr(null)
    try {
      await setIncome({year, month, amount: Number(values.amount ?? 0)}).unwrap()
      onClose()
    } catch (e) {
      setErr(getErrorMessage(e))
    }
  })

  return (
    <div
      className="budget-modal-overlay"
      onClick={(e) => { if (e.target === e.currentTarget) onClose() }}
      data-testid="bdg-set-income-dialog"
    >
      <form className="budget-modal" onSubmit={onSubmit} noValidate>
        <h3>Monthly income — {MONTHS[month - 1]} {year}</h3>
        <div className="subtitle">All money you expect to receive this month, before assigning to envelopes.</div>

        <div className="budget-modal-field">
          <div className="budget-modal-label">Amount (THB)</div>
          <Controller
            control={control}
            name="amount"
            rules={{validate: v => (v != null && Number(v) >= 0) || 'Must be 0 or more.'}}
            render={({field}) => (
              <NumericTextBox
                min={0}
                value={field.value ?? null}
                onChange={e => field.onChange((e.value as number | null) ?? null)}
              />
            )}
          />
          {formState.errors.amount && <p className="field-error">{formState.errors.amount.message}</p>}
        </div>

        {err && <p className="field-error">{err}</p>}

        <div className="budget-modal-footer">
          <Button type="button" variant={Variant.Outlined} color={Color.Secondary} onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" variant={Variant.Filled} color={Color.Primary} disabled={isLoading}>
            {isLoading ? '…' : 'Save'}
          </Button>
        </div>
      </form>
    </div>
  )
}
