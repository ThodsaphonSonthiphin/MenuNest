# Decision map - shortcut rail with undo/redo on the budget page (#106)

```mermaid
graph TD
    MAP["map (this file)"] --> T["tickets/*.md — one decision each"]
    T --> D["Decisions so far (index below)"]
```

## Destination
The budget page carries a shortcut rail with working undo and redo, shipped to prod and matching a mock the user approved, built so trips and meal-plan can adopt the same rail later.

## Notes

<!-- decision-map:notes:start -->
- Tracking issue: https://github.com/ThodsaphonSonthiphin/MenuNest/issues/106 - every commit references it per CLAUDE.md, and new ADRs are named menunest-<number>-<slug>.md.
- The issue body is one annotated phone screenshot: a red sketch of ~3 stacked buttons down the RIGHT edge of /budget, beside the still-to-place card and the quick-assign chips. The title asks for shortcut buttons 'such as undo redo' and for a design.
- Settled at chart time and binding on every ticket: undo AND redo both; on-screen rail AND desktop Ctrl+Z / Ctrl+Shift+Z; budget page first but architected so other pages can adopt it; the destination is prod, not a design doc.
- There is NO undo infrastructure today - no audit, event or history entity anywhere in MenuNest.Domain/Entities. The only 'undo' is TransactionUndoToast.tsx, a 5-second delay-the-delete toast used on AccountDetailPage; it withholds the write rather than reversing a committed one.
- Already installed, so a library decision does not have to mean a new dependency: @syncfusion/ej2-buttons 33.1.49 ships Fab and SpeedDial (with RadialSettings), and @dnd-kit/core+modifiers+sortable+utilities is already used for drag-reorder in frontend/src/pages/trips.
- Gap to check before committing to Syncfusion: @syncfusion/react-buttons exposes a React floating-action-button but NO React SpeedDial wrapper, so a SpeedDial means mounting the vanilla EJ2 class by hand or adding @syncfusion/ej2-react-buttons.
- .bdg-fab is defined in BudgetPage.css but rendered only by AccountDetailPage.tsx - /budget itself has no FAB, so the bottom-right corner is free there and occupied on account-detail.
- Budget mutations a rail would have to reason about: set assigned amount, move money, cover overspending, quick-assign (fill targets / equally), transaction create/edit/delete, account CRUD.
- CLAUDE.md: the frontend has NO component/visual test harness, so tsc + build + vitest cannot catch a rendering bug. Any UI decision here must be verified interactively or against a docs/mocks/ file, and a new UI surface needs at least a smoke Playwright spec before it ships. Prod deploys on push to main.
- Slot rule, binding on every later ticket (menunest-191): a button earns a Shortcut rail slot ONLY by acting on the user's own recent acts. Everything else stays where its context already lives, because the existing one-tap controls know which Envelope is meant and a floating copy would have to ask. The rail is three slots - undo, redo, change history - and that is the whole rail.
- CONTEXT.md now defines Shortcut rail and Change history. Change history is deliberately NOT the /budget/transactions list (that holds only Budget transactions) and NOT the Budgeting event of menunest-181/185 (that names only the three acts re-freezing the Daily allowance). Use the glossary terms.
- Rail shape is settled and binding on mock-signoff and build-ship (menunest-192): ONE button resting bottom-right, tap expands three items vertically upward, NOT draggable, hides on scroll down and returns on scroll up or after ~1s idle. Two guards are part of the decision, not detail - it never hides while the dial is open, and it returns on idle so it cannot be lost mid-flick.
- Syncfusion SpeedDial has NO hide-on-scroll of its own, so that behaviour is roughly twenty lines MenuNest owns on top of the component library-choice picked. It does not overturn library-choice; it does add to build-ship. position=BottomRight and mode=Linear direction=Up ARE the component's own properties, so the corner and the expansion need no custom positioning.
- The rail-interaction prototype is at https://claude.ai/code/artifact/21ac73e6-a87a-4dbb-a3a5-70555a8e0202 - a throwaway grilling aid built on the app's own tokens, NOT the mock. mock-signoff still owes a docs/mocks/ file for the build to be diffed against.
<!-- decision-map:notes:end -->

## Milestones

<!-- decision-map:milestones:start -->
- `rail-visible` see the button and press it open - rail UX decided and mocked, undo engine not yet built [library-choice, rail-contents, rail-interaction, mock-signoff]
<!-- decision-map:milestones:end -->

## Decisions so far

<!-- decision-map:decisions:start -->
#### rail-visible — see the button and press it open - rail UX decided and mocked, undo engine not yet built

- [Library - build the FAB and its expansion on Syncfusion, on dnd-kit, or by hand?](tickets/library-choice.md) — Syncfusion SpeedDialComponent - one new dep, but its CSS already ships via main.tsx:38. Not @dnd-kit: a free-floating FAB has no droppable.
- [Rail contents - besides undo and redo, which shortcuts earn a slot?](tickets/rail-contents.md) — A history control, not a launcher: exactly three slots - undo, redo, change history - all working in v1, because every launcher candidate is already one contextual tap away.
- [Rail interaction - one button that expands, or an always-open rail, and can it be dragged?](tickets/rail-interaction.md) — One button bottom-right, tap expands the three items upward. Not draggable: it hides on scroll down instead, which solves the occlusion drag was for at none of drag's cost.
<!-- decision-map:decisions:end -->

## Not yet specified

<!-- decision-map:fog:start -->
- What undo means when another family member changed the same envelope in between - cannot be phrased sharply until undo-semantics lands.
- How a first-time user learns the rail exists at all - no evidence yet that discoverability is a real problem here.
- How the generalized rail coexists with the .bdg-fab already occupying the bottom-right corner of AccountDetailPage - needs rail-architecture first.
- Whether undo/redo should also be reachable from the AI/MCP surface, or stay a UI-only affordance.
- Whether Change history should be reachable from anywhere other than the Shortcut rail - the month strip already carries a list icon to /budget/transactions, so two history-ish entry points may confuse rather than help.
- Whether the rail should hide on scroll on DESKTOP too, where Ctrl+Z exists, there is no thumb-reach problem and a mouse wheel scrolls for different reasons than a thumb flick. Small, but it has no obvious home yet - keyboard-bindings is about key handling and build-ship should not be inventing behaviour.
<!-- decision-map:fog:end -->

## Out of scope

<!-- decision-map:scope:start -->
- Rolling the rail out to trips, meal-plan or health - budget only for this effort; those are a later effort with their own tickets.
- Re-architecting the domain into event sourcing or a general audit log.
- Undo for anything outside the budget feature.
- Replacing or removing the existing TransactionUndoToast delete flow on AccountDetailPage, unless a ticket here explicitly decides to.
- Putting launcher actions on the Shortcut rail - add transaction, move money, cover overspending, quick-assign, jump to today. Ruled out by menunest-191: each is already one CONTEXTUAL tap away, so a floating copy would be slower, not faster.
- A draggable rail, as issue #106 originally asked for - rejected by menunest-192 on prototype evidence, not taste: instant drag needs touch-action:none and kills scrolling from the button, hold-to-drag makes every deliberate drag slow, and a saved position lands off a narrower screen. Hide-on-scroll solves the occlusion it was meant to solve at none of that cost.
<!-- decision-map:scope:end -->
