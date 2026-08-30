# The payment button's label follows the account type

```mermaid
flowchart TD
    Q{"one action pays cards and loans —<br/>what does the button say?"}
    Q -->|chosen| A["the label follows the Account:<br/>จ่ายบัตร on a card, จ่ายค่างวด on a loan"]
    Q -->|rejected| B["จ่ายหนี้ everywhere —<br/>หนี้ is heavy for a card cleared each month"]
    Q -->|rejected| C["ชำระ everywhere —<br/>bank language, not how the User speaks"]
```

Closes the naming question menunest-207 left open. One action, one command, one MCP tool
(menunest-211) — only the word on the button varies, resolved from the **Account**'s type at
render time. That costs nothing: no branch exists below the label.

The **Payment envelope** keeps the card-specific name **จ่ายบัตร &lt;account&gt;**, and needs no wider
one, because menunest-206 gives a **Loan** none.

_Avoid_, per CONTEXT.md: **จ่ายหนี้**, **ชำระ**, transfer, pay off, settle.
