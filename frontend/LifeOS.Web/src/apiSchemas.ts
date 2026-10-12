import { z } from 'zod';

import type {
  AccountListProjection,
  AccountProjection,
  AccountStateProjection,
  AuthMe,
  CreateSetAsideCommand,
  CreateTransactionCommand,
  CreateUpcomingEventCommand,
  FinanceStateProjection,
  InterpretResponse,
  RealizeUpcomingEventResult,
  SetAsideEntryProjection,
  SetAsideHistoryPage,
  SetAsideOperationResult,
  SetAsideProjection,
  TransactionProjection,
  UpcomingEventProjection,
} from './types';

const AccountTypeSchema = z.enum(['Cash', 'Bank', 'EWallet', 'Credit']);
const TransactionTypeSchema = z.enum(['Income', 'Expense', 'Transfer', 'Refund', 'Reversal', 'Adjustment']);
const SetAsideKindSchema = z.enum(['Saving', 'RoutineIncremental', 'RoutineBatch', 'SingleSpend']);
const SetAsideCycleKindSchema = z.enum(['None', 'Weekly', 'Monthly', 'Quarterly', 'SemiAnnual', 'Annual']);
const SetAsideEntryTypeSchema = z.enum(['Opened', 'Added', 'Withdrawn', 'Spent', 'CycleFunding', 'Released', 'Closed']);
const SetAsideStatusSchema = z.enum(['Active', 'Closed']);
const SetAsideCloseReasonSchema = z.enum(['Spent', 'Withdrawn', 'Cancelled']);
const UpcomingEventDirectionSchema = z.enum(['Income', 'Expense']);
const UpcomingEventStatusSchema = z.enum(['Scheduled', 'Realized', 'Skipped', 'Cancelled']);
const UpcomingEventScheduleKindSchema = z.enum(['Scheduled', 'Flexible']);
const UpcomingEventRecurrenceSchema = z.enum(['None', 'Weekly', 'Monthly', 'Quarterly', 'Annual']);

const CreateTransactionEntryCommandSchema = z.object({
  accountId: z.string(),
  amount: z.number(),
}).passthrough();

const CreateTransactionCommandSchema: z.ZodType<CreateTransactionCommand> = z.object({
  type: TransactionTypeSchema,
  amount: z.number(),
  description: z.string().optional(),
  categoryName: z.string().optional(),
  occurredOn: z.string(),
  relatedTransactionId: z.string().optional(),
  feeAmount: z.number().optional(),
  entries: z.array(CreateTransactionEntryCommandSchema),
  setAsideId: z.string().nullable().optional(),
}).passthrough();

const CreateSetAsideCommandSchema: z.ZodType<CreateSetAsideCommand> = z.object({
  sourceAccountId: z.string().nullable().optional(),
  name: z.string(),
  kind: SetAsideKindSchema.optional(),
  note: z.string().optional(),
  transactionCategory: z.string().optional(),
  targetAmount: z.number().nullable().optional(),
  cycleKind: SetAsideCycleKindSchema.optional(),
  amount: z.number().optional(),
}).passthrough();

const CreateUpcomingEventCommandSchema: z.ZodType<CreateUpcomingEventCommand> = z.object({
  accountId: z.string().nullable().optional(),
  defaultSourceAccountId: z.string().nullable().optional(),
  title: z.string(),
  amount: z.number(),
  direction: UpcomingEventDirectionSchema,
  categoryName: z.string().optional(),
  note: z.string().optional(),
  dueDate: z.string().nullable().optional(),
  scheduleKind: UpcomingEventScheduleKindSchema.optional(),
  recurrence: UpcomingEventRecurrenceSchema.optional(),
}).passthrough();

export const AuthMeSchema: z.ZodType<AuthMe> = z.object({
  userId: z.string(),
  userName: z.string(),
  email: z.string(),
  isAuthenticated: z.boolean(),
}).passthrough();

export const AccountProjectionSchema: z.ZodType<AccountProjection> = z.object({
  id: z.string(),
  name: z.string(),
  type: AccountTypeSchema,
  isArchived: z.boolean(),
  actualBalance: z.number(),
  setAsideAmount: z.number(),
  availableBalance: z.number(),
  createdAt: z.string(),
}).passthrough();

const AccountStateProjectionSchema: z.ZodType<AccountStateProjection> = z.object({
  id: z.string(),
  name: z.string(),
  type: AccountTypeSchema,
  isArchived: z.boolean(),
  actualBalance: z.number(),
  setAsideAmount: z.number(),
  availableBalance: z.number(),
  createdAt: z.string(),
}).passthrough();

