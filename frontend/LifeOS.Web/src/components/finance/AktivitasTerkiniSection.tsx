import { useMemo, useState } from 'react';

import {
  formatCurrencyRaw,
  formatSignedCurrency,
  type Account,
  type TimePeriod,
  type Transaction,
} from '../../finance';
import {
  cardBase,
  EmptyState,
  Icon,
  inputBase,
  Modal,
  Pagination,
  selectBase,
} from './shared';

const ACTIVITY_PER_PAGE = 5;

interface AktivitasTerkiniSectionProps {
  transactions: Transaction[];
  accounts: Account[];
  filter: TimePeriod | 'all';
  page: number;
  onFilterChange: (f: TimePeriod | 'all') => void;
  onPageChange: (p: number) => void;
  onVoid: (id: string, reason: string) => void;
}

type ModalMode = null | { kind: 'history' } | { kind: 'void'; tx: Transaction };

const FILTER_TABS: Array<{ key: TimePeriod | 'all'; label: string }> = [
  { key: 'all', label: 'Semua' },
  { key: 'today', label: 'Hari Ini' },
  { key: 'week', label: 'Minggu Ini' },
  { key: 'month', label: 'Bulan Ini' },
];

export default function AktivitasTerkiniSection({
  transactions,
  accounts,
  filter,
  page,
  onFilterChange,
  onPageChange,
  onVoid,
}: AktivitasTerkiniSectionProps) {
  const [modal, setModal] = useState<ModalMode>(null);

  const filtered = useMemo(() => {
    return transactions.filter((item) => {
      if (filter === 'all') return true;
      if (filter === 'today') return item.timePeriod === 'today';
      if (filter === 'week')
        return item.timePeriod === 'today' || item.timePeriod === 'week';
      return true;
    });
  }, [transactions, filter]);

  const totalPages = Math.max(1, Math.ceil(filtered.length / ACTIVITY_PER_PAGE));
  const safePage = Math.min(page, totalPages);
  const startIndex = (safePage - 1) * ACTIVITY_PER_PAGE;
  const pageItems = filtered.slice(startIndex, startIndex + ACTIVITY_PER_PAGE);

  return (
    <div className={`${cardBase} p-6 sm:p-7`}>
      <div className="flex flex-col sm:flex-row sm:items-center justify-between pb-5 border-b border-lo-border-hairline gap-2">
        <div className="flex items-center gap-2.5">
          <span className="w-2.5 h-2.5 rounded-full bg-lo-secondary ring-4 ring-lo-secondary/15" />
          <h2 className="font-headline text-xl font-medium text-lo-text-ink tracking-tight">
            Aktivitas Terkini
          </h2>
        </div>
        <span className="inline-flex items-center gap-1 px-2.5 py-1 rounded-full bg-lo-accent-wash text-[11px] font-medium text-lo-secondary self-start sm:self-auto">
          <Icon name="verified_user" className="text-[13px]" />
          Mutasi Riil Terverifikasi
        </span>
      </div>

      <div className="mt-5 p-1 bg-lo-surface-recessed/80 rounded-2xl flex items-center gap-1 text-xs">
        {FILTER_TABS.map((tab) => {
          const isActive = filter === tab.key;
          return (
            <button
              key={tab.key}
              type="button"
              onClick={() => onFilterChange(tab.key)}
              className={`flex-1 py-1.5 px-3 rounded-xl font-medium transition-all cursor-pointer text-center ${
                isActive
                  ? 'bg-white text-lo-text-ink shadow-xs'
                  : 'text-lo-text-subtle hover:text-lo-text-ink'
              }`}
            >
              {tab.label}
            </button>
          );
        })}
      </div>

      <div className="mt-4 space-y-2.5">
        {pageItems.length === 0 ? (
          <EmptyState
            icon="search_off"
            title="Tidak ada transaksi ditemukan"
            hint="Coba pilih filter periode yang berbeda."
          />
        ) : (
          pageItems.map((tx) => (
            <TxRow key={tx.id} tx={tx} onVoid={() => setModal({ kind: 'void', tx })} />
          ))
        )}
      </div>

      <Pagination
        page={safePage}
        totalPages={totalPages}
        totalItems={filtered.length}
        onChange={onPageChange}
        infoLabel={(from, to, total) => (
          <>
            Menampilkan{' '}
            <span className="font-semibold text-lo-text-ink">
              {total === 0 ? 0 : from}–{to}
            </span>{' '}
            dari <span className="font-semibold text-lo-text-ink">{total}</span> aktivitas
          </>
        )}
      />

      <div className="mt-5 pt-4 border-t border-dashed border-lo-border-hairline flex items-center justify-between">
        <span className="text-[11px] text-lo-text-subtle flex items-center gap-1">
          <Icon name="auto_stories" className="text-[14px]" />
          Arsip lengkap tersimpan rapi
        </span>
        <button
          type="button"
          onClick={() => setModal({ kind: 'history' })}
          className="inline-flex items-center gap-1 text-xs font-semibold text-lo-secondary hover:text-lo-primary transition-colors cursor-pointer group"
        >
          <span>Buka Riwayat Lengkap ({transactions.length})</span>
          <Icon
            name="arrow_forward"
            className="text-[15px] group-hover:translate-x-0.5 transition-transform"
          />
        </button>
      </div>

      {modal?.kind === 'history' ? (
        <HistoryModal
          transactions={transactions}
          accounts={accounts}
          onClose={() => setModal(null)}
          onVoid={(id) => {
            const tx = transactions.find((t) => t.id === id);
            if (tx) setModal({ kind: 'void', tx });
          }}
        />
      ) : null}

      {modal?.kind === 'void' ? (
        <VoidModal
          tx={modal.tx}
          onClose={() => setModal(null)}
          onConfirm={(reason) => {
            onVoid(modal.tx.id, reason);
            setModal(null);
          }}
        />
      ) : null}
    </div>
  );
}

