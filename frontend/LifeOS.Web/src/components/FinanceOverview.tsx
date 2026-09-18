import { useState, useEffect } from 'react';
import type { AccountListProjection, TransactionProjection } from '../types';
import { getAccounts, getTransactions, logout } from '../api';
import AccountCard from './AccountCard';
import TransactionList from './TransactionList';
import AddTransactionForm from './AddTransactionForm';
import CreateAccountForm from './CreateAccountForm';
import NaturalInput from './NaturalInput';

interface FinanceOverviewProps {
  isGuest: boolean;
  onLogout: () => void;
}

function formatCurrency(amount: number): string {
  return `Rp ${amount.toLocaleString('id-ID', { minimumFractionDigits: 0, maximumFractionDigits: 0 })}`;
}

type View = 'overview' | 'add-transaction' | 'create-account';

export default function FinanceOverview({ isGuest, onLogout }: FinanceOverviewProps) {
  const [accounts, setAccounts] = useState<AccountListProjection | null>(null);
  const [transactions, setTransactions] = useState<TransactionProjection[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [view, setView] = useState<View>('overview');
  const [refreshKey, setRefreshKey] = useState(0);

  useEffect(() => {
    let cancelled = false;

    Promise.all([getAccounts(), getTransactions()])
      .then(([acctResult, txResult]) => {
        if (!cancelled) {
          setAccounts(acctResult);
          setTransactions(txResult.items);
          setLoading(false);
        }
      })
      .catch((e: unknown) => {
        if (!cancelled) {
          setError(e instanceof Error ? e.message : 'Failed to load data');
          setLoading(false);
        }
      });

    return () => { cancelled = true; };
  }, [refreshKey]);

  const refresh = () => {
    setError('');
    setLoading(true);
    setRefreshKey(k => k + 1);
  };

  const handleLogout = async () => {
    try {
      await logout();
    } catch {
      // ignore logout errors
    }
    onLogout();
  };

  if (loading && !accounts) {
    return (
      <div className="finance-loading">
        <div className="spinner" />
        <p>Loading finance data...</p>
      </div>
    );
  }

  if (error && !accounts) {
    return (
      <div className="finance-error">
        <p>{error}</p>
        <button className="btn btn-primary" onClick={refresh}>Retry</button>
      </div>
    );
  }

  if (view === 'add-transaction') {
    return (
      <div className="finance-screen">
        <header className="finance-header">
          <button className="btn btn-text back-btn" onClick={() => setView('overview')}>
            ← Back
          </button>
        </header>
        <AddTransactionForm
          accounts={accounts?.accounts ?? []}
          onSuccess={() => { setView('overview'); refresh(); }}
          onCancel={() => setView('overview')}
        />
      </div>
    );
  }

  if (view === 'create-account') {
    return (
      <div className="finance-screen">
        <header className="finance-header">
          <button className="btn btn-text back-btn" onClick={() => setView('overview')}>
            ← Back
          </button>
        </header>
        <CreateAccountForm
          onSuccess={() => { setView('overview'); refresh(); }}
          onCancel={() => setView('overview')}
        />
      </div>
    );
  }

  const hasAccounts = accounts && accounts.accounts.length > 0;

  return (
    <div className="finance-screen">
      <header className="finance-header">
        <h1>Finance</h1>
        {!isGuest && (
          <button className="btn btn-text" onClick={handleLogout}>Logout</button>
        )}
      </header>

      {error && <div className="error-message">{error}</div>}

      {hasAccounts && (
        <NaturalInput
          accounts={accounts!.accounts}
          onSuccess={refresh}
        />
      )}

      <section className="summary-section">
        <div className="summary-card total">
          <span className="summary-label">Total Money</span>
          <span className="summary-value">{formatCurrency(accounts?.totalBalance ?? 0)}</span>
        </div>
        <div className="summary-card available">
          <span className="summary-label">Available</span>
          <span className="summary-value">{formatCurrency(accounts?.totalAvailable ?? 0)}</span>
        </div>
      </section>

      <section className="section">
        <div className="section-header">
          <h2>Accounts</h2>
          <button className="btn btn-text" onClick={() => setView('create-account')}>+ Add</button>
        </div>
        {!hasAccounts ? (
          <div className="empty-state">
            <p>No accounts yet.</p>
            <button
              className="btn btn-primary"
              onClick={() => setView('create-account')}
            >
              Create Account
            </button>
          </div>
        ) : (
          <div className="account-list">
            {accounts!.accounts.map(acc => (
              <AccountCard key={acc.id} account={acc} />
            ))}
          </div>
        )}
      </section>

      <section className="section">
        <div className="section-header">
          <h2>Recent Transactions</h2>
        </div>
        <TransactionList transactions={transactions} />
      </section>

      <button
        className="fab"
        onClick={() => setView('add-transaction')}
        aria-label="Add transaction"
      >
        +
      </button>
    </div>
  );
}
