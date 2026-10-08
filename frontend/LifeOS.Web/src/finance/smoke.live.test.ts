/**
 * Live authenticated smoke flow for the Finance FE → BE integration.
 *
 * Runs the real `api.ts` client and the real `apiMapping` translation against a
 * running backend, using the project's existing Guest session mechanism.
 *
 * Skipped unless FINANCE_SMOKE_URL is set, so the normal test run stays hermetic.
 */

import { afterAll, beforeAll, describe, expect, it } from 'vitest';

// Vitest exposes Node's process.env at runtime; this app's tsconfig targets the
// browser, so the ambient name is declared locally instead of pulling in @types/node.
declare const process: { env: Record<string, string | undefined> };

const BASE = process.env.FINANCE_SMOKE_URL ?? '';
const enabled = BASE.length > 0;

// The api client resolves its base from `import.meta.env.VITE_API_BASE_URL`.
// Set it before the client module is imported inside `beforeAll`.
if (enabled) {
  process.env.VITE_API_BASE_URL = BASE;
}

type CookieJar = Map<string, string>;

const jar: CookieJar = new Map();
const realFetch = globalThis.fetch.bind(globalThis);

function parseSetCookie(headers: Headers): void {
  const raw = headers.getSetCookie?.() ?? [];
  for (const line of raw) {
    const [pair] = line.split(';');
    const idx = pair.indexOf('=');
    if (idx < 0) continue;
    const name = pair.slice(0, idx).trim();
    const value = pair.slice(idx + 1).trim();
    if (value.toLowerCase() === 'deleted') jar.delete(name);
    else jar.set(name, value);
  }
}

function cookieHeader(): string {
  return [...jar.entries()].map(([k, v]) => `${k}=${v}`).join('; ');
}

function installCookieJar(): void {
  globalThis.fetch = (async (
    input: RequestInfo | URL,
    init?: RequestInit
  ): Promise<Response> => {
    const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url;
    const headers = new Headers(init?.headers);
    if (jar.size > 0 && !headers.has('cookie')) headers.set('cookie', cookieHeader());
    const res = await realFetch(url, { ...init, headers, redirect: 'manual' });
    parseSetCookie(res.headers);
    return res;
  }) as typeof fetch;
}

function restoreFetch(): void {
  globalThis.fetch = realFetch;
}