function TxRow({
  tx,
  onVoid,
  compact = false,
}: {
  tx: Transaction;
  onVoid?: () => void;
  compact?: boolean;
}) {
  const isIncome = tx.amount > 0;
  const isVoided = Boolean(tx.isVoided);
  const isReversal = Boolean(tx.isReversal);

  let amountColor = isIncome ? 'text-lo-secondary font-semibold' : 'text-lo-text-ink font-semibold';
  let avatarBg = isIncome
    ? 'bg-lo-secondary-container/60 text-lo-secondary'
    : 'bg-lo-surface-recessed text-lo-text-subtle';
  let cardBg = compact ? 'hover:bg-lo-surface-cream/70' : 'bg-lo-surface/50 hover:bg-lo-surface-recessed/60';
  let titleStyle = `font-medium text-lo-text-ink ${compact ? 'text-xs sm:text-sm' : 'text-xs sm:text-sm'} truncate leading-snug`;

  if (isVoided) {
    amountColor = 'text-lo-text-subtle line-through opacity-75';
    avatarBg = 'bg-lo-surface-recessed/50 text-lo-text-subtle/70';
    cardBg = 'bg-lo-surface-cream/50 opacity-80';
    titleStyle = `font-medium text-lo-text-subtle line-through ${compact ? 'text-xs sm:text-sm' : 'text-xs sm:text-sm'} truncate leading-snug`;
  } else if (isReversal) {
    avatarBg = 'bg-lo-warning/10 text-lo-warning';
    amountColor = 'text-lo-warning font-semibold';
  }

  return (
    <div
      className={`${
        compact ? 'p-3 sm:px-4' : 'group p-3 sm:py-3 sm:px-4 rounded-2xl border border-lo-border-hairline/80'
      } ${cardBg} flex items-center justify-between text-xs transition-all duration-150`}
    >
      <div className="flex items-center gap-2.5 min-w-0 pr-2">
        <div
          className={`${
            compact ? 'w-8 h-8 rounded-xl' : 'w-9 h-9 rounded-xl'
          } ${avatarBg} flex items-center justify-center shrink-0 border border-lo-border-hairline/60`}
        >
          <Icon name={tx.icon} className={compact ? 'text-[16px]' : 'text-[18px]'} />
        </div>
        <div className="truncate">
          <p className={titleStyle}>{tx.title}</p>
          <div className="flex items-center gap-1.5 mt-0.5 text-[11px] text-lo-text-subtle truncate">
            <span>{tx.accountLabel}</span>
            <span className="inline-block w-1 h-1 rounded-full bg-lo-border-hairline" />
            <span>{compact ? tx.time || tx.date : tx.date}</span>
          </div>
        </div>
      </div>
      <div className="text-right shrink-0 flex flex-col items-end gap-1">
        <p
          className={`font-headline ${compact ? 'text-xs sm:text-sm' : 'text-sm'} tabular-nums tracking-tight ${amountColor}`}
        >
          {formatSignedCurrency(tx.amount)}
        </p>
        <div className="flex items-center gap-1.5">
          <span className="inline-block px-2 py-0.5 rounded-full bg-lo-surface-recessed text-lo-text-subtle text-[10px] font-medium">
            {tx.category}
          </span>
          {isVoided ? (
            <span
              className="inline-flex items-center gap-0.5 px-2 py-0.5 rounded-full bg-lo-warning/10 text-lo-warning text-[10px] font-medium"
              title={`Alasan: ${tx.voidReason || 'Dibatalkan'}`}
            >
              <Icon name="block" className="text-[11px]" />
              Dibatalkan
            </span>
          ) : isReversal ? (
            <span className="inline-flex items-center gap-0.5 px-2 py-0.5 rounded-full bg-lo-accent-wash text-lo-secondary text-[10px] font-medium">
              <Icon name="history" className="text-[11px]" />
              Pembalikan
            </span>
          ) : onVoid ? (
            <button
              type="button"
              onClick={onVoid}
              className="inline-flex items-center gap-1 px-2 py-0.5 rounded-lg text-lo-text-subtle hover:text-lo-warning hover:bg-lo-warning/10 text-[11px] font-medium transition-colors cursor-pointer"
              title="Batalkan transaksi ini"
            >
              <Icon name="undo" className="text-[13px]" />
              <span>Batalkan</span>
            </button>
          ) : null}
        </div>
      </div>
    </div>
  );
}

