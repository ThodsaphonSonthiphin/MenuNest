import { Button, Color, Size, Variant } from '@syncfusion/react-buttons'
import type { ShoppingListItemDto } from '../../../shared/api/api'

interface ShoppingItemRowProps {
  item: ShoppingListItemDto
  listId: string
  onBuy: (itemId: string) => void
  onUnbuy: (itemId: string) => void
  onDelete: (itemId: string, itemName: string) => void
}

export function ShoppingItemRow({ item, onBuy, onUnbuy, onDelete }: ShoppingItemRowProps) {
  const hasSource =
    item.sourceMealPlanEntryIds != null && item.sourceMealPlanEntryIds.length > 0

  if (item.isBought) {
    const boughtTime = item.boughtAt
      ? new Date(item.boughtAt).toLocaleTimeString('th-TH', {
          hour: '2-digit',
          minute: '2-digit',
        })
      : null

    return (
      <tr style={{ opacity: 0.75 }}>
        <td style={{ width: 36 }}>
          <input type="checkbox" checked disabled aria-label={`${item.ingredientName} (ซื้อแล้ว)`} />
        </td>
        <td>
          <span style={{ textDecoration: 'line-through', color: 'var(--color-text-muted)' }}>
            {item.ingredientName}
          </span>
        </td>
        <td style={{ fontSize: 13, color: 'var(--color-text-muted)' }}>
          {item.quantity} {item.unit}
        </td>
        <td style={{ fontSize: 12, color: 'var(--color-text-muted)' }}>
          {boughtTime && <span>ซื้อ {boughtTime}</span>}
        </td>
        <td style={{ textAlign: 'right' }}>
          <Button
            type="button"
            size={Size.Small}
            variant={Variant.Outlined}
            color={Color.Secondary}
            onClick={() => onUnbuy(item.id)}
            aria-label="ยกเลิกซื้อ"
          >
            ↩
          </Button>
        </td>
      </tr>
    )
  }

  return (
    <tr>
      <td style={{ width: 36 }}>
        <input
          type="checkbox"
          checked={false}
          onChange={() => onBuy(item.id)}
          aria-label={`ซื้อ ${item.ingredientName}`}
        />
      </td>
      <td style={{ fontWeight: 500 }}>
        {item.ingredientName}
        {hasSource && (
          <span
            style={{
              marginLeft: 6,
              fontSize: 11,
              color: 'var(--color-text-muted)',
              background: '#fff3e0',
              borderRadius: 4,
              padding: '2px 6px',
            }}
          >
            จาก meal plan
          </span>
        )}
      </td>
      <td style={{ fontSize: 13 }}>
        {item.quantity} {item.unit}
      </td>
      <td />
      <td style={{ textAlign: 'right' }}>
        <Button
          type="button"
          size={Size.Small}
          variant={Variant.Outlined}
          color={Color.Error}
          onClick={() => onDelete(item.id, item.ingredientName)}
          aria-label="ลบ"
        >
          🗑
        </Button>
      </td>
    </tr>
  )
}
