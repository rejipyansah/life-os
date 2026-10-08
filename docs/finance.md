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

API JSON contract:

- All enum values (AccountType, TransactionType) are represented as readable strings in JSON requests and responses (e.g. `"Cash"`, `"Income"`).
- Numeric integer enum values are also accepted for backward compatibility but string values are canonical.

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
- Default transaction/business dates are calculated in WIB (UTC+7).
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

## SetAside (Dana yang Disisihkan)

SetAside is an allocation/reservation of money for a purpose — a pool of intent, NOT a money movement and NOT tied to any Account (Sumber Dana).

Fields:

- Id: Guid
- ScopeId: Guid
- Name: string
- Kind: SetAsideKind? (Saving / RoutineIncremental / RoutineBatch / SingleSpend)
- Note: string? 
- TargetAmount: decimal?
- CycleKind: SetAsideCycleKind
- CycleAnchorDate: DateOnly
- CycleFundingShortfall: decimal — active-cycle target amount awaiting available FreeCash
- Status: SetAsideStatus (Active / Closed)
- CloseReason: SetAsideCloseReason?
- CreatedAt / UpdatedAt
- AccountId: Guid? — LEGACY ONLY. Retained for historical rows; never written for new set-asides; never used in any balance, available, funding, or allocation calculation.

Current reserved amount is derived from append-only SetAsideEntry history: amount = SUM(SetAsideEntry.Amount).

Rules:

- SetAside belongs to exactly one Scope. Cross-scope references must be rejected.
- SetAside does NOT belong to any Account. Alokasi independen dari Sumber Dana.
- SetAside does not change Account balances. It never creates a TransactionEntry under an account name.
- SetAside only reduces derived figures: TotalAvailable and FreeCash.
- A set-aside does not create a Transaction. It has no TransactionId on the SetAside itself (SetAsideEntry.Spent may link to a transaction when real spending happens).
- New SetAside starts with Status = Active.
- Closed set-asides release their remaining reserved amount back to TotalAvailable.
- Set-asides are not hard-deleted through the normal API.
- TotalAvailable = TotalActual − TotalSetAside (scope-wide): money not yet allocated to any set-aside.
- FreeCash (Uang Bebas) = TotalActual − TotalSetAside + PendingCycleSurplus − ScheduledExpenseCommitments. Unfunded cycle shortfall is shown separately and does not reduce FreeCash. FreeCash and TotalAvailable are DIFFERENT figures — never conflate them.
- FreeCash is never clamped to 0; negative FreeCash is a real condition surfaced to the user.
- New allocations/top-ups/cycle funding use scope-wide FreeCash; an unfunded cycle target cannot reserve money that does not exist or is committed to scheduled expenses.
- Actual insufficient Account balance remains a hard constraint for financial transactions (money must physically exist in the chosen Sumber Dana).
- DefaultSourceAccountId is an optional persistent reference for the account the user usually associates with a set-aside. It is not debited and its balance does not limit create/top-up; create/top-up is limited by scope-wide FreeCash. A top-up/withdraw only reallocates money between FreeCash and the set-aside; account balances do not move. Withdraw does not select an account.
- Spending still requires an active source account with sufficient actual balance, because it records a real expense.
- Monthly cycles follow calendar months (day 1 through the last day), including for existing positions and positions created mid-month. All financial business dates use WIB (UTC+7).
- Cycle funding on rollover uses available scope-wide FreeCash in SetAside creation order. Any amount still unfunded is stored/displayed as a shortfall, does not reduce FreeCash, and is automatically funded from later unallocated income in creation order. Reversal of that income reverses the corresponding funding in the same cycle.
- RoutineBatch is a one-execution-per-cycle allocation. Recording its actual expense releases any unused cycle balance immediately to scope-wide TotalAvailable; an actual cost above the allocation is allowed only when the source account and scope-wide unallocated balance can cover it.
- An expense attributed to a RoutineBatch set-aside through the dedicated spend action, a regular transaction, or agenda realization all completes that cycle. A reversed expense no longer counts as the cycle execution.
- A SingleSpend set-aside is closed with CloseReason=Spent after its expense is recorded. Its remaining allocation is released in the same database transaction. Reversing that expense reopens the set-aside and compensates the allocation entries.

### SetAsideEntry (append-only history)

Fields: Id, SetAsideId, ScopeId, Type, Amount (signed delta), TransactionId?, Note?, CreatedAt.

Entry types: Opened, Added, Withdrawn, Spent, CycleFunding, Released, Closed.