function HistoryModal({
  transactions,
  accounts,
  onClose,
  onVoid,
}: {
  transactions: Transaction[];
  accounts: Account[];
  onClose: () => void;
  onVoid: (id: string) => void;
}) {
  const [query, setQuery] = useState('');
  const [flow, setFlow] = useState<'all' | 'out' | 'in'>('all');
  const [accountFilter, setAccountFilter] = useState('all');

  const filtered = useMemo(() => {
    return transactions.filter((item) => {
      let matchFlow = true;
      if (flow === 'out') matchFlow = item.amount < 0;
      else if (flow === 'in') matchFlow = item.amount > 0;

      let matchAccount = true;
      if (accountFilter !== 'all') {
        matchAccount = item.accountLabel
          .toLowerCase()
          .includes(accountFilter.toLowerCase());
      }

      let matchQuery = true;
      if (query.trim() !== '') {
        const q = query.toLowerCase();
        matchQuery =
          item.title.toLowerCase().includes(q) ||
          item.accountLabel.toLowerCase().includes(q) ||
          item.category.toLowerCase().includes(q);
      }

      return matchFlow && matchAccount && matchQuery;
    });
  }, [transactions, flow, accountFilter, query]);

  const groups = useMemo(() => {
    const map = new Map<string, Transaction[]>();
    filtered.forEach((tx) => {
      const list = map.get(tx.dateGroup) || [];
      list.push(tx);
      map.set(tx.dateGroup, list);
    });
    return Array.from(map.entries());
  }, [filtered]);

  return (
    <Modal
      open
      onClose={onClose}
      title="Riwayat Aktivitas"
      subtitle="Arsip seluruh mutasi riil terverifikasi secara kronologis."
      icon="history"
      maxWidth="max-w-2xl"
      footer={
        <>
          <div className="flex items-center gap-2 mr-auto">
            <span className="w-2 h-2 rounded-full bg-lo-secondary/80" />
            <span className="text-xs text-lo-text-subtle">
              Menampilkan {filtered.length} dari {transactions.length} transaksi tersinkron
            </span>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="px-5 py-2 rounded-full bg-lo-primary-container text-lo-surface-cream text-xs font-medium hover:bg-lo-primary transition-colors cursor-pointer shadow-xs"
          >
            Tutup Riwayat
          </button>
        </>
      }
    >
      <div className="space-y-3">
        <div className="relative">
          <Icon
            name="search"
            className="absolute left-3 top-1/2 -translate-y-1/2 text-[18px] text-lo-text-subtle pointer-events-none"
          />
          <input
            type="text"
            className={`${inputBase} pl-9 pr-9 text-xs`}
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Cari mutasi, nama toko, keperluan..."
          />
          {query ? (
            <button
              type="button"
              onClick={() => setQuery('')}
              className="absolute right-2.5 top-1/2 -translate-y-1/2 text-lo-text-subtle hover:text-lo-text-ink cursor-pointer"
              title="Hapus pencarian"
            >
              <Icon name="cancel" className="text-[16px]" />
            </button>
          ) : null}
        </div>

        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2.5 text-xs">
          <div className="inline-flex p-1 bg-lo-surface-recessed rounded-xl gap-1 shrink-0 self-start sm:self-auto">
            {(
              [
                { key: 'all' as const, label: 'Semua' },
                { key: 'out' as const, label: 'Pengeluaran' },
                { key: 'in' as const, label: 'Pemasukan' },
              ]
            ).map((f) => (
              <button
                key={f.key}
                type="button"
                onClick={() => setFlow(f.key)}
                className={`px-3 py-1 rounded-lg text-xs font-medium transition-all cursor-pointer ${
                  flow === f.key
                    ? 'bg-white text-lo-text-ink shadow-xs'
                    : 'text-lo-text-subtle hover:text-lo-text-ink'
                }`}
              >
                {f.label}
              </button>
            ))}
          </div>
          <div className="relative w-full sm:w-auto">
            <select
              className={`${selectBase} text-xs py-1.5 pl-3 pr-8`}
              value={accountFilter}
              onChange={(e) => setAccountFilter(e.target.value)}
            >
              <option value="all">Semua Sumber Dana</option>
              {accounts.map((a) => (
                <option key={a.id} value={a.name}>
                  {a.name}
                </option>
              ))}
            </select>
          </div>
        </div>
      </div>

      <div className="mt-4 space-y-6 max-h-[50vh] overflow-y-auto pr-1">
        {filtered.length === 0 ? (
          <div className="py-16 text-center">
            <div className="w-12 h-12 rounded-2xl bg-lo-surface-recessed flex items-center justify-center text-lo-text-subtle mx-auto mb-3">
              <Icon name="search_off" className="text-[24px]" />
            </div>
            <p className="text-sm font-medium text-lo-text-ink">
              Tidak ada transaksi yang sesuai kata kunci
            </p>
            <p className="text-xs text-lo-text-subtle mt-1">
              Coba gunakan kata kunci pencarian atau sesuaikan filter sumber dana &amp; arah kas.
            </p>
          </div>
        ) : (
          groups.map(([groupName, items]) => (
            <div key={groupName} className="space-y-2">
              <div className="flex items-center gap-2 pt-1 pb-1">
                <span className="text-[11px] font-semibold tracking-wider text-lo-text-subtle uppercase select-none">
                  {groupName}
                </span>
                <div className="h-px bg-lo-border-hairline flex-1" />
              </div>
              <div className="bg-white rounded-2xl border border-lo-border-hairline divide-y divide-lo-border-hairline/60 overflow-hidden shadow-xs">
                {items.map((tx) => (
                  <TxRow
                    key={tx.id}
                    tx={tx}
                    compact
                    onVoid={
                      !tx.isVoided && !tx.isReversal
                        ? () => onVoid(tx.id)
                        : undefined
                    }
                  />
                ))}
              </div>
            </div>
          ))
        )}
        <div className="pt-3 pb-1 text-center">
          <div className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full bg-lo-surface-recessed/80 text-[11px] text-lo-text-subtle">
            <Icon name="history" className="text-[14px]" />
            <span>Semua riwayat mutasi riil terindeks teratur</span>
          </div>
        </div>
      </div>
    </Modal>
  );
}

