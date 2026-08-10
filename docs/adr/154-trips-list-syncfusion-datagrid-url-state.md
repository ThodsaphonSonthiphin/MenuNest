# 154. Trips List uses Syncfusion DataGrid with Explicit URL State

Date: 2026-08-10

## Status

Accepted

## Context

The "Trips" page needs to display a list of trips with server-side pagination (10 items per page), sorting (by date or name), and filtering (by trip name or location). A key requirement is that the view state must be kept in the URL so that users can bookmark or share the link and land exactly on the same view.

The project already heavily uses Syncfusion components. We needed to decide whether to use the full-featured Syncfusion DataGrid or build a custom list view using standard Syncfusion inputs (e.g. TextBox, DropDown, Pager). Furthermore, if using the DataGrid, we had to decide how to encode its state into the URL: either through explicit query parameters (e.g., `?page=1&sort=date_desc`) or by dumping the Syncfusion serialized state (e.g., a Base64-encoded `DataManagerRequest`).

## Decision

We will use the **Syncfusion DataGrid** coupled with **React Router's URL search parameters using explicit mapping**.

1. **Component**: `Syncfusion DataGrid` provides built-in UI for pagination, sorting, and filtering headers. We will use a `CustomAdaptor` to manage the server-side data fetching.
2. **URL State Structure**: We will use **Explicit Standard Parameters** (e.g., `?page=1&sort=date_desc&filterName=foo`).

## Rationale

- **Syncfusion DataGrid** saves us from reinventing complex UI components like multi-column sort headers and pagers.
- **Explicit URL Parameters** are readable, SEO-friendly, and most importantly, not tightly coupled to Syncfusion's internal data structures. This prevents vendor lock-in and protects bookmarked URLs from breaking if Syncfusion changes its internal state serialization in future versions.
- The cost of writing explicit mapping logic (intercepting Grid events to update the URL, and parsing URL parameters to feed the Grid's initial state) is accepted as worthwhile technical debt compared to the fragility and unreadability of serialized state.

## Consequences

- We must intercept the Grid's `actionBegin` events to prevent its default data binding and instead push the new state to the URL.
- A `useEffect` hook will listen to URL changes to trigger the DataGrid's data fetch with the mapped parameters.
- We must manually maintain the mapping logic between URL query parameters and the `DataManagerRequest` shape.
