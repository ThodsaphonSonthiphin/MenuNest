import { useCallback } from 'react'
import { Link, useParams } from 'react-router-dom'
import { Button, Color, Size, Variant } from '@syncfusion/react-buttons'
import { Grid, Column, Columns } from '@syncfusion/react-grid'
import type { ColumnTemplateProps } from '@syncfusion/react-grid'
import {
  useGetShoppingListDetailQuery,
  useListIngredientsQuery,
  api,
} from '../../shared/api/api'
import type { ShoppingListItemDto, ShoppingListDetailDto } from '../../shared/api/api'
import { useShoppingListDetail } from './hooks/useShoppingListDetail'
import { useAuthDataManager } from '../../shared/data/useAuthDataManager'
import { useAppDispatch } from '../../store'

export function ShoppingListDetailPage() {
  const { id } = useParams<{ id: string }>()
  const listId = id!
  const dispatch = useAppDispatch()

  const { data, isLoading, error } = useGetShoppingListDetailQuery(listId)
  const { data: allIngredients } = useListIngredientsQuery()

  const {
    errorMessage,
    isBuying,
    isUnbuying,
    isCompleting,
    isRegenerating,
    handleBuy,
    handleUnbuy,
    handleComplete,
    handleRegenerate,
  } = useShoppingListDetail(listId)

  // DataManager for unbought-items grid CRUD (Add / Delete).
  // GET  → /api/shopping-lists/{id}  (detail endpoint, then extract unbought items)
  // POST → /api/shopping-lists/{id}/items
  // DEL  → /api/shopping-lists/{id}/items/{itemId}
  const transformResponse = useCallback(
    (raw: unknown) => {
      const detail = raw as ShoppingListDetailDto
      if (detail?.items) {
        return detail.items.filter((i) => !i.isBought)
      }
      // If the response is already an array (e.g. after insert/delete) pass through
      return raw
    },
    [],
  )

  const dm = useAuthDataManager({
    url: `/api/shopping-lists/${listId}/items`,
    readUrl: `/api/shopping-lists/${listId}`,
    transformResponse,
  })

  // After the Grid completes an Add/Delete via DataManager, invalidate RTK
  // Query cache so counts, bought/unbought split, and other pages stay in sync.
  const handleDataChangeComplete = useCallback(() => {
    dispatch(api.util.invalidateTags([
      { type: 'ShoppingListDetail', id: listId },
      { type: 'ShoppingLists', id: 'LIST' },
    ]))
  }, [dispatch, listId])

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

  const existingIngredientIds = new Set(data.items.map((i) => i.ingredientId))
  const availableIngredients = (allIngredients ?? []).filter(
    (i) => !existingIngredientIds.has(i.id),
  )

  const hasMealPlanSource = data.items.some(
    (i) => i.sourceMealPlanEntryIds != null && i.sourceMealPlanEntryIds.length > 0,
  )

  const isActive = data.status === 'Active'

  const percent =
    data.totalCount > 0
      ? Math.round((data.boughtCount / data.totalCount) * 100)
      : 0

  const isBusy = isBuying || isUnbuying || isCompleting || isRegenerating

  /* ---------- Column templates for unbought items ---------- */

  const UnboughtNameTemplate = ({ data: item }: ColumnTemplateProps<ShoppingListItemDto>) => {
    const hasSource =
      item.sourceMealPlanEntryIds != null && item.sourceMealPlanEntryIds.length > 0
    return (
      <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
        <input
          type="checkbox"
          checked={false}
          onChange={() => handleBuy(item.id)}
          aria-label={`ซื้อ ${item.ingredientName}`}
        />
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
      </div>
    )
  }

  const UnboughtQtyTemplate = ({ data: item }: ColumnTemplateProps<ShoppingListItemDto>) => (
    <span style={{ fontSize: 13 }}>
      {item.quantity} {item.unit}
    </span>
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

      {/* Unbought section — uses DataManager for Add/Delete when active */}
      <div style={{ marginBottom: 24 }}>
        {unboughtItems.length > 0 && (
          <h2 style={{ fontSize: 15, marginBottom: 8, color: 'var(--color-text-muted)' }}>
            ยังไม่ได้ซื้อ ({unboughtItems.length})
          </h2>
        )}
        {isActive && dm ? (
          <Grid
            dataSource={dm}
            toolbar={['Add', 'Delete', 'Update', 'Cancel']}
            editSettings={{
              allowAdd: true,
              allowEdit: false,
              allowDelete: true,
              mode: 'Normal',
              confirmOnDelete: true,
            }}
            onDataChangeComplete={handleDataChangeComplete}
            height="auto"
          >
            <Columns>
              <Column field="id" isPrimaryKey visible={false} />
              <Column
                field="ingredientId"
                headerText="วัตถุดิบ"
                template={UnboughtNameTemplate}
                edit={{
                  type: 'DropDownEdit',
                  params: {
                    dataSource: availableIngredients,
                    fields: { text: 'name', value: 'id' },
                    placeholder: 'เลือกวัตถุดิบ',
                  },
                }}
                validationRules={{ required: true }}
              />
              <Column
                field="quantity"
                headerText="จำนวน"
                width={160}
                template={UnboughtQtyTemplate}
                edit={{
                  type: 'NumericEdit',
                  params: { min: 1, decimals: 2 },
                }}
                validationRules={{ required: true }}
              />
            </Columns>
          </Grid>
        ) : isActive && !dm ? (
          <p style={{ color: 'var(--color-text-muted)' }}>Loading...</p>
        ) : (
          unboughtItems.length > 0 && (
            <Grid dataSource={unboughtItems as ShoppingListItemDto[]} height="auto">
              <Columns>
                <Column field="ingredientName" headerText="วัตถุดิบ" template={UnboughtNameTemplate} />
                <Column field="quantity" headerText="จำนวน" width={160} template={UnboughtQtyTemplate} />
              </Columns>
            </Grid>
          )
        )}
      </div>

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
          ยังไม่มีรายการ — คลิก + Add ในตารางด้านบนเพื่อเพิ่มรายการ
        </p>
      )}
    </section>
  )
}