function VoidModal({
  tx,
  onClose,
  onConfirm,
}: {
  tx: Transaction;
  onClose: () => void;
  onConfirm: (reason: string) => void;
}) {
  const [reason, setReason] = useState('Salah catat / nominal typo');
  const isIncome = tx.amount > 0;
  const absAmount = formatCurrencyRaw(Math.abs(tx.amount));

  return (
    <Modal
      open
      onClose={onClose}
      title="Batalkan Transaksi"
      subtitle="Koreksi pencatatan mutasi riil"
      icon="undo"
      maxWidth="max-w-md"
      footer={
        <>
          <button
            type="button"
            onClick={onClose}
            className="px-4 py-2 rounded-xl text-xs font-medium text-lo-text-subtle hover:text-lo-text-ink hover:bg-lo-surface-recessed transition-colors cursor-pointer"
          >
            Kembali
          </button>
          <button
            type="button"
            onClick={() => onConfirm(reason)}
            className="px-5 py-2 rounded-xl text-xs font-semibold bg-lo-warning text-white hover:bg-lo-warning/90 transition-colors shadow-xs inline-flex items-center gap-1.5 cursor-pointer"
          >
            <Icon name="check_circle" className="text-[16px]" />
            Konfirmasi Pembatalan
          </button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="p-3.5 rounded-2xl bg-white border border-lo-border-hairline space-y-2">
          <div className="flex items-center justify-between">
            <span className="text-[11px] text-lo-text-subtle font-medium">
              Transaksi Terpilih
            </span>
            <span className="text-[10px] px-2 py-0.5 rounded-full bg-lo-surface-recessed text-lo-text-subtle font-medium">
              {tx.category}
            </span>
          </div>
          <div className="flex items-center justify-between pt-1">
            <p className="text-xs sm:text-sm font-semibold text-lo-text-ink truncate max-w-[200px]">
              {tx.title}
            </p>
            <p className="font-headline text-sm sm:text-base tabular-nums font-semibold">
              {formatSignedCurrency(tx.amount)}
            </p>
          </div>
          <div className="flex items-center gap-2 pt-1 border-t border-lo-border-hairline/60 text-[11px] text-lo-text-subtle">
            <Icon name="account_balance_wallet" className="text-[14px]" />
            <span>{tx.accountLabel}</span>
          </div>
        </div>

        <div className="p-3 rounded-2xl bg-lo-accent-wash/60 border border-lo-secondary/20 flex items-start gap-2.5">
          <Icon name="info" className="text-[18px] text-lo-secondary mt-0.5 shrink-0" />
          <div className="text-xs text-lo-text-ink leading-relaxed">
            <strong>Dampak Saldo:</strong>{' '}
            {isIncome ? (
              <>
                Pemasukan sebesar <span className="font-medium font-headline">{absAmount}</span>{' '}
                akan dikurangkan kembali dari sumber dana <strong>{tx.accountLabel}</strong>.
              </>
            ) : (
              <>
                Pengeluaran sebesar{' '}
                <span className="font-medium font-headline">{absAmount}</span> akan
                dikembalikan seutuhnya ke saldo sumber dana <strong>{tx.accountLabel}</strong> dan
                uang bebas Anda.
              </>
            )}
          </div>
        </div>

        <div>
          <label className="block text-xs font-semibold text-lo-text-ink mb-2">
            Pilih Alasan Pembatalan
          </label>
          <div className="grid grid-cols-1 gap-2">
            {[
              'Salah catat / nominal typo',
              'Batal terlaksana / pesanan dibatalkan',
              'Duplikat transaksi / tercatat dua kali',
            ].map((r) => (
              <label
                key={r}
                className="flex items-center gap-2.5 p-2.5 rounded-xl border border-lo-border-hairline bg-white hover:bg-lo-surface cursor-pointer text-xs transition-colors"
              >
                <input
                  type="radio"
                  name="void_reason"
                  value={r}
                  checked={reason === r}
                  onChange={() => setReason(r)}
                  className="text-lo-secondary focus:ring-lo-secondary/20"
                />
                <span className="text-lo-text-ink font-medium">{r}</span>
              </label>
            ))}
          </div>
        </div>
      </div>
    </Modal>
  );
}
