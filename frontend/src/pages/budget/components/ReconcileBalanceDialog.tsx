import {useCallback, useEffect, useState} from 'react'
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
 *
 * menunest-190: this dialog stays reachable on every viewed month, but an
 * Account's balance is now derived as-of the viewed month (menunest-183)
 * while the server always corrects against TODAY's balance — so the
 * comparison can no longer be seeded from the monthly summary (that was
 * the actual bug: a July screen previewing a diff against July's figure
 * while the server writes against today's). There is deliberately no
 * second `balanceToday` field on the account DTO to read instead — that
 * would just be a second number that can drift from the first one again.
 * Instead this dialog loads its preview from the same
 * `correct_account_balance` gate menunest-187 built for the assistant: an
 * unconfirmed call (confirmed=false) writes nothing and returns today's
 * derived balance. The preview and the eventual write are now the exact
 * same server computation, so they cannot disagree again.
 */
export function ReconcileBalanceDialog({
  accountId,
  onClose,
}: {
  accountId: string
  onClose: () => void
}) {
  const [loadPreview] = useCorrectAccountBalanceMutation()
  const [correctBalance, {isLoading: isSaving}] = useCorrectAccountBalanceMutation()
  const [todayBalance, setTodayBalance] = useState<number | null>(null)
  const [loadErr, setLoadErr] = useState<string | null>(null)
  const [isLoadingPreview, setIsLoadingPreview] = useState(false)
  const [err, setErr] = useState<string | null>(null)
  const {control, handleSubmit, watch, formState, reset} = useForm<FormValues>({
    defaultValues: {actualBalance: null},
  })
  const actual = watch('actualBalance')
  const diff = actual == null || todayBalance == null ? 0 : Number(actual) - todayBalance

  // The load step (menunest-190): probe with confirmed=false to learn
  // today's derived balance before rendering any comparison — the dialog
  // cannot show a trustworthy number before this returns. `actualBalance`
  // here is a throwaway value: the handler returns `derivedBalance`
  // regardless of what it's compared against, and never writes anything
  // when `confirmed` is false, so 0 is as good as any other number.
  const load = useCallback(async () => {
    setIsLoadingPreview(true)
    setLoadErr(null)
    try {
      const res = await loadPreview({
        accountId,
        actualBalance: 0,
        confirmed: false,
        date: null,
        notes: null,
        timeZoneId: getViewerTimeZone(),
      }).unwrap()
      setTodayBalance(res.derivedBalance)
      reset({actualBalance: res.derivedBalance})
    } catch (e) {
      setLoadErr(getErrorMessage(e))
    } finally {
      setIsLoadingPreview(false)
    }
  }, [accountId, loadPreview, reset])

  useEffect(() => { void load() }, [load])

  const onSubmit = handleSubmit(async values => {
    setErr(null)
    if (values.actualBalance == null) { setErr('Enter the actual balance.'); return }
    if (todayBalance != null && Number(values.actualBalance) === todayBalance) { onClose(); return }
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

        {isLoadingPreview && (
          <div className="budget-modal-field" data-testid="bdg-reconcile-loading">Loading today's balance…</div>
        )}

        {loadErr && (
          <div className="budget-modal-field">
            <p className="field-error">{loadErr}</p>
            <Button type="button" variant={Variant.Outlined} color={Color.Secondary} onClick={() => void load()}>
              Retry
            </Button>
          </div>
        )}

        {todayBalance != null && (
          <>
            <div className="budget-modal-field">
              <div className="budget-modal-label">Tracked today</div>
              <div style={{fontSize: 15, fontWeight: 700}}>{formatTHB(todayBalance)}</div>
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
          </>
        )}

        {err && <p className="field-error">{err}</p>}

        <div className="budget-modal-footer">
          <Button type="button" variant={Variant.Outlined} color={Color.Secondary} onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" variant={Variant.Filled} color={Color.Primary} disabled={isSaving || todayBalance == null}>
            {isSaving ? '…' : diff === 0 ? 'No change' : 'Save adjustment'}
          </Button>
        </div>
      </form>
    </div>
  )
}
