import { useState, useEffect } from 'react';
import type { AccountListProjection, TransactionProjection, AllocationProjection } from '../types';
import { getAccounts, getTransactions, getAllocations, logout } from '../api';
import AccountCard from './AccountCard';
import TransactionList from './TransactionList';
import TransactionDetail from './TransactionDetail';
import AddTransactionForm from './AddTransactionForm';
import CreateAccountForm from './CreateAccountForm';
import AllocationForm from './AllocationForm';
import AllocationList from './AllocationList';
import NaturalInput from './NaturalInput';

interface FinanceOverviewProps {
  isGuest: boolean;
  onLogout: () => void;
  onRequestLogin: () => void;
}

function formatCurrency(amount: number): string {
  return `Rp ${amount.toLocaleString('id-ID', { minimumFractionDigits: 0, maximumFractionDigits: 0 })}`;
}

type View = 'overview' | 'add-transaction' | 'create-account' | 'transaction-detail' | 'add-allocation' | 'edit-allocation';

export default function FinanceOverview({ isGuest, onLogout, onRequestLogin }: FinanceOverviewProps) {
  const [accounts, setAccounts] = useState<AccountListProjection | null>(null);
  const [transactions, setTransactions] = useState<TransactionProjection[]>([]);
  const [allocations, setAllocations] = useState<AllocationProjection[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [view, setView] = useState<View>('overview');
  const [selectedTransaction, setSelectedTransaction] = useState<TransactionProjection | null>(null);
  const [editingAllocation, setEditingAllocation] = useState<AllocationProjection | null>(null);
  const [refreshKey, setRefreshKey] = useState(0);
  const [showHelp, setShowHelp] = useState(false);

  useEffect(() => {
    let cancelled = false;

    Promise.all([getAccounts(true), getTransactions(), getAllocations()])
      .then(([acctResult, txResult, allocResult]) => {
        if (!cancelled) {
          setAccounts(acctResult);
          setTransactions(txResult.items);
          setAllocations(allocResult);
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

  const handleSelectTransaction = (tx: TransactionProjection) => {
    setSelectedTransaction(tx);
    setView('transaction-detail');
  };

  const handleBackToOverview = () => {
    setSelectedTransaction(null);
    setEditingAllocation(null);
    setView('overview');
  };

  const handleEditAllocation = (allocation: AllocationProjection) => {
    setEditingAllocation(allocation);
    setView('edit-allocation');
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
        <header className="app-header">
          <div className="header-left">
            <span className="brand-mark">Life OS</span>
            <div className="domain-crumb">
              <span className="active">Keuangan</span>
            </div>
          </div>
          <div className="header-right">
            <button className="btn-header-action" onClick={handleBackToOverview}>
              Kembali
            </button>
          </div>
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
        <header className="app-header">
          <div className="header-left">
            <span className="brand-mark">Life OS</span>
            <div className="domain-crumb">
              <span className="active">Keuangan</span>
            </div>
          </div>
          <div className="header-right">
            <button className="btn-header-action" onClick={handleBackToOverview}>
              Kembali
            </button>
          </div>
        </header>
        <CreateAccountForm
          onSuccess={() => { setView('overview'); refresh(); }}
          onCancel={() => setView('overview')}
        />
      </div>
    );
  }

  if (view === 'add-allocation') {
    return (
      <div className="finance-screen">
        <header className="app-header">
          <div className="header-left">
            <span className="brand-mark">Life OS</span>
            <div className="domain-crumb">
              <span className="active">Keuangan</span>
            </div>
          </div>
          <div className="header-right">
            <button className="btn-header-action" onClick={handleBackToOverview}>
              Kembali
            </button>
          </div>
        </header>
        <AllocationForm
          accounts={accounts?.accounts ?? []}
          onSuccess={() => { setView('overview'); refresh(); }}
          onCancel={() => setView('overview')}
        />
      </div>
    );
  }

  if (view === 'edit-allocation' && editingAllocation) {
    return (
      <div className="finance-screen">
        <header className="app-header">
          <div className="header-left">
            <span className="brand-mark">Life OS</span>
            <div className="domain-crumb">
              <span className="active">Keuangan</span>
            </div>
          </div>
          <div className="header-right">
            <button className="btn-header-action" onClick={handleBackToOverview}>
              Kembali
            </button>
          </div>
        </header>
        <AllocationForm
          accounts={accounts?.accounts ?? []}
          editAllocation={editingAllocation}
          onSuccess={() => { setView('overview'); refresh(); }}
          onCancel={() => setView('overview')}
        />
      </div>
    );
  }

  if (view === 'transaction-detail' && selectedTransaction) {
    const isAlreadyReversed = transactions.some(
      t => t.type === 'Reversal' && t.relatedTransactionId === selectedTransaction.id
    );
    return (
      <div className="finance-screen">
        <header className="app-header">
          <div className="header-left">
            <span className="brand-mark">Life OS</span>
            <div className="domain-crumb">
              <span className="active">Keuangan</span>
            </div>
          </div>
          <div className="header-right">
            <button className="btn-header-action" onClick={handleBackToOverview}>
              Kembali
            </button>
          </div>
        </header>
        <TransactionDetail
          transaction={selectedTransaction}
          isAlreadyReversed={isAlreadyReversed}
          allTransactions={transactions}
          onSuccess={() => { handleBackToOverview(); refresh(); }}
        />
      </div>
    );
  }

  const hasAccounts = accounts && accounts.accounts.length > 0;
  const totalAllocated = accounts?.totalAllocated ?? 0;
  const hasAllocations = totalAllocated > 0;
  const activeAccounts = accounts?.accounts.filter(a => !a.isArchived) ?? [];
  const archivedAccounts = accounts?.accounts.filter(a => a.isArchived) ?? [];

  return (
    <div className="finance-screen">
      <header className="app-header">
        <div className="header-left">
          <span className="brand-mark">Life OS</span>
          <div className="domain-crumb">
            <span className="active">Keuangan</span>
          </div>
        </div>
        <div className="header-right">
          <button
            className="btn-header-action"
            onClick={() => setShowHelp(!showHelp)}
          >
            {showHelp ? 'Tutup' : 'Cara Kerja'}
          </button>
          {isGuest && (
            <button className="btn-header-action" onClick={onRequestLogin}>Masuk</button>
          )}
          {!isGuest && (
            <button className="btn-header-action" onClick={handleLogout}>Keluar</button>
          )}
          {!isGuest && (
            <span className="user-badge">Ruang Pribadi</span>
          )}
        </div>
      </header>

      <div className={`help-drawer ${showHelp ? 'open' : ''}`}>
        <div className="help-inner">
          <div className="help-heading">Cara berinteraksi dengan Keuangan di Life OS</div>
          <p className="help-text">
            Cukup ceritakan apa yang terjadi seperti berbicara pada teman. Life OS memetakan nominal, kategori, dan rekening secara otomatis.
          </p>
          <div className="help-examples">
            <span className="help-code-chip">&ldquo;Makan siang 25rb tunai&rdquo;</span>
            <span className="help-code-chip">&ldquo;Kopi 22k qris bca&rdquo;</span>
            <span className="help-code-chip">&ldquo;Gaji 12jt masuk mandiri&rdquo;</span>
            <span className="help-code-chip">&ldquo;Transfer 300rb ke jago&rdquo;</span>
            <span className="help-code-chip">&ldquo;Sisihkan 350rb buat wifi dari mandiri&rdquo;</span>
          </div>
        </div>
      </div>

      {error && <div className="error-message">{error}</div>}

      <section className="summary-bar">
        <div className="summary-primary-group">
          <span className="summary-label">Total Dana</span>
          <span className="summary-figure">{formatCurrency(accounts?.totalBalance ?? 0)}</span>
        </div>
        {(accounts?.totalAvailable ?? 0) > 0 || hasAllocations ? (
          <div className="summary-breakdown">
            <div className="summary-metric">
              <span className="metric-dot avail" />
              <span>Tersedia:</span>
              <span className="metric-val">{formatCurrency(accounts?.totalAvailable ?? 0)}</span>
            </div>
            {hasAllocations && (
              <div className="summary-metric">
                <span className="metric-dot alloc" />
                <span>Dialokasikan:</span>
                <span className="metric-val">{formatCurrency(totalAllocated)}</span>
              </div>
            )}
          </div>
        ) : null}
      </section>

      {!hasAccounts ? (
        <div className="empty-view-container">
          <div className="empty-intro-box">
            <div className="empty-step-tag">Langkah Pertama</div>
            <h2 className="empty-title">Tentukan tempat uangmu berada</h2>
            <p className="empty-desc">
              Sebelum mencatat transaksi pertama, Life OS perlu tahu wadah keuangan apa saja yang kamu gunakan sehari-hari. Mulai dari yang paling sering kamu pakai, seperti dompet tunai atau rekening bank utama.
            </p>
            <button
              className="btn-empty-action"
              onClick={() => setView('create-account')}
            >
              + Buat Akun Pertama
            </button>
          </div>
        </div>
      ) : (
        <div className="finance-body">
          <section className="stream-column">
            <div className="natural-module">
              <div className="natural-box-head">
                <span className="natural-title">Ceritakan Aktivitasmu</span>
                <button
                  className="natural-fallback-link"
                  onClick={() => setView('add-transaction')}
                >
                  Catat Manual
                </button>
              </div>
              <NaturalInput
                accounts={accounts!.accounts}
                hasTransactions={transactions.length > 0}
                onSuccess={refresh}
              />
            </div>

            <AllocationList
              allocations={allocations}
              accounts={accounts!.accounts}
              onRefresh={refresh}
              onEdit={handleEditAllocation}
            />

            {allocations.length === 0 && (
              <div className="allocation-add-hint">
                <button className="btn-add-allocation" onClick={() => setView('add-allocation')}>
                  + Alokasi
                </button>
              </div>
            )}

            {allocations.length > 0 && (
              <div className="allocation-add-hint">
                <button className="btn-add-allocation" onClick={() => setView('add-allocation')}>
                  + Alokasi Baru
                </button>
              </div>
            )}

            <div className="ledger-section">
              <div className="ledger-head">
                <span className="section-title">Riwayat Transaksi</span>
                <span className="ledger-count">{transactions.length} catatan</span>
              </div>
              <TransactionList transactions={transactions} onSelect={handleSelectTransaction} />
            </div>
          </section>

          <aside className="context-column">
            <div className="accounts-head">
              <span className="section-title">Tempat Uangmu</span>
              <button className="btn-add-account" onClick={() => setView('create-account')}>+ Tambah</button>
            </div>

            <div className="accounts-register">
              {activeAccounts.map(acc => (
                <AccountCard key={acc.id} account={acc} onUpdate={refresh} />
              ))}
              {archivedAccounts.map(acc => (
                <AccountCard key={acc.id} account={acc} onUpdate={refresh} />
              ))}
            </div>

            {archivedAccounts.length > 0 && (
              <div className="archive-toggle-bar">
                <span>{archivedAccounts.length} akun tersimpan</span>
              </div>
            )}

            <div className="context-guidance">
              <span className="guidance-title">Prinsip Keuangan</span>
              <p className="guidance-p">
                Saldo dihitung murni dari riwayat peristiwa. Tiap pengeluaran dan pemasukan tidak merusak catatan masa lalu, melainkan merefleksikan alur hidup yang nyata.
              </p>
            </div>
          </aside>
        </div>
      )}
    </div>
  );
}
