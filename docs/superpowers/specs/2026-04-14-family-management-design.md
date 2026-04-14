# Family Management — Design Spec

## Goal

Complete the remaining Family Management features: join via invite code
(with QR code support), member list, relationship management, invite code
rotation, and leave family. All UI uses Syncfusion components — never
hand-rolled.

## Scope

| Feature | Status before | After |
|---------|--------------|-------|
| Create family | Done | No change |
| Join via invite code | Stubbed (disabled input) | Fully functional + QR scan |
| List members | Not started | Read-only DataGrid on FamilyPage |
| Relationships CRUD | Entity exists, no handlers | Full CRUD with DataGrid + Dialog |
| Rotate invite code | Not started | Button on FamilyPage |
| Leave family | Not started | Danger zone button on FamilyPage |

## API Endpoints (new)

| Method | Route | Purpose | Request | Response |
|--------|-------|---------|---------|----------|
| POST | `/api/families/join` | Join via invite code | `{ inviteCode: string }` | `FamilyDto` |
| GET | `/api/families/me/members` | List members + relationships | — | `FamilyMemberDto[]` |
| POST | `/api/families/me/invite-codes/rotate` | Regenerate invite code | — | `{ inviteCode: string }` |
| POST | `/api/families/leave` | Leave current family | — | 204 |
| POST | `/api/families/me/relationships` | Add relationship | `{ fromUserId, toUserId, relationType }` | `RelationshipDto` |
| DELETE | `/api/families/me/relationships/{id}` | Delete relationship | — | 204 |

### DTOs

```csharp
// Returned by GET /api/families/me/members
public sealed record FamilyMemberDto(
    Guid UserId,
    string DisplayName,
    string Email,
    DateTime JoinedAt,
    bool IsCreator,
    RelationshipLabelDto[] Relationships);

public sealed record RelationshipLabelDto(
    Guid RelationshipId,
    string RelationType,    // "Parent", "Child", "Spouse", "Sibling", "Other"
    string Label);          // Thai display: "พ่อ/แม่", "ลูก", "คู่สมรส", "พี่น้อง", "อื่นๆ"

// Returned by GET /api/families/me/relationships (flat list)
public sealed record RelationshipDto(
    Guid Id,
    Guid FromUserId,
    string FromUserName,
    Guid ToUserId,
    string ToUserName,
    string RelationType);   // enum name
```

### Validation rules

- **Join**: invite code must match `XXXX-XXXX` format (Crockford alphabet).
  User must not already belong to a family.
- **Leave**: user must belong to a family. `User.LeaveFamily()` nulls
  `FamilyId`. If the user is the last member, the family remains in the DB
  (orphaned but harmless — no cleanup needed in MVP).
- **Relationships**: `fromUserId` and `toUserId` must be different, both
  must belong to the same family. Duplicate `(from, to, type)` rejected by
  unique constraint.
- **Rotate**: only allowed for family members (any member, no role check in
  MVP).

## Frontend — Pages

### FamilyPage (`/family`)

Stacked sections layout (single scrollable page). All controls are
Syncfusion.

**Section 1 — Invite Code**

- Display current invite code in large monospace text.
- Buttons: "คัดลอก" (copy to clipboard), "สร้างรหัสใหม่" (rotate with
  confirm dialog).
- QR code on the right using `QRCodeGeneratorComponent` from
  `@syncfusion/ej2-react-barcode-generator` (ej2 fallback — Pure React not
  yet available). Encodes URL: `${origin}/join?code=${inviteCode}`.
- QR customization:
  - `foreColor: '#E65100'` (brand primary dark)
  - `backgroundColor: 'transparent'`
  - `errorCorrectionLevel: 30` (High — required for logo)
  - `logo`: MenuNest "M" SVG badge (orange circle + white "M"), 30x30px
  - `mode: 'SVG'` (crisp at any zoom)
  - `margin: { left: 2, right: 2, top: 2, bottom: 2 }`
  - `displayText: { visibility: false }`
- QR regenerates when invite code changes (rotate).

**Section 2 — Members**

- Read-only Syncfusion `Grid` (`@syncfusion/react-grid`).
- Columns: ชื่อ (avatar + name template), อีเมล, ความสัมพันธ์ (badge
  template from relationship data), เข้าร่วม (date).
- No toolbar (read-only — members join/leave via invite code, not manual
  add).
- Data source: `useListFamilyMembersQuery()` RTK Query hook.

**Section 3 — Relationships**

- Syncfusion `Grid` with toolbar `['Add', 'Delete']`.
- Columns: จาก (fromUserName), ความสัมพันธ์ (badge template), ถึง
  (toUserName).
- **Add**: toolbar Add button opens a Syncfusion `Dialog`
  (`@syncfusion/react-popups`) containing a `react-hook-form` form with 3
  Syncfusion `DropDownList` fields:
  1. จากสมาชิก — member list dropdown
  2. เป็น — relation type dropdown (Parent/Child/Spouse/Sibling/Other with
     Thai labels)
  3. ของสมาชิก — member list dropdown (filtered to exclude selected "from"
     member)
