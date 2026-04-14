import { useCallback, useState } from 'react'
import { Grid, Column, Columns } from '@syncfusion/react-grid'
import type { SaveEvent, DeleteEvent } from '@syncfusion/react-grid'
import {
  useListIngredientsQuery,
  useCreateIngredientMutation,
  useUpdateIngredientMutation,
  useDeleteIngredientMutation,
} from '../../shared/api/api'
import type { IngredientDto } from '../../shared/api/api'

export function IngredientsPage() {
  const { data, isLoading, error } = useListIngredientsQuery()
  const [createIngredient] = useCreateIngredientMutation()
  const [updateIngredient] = useUpdateIngredientMutation()
  const [deleteIngredient] = useDeleteIngredientMutation()

  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const handleDataChangeStart = useCallback(
    async (e: SaveEvent<IngredientDto> | DeleteEvent<IngredientDto>) => {
      // Let RTK Query cache handle data refresh via tag invalidation;
      // cancel the Grid's local dataSource mutation.
      e.cancel = true
      setErrorMessage(null)

      try {
        if (e.action === 'Add') {
          const row = (e as SaveEvent<IngredientDto>).data
          await createIngredient({ name: row.name.trim(), unit: row.unit.trim() }).unwrap()
        } else if (e.action === 'Edit') {
          const row = (e as SaveEvent<IngredientDto>).data
          await updateIngredient({
            id: row.id,
            name: row.name.trim(),
            unit: row.unit.trim(),
          }).unwrap()
        } else if (e.action === 'Delete') {
          const rows = (e as DeleteEvent<IngredientDto>).data
          await deleteIngredient(rows[0].id).unwrap()
        }
      } catch (err) {
        setErrorMessage(getErrorMessage(err))
      }
    },
    [createIngredient, updateIngredient, deleteIngredient],
  )

  return (
    <section className="page page--ingredients">
      <header className="page__header">
        <h1>Ingredients</h1>
      </header>

      <p style={{ color: 'var(--color-text-muted)', marginBottom: 16 }}>
        Master list นี้ใช้เป็น autocomplete เวลาสร้าง recipe / เพิ่ม stock — 1 ingredient = 1 หน่วย
      </p>

      {errorMessage && <div className="error-banner">{errorMessage}</div>}

      {isLoading && <p>Loading...</p>}
      {error && !isLoading && <p>Failed to load ingredients.</p>}

      {data && (
        <Grid
          dataSource={data as IngredientDto[]}
          toolbar={['Add', 'Edit', 'Delete', 'Update', 'Cancel']}
          editSettings={{
            allowAdd: true,
            allowEdit: true,
            allowDelete: true,
            mode: 'Normal',
            confirmOnDelete: true,
          }}
          onDataChangeStart={handleDataChangeStart}
          height="auto"
        >
          <Columns>
            <Column field="id" isPrimaryKey visible={false} />
            <Column
              field="name"
              headerText="ชื่อ"
              validationRules={{ required: true, maxLength: 120 }}
            />
            <Column
              field="unit"
              headerText="หน่วย"
              validationRules={{ required: true, maxLength: 40 }}
            />
          </Columns>
        </Grid>
      )}
    </section>
  )
}

function getErrorMessage(err: unknown): string {
  if (typeof err === 'object' && err !== null && 'data' in err) {
    const data = (err as { data?: { detail?: string; title?: string; errors?: Record<string, string[]> } }).data
    if (data?.errors) {
      const first = Object.values(data.errors)[0]?.[0]
      if (first) return first
    }
    if (data?.detail) return data.detail
    if (data?.title) return data.title
  }
  return 'Something went wrong. Please try again.'
}