export const AccountListProjectionSchema: z.ZodType<AccountListProjection> = z.object({
  accounts: z.array(AccountProjectionSchema),
  totalActualBalance: z.number(),
  totalSetAsideAmount: z.number(),
  totalAvailableBalance: z.number(),
}).passthrough();

const TransactionEntryProjectionSchema = z.object({
  accountId: z.string(),
  accountName: z.string(),
  amount: z.number(),
}).passthrough();

export const TransactionProjectionSchema: z.ZodType<TransactionProjection> = z.object({
  id: z.string(),
  type: TransactionTypeSchema,
  amount: z.number(),
  description: z.string().nullable(),
  categoryName: z.string().nullable(),
  occurredOn: z.string(),
  createdAt: z.string(),
  relatedTransactionId: z.string().nullable(),
  feeAmount: z.number().nullable(),
  entries: z.array(TransactionEntryProjectionSchema),
  isReversed: z.boolean(),
  reversalReason: z.string().nullable(),
  setAsideId: z.string().nullable(),
  setAsideName: z.string().nullable(),
}).passthrough();

const SetAsideTransactionSummarySchema = z.object({
  id: z.string(),
  type: TransactionTypeSchema,
  amount: z.number(),
  description: z.string().nullable(),
  categoryName: z.string().nullable(),
  occurredOn: z.string(),
  relatedDescription: z.string().nullable(),
}).passthrough();

export const SetAsideEntryProjectionSchema: z.ZodType<SetAsideEntryProjection> = z.object({
  id: z.string(),
  type: SetAsideEntryTypeSchema,
  amount: z.number(),
  transactionId: z.string().nullable(),
  note: z.string().nullable(),
  transactionCategory: z.string().nullable().optional(),
  createdAt: z.string(),
  balanceAfter: z.number(),
  transaction: SetAsideTransactionSummarySchema.nullable(),
}).passthrough();

export const SetAsideHistoryPageSchema: z.ZodType<SetAsideHistoryPage> = z.object({
  items: z.array(SetAsideEntryProjectionSchema),
  hasMore: z.boolean(),
  nextCursor: z.string().nullable(),
}).passthrough();

export const SetAsideProjectionSchema: z.ZodType<SetAsideProjection> = z.object({
  id: z.string(),
  accountId: z.string().nullable(),
  accountName: z.string().nullable(),
  defaultSourceAccountId: z.string().nullable(),
  defaultSourceAccountName: z.string().nullable(),
  name: z.string(),
  kind: SetAsideKindSchema.nullable(),
  note: z.string().nullable(),
  transactionCategory: z.string().nullable().optional(),
  amount: z.number(),
  targetAmount: z.number().nullable(),
  targetShortfall: z.number(),
  cycleKind: SetAsideCycleKindSchema,
  cycleAnchorDate: z.string(),
  currentCycleStart: z.string().nullable(),
  currentCycleEnd: z.string().nullable(),
  isCycleRolloverPending: z.boolean(),
  cycleFundingRequired: z.number(),
  cycleSurplus: z.number(),
  cycleFundingShortfall: z.number(),
  isUnderfunded: z.boolean(),
  isCycleExecuted: z.boolean(),
  usedAmount: z.number(),
  status: SetAsideStatusSchema,
  closeReason: SetAsideCloseReasonSchema.nullable(),
  createdAt: z.string(),
  updatedAt: z.string(),
  recentEntries: z.array(SetAsideEntryProjectionSchema),
}).passthrough();

export const SetAsideOperationResultSchema: z.ZodType<SetAsideOperationResult> = z.object({
  setAside: SetAsideProjectionSchema,
  entry: SetAsideEntryProjectionSchema,
  transactionId: z.string().nullable(),
  cycleFundingShortfall: z.number(),
}).passthrough();

export const UpcomingEventProjectionSchema: z.ZodType<UpcomingEventProjection> = z.object({
  id: z.string(),
  accountId: z.string().nullable(),
  accountName: z.string().nullable(),
  defaultSourceAccountId: z.string().nullable().optional(),
  defaultSourceAccountName: z.string().nullable().optional(),
  realizedSourceAccountId: z.string().nullable().optional(),
  realizedSourceAccountName: z.string().nullable().optional(),
  title: z.string(),
  amount: z.number(),
  direction: UpcomingEventDirectionSchema,
  categoryName: z.string().nullable(),
  note: z.string().nullable(),
  dueDate: z.string().nullable(),
  scheduleKind: UpcomingEventScheduleKindSchema,
  recurrence: UpcomingEventRecurrenceSchema,
  status: UpcomingEventStatusSchema,
  realizedTransactionId: z.string().nullable(),
  statusReason: z.string().nullable(),
  isDue: z.boolean(),
  isOverdue: z.boolean(),
  commitmentStartDate: z.string().nullable().optional(),
  isCommitmentActive: z.boolean().optional(),
  createdAt: z.string(),
  updatedAt: z.string(),
}).passthrough();

