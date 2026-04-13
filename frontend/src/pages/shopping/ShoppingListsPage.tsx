import { useListShoppingListsQuery } from '../../shared/api/api'

export function ShoppingListsPage() {
  const { data, isLoading, error } = useListShoppingListsQuery()

  return (
    <section className="page page--shopping">
      <header className="page__header">
        <h1>Shopping Lists</h1>
        <button type="button" className="btn btn--primary">+ New list</button>
      </header>

      {isLoading && <p>Loading…</p>}
      {error && <p>Failed to load shopping lists.</p>}
      {data && data.length === 0 && <p>No shopping lists yet — create your first.</p>}

      {data && data.length > 0 && (
        <ul className="shopping-list">
          {data.map((list) => (
            <li key={list.id}>
              <strong>{list.name}</strong>
              <span>
                {list.boughtCount} / {list.itemCount} bought
              </span>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
