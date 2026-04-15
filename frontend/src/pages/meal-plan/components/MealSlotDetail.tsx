import { Button, Color, Size, Variant } from '@syncfusion/react-buttons'
import { Grid, Column, Columns } from '@syncfusion/react-grid'
import type { ColumnTemplateProps } from '@syncfusion/react-grid'
import type { MealPlanEntryDto } from '../../../shared/api/api'
import { useMealSlotDetail } from '../hooks/useMealSlotDetail'
import { RowStockBadge } from './RowStockBadge'

interface MealSlotDetailProps {
  entries: MealPlanEntryDto[]
  onAddRecipe: () => void
  onClose: () => void
}

export function MealSlotDetail({ entries, onAddRecipe, onClose }: MealSlotDetailProps) {
  const {
    selectedIds,
    selectedArray,
    toggle,
    allPlannedStockCheck,
    stockCheck,
    errorMessage,
    isCooking,
    isDeleting,
    handleDelete,
    handleCook,
  } = useMealSlotDetail(entries)

  const CheckboxTemplate = ({ data: entry }: ColumnTemplateProps<MealPlanEntryDto>) => {
    const isPlanned = entry.status === 'Planned'
    if (isPlanned) {
      return (
        <input
          type="checkbox"
          checked={selectedIds.has(entry.id)}
          onChange={() => toggle(entry.id, entry.status)}
          aria-label={`เลือก ${entry.recipeName}`}
        />
      )
    }
    return <span style={{ color: 'var(--color-text-muted)' }}>—</span>
  }

  const RecipeTemplate = ({ data: entry }: ColumnTemplateProps<MealPlanEntryDto>) => (
    <span style={{ fontWeight: 500 }}>{entry.recipeName}</span>
  )

  const StockTemplate = ({ data: entry }: ColumnTemplateProps<MealPlanEntryDto>) => (
    <RowStockBadge status={entry.status} stockCheck={allPlannedStockCheck} />
  )

  const StatusTemplate = ({ data: entry }: ColumnTemplateProps<MealPlanEntryDto>) => {
    const isPlanned = entry.status === 'Planned'
    if (isPlanned) {
      return (
        <div>
          <span className="status status--planned">Planned</span>
          <Button
            type="button"
            size={Size.Small}
            variant={Variant.Outlined}
            color={Color.Error}
            onClick={() => handleDelete(entry)}
            disabled={isDeleting}
            aria-label="ลบ"
            style={{ marginLeft: 6 }}
          >
            🗑
          </Button>
        </div>
      )
    }
    return (
      <div>
        <span className="status status--cooked">✓ Cooked</span>
        {entry.cookedAt && (
          <span
            style={{
              color: 'var(--color-text-muted)',
              fontSize: 12,
              marginLeft: 6,
            }}
          >
            {new Date(entry.cookedAt).toLocaleTimeString('th-TH', {
              hour: '2-digit',
              minute: '2-digit',
            })}
          </span>
        )}
      </div>
    )
  }

  const rowClass = (props?: { data?: MealPlanEntryDto }) => {
    if (props?.data?.status === 'Cooked') return 'row--cooked'
    return ''
  }

  return (
    <div>
      {errorMessage && <div className="error-banner">{errorMessage}</div>}

      <div style={{ marginBottom: 12 }}>
        <Grid
          dataSource={entries as MealPlanEntryDto[]}
          height="auto"
          rowClass={rowClass}
        >
          <Columns>
            <Column headerText="เลือก" width={50} template={CheckboxTemplate} />
            <Column field="recipeName" headerText="Recipe" template={RecipeTemplate} />
            <Column headerText="Stock" width={200} template={StockTemplate} />
            <Column headerText="สถานะ" width={200} template={StatusTemplate} />
          </Columns>
        </Grid>
      </div>

      {stockCheck && stockCheck.missingCount > 0 && (
        <div
          style={{
            background: '#fff3e0',
            border: '1px solid #ffb74d',
            borderRadius: 6,
            padding: '10px 14px',
            fontSize: 13,
            color: '#e65100',
            marginBottom: 12,
          }}
        >
          ⚠️ ขาด{' '}
          {stockCheck.lines
            .filter((l) => l.missing > 0)
            .map((l) => `${l.ingredientName} ${l.missing} ${l.unit}`)
            .join(', ')}{' '}
          — เมื่อกด Cook ระบบจะหักเท่าที่มี
        </div>
      )}

      <div
        style={{
          display: 'flex',
          gap: 8,
          alignItems: 'center',
          paddingTop: 8,
          borderTop: '1px solid #eee',
        }}
      >
        <Button
          type="button"
          variant={Variant.Outlined}
          color={Color.Primary}
          onClick={onAddRecipe}
        >
          + เพิ่ม recipe
        </Button>
        <div style={{ flex: 1 }} />
        <Button
          type="button"
          variant={Variant.Outlined}
          color={Color.Secondary}
          onClick={onClose}
        >
          ยกเลิก
        </Button>
        <Button
          type="button"
          variant={Variant.Filled}
          color={Color.Primary}
          onClick={() => handleCook(onClose)}
          disabled={selectedArray.length === 0 || isCooking}
        >
          🍳 Cook selected ({selectedArray.length})
        </Button>
      </div>
    </div>
  )
}
