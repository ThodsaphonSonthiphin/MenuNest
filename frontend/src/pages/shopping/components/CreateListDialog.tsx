import { Controller } from 'react-hook-form'
import { Dialog } from '@syncfusion/react-popups'
import { Button, Color, Variant } from '@syncfusion/react-buttons'
import { TextBox } from '@syncfusion/react-inputs'
import { useCreateShoppingList } from '../hooks/useCreateShoppingList'

interface CreateListDialogProps {
  open: boolean
}

export function CreateListDialog({ open }: CreateListDialogProps) {
  const { form, isLoading, errorMessage, onSubmit, handleClose } = useCreateShoppingList()

  const {
    control,
    watch,
    register,
    formState: { errors },
  } = form

  const useDateRange = watch('useDateRange')

  return (
    <Dialog
      open={open}
      onClose={handleClose}
      modal
      header="สร้างรายการซื้อของ"
      style={{ width: '480px' }}
    >
      <form onSubmit={onSubmit} noValidate style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
        {errorMessage && <div className="error-banner">{errorMessage}</div>}

        <div>
          <label style={{ display: 'block', marginBottom: 4, fontWeight: 500 }}>
            ชื่อรายการ <span className="field-required">*</span>
          </label>
          <Controller
            control={control}
            name="name"
            rules={{
              required: 'กรุณากรอกชื่อรายการ',
              maxLength: { value: 200, message: 'ยาวเกิน 200 ตัวอักษร' },
              validate: (v) => v.trim().length > 0 || 'กรุณากรอกชื่อรายการ',
            }}
            render={({ field }) => (
              <TextBox
                placeholder="เช่น ซื้อของสัปดาห์นี้"
                disabled={isLoading}
                value={field.value}
                onChange={(e) => field.onChange(e.value ?? '')}
              />
            )}
          />
          {errors.name && <p className="field-error">{errors.name.message}</p>}
        </div>

        <div>
          <label style={{ display: 'flex', alignItems: 'center', gap: 8, cursor: 'pointer' }}>
            <input type="checkbox" {...register('useDateRange')} />
            <span>📅 คำนวณจาก meal plan</span>
          </label>
        </div>

        {useDateRange && (
          <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
            <div style={{ flex: 1, minWidth: 140 }}>
              <label style={{ display: 'block', marginBottom: 4, fontWeight: 500 }}>
                วันเริ่มต้น <span className="field-required">*</span>
              </label>
              <input
                type="date"
                {...register('fromDate', {
                  required: useDateRange ? 'กรุณาระบุวันเริ่มต้น' : false,
                })}
                style={{
                  padding: '8px 12px',
                  border: '1px solid var(--color-border)',
                  borderRadius: 6,
                  font: 'inherit',
                  width: '100%',
                }}
              />
              {errors.fromDate && <p className="field-error">{errors.fromDate.message}</p>}
            </div>
            <div style={{ flex: 1, minWidth: 140 }}>
              <label style={{ display: 'block', marginBottom: 4, fontWeight: 500 }}>
                วันสิ้นสุด <span className="field-required">*</span>
              </label>
              <input
                type="date"
                {...register('toDate', {
                  required: useDateRange ? 'กรุณาระบุวันสิ้นสุด' : false,
                })}
                style={{
                  padding: '8px 12px',
                  border: '1px solid var(--color-border)',
                  borderRadius: 6,
                  font: 'inherit',
                  width: '100%',
                }}
              />
              {errors.toDate && <p className="field-error">{errors.toDate.message}</p>}
            </div>
          </div>
        )}

        <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end', paddingTop: 8 }}>
          <Button
            type="button"
            variant={Variant.Outlined}
            color={Color.Secondary}
            onClick={handleClose}
            disabled={isLoading}
          >
            ยกเลิก
          </Button>
          <Button
            type="submit"
            variant={Variant.Filled}
            color={Color.Primary}
            disabled={isLoading}
          >
            {isLoading ? '...' : '+ สร้าง'}
          </Button>
        </div>
      </form>
    </Dialog>
  )
}
