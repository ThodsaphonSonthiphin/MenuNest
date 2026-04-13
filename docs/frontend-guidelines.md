# MenuNest — Frontend Guidelines

Operational rules for anyone (or any AI agent) writing frontend code in this
repo. Keep this short — when in doubt, the rules below override "what feels
idiomatic in React" because they reflect concrete decisions taken by the
project owner.

## 1. Stack

- **React 19** (`react@^19.2`) + **TypeScript** + **Vite 8**.
- **Redux Toolkit** for state, with **one** `createApi` instance for the whole
  app at `src/shared/api/api.ts`. **Do not** call `injectEndpoints` from feature
  folders — every endpoint definition lives in that single file, organised by
  `// --- Section ---` comment blocks.
- **React Router v7** with `createBrowserRouter`.
- **MSAL.js** (`@azure/msal-react`) for Entra ID authentication.

## 2. UI components — Syncfusion first

For any control you'd otherwise hand-roll, **reach for the corresponding
Syncfusion `@syncfusion/ej2-react-*` component first** and adapt it. The
Community License is paid for and these components give us accessibility,
keyboard navigation, theming, and edit-in-place for free.

| UI need | Component |
|---|---|
| Editable tables / lists | `GridComponent` (`@syncfusion/ej2-react-grids`) |
| Calendar / weekly planner | `ScheduleComponent` (`@syncfusion/ej2-react-schedule`) |
| Modal dialogs | `DialogComponent` (`@syncfusion/ej2-react-popups`) |
| Toasts | `ToastComponent` (`@syncfusion/ej2-react-notifications`) |
| Autocomplete / combobox | `AutoCompleteComponent`, `ComboBoxComponent` (`@syncfusion/ej2-react-dropdowns`) |
| Tabs / nav | `@syncfusion/ej2-react-navigations` |

**Disable built-in editors when needed.** Syncfusion components ship with
default popups and inline editors (Schedule's "quick info", Grid's CRUD
dialog, etc.). When our domain flow doesn't match — e.g. the cook action on
a meal plan entry needs its own confirm dialog — turn the built-ins off
(`showQuickInfo={false}`) and cancel events with `args.cancel = true` in
`popupOpen` / `actionBegin`. Then drive the mutation through your own
`DialogComponent` + RTK Query.

**Custom HTML stays for layout only.** `<section>`, `<header>`, the page CSS
grid — fine. Never re-implement table/dialog/dropdown primitives.

## 3. Forms — react-hook-form, per-field validation

Every form uses `react-hook-form` v7 with field-level required messages.
Generic "Something went wrong" banners are **not** acceptable for missing
fields.

```tsx
const { register, handleSubmit, formState: { errors } } = useForm<Shape>()

<input
  aria-invalid={errors.name ? 'true' : 'false'}
  {...register('name', { required: 'กรุณากรอกชื่อ', maxLength: { value: 120, message: 'ยาวเกิน 120 ตัวอักษร' } })}
/>
{errors.name && <p className="field-error">{errors.name.message}</p>}
```

Mark required inputs with `<span className="field-required">*</span>` next to
the label. Set `noValidate` on the `<form>` so the browser's native tooltip
doesn't fight RHF.

For dynamic line lists (e.g. recipe ingredients), use `useFieldArray` — do
not maintain a parallel `useState<Line[]>`.

**Bridging Syncfusion to RHF.** Syncfusion components are controlled —
register them through RHF's `Controller`:

```tsx
<Controller
  control={control}
  name="ingredientId"
  rules={{ required: 'กรุณาเลือกวัตถุดิบ' }}
  render={({ field, fieldState }) => (
    <>
      <DropDownListComponent
        dataSource={ingredients}
        fields={{ text: 'name', value: 'id' }}
        value={field.value}
        change={(e) => field.onChange(e.value)}
      />
      {fieldState.error && <p className="field-error">{fieldState.error.message}</p>}
    </>
  )}
/>
```

## 4. Folder convention

```
src/
  pages/{feature}/
    components/
    hooks/
    {feature}Slice.ts        -- local UI state only (filters, dialog open flags)
    {Feature}Page.tsx        -- container
    index.ts                 -- barrel exports
  shared/
    api/api.ts               -- single RTK Query instance, all endpoints
    auth/                    -- MSAL config + helpers
    components/              -- AppLayout, NavBar, ProtectedRoute, FamilyRequiredRoute
    hooks/                   -- useCurrentUser, useDebounce, etc.
    utils/
  store/index.ts             -- configureStore with the api reducer + each feature slice
  router.tsx
```

Each page **must** keep server state in `shared/api/api.ts`. The slice is
only for UI state (filter strings, "is this dialog open", focused entry id).

## 5. Languages

- Code, comments, commit messages, this doc, README → **English**.
- User-visible UI strings (labels, buttons, error messages shown to end
  users) → **Thai** (since the app targets Thai families). Backend domain
  validators throw English `DomainException` messages — that's OK, they go
  through the ProblemDetails middleware and the SPA shows them as-is, but
  the form's required-field messages are written in Thai because they live
  in the SPA.

## 6. Error handling pattern

Network/domain errors come back from RTK Query as `FetchBaseQueryError`
with a `data` payload that the backend's `ExceptionHandlingMiddleware`
formats as RFC 7807 `ProblemDetails`:

```jsonc
// 400 — DomainException
{ "title": "Request rejected", "detail": "...", "status": 400 }
// 400 — FluentValidation
{ "title": "...", "errors": { "Name": ["..."] }, "status": 400 }
```

Use this helper to surface a human-readable message in any page:

```ts
function getErrorMessage(err: unknown): string {
  if (typeof err === 'object' && err !== null && 'data' in err) {
    const data = (err as { data?: { detail?: string; title?: string; errors?: Record<string, string[]> } }).data
    if (data?.errors) return Object.values(data.errors)[0]?.[0] ?? 'Validation failed.'
    if (data?.detail) return data.detail
    if (data?.title) return data.title
  }
  return 'Something went wrong. Please try again.'
}
```

## 7. Auth / route guards

Two layered guards under `<MsalProvider>`:

1. `ProtectedRoute` — waits for MSAL `inProgress === None` then requires an
   account; otherwise navigates to `/login`. Critical: don't redirect during
   `inProgress !== None` or you'll bounce authenticated users back to login
   on the first render.
2. `FamilyRequiredRoute` — waits for `useGetMeQuery` to resolve, then
   requires `familyId`; otherwise navigates to `/join-family`.

`useCurrentUser` is the single source of truth — it merges MSAL account
claims with the backend `/api/me` reply. Don't read MSAL accounts directly
from page components.