describe.skipIf(!enabled)('live Finance smoke flow (guest session)', () => {
  // Remote dev database: each call is a network round trip.
  const LIVE_TIMEOUT = 120_000;

  let api: typeof import('../api');
  let mapping: typeof import('./apiMapping');

  beforeAll(async () => {
    installCookieJar();
    // Imported after the fetch wrapper so the client picks it up.
    api = await import('../api');
    mapping = await import('./apiMapping');
  }, LIVE_TIMEOUT);

  afterAll(() => {
    restoreFetch();
  });

  it('creates a guest session and loads an empty finance state', async () => {
    const session = await api.createGuestSession();
    expect(session.isGuest).toBe(true);

    const state = await api.getFinanceState();
    expect(state).toBeTruthy();
    expect(typeof state.freeCash).toBe('number');
    expect(Array.isArray(state.accounts)).toBe(true);
    expect(Array.isArray(state.setAsides)).toBe(true);
    expect(Array.isArray(state.upcomingEvents)).toBe(true);
  }, LIVE_TIMEOUT);

  it('accounts, transactions, set-asides and upcoming events drive the read model', async () => {
    await api.createGuestSession();

    const sea = await api.createAccount({ name: `SeaBank Smoke ${Date.now()}`, type: 'Bank' });
    const bca = await api.createAccount({ name: `BCA Smoke ${Date.now()}`, type: 'Bank' });

    // Income raises the actual balance.
    await api.createTransaction({
      type: 'Income',
      amount: 2_000_000,
      description: 'Smoke seed',
      occurredOn: new Date().toISOString().slice(0, 10),
      entries: [{ accountId: sea.accountId, amount: 2_000_000 }],
    });

    let state = await api.getFinanceState();
    expect(state.totalActualBalance).toBeGreaterThanOrEqual(2_000_000);
    expect(state.accounts.find((a) => a.id === sea.accountId)?.actualBalance).toBe(2_000_000);

    // Set-aside lowers available and Uang Bebas, never the actual balance.
    // sourceAccountId hanya divalidasi sekali pakai — pos tidak terikat akun.
    const pos = await api.createSetAside({
      sourceAccountId: sea.accountId,
      name: `Dana Makan Smoke ${Date.now()}`,
      kind: 'RoutineIncremental',
      targetAmount: 300_000,
      cycleKind: 'Monthly',
      amount: 300_000,
    });

    state = await api.getFinanceState();
    const actualBeforeSpend = state.totalActualBalance;
    expect(state.accounts.find((a) => a.id === sea.accountId)?.setAsideAmount).toBe(
      state.accounts.find((a) => a.id === sea.accountId)?.setAsideAmount
    );
    expect(state.totalSetAside).toBeGreaterThanOrEqual(300_000);
    expect(state.freeCash).toBeLessThan(actualBeforeSpend);

    // Add / withdraw / spend.
    await api.addToSetAside(pos.id, { amount: 100_000, note: 'topup' });
    let posView = await api.getSetAside(pos.id);
    expect(posView.amount).toBe(400_000);

    await api.withdrawFromSetAside(pos.id, { amount: 50_000, note: 'tarik' });
    posView = await api.getSetAside(pos.id);
    expect(posView.amount).toBe(350_000);

    await api.spendFromSetAside(pos.id, {
      // Sumber Dana tempat uang BENAR-BENAR keluar. Wajib.
      sourceAccountId: sea.accountId,
      amount: 200_000,
      description: 'Makan smoke',
      occurredOn: new Date().toISOString().slice(0, 10),
    });
    posView = await api.getSetAside(pos.id);
    expect(posView.amount).toBe(150_000);

    const history = await api.getSetAsideHistory(pos.id);
    expect(history.items.map((e) => e.type)).toEqual(
      expect.arrayContaining(['Opened', 'Added', 'Withdrawn', 'Spent'])
    );

    state = await api.getFinanceState();
    expect(state.accounts.find((a) => a.id === sea.accountId)?.actualBalance).toBe(
      2_000_000 - 200_000
    );

    // Upcoming expense due today becomes an obligation and lowers Uang Bebas.
    const today = new Date().toISOString().slice(0, 10);
    const bill = await api.createUpcomingEvent({
      accountId: bca.accountId,
      title: 'WiFi Smoke',
      amount: 340_440,
      direction: 'Expense',
      dueDate: today,
      scheduleKind: 'Scheduled',
    });

    state = await api.getFinanceState();
    expect(state.dueObligations).toBeGreaterThanOrEqual(340_440);
    expect(state.dueEvents.some((e) => e.id === bill.id)).toBe(true);

    // Postpone clears the obligation without moving money.
    await api.postponeUpcomingEvent(bill.id, { newDueDate: '2099-01-01' });
    state = await api.getFinanceState();
    expect(state.dueEvents.some((e) => e.id === bill.id)).toBe(false);

    // Realize an expense — accountId wajib dipilih saat realizasi.
    const sewa = await api.createUpcomingEvent({
      title: 'Sewa Smoke',
      amount: 500_000,
      direction: 'Expense',
      dueDate: '2099-01-01',
      scheduleKind: 'Scheduled',
    });
    const realized = await api.realizeUpcomingEvent(sewa.id, {
      accountId: sea.accountId,
      occurredOn: today,
    });
    expect(realized.event.status).toBe('Realized');
    expect(realized.transactionId).toBeTruthy();

    // Reversal restores the money and keeps the audit trail.
    const before = await api.getFinanceState();
    const tx = await api.reverseTransaction(realized.transactionId, {
      reason: 'Salah catat / nominal typo',
    });
    expect(tx.relatedTransactionId).toBe(realized.transactionId);

    const after = await api.getFinanceState();
    expect(after.totalActualBalance).toBe(before.totalActualBalance + 500_000);

    const txs = await api.getTransactions();
    const original = txs.items.find((t) => t.id === realized.transactionId);
    expect(original?.isReversed).toBe(true);
    expect(original?.reversalReason).toBe('Salah catat / nominal typo');
  }, LIVE_TIMEOUT);

  it('translates the live projection into the UI view-model without recomputing state', async () => {
    await api.createGuestSession();

    const account = await api.createAccount({
      name: `Map Smoke ${Date.now()}`,
      type: 'Bank',
    });
    await api.createTransaction({
      type: 'Income',
      amount: 1_000_000,
      occurredOn: new Date().toISOString().slice(0, 10),
      entries: [{ accountId: account.accountId, amount: 1_000_000 }],
    });
    // Rencana tidak terikat akun — sourceAccountId hanya validasi pendanaan awal.
    await api.createSetAside({
      sourceAccountId: account.accountId,
      name: `Pos Map ${Date.now()}`,
      kind: 'Saving',
      amount: 250_000,
    });

    const projection = await api.getFinanceState();
    const mapped = mapping.mapFinanceState(projection);

    expect(mapped.accounts.find((a) => a.id === account.accountId)?.balance).toBe(1_000_000);
    expect(mapped.derived.freeCash).toBe(projection.freeCash);
    expect(mapped.derived.totalLiquidity).toBe(projection.totalActualBalance);
    expect(mapped.derived.savingsCommitment).toBe(projection.totalCommittedSetAside);
    // accountLabel adalah LEGACY ONLY — pos baru tidak terikat rekening.
    expect(mapped.posItems.some((p) => p.name.startsWith('Pos Map'))).toBe(true);
    expect(mapped.posItems.find((p) => p.name.startsWith('Pos Map'))?.accountLabel).toBeUndefined();
  }, LIVE_TIMEOUT);
});
