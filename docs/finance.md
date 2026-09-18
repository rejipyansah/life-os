# Finance v1

Finance is the first domain module of Life OS. It captures, organizes, and represents the financial events of a user's life.

This document records finalized v1 architecture decisions. It is not an implementation manual.

---

## Account

Account represents where money is stored or held.

Identity is defined by the user-provided Name. Account does not have a Provider or Institution field in v1. Users are free to name accounts according to their own mental model.

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

Type is a small structural classification. Provider/institution taxonomy is not introduced in v1.

Rules:

- Account belongs to exactly one Scope.
- Scope is resolved server-side; never trust arbitrary ScopeId from the client.
- Account Name remains user-defined and may be changed.
- Account Type may be changed while the Account has no TransactionEntry history.
- Once the Account has at least one TransactionEntry, Account Type becomes immutable.
- Do not add a separate HasTransactions field; determine this from ledger history.
- Archived Accounts retain their historical data and cannot receive new financial transactions.
- Account may be created with zero balance.
- Opening balance is optional and is not required for Account creation.
- Account does not store Balance as source of truth.
- Balance is derived from financial ledger entries.
- Cash, Bank, and EWallet must not go below zero in v1.
- Accounts are archived instead of hard-deleted so transaction history remains intact.
- Account name does not need to be globally unique.
- Preserve Scope isolation.
- Do not add Provider, Institution, Purpose, Balance, Currency, or other new Account fields.

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

Allocation is a reservation or intention, not a money movement. It represents a user's plan to set aside funds for a purpose.

Fields:

- Id: Guid
- ScopeId: Guid
- AccountId: Guid
- Name: string
- Amount: decimal (strictly greater than 0)
- IsActive: bool
- CreatedAt: DateTime UTC

Rules:

- Allocation belongs to exactly one Account.
- Allocation belongs to the same Scope as its Account. Cross-scope references must be rejected.
- Amount must be strictly greater than 0.
- Allocation does not change Account balance.
- Allocation does not create a Transaction or TransactionEntry. It has no TransactionId.
- New Allocation starts with IsActive = true.
- Active allocations contribute to the contextual "Allocated" amount.
- Inactive allocations do not contribute to active allocation totals.
- Completing an allocation means setting IsActive = false.
- Allocations are not hard-deleted through the normal API.
- Available amount is conceptually: Actual Account Balance minus SUM(Active Allocations).
- Over-allocation is allowed. Over-allocation is a warning/context condition, not a validation failure.
  Example: Balance = 500,000; Active allocations = 700,000; Available = -200,000.
- Actual insufficient Account balance remains a hard constraint for financial transactions.
- Allocation must not prevent or hard-block an otherwise valid financial transaction.
- No due date, bill, or reminder semantics in v1.
- No automatic matching to transactions.
- No category dependency.

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
- Provider/institution taxonomy on Account
- Purpose on Account
- Balance on Account
- Currency on Account
