# Paying a card is one paired action, not two hand-written transactions

```mermaid
flowchart TD
    Q{"how does a User pay a Credit Account's bill?"}
    Q -->|chosen| A["a จ่ายบัตร action: pick the paying Account,<br/>type an amount, the app writes both<br/>Budget transactions as one pair"]
    Q -->|rejected| B["two hand-written Budget transactions —<br/>the bare inflow on the card reads as income,<br/>and half a pair is a valid save"]
```

MenuNest has no transfer: a **Budget transaction** carries exactly one `AccountId`. Paying a card
by hand therefore means two of them — an outflow on the paying **Account** and an inflow on the
Credit **Account** — and the app cannot tell that they belong together.

Two things break if we leave it there. The bare positive on the Credit **Account** carries no
**Envelope**, and `GetMonthlySummaryHandler` counts every uncategorised positive row in the month
as **Income**, so paying your own card reports as money arriving. And nothing binds the halves:
saving one and abandoning the other leaves the budget wrong with no error, which is exactly the
failure menunest-202 exists to prevent.

A single จ่ายบัตร action writes both rows together, spends down the payment **Envelope**, and is
the one place that knows the pair is a payment rather than income.
