import {useState} from 'react'
import {Controller, useForm} from 'react-hook-form'
import {Button, Color, Variant} from '@syncfusion/react-buttons'
import {DropDownList} from '@syncfusion/react-dropdowns'
import {NumericTextBox, TextArea} from '@syncfusion/react-inputs'
import {
  useMakePaymentMutation,
  useUpdatePaymentMutation,
  type BudgetAccountDto,
  type EnvelopeGroupDto,
} from '../../../shared/api/api'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'
import {getViewerTimeZone} from '../../../shared/utils/timeZone'
import {formatTHB} from '../BudgetPage.hooks'
import {payActionWord, shortfallLine} from '../lib/paymentLabel'
import {
  fundingEnvelopeOptions,
  needsFundingEnvelope,
  payingAccountOptions,
  payingCardWarning,
} from '../lib/paymentOptions'
// PaymentDraft lives beside `paymentDraftFromRow`, which is the only thing
// that builds one — see lib/paymentRows.ts.
import type {PaymentDraft} from '../lib/paymentRows'

interface FormValues {
  fromAccountId: string
  categoryId: string
  amount: number | null
  date: string
  notes: string
}

function todayIso(): string {
  const d = new Date()
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${y}-${m}-${day}`
}

/**
 * menunest-204 / menunest-207 / menunest-214 — pay a Credit card or a Loan.
 *
 * One action, one command: the API writes BOTH `BudgetTransaction` legs in a
 * single `SaveChangesAsync`, so there is no moment at which half a payment
 * exists, and no "transfer" concept leaks into the UI.
 *
 * The funding-Envelope picker appears ONLY when the target is a **Loan**
 * (menunest-214): a Loan has no Payment envelope of its own, so the Envelope
 * is the only thing a loan payment ever spends. Paying a **Credit** card sends
 * no category at all — the card's Payment envelope already falls by
 * derivation, and categorising the outflow leg too would double-count it.
 */
export function PaymentDialog({
  toAccount,
  accounts,
  groups,
  existing,
  onClose,
  onSaved,
}: {
  /** The debt account being paid — the Payment envelope's own card, or a Loan. */
  toAccount: BudgetAccountDto
  accounts: BudgetAccountDto[]
  groups: EnvelopeGroupDto[]
  existing?: PaymentDraft
  onClose: () => void
  onSaved?: () => void
}) {
  const [makePayment, {isLoading: isCreating}] = useMakePaymentMutation()
  const [updatePayment, {isLoading: isUpdating}] = useUpdatePaymentMutation()
  const isLoading = isCreating || isUpdating
  const [err, setErr] = useState<string | null>(null)

  const action = payActionWord(toAccount.type)
  const needsEnvelope = needsFundingEnvelope(toAccount.type)

  const {control, handleSubmit, formState, watch} = useForm<FormValues>({
    defaultValues: {
      fromAccountId: existing?.fromAccountId ?? '',
      categoryId: existing?.categoryId ?? '',
      amount: existing?.amount ?? null,
      date: existing?.date?.slice(0, 10) ?? todayIso(),
      notes: existing?.notes ?? '',
    },
  })

  const fromAccountId = watch('fromAccountId')
  const amount = watch('amount')
  const fromAccount = accounts.find(a => a.id === fromAccountId) ?? null

  const accountOptions = payingAccountOptions(accounts, toAccount.id)
  const envelopeOptions = fundingEnvelopeOptions(groups)

  // Correction #4: paying one card with another moves Ready to Assign UP.
  // Correct — the paid card's envelope empties while the paying card's debt
  // widens with no offsetting envelope — but surprising enough to read as a
  // bug, so the paying card's own shortfall is named here.
  const payingEnvelope = fromAccount
    ? groups.flatMap(g => g.categories).find(c => c.paymentForAccountId === fromAccount.id)
    : undefined
  const cardWarning = payingCardWarning(
    fromAccount, payingEnvelope?.available ?? 0, amount, toAccount.type)

  const owed = shortfallLine(toAccount.shortfall)

  const onSubmit = handleSubmit(async values => {
    setErr(null)
    // menunest-214: the category goes with a Loan and is REFUSED on a card.
    const categoryId = needsEnvelope ? values.categoryId : null
    try {
      if (existing) {
        await updatePayment({
          paymentId: existing.paymentId,
          fromAccountId: values.fromAccountId,
          toAccountId: toAccount.id,
          amount: Number(values.amount),
          date: values.date,
          notes: values.notes.trim() || null,
          categoryId,
        }).unwrap()
      } else {
        await makePayment({
          fromAccountId: values.fromAccountId,
          toAccountId: toAccount.id,
          amount: Number(values.amount),
          date: values.date,
          notes: values.notes.trim() || null,
          timeZoneId: getViewerTimeZone(),
          categoryId,
        }).unwrap()
      }
      onSaved?.()
      onClose()
    } catch (e) {
      setErr(getErrorMessage(e))
    }
  })

  return (
    <div
      className="budget-modal-overlay"
      onClick={e => {
        if (e.target === e.currentTarget) onClose()
      }}
    >
      <form className="budget-modal" onSubmit={onSubmit} noValidate data-testid="bdg-payment-dialog">
        <h3>{existing ? `แก้ไข ${action}` : action}</h3>
        <div className="subtitle">
          <strong>{toAccount.name}</strong> · ยอด {formatTHB(toAccount.balance)}
          {owed && <> · <b className={owed.tone === 'short' ? 'short' : undefined}>{owed.text}</b></>}
        </div>

        <div className="budget-modal-field">
          <div className="budget-modal-label">จ่ายจากบัญชี</div>
          <Controller
            control={control}
            name="fromAccountId"
            rules={{required: 'เลือกบัญชีที่ใช้จ่าย'}}
            render={({field}) => (
              <DropDownList
                dataSource={accountOptions}
                fields={{text: 'label', value: 'id'}}
                value={field.value || null}
                placeholder="เลือกบัญชี…"
                onChange={(e: {value: unknown}) => field.onChange((e.value as string) ?? '')}
              />
            )}
          />
          {formState.errors.fromAccountId && (
            <p className="field-error">{formState.errors.fromAccountId.message}</p>
          )}
        </div>

        {/* menunest-214 — Loan only. A card must send no category at all. */}
        {needsEnvelope && (
          <div className="budget-modal-field">
            <div className="budget-modal-label">ซองที่ใช้จ่ายค่างวด</div>
            <Controller
              control={control}
              name="categoryId"
              rules={{required: 'เลือกซองที่ใช้จ่ายค่างวดนี้'}}
              render={({field}) => (
                <DropDownList
                  dataSource={envelopeOptions}
                  fields={{text: 'label', value: 'id'}}
                  value={field.value || null}
                  placeholder="เลือกซอง…"
                  onChange={(e: {value: unknown}) => field.onChange((e.value as string) ?? '')}
                />
              )}
            />
            {formState.errors.categoryId && (
              <p className="field-error">{formState.errors.categoryId.message}</p>
            )}
          </div>
        )}

        {/* Amount and date each take a full row, matching TransactionDialog:
            a Syncfusion NumericTextBox and a native date input do not share a
            baseline, so pairing them in a .budget-modal-row visibly staggers. */}
        <div className="budget-modal-field">
          <div className="budget-modal-label">จำนวนเงิน</div>
          <Controller
            control={control}
            name="amount"
            rules={{validate: v => (v != null && Number(v) > 0) || 'ต้องมากกว่า 0'}}
            render={({field}) => (
              <NumericTextBox
                min={0}
                value={field.value ?? null}
                onChange={e => field.onChange((e.value as number | null) ?? null)}
              />
            )}
          />
          {formState.errors.amount && (
            <p className="field-error">{formState.errors.amount.message}</p>
          )}
        </div>

        <div className="budget-modal-field">
          <div className="budget-modal-label">วันที่</div>
          <Controller
            control={control}
            name="date"
            rules={{required: 'เลือกวันที่'}}
            render={({field}) => (
              <input
                type="date"
                className="budget-assigned-input"
                style={{width: '100%', textAlign: 'left'}}
                value={field.value}
                onChange={e => field.onChange(e.target.value)}
              />
            )}
          />
          {formState.errors.date && (
            <p className="field-error">{formState.errors.date.message}</p>
          )}
        </div>

        <div className="budget-modal-field">
          <div className="budget-modal-label">บันทึก</div>
          <Controller
            control={control}
            name="notes"
            rules={{maxLength: {value: 500, message: 'ไม่เกิน 500 ตัวอักษร'}}}
            render={({field}) => (
              <TextArea
                rows={2}
                value={field.value}
                onChange={e => field.onChange(e.value ?? '')}
              />
            )}
          />
          {formState.errors.notes && (
            <p className="field-error">{formState.errors.notes.message}</p>
          )}
        </div>

        {cardWarning && (
          <p className="bdg-payment-note" data-testid="bdg-payment-card-note">
            {cardWarning.text}
          </p>
        )}

        {err && <p className="field-error">{err}</p>}

        <div className="budget-modal-footer">
          <Button type="button" variant={Variant.Outlined} color={Color.Secondary} onClick={onClose}>
            ยกเลิก
          </Button>
          <Button type="submit" variant={Variant.Filled} color={Color.Primary} disabled={isLoading}>
            {isLoading ? '…' : existing ? 'บันทึก' : action}
          </Button>
        </div>
      </form>
    </div>
  )
}
