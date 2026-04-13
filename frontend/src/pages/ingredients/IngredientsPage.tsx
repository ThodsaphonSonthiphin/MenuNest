import { useListIngredientsQuery } from '../../shared/api/api'

export function IngredientsPage() {
  const { data, isLoading, error } = useListIngredientsQuery()

  return (
    <section className="page page--ingredients">
      <header className="page__header">
        <h1>Ingredients</h1>
        <p>
          Master list powering autocomplete across recipes and stock — one
          ingredient per row, one fixed unit per ingredient.
        </p>
      </header>

      {isLoading && <p>Loading…</p>}
      {error && <p>Failed to load ingredients.</p>}
      {data && data.length === 0 && <p>No ingredients yet — start adding.</p>}

      {data && data.length > 0 && (
        <table className="ingredients-table">
          <thead>
            <tr>
              <th>Name</th>
              <th>Unit</th>
            </tr>
          </thead>
          <tbody>
            {data.map((ingredient) => (
              <tr key={ingredient.id}>
                <td>{ingredient.name}</td>
                <td>{ingredient.unit}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  )
}