export const FinanceStateProjectionSchema: z.ZodType<FinanceStateProjection> = z.object({
  today: z.string(),
  totalActualBalance: z.number(),
  totalSetAside: z.number(),
  pendingCycleFunding: z.number(),
  pendingCycleSurplus: z.number(),
  totalCommittedSetAside: z.number(),
  totalAvailable: z.number(),
  dueObligations: z.number(),
  dueObligationsCount: z.number(),
  overdueObligationsCount: z.number(),
  scheduledExpenseCommitments: z.number(),
  freeCash: z.number(),
  hasUnpaidBills: z.boolean(),
  allBillsPaid: z.boolean(),
  accounts: z.array(AccountStateProjectionSchema),
  setAsides: z.array(SetAsideProjectionSchema),
  upcomingEvents: z.array(UpcomingEventProjectionSchema),
  archivedEvents: z.array(UpcomingEventProjectionSchema).optional(),
  dueEvents: z.array(UpcomingEventProjectionSchema),
  recentTransactions: z.array(TransactionProjectionSchema),
}).passthrough();

const CreateTransactionResponseSchema = z.object({
  transactionId: z.string(),
  type: TransactionTypeSchema,
  amount: z.number(),
  occurredOn: z.string(),
  createdAt: z.string(),
  entryCount: z.number(),
}).passthrough();

const ReverseTransactionResponseSchema = z.object({
  transactionId: z.string(),
  type: TransactionTypeSchema,
  amount: z.number(),
  relatedTransactionId: z.string().nullable(),
  occurredOn: z.string(),
  createdAt: z.string(),
}).passthrough();

const CreateAccountResponseSchema = z.object({
  accountId: z.string(),
  name: z.string(),
  type: AccountTypeSchema,
  isArchived: z.boolean(),
  createdAt: z.string(),
}).passthrough();

const RealizeUpcomingEventResultSchema: z.ZodType<RealizeUpcomingEventResult> = z.object({
  event: UpcomingEventProjectionSchema,
  transactionId: z.string(),
}).passthrough();

const InterpretResponseSchema: z.ZodType<InterpretResponse> = z.object({
  intent: z.string(),
  state: z.enum(['Ready', 'NeedsClarification', 'Unsupported']),
  preview: z.object({
    type: z.string(),
    amount: z.number(),
    description: z.string().nullable(),
    account: z.string().nullable(),
    toAccount: z.string().nullable(),
    date: z.string().nullable(),
    feeAmount: z.number().nullable(),
  }).nullable(),
  command: CreateTransactionCommandSchema.nullable(),
  setAsidePreview: z.object({
    name: z.string(),
    amount: z.number(),
    account: z.string().nullable(),
  }).nullable(),
  setAsideCommand: CreateSetAsideCommandSchema.nullable(),
  eventPreview: z.object({
    title: z.string(),
    amount: z.number(),
    direction: z.string(),
    account: z.string().nullable(),
    date: z.string().nullable(),
  }).nullable(),
  eventCommand: CreateUpcomingEventCommandSchema.nullable(),
  clarifications: z.array(z.string()),
}).passthrough();

export const ApiResponseSchemas = {
  authMe: AuthMeSchema,
  login: z.object({ isAuthenticated: z.boolean() }).passthrough(),
  guestSession: z.object({ isGuest: z.literal(true) }).passthrough(),
  financeState: FinanceStateProjectionSchema,
  accountList: AccountListProjectionSchema,
  account: AccountProjectionSchema,
  accountMutation: CreateAccountResponseSchema,
  transactions: z.object({ items: z.array(TransactionProjectionSchema) }).passthrough(),
  transaction: TransactionProjectionSchema,
  createTransaction: CreateTransactionResponseSchema,
  reverseTransaction: ReverseTransactionResponseSchema,
  setAsides: z.object({ items: z.array(SetAsideProjectionSchema) }).passthrough(),
  setAside: SetAsideProjectionSchema,
  setAsideHistory: SetAsideHistoryPageSchema,
  setAsideOperation: SetAsideOperationResultSchema,
  upcomingEvents: z.object({ items: z.array(UpcomingEventProjectionSchema) }).passthrough(),
  upcomingEvent: UpcomingEventProjectionSchema,
  realizeUpcomingEvent: RealizeUpcomingEventResultSchema,
  interpret: InterpretResponseSchema,
} as const;
