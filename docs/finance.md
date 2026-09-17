# Finance v1

Finance is the first domain module of Life OS. It captures, organizes, and represents the financial events of a user's life.

This document records finalized v1 architecture decisions. It is not an implementation manual.

---

## Account

Account represents where money is stored or held.

Fields:

- Id: Guid
- ScopeId: Guid
- Name
- Type
- IsArchived
- CreatedAt

Current AccountType values:

- Cash
- Bank
- EWallet

Rules:

- Account may be created with zero balance.
- Opening balance is optional.
- Account does not store Balance as source of truth.
- Balance is derived from financial ledger entries.
- Cash, Bank, and EWallet must not go below zero in v1.
- Accounts are archived instead of hard-deleted so transaction history remains intact.
- Purpose is not a permanent Account property.

---

## Transaction

Transaction represents an actual financial event.

Fields:

- Id: Guid
- ScopeId: Guid
- Type
- Amount
- Description
- CategoryName nullable
- OccurredOn: DateOnly
- CreatedAt
- RelatedTransactionId nullable
- FeeAmount nullable

Current TransactionType values:

- Income
- Expense
- Transfer
- Refund
- Reversal
- Adjustment

Rules:

- Transaction Amount is non-negative; direction is represented by TransactionEntry.Amount.
- FeeAmount is only used for Transfer in v1.
- OccurredOn represents the user/business date and is not a timestamp.
- CreatedAt is system metadata.
- Posted financial transactions are immutable.
- User-facing edits must be represented as corrections/reversals rather than mutating historical ledger entries.

---

## TransactionEntry

TransactionEntry represents the effect of a Transaction on an Account.

Fields:

- Id
- TransactionId
- AccountId
- Amount

Rules:

- Amount is signed: positive means money enters the Account; negative means money leaves the Account.
- TransactionEntry does not duplicate ScopeId; its scope is inherited through Transaction and Account.
- A Transaction may only reference Accounts belonging to the same Scope.
- A Transaction does not need to net to zero:
  - Income increases total funds.
  - Expense decreases total funds.
  - Transfer without fee nets to zero.
  - Transfer with fee decreases total funds by the fee.

---

## Transaction semantics

These examples describe how each TransactionType translates to TransactionEntry records.

**Income**: one positive entry.

**Expense**: one negative entry.

**Transfer**: one negative source entry + one positive destination entry. Source and destination must differ.

**Transfer fee**: source is reduced by Amount + FeeAmount. Destination receives Amount.

**Refund**: positive entry linked to the original transaction.

**Reversal**: corrective opposite movement linked to the original transaction.

**Adjustment**: standalone signed correction with non-zero amount.

---

## Category

- Category is intentionally not a separate entity in v1.
- Transaction has nullable CategoryName.
- Transactions remain valid without a category.
- Category taxonomy may evolve later based on real usage.

---

## Allocation

Allocation is a reservation or intention, not a money movement.

Fields:

- Id
- ScopeId
- AccountId
- Name
- Amount
- IsActive
- CreatedAt

Rules:

- Allocation belongs to one Account.
- Allocation does not change Account balance.
- Available amount is conceptually: Actual Balance minus Active Allocations.
- Over-allocation is allowed as a warning or context signal.
- Actual insufficient Account balance remains a hard constraint for financial transactions.
- No due date, bill, or reminder semantics in v1.
- Completed allocations become inactive rather than being hard-deleted.

---

## Scope isolation

All Finance data belongs to a Scope.

Rules:

- The server resolves the current Scope.
- Client must never choose arbitrary ScopeId.
- Owner and Guest use the same Finance business logic.
- Cross-scope Account, Transaction, and Allocation relationships must be rejected.

---

## Explicitly deferred

The following are intentionally excluded from Finance v1:

- Multi-currency
- Credit card or negative-liability account modeling
- Generic fee system
- Category entity or taxonomy management
- Bills, reminders, and due dates
- Complex envelope budgeting
- Automatic allocation matching
- Event sourcing
- Finance-specific Owner/Guest services
