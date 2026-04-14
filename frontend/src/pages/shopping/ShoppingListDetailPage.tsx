import { Link, useParams } from 'react-router-dom'
import { Button, Color, Size, Variant } from '@syncfusion/react-buttons'
import { Grid, Column, Columns } from '@syncfusion/react-grid'
import type { ColumnTemplateProps } from '@syncfusion/react-grid'
import { useGetShoppingListDetailQuery } from '../../shared/api/api'
import type { ShoppingListItemDto } from '../../shared/api/api'
import { useShoppingListDetail } from './hooks/useShoppingListDetail'
import { AddItemForm } from './components/AddItemForm'

export function ShoppingListDetailPage() {
  const { id } = useParams<{ id: string }>()
  const listId = id!

  const { data, isLoading, error } = useGetShoppingListDetailQuery(listId)

  const {
    errorMessage,
    isBuying,
    isUnbuying,
    isDeletingItem,
    isCompleting,
    isRegenerating,
    isAddingItem,
    handleBuy,
    handleUnbuy,
    handleDeleteItem,
    handleComplete,
    handleRegenerate,
    handleAddItem,
  } = useShoppingListDetail(listId)

  if (isLoading) {
    return (
      <section className="page">
        <p style={{ color: 'var(--color-text-muted)' }}>กำลังโหลด…</p>
      </section>
    )
  }

  if (error || !data) {
    return (
      <section className="page">
        <Link to="/shopping" style={{ fontSize: 14 }}>← Shopping Lists</Link>
        <div className="error-banner" style={{ marginTop: 12 }}>โหลดรายการไม่สำเร็จ</div>
      </section>
    )
  }

  const unboughtItems = data.items.filter((i) => !i.isBought)
  const boughtItems = data.items.filter((i) => i.isBought)

  const hasMealPlanSource = data.items.some(
    (i) => i.sourceMealPlanEntryIds != null && i.sourceMealPlanEntryIds.length > 0,
  )

  const isActive = data.status === 'Active'

  const percent =
    data.totalCount > 0
      ? Math.round((data.boughtCount / data.totalCount) * 100)
      : 0

  const isBusy = isBuying || isUnbuying || isDeletingItem || isCompleting || isRegenerating

  /* ---------- Column templates for unbought items ---------- */

  const UnboughtCheckboxTemplate = ({ data: item }: ColumnTemplateProps<ShoppingListItemDto>) => (
    <input
      type="checkbox"
      checked={false}
      onChange={() => handleBuy(item.id)}
      aria-label={`ซื้อ ${item.ingredientName}`}
    />
  )

  const UnboughtNameTemplate = ({ data: item }: ColumnTemplateProps<ShoppingListItemDto>) => {
    const hasSource =
      item.sourceMealPlanEntryIds != null && item.sourceMealPlanEntryIds.length > 0
    return (
      <span style={{ fontWeight: 500 }}>
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
      </span>
    )
  }

  const UnboughtQtyTemplate = ({ data: item }: ColumnTemplateProps<ShoppingListItemDto>) => (
    <span style={{ fontSize: 13 }}>
      {item.quantity} {item.unit}
    </span>
  )

  const UnboughtActionTemplate = ({ data: item }: ColumnTemplateProps<ShoppingListItemDto>) => (
    <div style={{ textAlign: 'right' }}>
      <Button
        type="button"
        size={Size.Small}
        variant={Variant.Outlined}
        color={Color.Error}
        onClick={() => handleDeleteItem(item.id, item.ingredientName)}
        aria-label="ลบ"
      >
        🗑
      </Button>
    </div>
  )

  /* ---------- Column templates for bought items ---------- */

  const BoughtCheckboxTemplate = ({ data: item }: ColumnTemplateProps<ShoppingListItemDto>) => (
    <input type="checkbox" checked disabled aria-label={`${item.ingredientName} (ซื้อแล้ว)`} />
  )

  const BoughtNameTemplate = ({ data: item }: ColumnTemplateProps<ShoppingListItemDto>) => (
    <span style={{ textDecoration: 'line-through', color: 'var(--color-text-muted)' }}>
      {item.ingredientName}
    </span>
  )

  const BoughtQtyTemplate = ({ data: item }: ColumnTemplateProps<ShoppingListItemDto>) => (
    <span style={{ fontSize: 13, color: 'var(--color-text-muted)' }}>
      {item.quantity} {item.unit}
    </span>
  )

  const BoughtTimeTemplate = ({ data: item }: ColumnTemplateProps<ShoppingListItemDto>) => {
    const boughtTime = item.boughtAt
      ? new Date(item.boughtAt).toLocaleTimeString('th-TH', {
          hour: '2-digit',
          minute: '2-digit',
        })
      : null
    return (
      <span style={{ fontSize: 12, color: 'var(--color-text-muted)' }}>
        {boughtTime && <>ซื้อ {boughtTime}</>}
      </span>
    )
  }

  const BoughtActionTemplate = ({ data: item }: ColumnTemplateProps<ShoppingListItemDto>) => (
    <div style={{ textAlign: 'right' }}>
      <Button
        type="button"
        size={Size.Small}
        variant={Variant.Outlined}
        color={Color.Secondary}
        onClick={() => handleUnbuy(item.id)}
        aria-label="ยกเลิกซื้อ"
      >
        ↩
      </Button>
    </div>
  )

  return (
    <section className="page page--shopping-detail">
      <header className="page__header">
        <div>
          <Link to="/shopping" style={{ fontSize: 14 }}>← Shopping Lists</Link>
          <h1 style={{ margin: '4px 0 0' }}>{data.name}</h1>
        </div>

        {isActive && (
          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
            {hasMealPlanSource && (
              <Button
                type="button"
                variant={Variant.Outlined}
                color={Color.Primary}
                onClick={handleRegenerate}
                disabled={isRegenerating || isBusy}
              >
                🔄 คำนวณใหม่
              </Button>
            )}
            <Button
              type="button"
              variant={Variant.Filled}
              color={Color.Primary}
              onClick={handleComplete}
              disabled={isCompleting || isBusy}
            >
              ✓ เสร็จสิ้น
            </Button>
          </div>
        )}
      </header>

      {errorMessage && <div className="error-banner">{errorMessage}</div>}

      {/* Progress */}
      <div style={{ marginBottom: 16 }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 14, marginBottom: 6 }}>
          <span style={{ color: 'var(--color-text-muted)' }}>
            {data.boughtCount} / {data.totalCount} ซื้อแล้ว
          </span>
          <span style={{ color: 'var(--color-text-muted)' }}>{percent}%</span>
        </div>
        <div className="progress-bar">
          <div className="progress-bar__fill" style={{ width: `${percent}%` }} />
        </div>
      </div>

      {/* Unbought section */}
      {unboughtItems.length > 0 && (
        <div style={{ marginBottom: 24 }}>
          <h2 style={{ fontSize: 15, marginBottom: 8, color: 'var(--color-text-muted)' }}>
            ยังไม่ได้ซื้อ ({unboughtItems.length})
          </h2>
          <Grid dataSource={unboughtItems as ShoppingListItemDto[]} height="auto">
            <Columns>
              <Column headerText="" width={36} template={UnboughtCheckboxTemplate} />
              <Column field="ingredientName" headerText="วัตถุดิบ" template={UnboughtNameTemplate} />
              <Column headerText="จำนวน" width={120} template={UnboughtQtyTemplate} />
              <Column headerText="" width={100} />
              <Column headerText="" width={60} template={UnboughtActionTemplate} />
            </Columns>
          </Grid>
        </div>
      )}

      {/* Bought section */}
      {boughtItems.length > 0 && (
        <div style={{ marginBottom: 24 }}>
          <h2 style={{ fontSize: 15, marginBottom: 8, color: 'var(--color-text-muted)' }}>
            ซื้อแล้ว ({boughtItems.length})
          </h2>
          <Grid dataSource={boughtItems as ShoppingListItemDto[]} height="auto">
            <Columns>
              <Column headerText="" width={36} template={BoughtCheckboxTemplate} />
              <Column field="ingredientName" headerText="วัตถุดิบ" template={BoughtNameTemplate} />
              <Column headerText="จำนวน" width={120} template={BoughtQtyTemplate} />
              <Column headerText="เวลา" width={100} template={BoughtTimeTemplate} />
              <Column headerText="" width={60} template={BoughtActionTemplate} />
            </Columns>
          </Grid>
        </div>
      )}

      {unboughtItems.length === 0 && boughtItems.length === 0 && (
        <p style={{ textAlign: 'center', padding: 32, color: 'var(--color-text-muted)' }}>
          ยังไม่มีรายการ — เพิ่มด้านล่างได้เลย
        </p>
      )}

      {/* Add item form — only for active lists */}
      {isActive && (
        <AddItemForm
          existingItems={data.items}
          onAdd={handleAddItem}
          isAdding={isAddingItem}
        />
      )}
    </section>
  )
}
