import { useListStockQuery } from '../../shared/api/api'

export function StockPage() {
  const { data, isLoading, error } = useListStockQuery()

  return (
    <section className="page page--stock">
      <header className="page__header">
        <h1>Stock</h1>
        <button type="button" className="btn btn--primary">+ Add ingredient</button>
      </header>

      {isLoading && <p>Loading…</p>}
      {error && <p>Failed to load stock.</p>}
      {data && data.length === 0 && <p>No stock yet — add your first ingredient.</p>}

      {data && data.length > 0 && (
        <table className="stock-table">
          <thead>
            <tr>
              <th>Ingredient</th>
              <th>On hand</th>
              <th>Last updated</th>
            </tr>
          </thead>
          <tbody>
            {data.map((item) => (
              <tr key={item.id}>
                <td>{item.ingredientName}</td>
                <td>{item.quantity} {item.unit}</td>
                <td>{new Date(item.updatedAt).toLocaleString()}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  )
}
