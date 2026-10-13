import { afterEach, describe, expect, it, vi } from 'vitest';

import { ApiRequestError, getFinanceState, GuestSessionExpiredError, interpret, resumeGuestSession } from './api';

describe('resumeGuestSession', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('reports an expired session and notifies the app', async () => {
    const expiredEvent = vi.fn();
    const eventTarget = new EventTarget();
    vi.stubGlobal('window', eventTarget);
    eventTarget.addEventListener('lifeos:guest-session-expired', expiredEvent);
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(
      JSON.stringify({ code: 'GuestSessionExpired', error: 'expired' }),
      { status: 410, headers: { 'Content-Type': 'application/json' } },
    )));

    await expect(resumeGuestSession()).rejects.toBeInstanceOf(GuestSessionExpiredError);
    expect(expiredEvent).toHaveBeenCalledOnce();

    eventTarget.removeEventListener('lifeos:guest-session-expired', expiredEvent);
  });

  it('treats a visitor without a guest session as a normal entry flow', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(
      JSON.stringify({ code: 'NoGuestSession', error: 'No existing guest session.' }),
      { status: 401, headers: { 'Content-Type': 'application/json' } },
    )));

    await expect(resumeGuestSession()).rejects.toThrow('Tidak ada sesi guest yang dapat dilanjutkan.');
  });

  it('preserves status and machine code while exposing the localized backend message', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(
      JSON.stringify({
        code: 'validation_failed',
        error: 'Saldo atau Uang Bebas tidak mencukupi untuk tindakan ini.',
        traceId: 'trace-123',
      }),
      { status: 422, headers: { 'Content-Type': 'application/json' } },
    )));

    await expect(getFinanceState()).rejects.toMatchObject({
      status: 422,
      code: 'validation_failed',
      traceId: 'trace-123',
      message: 'Saldo atau Uang Bebas tidak mencukupi untuk tindakan ini.',
    });
  });

  it('does not surface programming details from an unstructured server error', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(
      'NullReferenceException at Secret.Internal.Path',
      { status: 500, headers: { 'Content-Type': 'text/plain' } },
    )));

    await expect(getFinanceState()).rejects.toMatchObject({
      status: 500,
      code: 'http_500',
      message: 'Terjadi kesalahan pada server. Silakan coba lagi.',
    });
  });

  it('rejects a successful response that violates the Finance projection contract', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(
      JSON.stringify({
        today: '2026-10-11',
        freeCash: 'not-a-number',
        accounts: [],
        setAsides: [],
        upcomingEvents: [],
        dueEvents: [],
        recentTransactions: [],
      }),
      { status: 200, headers: { 'Content-Type': 'application/json' } },
    )));

    await expect(getFinanceState()).rejects.toMatchObject({
      status: 502,
      code: 'invalid_api_response',
    } satisfies Partial<ApiRequestError>);
  });
});

describe('interpret', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('accepts a ready transaction response with nullable backend command fields', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(
      JSON.stringify({
        intent: 'CreateTransaction',
        state: 'Ready',
        preview: {
          type: 'Income',
          amount: 35_853,
          description: 'Dana awal',
          category: 'Pendapatan',
          account: 'Mandiri',
          toAccount: null,
          date: '2026-10-13',
          feeAmount: null,
        },
        command: {
          scopeId: 'scope-id',
          type: 'Income',
          amount: 35_853,
          description: 'Dana awal',
          categoryName: 'Pendapatan',
          occurredOn: '2026-10-13',
          relatedTransactionId: null,
          feeAmount: null,
          setAsideId: null,
          entries: [{ accountId: 'account-id', amount: 35_853 }],
        },
        setAsidePreview: null,
        setAsideCommand: null,
        eventPreview: null,
        eventCommand: null,
        clarifications: [],
      }),
      { status: 200, headers: { 'Content-Type': 'application/json' } },
    )));

    const response = await interpret('dana awal 35.853', 'Mandiri');

    expect(response.state).toBe('Ready');
    expect(response.preview?.category).toBe('Pendapatan');
    expect(response.command?.relatedTransactionId).toBeNull();
  });
});
