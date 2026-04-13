import { Button, Color, Size, Variant } from '@syncfusion/react-buttons'
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
    stockCheck,
    errorMessage,
    isCooking,
    isDeleting,
    handleDelete,
    handleCook,
  } = useMealSlotDetail(entries)

  return (
    <div>
      {errorMessage && <div className="error-banner">{errorMessage}</div>}

      <div className="table-scroll" style={{ marginBottom: 12 }}>
        <table className="data-table">
          <thead>
            <tr>
              <th style={{ width: 50 }}>เลือก</th>
              <th>Recipe</th>
              <th style={{ width: 200 }}>Stock</th>
              <th style={{ width: 200 }}>สถานะ</th>
            </tr>
          </thead>
          <tbody>
            {entries.map((entry) => {
              const isPlanned = entry.status === 'Planned'
              const checked = selectedIds.has(entry.id)
              return (
                <tr key={entry.id} className={isPlanned ? undefined : 'row--cooked'}>
                  <td>
                    {isPlanned ? (
                      <input
                        type="checkbox"
                        checked={checked}
                        onChange={() => toggle(entry.id, entry.status)}
                        aria-label={`เลือก ${entry.recipeName}`}
                      />
                    ) : (
                      <span style={{ color: 'var(--color-text-muted)' }}>—</span>
                    )}
                  </td>
                  <td style={{ fontWeight: 500 }}>{entry.recipeName}</td>
                  <td>
                    <RowStockBadge entryId={entry.id} status={entry.status} />
                  </td>
                  <td>
                    {isPlanned ? (
                      <>
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
                      </>
                    ) : (
                      <>
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
                      </>
                    )}
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
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