- **Delete**: select row + toolbar Delete with confirm.
- Data source: `useListRelationshipsQuery()` + mutations via RTK Query.
- **No inline editing** — relationships are add/delete only (no update in
  MVP; if needed, delete + re-add).

**Section 4 — Danger Zone**

- Red-tinted card with "ออกจากครอบครัว" button.
- Click opens Syncfusion `Dialog` confirm: "คุณจะไม่เห็น recipe, stock,
  meal plan, และ shopping list ของครอบครัวนี้อีกต่อไป — ต้องการออก?"
- On confirm: `useLeaveFamilyMutation()` → invalidate `'Me'` tag → redirect
  to `/join-family`.

### JoinFamilyPage (`/join-family`) — updates

- Enable the existing invite code `TextBox` input (`@syncfusion/react-inputs`)
  and "เข้าร่วม" `Button`.
- Read `?code=` query param from URL (from QR scan) → pre-fill the input.
- On submit: `useJoinFamilyMutation({ inviteCode })` → invalidate `'Me'`
  tag → redirect to `/`.
- Error handling: invalid code → inline error message below the input
  (Thai: "รหัสเชิญไม่ถูกต้องหรือหมดอายุ").

## RTK Query Endpoints (new)

All added to the single `shared/api/api.ts` file:

```typescript
// --- Family ---
joinFamily: build.mutation<FamilyDto, { inviteCode: string }>({
  query: (body) => ({ url: '/api/families/join', method: 'POST', body }),
  invalidatesTags: ['Me', 'Family'],
}),
listFamilyMembers: build.query<FamilyMemberDto[], void>({
  query: () => '/api/families/me/members',
  providesTags: ['FamilyMembers'],
}),
rotateInviteCode: build.mutation<{ inviteCode: string }, void>({
  query: () => ({ url: '/api/families/me/invite-codes/rotate', method: 'POST' }),
  invalidatesTags: ['Me'],
}),
leaveFamily: build.mutation<void, void>({
  query: () => ({ url: '/api/families/leave', method: 'POST' }),
  invalidatesTags: ['Me', 'Family', 'FamilyMembers'],
}),

// --- Relationships ---
listRelationships: build.query<RelationshipDto[], void>({
  query: () => '/api/families/me/relationships',
  providesTags: ['Relationships'],
}),
addRelationship: build.mutation<RelationshipDto, AddRelationshipRequest>({
  query: (body) => ({ url: '/api/families/me/relationships', method: 'POST', body }),
  invalidatesTags: ['Relationships', 'FamilyMembers'],
}),
deleteRelationship: build.mutation<void, string>({
  query: (id) => ({ url: `/api/families/me/relationships/${id}`, method: 'DELETE' }),
  invalidatesTags: ['Relationships', 'FamilyMembers'],
}),
```

## Backend Handlers (new)

| Handler | Command/Query | Key logic |
|---------|--------------|-----------|
| `JoinFamilyHandler` | `JoinFamilyCommand { InviteCode }` | Lookup family by InviteCode → `user.JoinFamily(family.Id)` → save |
| `ListFamilyMembersHandler` | `ListFamilyMembersQuery` | Query users by FamilyId, include relationships, map to DTOs with Thai labels |
| `RotateInviteCodeHandler` | `RotateInviteCodeCommand` | `family.RotateInviteCode()` → save → return new code |
| `LeaveFamilyHandler` | `LeaveFamilyCommand` | `user.LeaveFamily()` → save. Cascade-delete user's relationships in this family |
| `AddRelationshipHandler` | `AddRelationshipCommand { FromUserId, ToUserId, RelationType }` | Validate both users in same family → `UserRelationship.Create()` → save |
| `DeleteRelationshipHandler` | `DeleteRelationshipCommand { Id }` | Lookup → verify same family → remove |

## Syncfusion Components Used

| Component | Package | Tier |
|-----------|---------|------|
| `Grid`, `Column`, `Columns` | `@syncfusion/react-grid` | Pure React |
| `Dialog` | `@syncfusion/react-popups` | Pure React |
| `DropDownList` | `@syncfusion/react-dropdowns` | Pure React |
| `Button` | `@syncfusion/react-buttons` | Pure React |
| `TextBox` | `@syncfusion/react-inputs` | Pure React |
| `QRCodeGeneratorComponent` | `@syncfusion/ej2-react-barcode-generator` | ej2 fallback |

## Out of Scope

- Family name editing (can add later)
- Role-based permissions (MVP: all members equal)
- Relationship update/edit (delete + re-add)
- Real-time member list updates (polling via RTK Query refetch is sufficient)

## Mockup

See `frontend/mockup-family.html` — open in browser to view the full
interactive mockup with Syncfusion QR code rendering.