- Spent entries may link to the real Transaction via TransactionId.
- Added entries (income allocated to a pos) may also link to a transaction.
- Reversals create compensating SetAsideEntry records (opposite amount, linked to the reversal transaction) — append-only, original entries are never mutated.

---

## UpcomingEvent (Rencana Pengeluaran/Pemasukan)

UpcomingEvent is a planned future cash event — an intention, NOT a Transaction.

Fields:

- Id: Guid
- ScopeId: Guid
- Title: string
- Amount: decimal
- Direction: Income | Expense
- CategoryName: string?
- Note: string?
- DueDate: DateOnly?
- ScheduleKind: Scheduled | Flexible
- Recurrence: None / Weekly / Monthly / Quarterly / Annual
- Status: Scheduled / Realized / Skipped / Cancelled
- RealizedTransactionId: Guid?
- AccountId: Guid? — LEGACY ONLY. Rencana baru tidak pernah mengisi akun ini.

Rules:

- Rencana does NOT belong to any Account. It is not tied to a Sumber Dana.
- AccountId on realization is chosen by the user at realize time (RealizeUpcomingEventCommand.AccountId is required). The source account at plan creation is irrelevant.
- Scheduled expense commitments reduce FreeCash from the moment the plan is created — derived only, no transaction.
- Only realization turns an event into a real Transaction; passing the date does not.
- Realization may optionally release a set-aside (SetAsideId): the expense then draws min(amount, posBalance) from the pos, with the shortfall covered by unallocated money.
- Postpone/skip/cancel never move money.
- Realized agendas cannot be deleted; reverse the transaction instead.

---

## Three concepts — separation

| Concept | Entity | Meaning | Tied to Account? |
|---|---|---|---|
| Sumber Dana | Account | Where money is located right now | — (is the account) |
| Dana yang Disisihkan | SetAside | Allocation/purpose of money (scope-wide pool) | NO — never permanently |
| Rencana | UpcomingEvent | Planned future cash event | NO — account chosen only at realize time |

When recording a transaction, Sumber Dana and Dana yang Disisihkan are TWO INDEPENDENT selections:
- Sumber Dana → where money actually leaves/enters (required, validated against actual balance).
- Dana yang Disisihkan → which allocation is released (expense) or funded (income) (optional).

Defaults/recommendations may ease input, but no permanent binding between a set-aside/plan and a specific account is ever created.

Key example: Dana Pacaran Rp500.000, transfer Rp200.000 from SeaBank to Tunai → the pos balance stays Rp500.000; only the money's location changed. Transfer validates only the source account's actual balance; allocations are untouched.

---

## Scope isolation

All Finance data belongs to a Scope.

Rules:

- The server resolves the current Scope.
- Client must never choose arbitrary ScopeId.
- Owner and Guest use the same Finance business logic.
- Cross-scope Account, Transaction, SetAside, and UpcomingEvent relationships must be rejected.

---

## Explicitly deferred

The following are intentionally excluded from Finance v1:

- Multi-currency
- Credit card or negative-liability account modeling
- Generic fee system
- Category entity or taxonomy management
- Complex envelope budgeting
- Automatic allocation matching for general income (cycle-shortfall funding is a separate automatic rule)
- Event sourcing
- Finance-specific Owner/Guest services
- Provider/institution taxonomy on Account
- Purpose on Account
- Balance on Account
- Currency on Account

---

## Transaction Query API

The Transaction Query API provides read-only access to transaction history and details.

### Endpoints

**List transactions:**
- `GET /api/finance/transactions`
- Returns all transactions belonging to the server-resolved current Scope
- Ordered by OccurredOn DESC, then CreatedAt DESC
- Includes TransactionEntry projections with AccountName (read projection, not duplicated persistence)
- Includes SetAsideId/SetAsideName when the transaction released or funded an allocation
- Archived Accounts still appear in historical entries
- No pagination, no filter query parameters in v1

**Get transaction detail:**
- `GET /api/finance/transactions/{id}`
- Returns a single transaction with its entries
- Only returns transactions from the current Scope
- Cross-scope lookup returns 404 (does not reveal existence)

### Rules

- Scope is resolved server-side; client never provides ScopeId
- Archived Accounts remain visible in historical transaction entries
- DateOnly OccurredOn is returned as ISO date (e.g., "2026-09-18")
- CreatedAt is returned as UTC timestamp
- No update, delete, or mutation endpoints exist for transactions
- Transactions are immutable once posted
