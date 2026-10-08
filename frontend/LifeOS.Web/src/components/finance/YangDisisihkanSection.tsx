import { useCallback, useEffect, useMemo, useState } from 'react';
import { getSetAsideHistory } from '../../api';
import type { SetAsideEntryProjection } from '../../types';

import type { SetAsideCycleKind } from '../../types';
import {
  formatCurrencyRaw,
  formatNumberString,
  incrementalPlafonStatus,
  parseFormattedNumber,
  posCategoryLabel,
  quickAmountLabel,
  QUICK_AMOUNTS,
  type Account,
  type CreatePosInput,
  type PosCategory,
  type PosItem,
} from '../../finance';
import {
  btnPrimary,
  btnSecondary,
  cardBase,
  ConfirmDialog,
  EmptyState,
  Icon,
  inputBase,
  inputBaseSm,
  Modal,
  Pagination,
  ProgressBar,
  selectBase,
} from './shared';

const POS_PER_PAGE = 6;

type ModalMode =
  | null
  | { kind: 'create' }
  | { kind: 'savings'; pos: PosItem }
  | { kind: 'incremental'; pos: PosItem }
  | { kind: 'batch'; pos: PosItem }
  | { kind: 'single'; pos: PosItem }
  | { kind: 'edit'; pos: PosItem };

interface YangDisisihkanSectionProps {
  posItems: PosItem[];
  accounts: Account[];
  freeCash: number;
  filter: PosCategory | 'all';
  page: number;
  onFilterChange: (f: PosCategory | 'all') => void;
  onPageChange: (p: number) => void;
  onTopUp: (id: string, amount: number, sourceAccountId?: string) => Promise<boolean>;
  onWithdraw: (id: string, amount: number) => Promise<boolean>;
  /** sourceAccountId WAJIB — Sumber Dana tempat uang benar-benar keluar. */
  onUseIncremental: (
    id: string,
    amount: number,
    sourceAccountId: string,
    note?: string
  ) => Promise<boolean>;
  onExecuteBatch: (
    id: string,
    actualCost: number,
    sourceAccountId: string,
    note?: string
  ) => Promise<boolean>;
  onExecuteSingle: (
    id: string,
    actualCost: number,
    sourceAccountId: string,
    note?: string
  ) => Promise<boolean>;
  onCreate: (input: CreatePosInput) => Promise<boolean>;
  onDelete: (id: string) => Promise<boolean>;
  onUpdate: (id: string, command: { name: string; note: string; targetAmount?: number; removeTarget?: boolean; cycleKind: SetAsideCycleKind }) => Promise<boolean>;
}

const FILTERS: Array<{ key: PosCategory | 'all'; label: string }> = [
  { key: 'all', label: 'Semua Dana' },
  { key: 'saving', label: 'Tabungan & Simpanan' },
  { key: 'routine_incremental', label: 'Rutinitas Bertahap' },
  { key: 'routine_batch', label: 'Rutinitas Berkala' },
  { key: 'single_spend', label: 'Sekali Pakai' },
];

/** Rekening non-arsip yang bisa dipakai sebagai sumber transaksi riil. */
function sortedSourceAccounts(accounts: Account[]): Account[] {
  return accounts
    .filter((a) => !a.archived)
    .sort((a, b) => b.availableBalance - a.availableBalance);
}

/**
 * Pre-select hanya preferensi rekening yang tersimpan pada pos; tanpa preferensi
 * pengguna harus memilih rekening sendiri.
 */
function resolveManualSourceAccountId(pos: PosItem, accounts: Account[]): string {
  const active = accounts.filter((a) => !a.archived);
  const hinted = pos.defaultSourceAccountId
    ? active.find((a) => a.id === pos.defaultSourceAccountId)
    : undefined;
  if (hinted) return hinted.id;
  return '';
}

/** Rekening untuk top-up hanya referensi: tanpa preferensi, jangan menebak akun. */
function resolveReferenceAccountId(pos: PosItem, accounts: Account[]): string {
  if (!pos.defaultSourceAccountId) return '';
  return accounts.some((account) => !account.archived && account.id === pos.defaultSourceAccountId)
    ? pos.defaultSourceAccountId
    : '';
}

export default function YangDisisihkanSection({
  posItems,
  accounts,
  freeCash,
  filter,
  page,
  onFilterChange,
  onPageChange,
  onTopUp,
  onWithdraw,
  onUseIncremental,
  onExecuteBatch,
  onExecuteSingle,
  onCreate,
  onDelete,
  onUpdate,
}: YangDisisihkanSectionProps) {
  const [modal, setModal] = useState<ModalMode>(null);
  const [deleteTarget, setDeleteTarget] = useState<PosItem | null>(null);
  const [historyTarget, setHistoryTarget] = useState<PosItem | null>(null);
  const [showArchived, setShowArchived] = useState(false);

  const activeItems = useMemo(
    () => posItems.filter((p) => !p.archived),
    [posItems]
  );
  const archivedItems = useMemo(() => posItems.filter((p) => p.archived), [posItems]);
  const listedItems = showArchived ? archivedItems : activeItems;

  const filtered = useMemo(
    () => {
        const items = filter === 'all' ? listedItems : listedItems.filter((p) => p.category === filter);
      return [...items].sort((a, b) =>
        Number(a.category === 'routine_batch' && a.cycleExecuted) -
        Number(b.category === 'routine_batch' && b.cycleExecuted)
      );
    },
    [listedItems, filter]
  );

  const totalPages = Math.max(1, Math.ceil(filtered.length / POS_PER_PAGE));
  const safePage = Math.min(page, totalPages);
  const startIndex = (safePage - 1) * POS_PER_PAGE;
  const pageItems = filtered.slice(startIndex, startIndex + POS_PER_PAGE);

  const filterCount = (key: PosCategory | 'all') =>
    key === 'all' ? listedItems.length : listedItems.filter((p) => p.category === key).length;

  return (
    <div className={`${cardBase} p-4 sm:p-6 h-full flex flex-col`}>
      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-start justify-between gap-3 pb-4 border-b border-lo-border-hairline/60">
        <div className="min-w-0">
          <div className="flex items-center gap-2.5 flex-wrap">
            <h2 className="font-headline text-2xl font-medium text-lo-text-ink tracking-tight">
              Dana yang Disisihkan
            </h2>
            <span className="text-[12px] bg-lo-accent-wash/80 text-lo-secondary px-3 py-1 rounded-full font-medium border border-lo-secondary/20">
              {activeItems.length} Dana yang Disisihkan
            </span>
          </div>
          <p className="text-xs text-lo-text-subtle mt-1">
            Dana yang disisihkan untuk tabungan, kebutuhan mendatang, dan cadangan.
          </p>
        </div>
        <button
          type="button"
          onClick={() => setModal({ kind: 'create' })}
          className="inline-flex items-center gap-2 bg-lo-primary text-lo-surface-cream hover:bg-lo-primary-hover px-4 py-2.5 rounded-full text-xs font-medium transition-all cursor-pointer shadow-sm active:scale-95 shrink-0"
        >
          <Icon name="add" className="text-base" />
          <span>Tambah Dana</span>
        </button>
      </div>

      <div className="flex items-center gap-2 pt-4">
        <button type="button" onClick={() => { setShowArchived(false); onPageChange(1); }} className={`px-4 py-2 rounded-full text-xs cursor-pointer ${!showArchived ? 'bg-lo-secondary text-white' : 'bg-lo-surface-recessed text-lo-text-subtle'}`}>
          Aktif ({activeItems.length})
        </button>
        <button type="button" onClick={() => { setShowArchived(true); onPageChange(1); }} className={`px-4 py-2 rounded-full text-xs cursor-pointer ${showArchived ? 'bg-lo-secondary text-white' : 'bg-lo-surface-recessed text-lo-text-subtle'}`}>
          Arsip ({archivedItems.length})
        </button>
      </div>

      {/* Filter pills */}
      <div className="flex items-center gap-2 overflow-x-auto py-4 -mx-1 px-1">
        {FILTERS.map((f) => {
          const isActive = filter === f.key;
          return (
            <button
              key={f.key}
              type="button"
              onClick={() => onFilterChange(f.key)}
              className={`px-4 py-2 rounded-full text-xs transition-all shrink-0 cursor-pointer ${
                isActive
                  ? 'bg-lo-secondary text-lo-surface-cream font-medium shadow-xs'
                  : 'bg-lo-surface-recessed text-lo-text-subtle hover:text-lo-text-ink hover:bg-lo-container-low'
              }`}
            >
              {f.label} ({filterCount(f.key)})
            </button>
          );
        })}
      </div>

      {/* Items */}
      <div className="flex flex-col gap-3 flex-1">
        {pageItems.length === 0 ? (
          <EmptyState
            icon="savings"
            title={showArchived ? 'Belum ada pos di arsip' : 'Tidak ada pos pada filter ini'}
            hint={showArchived ? 'Pos yang ditutup akan tersimpan dan dapat dilihat di sini.' : 'Buat pos baru atau pilih filter lain.'}
          />
        ) : (
          pageItems.map((pos) => showArchived ? (
            <button key={pos.id} type="button" onClick={() => setHistoryTarget(pos)} className="w-full text-left rounded-2xl p-4 border border-lo-border-hairline bg-lo-surface-recessed/50 hover:bg-lo-surface-recessed cursor-pointer">
              <span className="font-semibold text-lo-text-ink">{pos.name}</span>
              <span className="block text-xs text-lo-text-subtle mt-1">Ditutup · {closeReasonLabel(pos.closeReason)} · saldo akhir {formatCurrencyRaw(pos.amount)}</span>
              <span className="text-[11px] text-lo-secondary">Lihat detail dan riwayat →</span>
            </button>
          ) : (
            <PosCard
              key={pos.id}
              pos={pos}
              onOpen={() => openPosModal(pos, setModal)}
              onDelete={() => setDeleteTarget(pos)}
              onEdit={() => setModal({ kind: 'edit', pos })}
              onHistory={() => setHistoryTarget(pos)}
            />
          ))
        )}
      </div>

      <Pagination
        page={safePage}
        totalPages={totalPages}
        totalItems={filtered.length}
        onChange={onPageChange}
        labels={{ prev: 'Sebelumnya', next: 'Berikutnya' }}
        infoLabel={(from, to, total) =>
          total === 0
            ? 'Tidak ada pos yang cocok.'
            : `Menampilkan ${from}–${to} dari ${total} dana`
        }
      />

      {historyTarget ? <PosHistoryModal pos={historyTarget} onClose={() => setHistoryTarget(null)} /> : null}
      {modal?.kind === 'edit' ? <EditPosModal pos={modal.pos} onClose={() => setModal(null)} onSave={async (command) => {
        const ok = await onUpdate(modal.pos.id, command);
        if (ok) setModal(null);
        return ok;
      }} /> : null}

      {/* Create modal */}
      <CreatePosModal
        open={modal?.kind === 'create'}
        onClose={() => setModal(null)}
        accounts={accounts}
        freeCash={freeCash}
        onCreate={async (input) => {
          const ok = await onCreate(input);
          if (ok) setModal(null);
          return ok;
        }}
      />

      {/* Savings modal */}
      {modal?.kind === 'savings' ? (
        <SavingsModal
          pos={modal.pos}
          accounts={accounts}
          freeCash={freeCash}
          onClose={() => setModal(null)}
          onTopUp={async (amt, sourceAccountId) => {
            const ok = await onTopUp(modal.pos.id, amt, sourceAccountId);
            if (ok) setModal(null);
            return ok;
          }}
          onWithdraw={async (amt) => {
            const ok = await onWithdraw(modal.pos.id, amt);
            if (ok) setModal(null);
            return ok;
          }}
          onDelete={() => {
            setDeleteTarget(modal.pos);
            setModal(null);
          }}
        />
      ) : null}

      {/* Incremental modal */}
      {modal?.kind === 'incremental' ? (
        <IncrementalModal
          pos={modal.pos}
          accounts={accounts}
          onClose={() => setModal(null)}
          onSubmit={async (amt, sourceAccountId, note) => {
            const ok = await onUseIncremental(modal.pos.id, amt, sourceAccountId, note);
            if (ok) setModal(null);
            return ok;
          }}
        />
      ) : null}

      {/* Batch modal */}
      {modal?.kind === 'batch' ? (
        <BatchModal
          pos={modal.pos}
          accounts={accounts}
          onClose={() => setModal(null)}
          onSubmit={async (cost, sourceAccountId, note) => {
            const ok = await onExecuteBatch(modal.pos.id, cost, sourceAccountId, note);
            if (ok) setModal(null);
            return ok;
          }}
        />
      ) : null}

      {/* Single spend modal */}
      {modal?.kind === 'single' ? (
        <SingleSpendModal
          pos={modal.pos}
          accounts={accounts}
          onClose={() => setModal(null)}
          onSubmit={async (cost, sourceAccountId, note) => {
            const ok = await onExecuteSingle(modal.pos.id, cost, sourceAccountId, note);
            if (ok) setModal(null);
            return ok;
          }}
        />
      ) : null}

      {/* Delete confirm */}
      <ConfirmDialog
        open={deleteTarget !== null}
        onClose={() => setDeleteTarget(null)}
        onConfirm={async () => {
          if (!deleteTarget) return false;
          const ok = await onDelete(deleteTarget.id);
          if (ok) setDeleteTarget(null);
          return ok;
        }}
        title={`Tutup pos ${deleteTarget?.name ?? ''}?`}
        description="Pos akan dipindahkan ke arsip. Dana yang tersisa dilepas kembali ke Uang Bebas; riwayatnya tetap dapat dilihat."
        icon="delete"
        actionLabel="Tutup Pos"
        danger
      />
    </div>
  );
}

function openPosModal(pos: PosItem, setModal: (m: ModalMode) => void) {
  if (pos.category === 'saving') setModal({ kind: 'savings', pos });
  else if (pos.category === 'routine_incremental') setModal({ kind: 'incremental', pos });
  else if (pos.category === 'routine_batch') setModal({ kind: 'batch', pos });
  else setModal({ kind: 'single', pos });
}

function useSubmitState() {
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const submit = async (action: () => Promise<boolean>) => {
    if (saving) return;
    setSaving(true);
    setError('');
    try {
      if (await action()) return true;
      setError('Perubahan belum tersimpan. Periksa pesan kesalahan, lalu coba lagi. Isian tetap tersimpan di formulir ini.');
      return false;
    } finally {
      setSaving(false);
    }
  };
  return { saving, error, submit };
}

function SubmitError({ message }: { message: string }) {
  return message ? <p role="alert" className="text-xs text-lo-error">{message}</p> : null;
}

function closeReasonLabel(reason?: string) {
  if (reason === 'Spent') return 'Belanja selesai';
  if (reason === 'Withdrawn') return 'Dana ditarik';
  return 'Dibatalkan';
}

function ledgerTypeLabel(type: SetAsideEntryProjection['type']) {
  switch (type) {
    case 'Opened': return 'Dana dialokasikan';
    case 'Added': return 'Alokasi ditambahkan';
    case 'Withdrawn': return 'Alokasi ditarik';
    case 'Spent': return 'Pengeluaran dari pos';
    case 'CycleFunding': return 'Pendanaan siklus';
    case 'Released': return 'Sisa alokasi dilepas';
    case 'Closed': return 'Pos ditutup';
  }
}

function transactionTypeLabel(type: NonNullable<SetAsideEntryProjection['transaction']>['type']) {
  switch (type) {
    case 'Income': return 'Pemasukan';
    case 'Expense': return 'Pengeluaran';
    case 'Transfer': return 'Transfer';
    case 'Refund': return 'Pengembalian';
    case 'Reversal': return 'Pembatalan transaksi';
    case 'Adjustment': return 'Penyesuaian';
  }
}

function ledgerNoteLabel(entry: SetAsideEntryProjection) {
  const note = entry.note?.trim();
  if (!note) return '';
  if (note.startsWith('Reversal of allocation entry')) return 'Koreksi otomatis dari pembatalan transaksi';
  if (note === 'Initial set-aside') return 'Saldo awal pos';
  if (note === 'Unused cycle allocation returned to available money') return 'Sisa dana periode dikembalikan ke Uang Bebas';
  if (note === 'Cycle funding from available money') return 'Diambil dari Uang Bebas untuk memenuhi target periode';
  if (note === 'Cycle shortfall funded from new income') return 'Kekurangan periode dipenuhi dari pemasukan baru';
  if (note === 'Single-spend set-aside completed') return 'Sisa alokasi dilepas setelah belanja';
  if (note.startsWith('Closed: ')) return `Alasan: ${closeReasonLabel(note.slice('Closed: '.length))}`;
  return note;
}

function PosHistoryModal({ pos, onClose }: { pos: PosItem; onClose: () => void }) {
  const [entries, setEntries] = useState<SetAsideEntryProjection[]>([]);
  const [cursor, setCursor] = useState<string | null>(null);
  const [hasMore, setHasMore] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const load = useCallback(async (nextCursor?: string | null) => {
    try {
      const page = await getSetAsideHistory(pos.id, nextCursor);
      setEntries((current) => nextCursor ? [...current, ...page.items] : page.items);
      setCursor(page.nextCursor);
      setHasMore(page.hasMore);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Riwayat gagal dimuat.');
    } finally {
      setLoading(false);
    }
  }, [pos.id]);

  useEffect(() => {
    let cancelled = false;
    getSetAsideHistory(pos.id).then((page) => {
      if (cancelled) return;
      setEntries(page.items);
      setCursor(page.nextCursor);
      setHasMore(page.hasMore);
    }).catch((e: unknown) => {
      if (!cancelled) setError(e instanceof Error ? e.message : 'Riwayat gagal dimuat.');
    }).finally(() => {
      if (!cancelled) setLoading(false);
    });
    return () => { cancelled = true; };
  }, [pos.id]);
  const reload = (nextCursor?: string | null) => {
    setLoading(true);
    setError('');
    void load(nextCursor);
  };
  return (
    <Modal open onClose={onClose} title={pos.name} subtitle="Detail pos dan riwayat perubahan alokasi" icon="history" maxWidth="max-w-2xl">
      <div className="space-y-4">
        <div className="grid grid-cols-2 gap-3 rounded-xl bg-lo-surface-recessed p-4 text-xs">
          <div><span className="block text-lo-text-subtle">Status</span><strong>{pos.archived ? `Diarsipkan · ${closeReasonLabel(pos.closeReason)}` : 'Aktif'}</strong></div>
          <div><span className="block text-lo-text-subtle">Saldo sekarang</span><strong>{formatCurrencyRaw(pos.amount)}</strong></div>
          <div><span className="block text-lo-text-subtle">Target</span><strong>{pos.targetAmount ? formatCurrencyRaw(pos.targetAmount) : 'Tidak ditentukan'}</strong></div>
          <div><span className="block text-lo-text-subtle">Siklus</span><strong>{pos.cycle ?? 'Tanpa siklus'}</strong></div>
          <div className="col-span-2"><span className="block text-lo-text-subtle">Catatan</span><strong>{pos.description || '—'}</strong></div>
        </div>
        <div className="flex items-end justify-between gap-2">
          <h4 className="text-sm font-semibold">Riwayat perubahan saldo</h4>
          <span className="text-[11px] text-lo-text-subtle">Terbaru lebih dulu</span>
        </div>
        <div className="space-y-2">
          {entries.map((entry) => {
            const transaction = entry.transaction;
            const note = ledgerNoteLabel(entry);
            const title = transaction?.description || transaction?.categoryName || (transaction ? transactionTypeLabel(transaction.type) : '');
            return (
              <article key={entry.id} className="rounded-xl border border-lo-border-hairline bg-white/80 p-3 sm:p-4">
                <div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
                  <div className="min-w-0">
                    <strong className="block text-xs sm:text-sm text-lo-text-ink">{ledgerTypeLabel(entry.type)}</strong>
                    <time className="mt-0.5 block text-[11px] text-lo-text-subtle" dateTime={entry.createdAt}>
                      {new Date(entry.createdAt).toLocaleString('id-ID', { timeZone: 'Asia/Jakarta', dateStyle: 'medium', timeStyle: 'short' })}
                    </time>
                    {note ? <p className="mt-1 break-words text-xs text-lo-text-subtle">{note}</p> : null}
                  </div>
                  <div className="flex shrink-0 items-center justify-between gap-4 sm:flex-col sm:items-end sm:gap-1">
                    <strong className={`text-sm tabular-nums ${entry.amount > 0 ? 'text-lo-secondary' : entry.amount < 0 ? 'text-lo-text-ink' : 'text-lo-text-subtle'}`}>
                      {entry.amount > 0 ? '+' : entry.amount < 0 ? '−' : ''}{formatCurrencyRaw(Math.abs(entry.amount))}
                    </strong>
                    <span className="text-[10px] text-lo-text-subtle">Saldo setelah: {formatCurrencyRaw(entry.balanceAfter)}</span>
                  </div>
                </div>
                {transaction ? (
                  <details className="mt-3 border-t border-lo-border-hairline pt-2 text-xs">
                    <summary className="cursor-pointer select-none font-medium text-lo-secondary">Transaksi terkait: {title}</summary>
                    <div className="mt-2 rounded-lg bg-lo-surface-recessed p-2.5 text-lo-text-subtle">
                      <div className="flex flex-wrap justify-between gap-2"><span>{transactionTypeLabel(transaction.type)}{transaction.categoryName ? ` · ${transaction.categoryName}` : ''}</span><strong className="text-lo-text-ink">{formatCurrencyRaw(transaction.amount)}</strong></div>
                      <div className="mt-1">Tanggal transaksi: {transaction.occurredOn}</div>
                      {transaction.relatedDescription ? <div className="mt-1">Transaksi asal: {transaction.relatedDescription}</div> : null}
                    </div>
                  </details>
                ) : null}
              </article>
            );
          })}
        </div>
        {loading ? <p className="text-xs text-lo-text-subtle">Memuat riwayat…</p> : null}
        {error ? <div role="alert" className="text-xs text-lo-error">{error} <button type="button" onClick={() => reload(cursor)} className="underline">Coba lagi</button></div> : null}
        {!loading && entries.length === 0 && !error ? <p className="text-xs text-lo-text-subtle">Belum ada entry riwayat.</p> : null}
        {hasMore ? <button type="button" disabled={loading} onClick={() => reload(cursor)} className={btnSecondary}>{loading ? 'Memuat…' : 'Muat riwayat berikutnya'}</button> : null}
      </div>
    </Modal>
  );
}

function EditPosModal({
  pos,
  onClose,
  onSave,
}: {
  pos: PosItem;
  onClose: () => void;
  onSave: (command: { name: string; note: string; targetAmount?: number; removeTarget?: boolean; cycleKind: SetAsideCycleKind }) => Promise<boolean>;
}) {
  const { saving, error, submit } = useSubmitState();
  const [name, setName] = useState(pos.name);
  const [note, setNote] = useState(pos.description);
  const [target, setTarget] = useState(pos.targetAmount ? formatNumberString(String(pos.targetAmount)) : '');
  const [cycleKind, setCycleKind] = useState<SetAsideCycleKind>(pos.cycleKind ?? 'None');
  const nextTarget = parseFormattedNumber(target);
  const hasCycle = pos.category === 'routine_incremental' || pos.category === 'routine_batch';
  const requiresCycleTarget = hasCycle || pos.category === 'single_spend';
  const targetLabel = pos.category === 'saving'
    ? 'Target tabungan (opsional)'
    : pos.category === 'routine_incremental'
      ? 'Plafon per periode'
      : pos.category === 'routine_batch'
        ? 'Estimasi biaya per realisasi'
        : 'Jumlah dana sekali pakai';
  const targetHelp = pos.category === 'saving'
    ? 'Target hanya membantu memantau progres, tidak mengubah saldo.'
    : pos.category === 'routine_incremental'
      ? 'Batas alokasi untuk setiap siklus. Perubahan tidak menambah/mengurangi saldo saat ini.'
      : pos.category === 'routine_batch'
        ? 'Estimasi dana untuk satu realisasi dalam setiap siklus.'
        : 'Target pos belanja satu kali. Perubahan tidak mengubah saldo yang sudah dialokasikan.';
  const canSave = name.trim().length > 0
    && (!requiresCycleTarget || nextTarget > 0)
    && (!hasCycle || cycleKind !== 'None');
  const projectedShortfall = hasCycle ? Math.max(0, nextTarget - pos.amount) : 0;

  return (
    <Modal open onClose={onClose} title={`Edit ${pos.name}`} icon="edit" maxWidth="max-w-xl">
      <div className="space-y-4">
        <SubmitError message={error} />
        <label className="block text-xs font-medium">Nama dana<input className={`${inputBase} mt-1`} value={name} onChange={(e) => setName(e.target.value)} /></label>
        <label className="block text-xs font-medium">Catatan<textarea className={`${inputBase} mt-1`} rows={2} value={note} onChange={(e) => setNote(e.target.value)} /></label>
        <label className="block text-xs font-medium">{targetLabel} (Rp)
          <input className={`${inputBase} mt-1`} inputMode="numeric" value={target} onChange={(e) => setTarget(formatNumberString(e.target.value))} placeholder={pos.category === 'saving' ? 'Opsional' : 'Masukkan nominal'} />
          <span className="mt-1 block text-[11px] font-normal text-lo-text-subtle">{targetHelp}</span>
        </label>
        {hasCycle ? (
          <label className="block text-xs font-medium">Siklus
            <select className={`${selectBase} mt-1`} value={cycleKind} onChange={(e) => setCycleKind(e.target.value as SetAsideCycleKind)}>
              <option value="Weekly">Mingguan</option><option value="Monthly">Bulanan</option><option value="Quarterly">Triwulanan</option><option value="SemiAnnual">Semesteran</option><option value="Annual">Tahunan</option>
            </select>
          </label>
        ) : null}
        <div className="rounded-xl bg-lo-accent-wash/60 p-3 text-xs text-lo-text-subtle">
          {hasCycle ? 'Sebelum siklus/target baru berlaku, siklus lama dinormalisasi. Perubahan metadata tidak mengubah saldo secara langsung.' : 'Perubahan nama, catatan, dan target tidak memindahkan atau mengubah saldo pos.'}
          <div className="mt-1 font-medium text-lo-text-ink">Saldo saat ini: {formatCurrencyRaw(pos.amount)} · {targetLabel.toLowerCase()}: {nextTarget > 0 ? formatCurrencyRaw(nextTarget) : 'tidak ditentukan'}</div>
          {projectedShortfall > 0 ? <div className="mt-1">Perkiraan kekurangan menuju target: {formatCurrencyRaw(projectedShortfall)}. Ini informasi, bukan saldo yang sudah tersedia.</div> : null}
        </div>
        <div className="sticky bottom-0 -mx-4 -mb-4 flex flex-col-reverse gap-2 border-t border-lo-border-hairline bg-lo-surface-cream p-4 sm:static sm:mx-0 sm:mb-0 sm:flex-row sm:justify-end sm:border-0 sm:bg-transparent sm:p-0">
          <button type="button" onClick={onClose} className={`${btnSecondary} justify-center`}>Batal</button>
          <button type="button" disabled={!canSave || saving} onClick={() => void submit(() => onSave({ name: name.trim(), note, targetAmount: nextTarget || undefined, removeTarget: !requiresCycleTarget && nextTarget <= 0, cycleKind: hasCycle ? cycleKind : 'None' }))} className={`${btnPrimary} justify-center`}>{saving ? 'Menyimpan…' : 'Simpan perubahan'}</button>
        </div>
      </div>
    </Modal>
  );
}

function PosCard({
  pos,
  onOpen,
  onDelete,
  onEdit,
  onHistory,
}: {
  pos: PosItem;
  onOpen: () => void;
  onDelete: () => void;
  onEdit: () => void;
  onHistory: () => void;
}) {
  const categoryLabel = posCategoryLabel(pos.category);
  const hasTarget = typeof pos.targetAmount === 'number' && pos.targetAmount > 0;
  const targetPct = hasTarget
    ? Math.min(100, Math.round((pos.amount / (pos.targetAmount as number)) * 100))
    : 0;
  // Rutinitas Bertahap — lihat incrementalPlafonStatus (financeCalc):
  // bar = SISA dana terhadap plafon, warna mengikuti margin aman,
  // overspend hanya warning dengan nominal, tanpa saldo negatif / overflow.
  const { limit, remaining, remainingPct, tone: remainingTone, overage } =
    incrementalPlafonStatus({
      amount: pos.amount,
      plafon: pos.plafon,
      targetAmount: pos.targetAmount,
      usedAmount: pos.usedAmount,
    });

  const actionLabel =
    pos.category === 'routine_incremental'
      ? 'Catat Pakai'
      : pos.category === 'routine_batch'
        ? pos.cycleExecuted ? 'Periode Selesai' : 'Eksekusi Servis'
        : pos.category === 'single_spend'
          ? 'Realisasikan Belanja'
          : hasTarget
            ? 'Tarik / Tambah'
            : 'Kelola Simpanan';

  const rightStatus =
    pos.category === 'routine_incremental'
      ? `Total Pengeluaran ${formatCurrencyRaw(pos.usedAmount || 0)}`
      : pos.category === 'routine_batch'
        ? pos.cycleExecuted ? 'Sudah dieksekusi periode ini' : ''
        : pos.category === 'single_spend'
          ? 'Dana terkumpul penuh'
          : pos.status || (hasTarget ? `${targetPct}% tercapai` : 'Fleksibel');

  // Penanda visual bahwa pemakaian sudah melewati plafon.
  const overPlafon = pos.category === 'routine_incremental' && overage > 0;

  return (
    <div className={`group transition-all duration-200 rounded-2xl p-4 sm:p-5 border ${pos.category === 'routine_batch' && pos.cycleExecuted
      ? 'bg-lo-surface-recessed/40 border-lo-border-hairline/40'
      : 'bg-lo-surface-cream/70 hover:bg-lo-surface-recessed/50 border-lo-border-hairline/60 hover:border-lo-secondary/30'}`}>
      <div className="flex items-start justify-between gap-4">
        <button
          type="button"
          onClick={onOpen}
          className="flex items-start gap-4 min-w-0 text-left flex-1 cursor-pointer"
        >
          <div className={`w-11 h-11 rounded-xl flex items-center justify-center shrink-0 border ${pos.category === 'routine_batch' && pos.cycleExecuted
            ? 'bg-lo-container-low text-lo-text-subtle border-lo-border-hairline/40'
            : 'bg-lo-accent-wash text-lo-secondary border-lo-secondary/15'}`}>
            <Icon name={pos.icon} className="text-2xl" />
          </div>
          <div className="min-w-0">
            <div className="flex items-center gap-2.5 flex-wrap">
              <h3 className="font-headline-sm text-base font-semibold text-lo-text-ink truncate">
                {pos.name}
              </h3>
              <span className={`inline-flex items-center gap-1 text-[11px] px-2.5 py-0.5 rounded-full font-medium ${pos.category === 'routine_batch' && pos.cycleExecuted
                ? 'bg-lo-container-low text-lo-text-subtle'
                : 'bg-lo-accent-wash text-lo-secondary'}`}>
                {categoryLabel}
              </span>
            </div>
            <p className="text-xs text-lo-text-subtle mt-1 truncate">{pos.description}</p>
          </div>
        </button>
        <div className="text-right shrink-0">
          <div className="font-headline text-[22px] font-semibold tabular-nums tracking-tight text-lo-text-ink leading-tight">
            {pos.category === 'routine_incremental'
              ? limit > 0
                ? `Sisa ${formatCurrencyRaw(remaining)} / ${formatCurrencyRaw(limit)}`
                : `Sisa ${formatCurrencyRaw(remaining)}`
              : formatCurrencyRaw(pos.amount)}
          </div>
          <div className="text-[11px] text-lo-text-subtle mt-0.5">
            {pos.category === 'routine_incremental'
              ? ''
              : pos.category === 'routine_batch'
                ? ''
                : hasTarget
                  ? `Target ${formatCurrencyRaw(pos.targetAmount as number)} (${targetPct}%)`
                  : pos.category === 'single_spend'
                    ? `${pos.status || 'Siap'}`
                    : ''}
          </div>
        </div>
      </div>

      {/* Progress — hanya Bertahap / Sekali Pakai / Tabungan ber-target.
          Rutinitas Berkala: dana sudah tersedia, tanpa progress bar. */}
      {pos.category === 'saving' && hasTarget ? (
        <div className="mt-4 pt-3 border-t border-lo-border-hairline/40">
          <ProgressBar percent={targetPct} />
        </div>
      ) : null}
      {pos.category === 'routine_incremental' && limit > 0 ? (
        <div className="mt-4 pt-3 border-t border-lo-border-hairline/40">
          <ProgressBar percent={remainingPct} tone={remainingTone} />
          {overage > 0 ? (
            <div className="mt-1.5 flex items-start gap-1.5 text-[11px] font-medium text-lo-error">
              <Icon name="warning" className="text-[14px] leading-4 shrink-0" />
              <span className="flex flex-col gap-0.5">
                <span>
                  Pengeluaran sudah melebihi dana
                </span>
                <span>
                  <span className="font-semibold">{formatCurrencyRaw(overage)}</span>
                  {' '}ditanggung uang yang belum dialokasikan.</span>
              </span>
            </div>
          ) : null}
        </div>
      ) : null}
      {pos.category === 'single_spend' && hasTarget ? (
        <div className="mt-4 pt-3 border-t border-lo-border-hairline/40">
          <ProgressBar percent={targetPct} />
        </div>
      ) : null}

      {/* Bottom action row */}
      <div
        className="mt-3 pt-3 border-t border-lo-border-hairline/50 flex flex-col sm:flex-row sm:items-center justify-between gap-2"
        onClick={(e) => e.stopPropagation()}
      >
        <button
          type="button"
          onClick={onOpen}
          disabled={pos.category === 'routine_batch' && pos.cycleExecuted}
          className={`w-full sm:w-auto justify-center ${pos.category === 'routine_batch' && pos.cycleExecuted
            ? 'inline-flex items-center gap-1.5 rounded-full bg-lo-container-low text-lo-text-subtle px-3.5 py-1.5 text-xs font-medium cursor-not-allowed'
            : pos.category === 'routine_batch' || pos.category === 'single_spend'
            ? `${btnPrimary} px-3.5 py-1.5 text-xs`
            : `${btnSecondary} px-3.5 py-1.5 text-xs`} disabled:opacity-50 disabled:cursor-not-allowed`}
        >
          <Icon
            name={
              pos.category === 'routine_incremental'
                ? 'edit_note'
                : pos.category === 'routine_batch'
                  ? pos.cycleExecuted ? 'check_circle' : 'build'
                  : pos.category === 'single_spend'
                    ? 'shopping_cart_checkout'
                    : 'savings'
            }
            className="text-[16px]"
          />
          <span>{actionLabel}</span>
        </button>
        <div className="flex w-full sm:w-auto items-center justify-between sm:justify-end gap-1.5">
          <span
            className={`min-w-0 flex-1 sm:flex-none truncate text-[10px] sm:text-[11px] ${
              overPlafon ? 'font-semibold text-lo-error' : 'text-lo-text-subtle'
            }`}
          >
            {rightStatus}
          </span>
          <button type="button" onClick={onEdit} title="Edit detail pos" aria-label={`Edit ${pos.name}`} className="w-8 h-8 rounded-lg flex items-center justify-center text-lo-text-subtle hover:text-lo-secondary hover:bg-lo-accent-wash transition-all cursor-pointer">
            <Icon name="edit" className="text-[16px]" />
          </button>
          <button type="button" onClick={onHistory} title="Lihat detail dan riwayat" aria-label={`Riwayat ${pos.name}`} className="w-8 h-8 rounded-lg flex items-center justify-center text-lo-text-subtle hover:text-lo-secondary hover:bg-lo-accent-wash transition-all cursor-pointer">
            <Icon name="history" className="text-[16px]" />
          </button>
          <button
            type="button"
            onClick={onDelete}
            title="Tutup pos"
            className={`w-8 h-8 rounded-lg flex items-center justify-center text-lo-text-subtle hover:text-lo-error hover:bg-red-50 transition-all cursor-pointer ${pos.category === 'routine_batch' && pos.cycleExecuted ? 'opacity-60 hover:opacity-100' : ''}`}
          >
            <Icon name="delete" className="text-[16px]" />
          </button>
        </div>
      </div>
    </div>
  );
}

/* ── Create pos modal ──
 * Rekening referensi bersifat OPSIONAL — tidak membatasi atau memindahkan dana.
 * Pos tidak pernah terikat ke rekening. */

function CreatePosModal({
  open,
  onClose,
  accounts,
  freeCash,
  onCreate,
}: {
  open: boolean;
  onClose: () => void;
  accounts: Account[];
  freeCash: number;
  onCreate: (input: CreatePosInput) => Promise<boolean>;
}) {
  const { saving, error, submit } = useSubmitState();
  const [name, setName] = useState('');
  const [category, setCategory] = useState<PosCategory>('saving');
  const [accountLabel, setAccountLabel] = useState('');
  const [target, setTarget] = useState('');
  const [plafon, setPlafon] = useState('');
  const [cycle, setCycle] = useState('Bulanan');
  const [cycleKind, setCycleKind] = useState<SetAsideCycleKind>('Monthly');
  const [description, setDescription] = useState('');

  // Rekening non-arsip untuk dipilih sebagai referensi (opsional).
  const baseAccounts = accounts.filter((a) => !a.archived);
  const selectedAccount = baseAccounts.find((a) => a.name === accountLabel);

  // Dana yang disiapkan = Plafon Siklus / Estimasi Biaya / Target Anggaran
  // (bukan input user). Rutinitas Berkala: dana langsung disisihkan saat create.
  const preparesInitialFunds =
    category === 'routine_incremental' ||
    category === 'routine_batch' ||
    category === 'single_spend';
  const prepareAmount = preparesInitialFunds ? parseFormattedNumber(plafon) : 0;
  const exceedsAvailable = prepareAmount > 0 && prepareAmount > freeCash;
  const missingNominal = preparesInitialFunds && prepareAmount <= 0;

  const canSubmit = name.trim().length >= 2 && !missingNominal && !exceedsAvailable;

  const reset = () => {
    setName('');
    setCategory('saving');
    setAccountLabel('');
    setTarget('');
    setPlafon('');
    setCycle('Bulanan');
    setCycleKind('Monthly');
    setDescription('');
  };

  return (
    <Modal
      open={open}
      onClose={() => {
        reset();
        onClose();
      }}
      title="Sisihkan Dana"
      icon="add_circle"
      maxWidth="max-w-2xl"
      footer={
        <>
          <button
            type="button"
            onClick={() => {
              reset();
              onClose();
            }}
            className="px-4 py-2 rounded-full text-xs text-lo-text-subtle hover:text-lo-text-ink transition-colors cursor-pointer"
          >
            Batal
          </button>
          <button
            type="button"
            disabled={!canSubmit || saving}
            onClick={() => void submit(() => onCreate({
                name: name.trim(),
                description: description.trim(),
                category,
                sourceAccountLabel: selectedAccount?.name,
                targetAmount:
                  category === 'saving' && target ? parseFormattedNumber(target) : undefined,
                plafon:
                  category === 'routine_incremental' ||
                  category === 'routine_batch' ||
                  category === 'single_spend'
                    ? parseFormattedNumber(plafon) || undefined
                    : undefined,
                cycle: category === 'routine_batch' ? cycle : undefined,
                cycleKind: category === 'routine_incremental' ? cycleKind : undefined,
              }))}
            className={`${btnPrimary} disabled:opacity-40 disabled:hover:bg-lo-primary`}
          >
            <Icon name="check" className="text-base" />
            <span>{saving ? 'Menyimpan…' : 'Simpan Pos Baru'}</span>
          </button>
        </>
      }
    >
      <div className="flex flex-col gap-5">
        <SubmitError message={error} />
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
          <div className="flex flex-col gap-1.5">
            <label className="text-xs font-medium text-lo-text-ink" htmlFor="new-pos-type">
              Jenis Dana <span className="text-lo-secondary">*</span>
            </label>
            <select
              id="new-pos-type"
              className={selectBase}
              value={category}
              onChange={(e) => setCategory(e.target.value as PosCategory)}
            >
              <option value="saving">Tabungan &amp; Simpanan Organik</option>
              <option value="routine_incremental">Rutinitas Bertahap</option>
              <option value="routine_batch">Rutinitas Berkala 1x Pakai</option>
              <option value="single_spend">Sekali Pakai Non-Rutin</option>
            </select>
          </div>
          <div className="flex flex-col gap-1.5">
            <label className="text-xs font-medium text-lo-text-ink" htmlFor="new-pos-account">
              Rekening Referensi
            </label>
            <select
              id="new-pos-account"
              className={selectBase}
              value={accountLabel}
              onChange={(e) => setAccountLabel(e.target.value)}
            >
              <option value="">Tanpa rekening referensi</option>
              {baseAccounts.map((a) => (
                <option key={a.id} value={a.name}>
                  {a.name}
                </option>
              ))}
            </select>
            <span className="text-[11px] text-lo-text-subtle">
              Penanda rekening yang biasanya digunakan. Top-up memakai Uang Bebas dan tidak memindahkan saldo rekening.
            </span>
          </div>
        </div>

        <div className="flex flex-col gap-1.5">
          <label className="text-xs font-medium text-lo-text-ink" htmlFor="new-pos-name">
            Nama Dana <span className="text-lo-secondary">*</span>
          </label>
          <input
            id="new-pos-name"
            type="text"
            className={inputBase}
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="Misal: Dana Makan Harian, Servis Mobil, Beli Sofa Baru"
          />
        </div>

        {category === 'saving' ? (
          <div className="flex flex-col gap-1.5">
            <label className="text-xs font-medium text-lo-text-ink" htmlFor="new-pos-target">
              Target Tabungan (Rp)
            </label>
            <input
              id="new-pos-target"
              type="text"
              inputMode="numeric"
              className={inputBase}
              value={target}
              onChange={(e) => setTarget(formatNumberString(e.target.value))}
              placeholder="Kosongkan jika simpanan bebas fleksibel"
            />
            <span className="text-[11px] text-lo-text-subtle">
              Saldo awal selalu mulai dari Rp 0 dan diisi bertahap lewat Top-Up.
            </span>
          </div>
        ) : null}

        {category === 'routine_incremental' ? (
          <div className="flex flex-col gap-1.5">
            <label className="text-xs font-medium text-lo-text-ink" htmlFor="new-pos-cycle-kind">
              Pengulangan <span className="text-lo-secondary">*</span>
            </label>
            <select
              id="new-pos-cycle-kind"
              className={selectBase}
              value={cycleKind}
              onChange={(e) => setCycleKind(e.target.value as SetAsideCycleKind)}
            >
              <option value="Weekly">Mingguan</option>
              <option value="Monthly">Bulanan</option>
              <option value="Quarterly">Triwulanan</option>
              <option value="SemiAnnual">Semesteran</option>
              <option value="Annual">Tahunan</option>
            </select>
          </div>
        ) : null}

        {category === 'routine_incremental' ? (
          <div className="flex flex-col gap-1.5">
            <label className="text-xs font-medium text-lo-text-ink" htmlFor="new-pos-plafon">
              Batas Dana per Periode (Rp) <span className="text-lo-secondary">*</span>
            </label>
            <input
              id="new-pos-plafon"
              type="text"
              inputMode="numeric"
              className={inputBase}
              value={plafon}
              onChange={(e) => setPlafon(formatNumberString(e.target.value))}
              placeholder="Contoh: 300000"
            />
            {exceedsAvailable ? (
              <p className="text-[11px] text-lo-error">
                Uang Bebas tidak cukup untuk alokasi awal {formatCurrencyRaw(prepareAmount)}.
                Tersedia {formatCurrencyRaw(freeCash)}.
              </p>
            ) : (
              <span className="text-[11px] text-lo-text-subtle">
                Setiap periode, alokasi akan disiapkan hingga mencapai batas pengeluaran ini.
              </span>
            )}
          </div>
        ) : null}

        {category === 'routine_batch' ? (
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <div className="flex flex-col gap-1.5">
              <label className="text-xs font-medium text-lo-text-ink" htmlFor="new-pos-cycle">
                Dilakukan Setiap
              </label>
              <select
                id="new-pos-cycle"
                className={selectBase}
                value={cycle}
                onChange={(e) => setCycle(e.target.value)}
              >
                <option value="Bulanan">Bulanan</option>
                <option value="Per 3 Bulan">Per 3 Bulan</option>
                <option value="Per 6 Bulan">Per 6 Bulan</option>
                <option value="Tahunan">Tahunan</option>
              </select>
            </div>
            <div className="flex flex-col gap-1.5">
              <label className="text-xs font-medium text-lo-text-ink" htmlFor="new-pos-batch-cost">
                Estimasi Biaya 1x Eksekusi (Rp) <span className="text-lo-secondary">*</span>
              </label>
              <input
                id="new-pos-batch-cost"
                type="text"
                inputMode="numeric"
                className={inputBase}
                value={plafon}
                onChange={(e) => setPlafon(formatNumberString(e.target.value))}
                placeholder="Contoh: 500000"
              />
            </div>
            <div className="sm:col-span-2">
              {exceedsAvailable ? (
                <p className="text-[11px] text-lo-error">
                  Uang Bebas tidak cukup untuk alokasi awal {formatCurrencyRaw(prepareAmount)}.
                  Tersedia {formatCurrencyRaw(freeCash)}.
                </p>
              ) : (
                <span className="text-[11px] text-lo-text-subtle">
                  Dana langsung disisihkan dari uang yang belum dialokasikan saat pos dibuat.
                  Digunakan saat kebutuhan terjadi.
                </span>
              )}
            </div>
          </div>
        ) : null}

        {category === 'single_spend' ? (
          <div className="flex flex-col gap-1.5">
            <label className="text-xs font-medium text-lo-text-ink" htmlFor="new-pos-single-budget">
              Jumlah Dana (Rp) <span className="text-lo-secondary">*</span>
            </label>
            <input
              id="new-pos-single-budget"
              type="text"
              inputMode="numeric"
              className={inputBase}
              value={plafon}
              onChange={(e) => setPlafon(formatNumberString(e.target.value))}
              placeholder="Contoh: 2000000"
            />
            {exceedsAvailable ? (
              <p className="text-[11px] text-lo-error">
                Uang Bebas tidak cukup untuk alokasi awal {formatCurrencyRaw(prepareAmount)}.
                Tersedia {formatCurrencyRaw(freeCash)}.
              </p>
            ) : (
              <span className="text-[11px] text-lo-text-subtle">
                Alokasi disiapkan sesuai jumlah ini dan digunakan saat kebutuhan terjadi.
              </span>
            )}
          </div>
        ) : null}

        <div className="flex flex-col gap-1.5">
          <label className="text-xs font-medium text-lo-text-ink" htmlFor="new-pos-note">
            Catatan
          </label>
          <textarea
            id="new-pos-note"
            className={`${inputBase} resize-none`}
            rows={2}
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            placeholder="Tuliskan pedoman atau catatan khusus..."
          />
        </div>

        {!canSubmit ? (
          <p className="text-[11px] text-lo-text-subtle">
            {exceedsAvailable
              ? 'Uang Bebas tidak cukup untuk alokasi awal. Kurangi nominal atau catat pemasukan terlebih dahulu.'
              : missingNominal
                ? 'Isi plafon siklus / estimasi biaya / target anggaran untuk melanjutkan.'
                : 'Isi nama pos untuk melanjutkan.'}
          </p>
        ) : null}
      </div>
    </Modal>
  );
}

/* ── Savings modal (Top-Up / Tarik) ── */

function SavingsModal({
  pos,
  accounts,
  freeCash,
  onClose,
  onTopUp,
  onWithdraw,
  onDelete,
}: {
  pos: PosItem;
  accounts: Account[];
  freeCash: number;
  onClose: () => void;
  onTopUp: (amount: number, sourceAccountId?: string) => Promise<boolean>;
  onWithdraw: (amount: number) => Promise<boolean>;
  onDelete: () => void;
}) {
  const { saving, error, submit } = useSubmitState();
  const [mode, setMode] = useState<'topup' | 'withdraw'>('topup');
  const [amountStr, setAmountStr] = useState('');
  // Hanya pre-select referensi tersimpan; jangan menebak rekening untuk top-up.
  const [sourceAccountId, setSourceAccountId] = useState<string>(() =>
    resolveReferenceAccountId(pos, accounts)
  );
  const amount = parseFormattedNumber(amountStr);

  const sourceAccounts = sortedSourceAccounts(accounts);
  const selectedSource = sourceAccounts.find((a) => a.id === sourceAccountId);
  const exceedsFreeCash = mode === 'topup' && amount > freeCash;
  const canSubmit =
    amount > 0 &&
    (mode === 'topup'
      ? !exceedsFreeCash
      : amount <= pos.amount);

  const setQuick = (v: number) => {
    // Chip "+1k" menambah nominal yang sudah ada, bukan menimpa.
    const current = parseFormattedNumber(amountStr);
    if (mode === 'topup') {
      setAmountStr(formatNumberString(String(current + v)));
    } else {
      setAmountStr(formatNumberString(String(Math.min(current + v, pos.amount))));
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      title={`Kelola Simpanan: ${pos.name}`}
      icon="savings"
      footer={
        <>
          <button
            type="button"
            onClick={onDelete}
            className="text-xs text-lo-warning hover:underline inline-flex items-center gap-1 cursor-pointer mr-auto"
          >
            <Icon name="delete" className="text-base" />
            Tutup Pos
          </button>
          <button
            type="button"
            onClick={onClose}
            className="px-4 py-2 rounded-full text-xs text-lo-text-subtle hover:text-lo-text-ink transition-colors cursor-pointer"
          >
            Tutup
          </button>
          <button
            type="button"
            disabled={!canSubmit || saving}
            onClick={() => void submit(() => mode === 'topup'
              ? onTopUp(amount, selectedSource?.id)
              : onWithdraw(amount))}
            className={`${btnPrimary} disabled:opacity-40`}
          >
            {saving ? 'Menyimpan…' : mode === 'topup' ? 'Konfirmasi Tambah Simpanan' : 'Konfirmasi Tarik Simpanan'}
          </button>
        </>
      }
    >
      <div className="space-y-4">
        <SubmitError message={error} />
        <p className="text-xs text-lo-text-subtle">{pos.description}</p>
        <div className="bg-lo-surface-recessed p-4 rounded-xl flex items-center justify-between">
          <div>
            <span className="text-[11px] text-lo-text-subtle block uppercase tracking-wide">
              Saldo Tabungan Saat Ini
            </span>
            <span className="text-2xl font-semibold tabular-nums text-lo-text-ink">
              {formatCurrencyRaw(pos.amount)}
            </span>
          </div>
          <div className="text-right">
            <span className="text-[11px] text-lo-text-subtle block uppercase tracking-wide">
              Target Capaian
            </span>
            <span className="text-base font-semibold text-lo-text-ink">
              {pos.targetAmount ? formatCurrencyRaw(pos.targetAmount) : 'Fleksibel'}
            </span>
          </div>
        </div>

        <div className="p-4 rounded-xl border border-lo-border-hairline bg-lo-container-low">
          <div className="flex items-center p-1 rounded-lg bg-lo-surface-recessed border border-lo-border-hairline mb-3">
            <button
              type="button"
              onClick={() => setMode('topup')}
              className={`flex-1 py-1.5 rounded-md text-xs font-semibold transition-all cursor-pointer inline-flex items-center justify-center gap-1.5 ${
                mode === 'topup'
                  ? 'bg-lo-secondary text-lo-surface-cream shadow-xs'
                  : 'text-lo-text-subtle hover:text-lo-text-ink'
              }`}
            >
              <Icon name="add_circle" className="text-base" />
              Tambah Simpanan (Top-Up)
            </button>
            <button
              type="button"
              onClick={() => setMode('withdraw')}
              className={`flex-1 py-1.5 rounded-md text-xs font-medium transition-all cursor-pointer inline-flex items-center justify-center gap-1.5 ${
                mode === 'withdraw'
                  ? 'bg-lo-secondary text-lo-surface-cream shadow-xs'
                  : 'text-lo-text-subtle hover:text-lo-text-ink'
              }`}
            >
              <Icon name="remove_circle" className="text-base" />
              Tarik Simpanan
            </button>
          </div>
          <p className="text-[12px] text-lo-text-subtle mb-3">
            {mode === 'topup'
              ? 'Tambah alokasi tabungan dari Uang Bebas. Saldo rekening tidak berubah; rekening referensi hanya penanda.'
              : 'Tarik sebagian simpanan. Alokasi yang ditarik dikembalikan ke uang yang belum dialokasikan — saldo rekening tidak berubah.'}
          </p>

          {mode === 'topup' ? (
            <div className="flex flex-col gap-1.5 mb-3">
              <label className="text-xs font-medium text-lo-text-ink" htmlFor="pos-topup-source">
                Rekening Referensi (opsional)
              </label>
              <select
                id="pos-topup-source"
                className={selectBase}
                value={sourceAccountId}
                onChange={(e) => setSourceAccountId(e.target.value)}
              >
                <option value="">Tanpa rekening referensi</option>
                {sourceAccounts.map((a) => (
                <option key={a.id} value={a.id}>
                    {a.name}
                </option>
                ))}
              </select>
              <span className="text-[11px] text-lo-text-subtle">
                Penanda rekening yang biasanya digunakan; tidak didebit dan tidak membatasi top-up.
              </span>
            </div>
          ) : null}

          <div className="grid grid-cols-4 gap-2 mb-3">
            {QUICK_AMOUNTS.map((v) => (
              <button
                key={v}
                type="button"
                onClick={() => setQuick(v)}
                className="py-2 rounded-lg text-[11px] bg-lo-surface-cream hover:bg-lo-accent-wash text-lo-text-ink border border-lo-border-hairline font-medium text-center transition-all cursor-pointer"
              >
                {quickAmountLabel(v)}
              </button>
            ))}
          </div>
          <div className="relative">
            <span className="absolute left-3.5 top-2.5 text-[11px] text-lo-text-subtle">Rp</span>
            <input
              type="text"
              inputMode="numeric"
              className={`${inputBase} pl-9 font-semibold tabular-nums`}
              value={amountStr}
              onChange={(e) => setAmountStr(formatNumberString(e.target.value))}
              placeholder="Ketik nominal transfer..."
            />
          </div>
          {mode === 'topup' && exceedsFreeCash ? (
            <p className="mt-2 text-[12px] text-lo-error">
              Uang Bebas tidak cukup. Tersedia {formatCurrencyRaw(freeCash)}; kurangi nominal atau catat pemasukan terlebih dahulu.
            </p>
          ) : null}
          <div className="mt-3 p-3 rounded-xl bg-lo-surface-recessed border border-lo-border-hairline text-[12px] text-lo-text-subtle">
            {mode === 'topup'
              ? `Maksimal top-up dari Uang Bebas: ${formatCurrencyRaw(freeCash)}.`
              : `Maksimal tarik: ${formatCurrencyRaw(pos.amount)}.`}
          </div>
        </div>
      </div>
    </Modal>
  );
}

/* ── Shared source-account picker untuk modal pakai/eksekusi pos ── */

function SourceAccountPicker({
  accounts,
  sourceAccountId,
  onChange,
  id,
  autoSelected = false,
}: {
  accounts: Account[];
  sourceAccountId: string;
  onChange: (id: string) => void;
  id: string;
  autoSelected?: boolean;
}) {
  const sourceAccounts = sortedSourceAccounts(accounts);
  return (
    <div className="flex flex-col gap-1.5">
      <label className="text-xs font-medium text-lo-text-ink" htmlFor={id}>
        Sumber Dana <span className="text-lo-secondary">*</span>
      </label>
      <span className="text-[11px] text-lo-text-subtle">
        Uang keluar dari rekening ini.
      </span>
      {autoSelected ? (
        <span className="text-[11px] font-medium text-lo-secondary">
          Otomatis dipilih dari preferensi pos — periksa sebelum menyimpan.
        </span>
      ) : null}
      <select
        id={id}
        className={selectBase}
        value={sourceAccountId}
        onChange={(e) => onChange(e.target.value)}
      >
        <option value="" disabled>
          {sourceAccounts.length === 0
            ? 'Tidak ada sumber dana aktif'
            : 'Pilih Sumber Dana…'}
        </option>
        {sourceAccounts.map((a) => (
          <option key={a.id} value={a.id}>
            {a.name} — {formatCurrencyRaw(a.availableBalance)} tersedia
          </option>
        ))}
      </select>
    </div>
  );
}

/* ── Incremental modal ── */

function IncrementalModal({
  pos,
  accounts,
  onClose,
  onSubmit,
}: {
  pos: PosItem;
  accounts: Account[];
  onClose: () => void;
  onSubmit: (amount: number, sourceAccountId: string, note?: string) => Promise<boolean>;
}) {
  const { saving, error, submit } = useSubmitState();
  const [amountStr, setAmountStr] = useState('');
  const [note, setNote] = useState('');
  const [sourceAccountId, setSourceAccountId] = useState(() =>
    resolveManualSourceAccountId(pos, accounts)
  );
  const amount = parseFormattedNumber(amountStr);

  // Plafon = batas aman, bukan hard limit. Pemakaian boleh melebihi saldo pos;
  // kelebihannya ditanggung uang yang belum dialokasikan (dicek backend).
  const fromPos = Math.min(amount, pos.amount);
  const shortfall = Math.max(0, amount - pos.amount);

  const sourceAccounts = sortedSourceAccounts(accounts);
  const selectedSource = sourceAccounts.find((a) => a.id === sourceAccountId);
  const sourceBalance = selectedSource?.availableBalance ?? 0;
  // Seluruh nominal keluar dari Sumber Dana — validasi terhadap saldo rekening.
  const exceedsSource = amount > 0 && !!selectedSource && amount > sourceBalance;

  const canSubmit = amount > 0 && !!selectedSource && !exceedsSource;

  return (
    <Modal
      open
      onClose={onClose}
      title="Catat Pengeluaran Bertahap"
      icon="receipt_long"
      footer={
        <>
          <button
            type="button"
            onClick={onClose}
            className="px-4 py-2 rounded-full text-xs text-lo-text-subtle hover:text-lo-text-ink transition-colors cursor-pointer"
          >
            Batal
          </button>
          <button
            type="button"
            disabled={!canSubmit || saving}
            onClick={() => void submit(() => onSubmit(amount, sourceAccountId, note || undefined))}
            className={`${btnPrimary} disabled:opacity-40`}
          >
            <Icon name="check" className="text-base" />
            <span>{saving ? 'Menyimpan…' : 'Simpan Pemakaian'}</span>
          </button>
        </>
      }
    >
      <div className="space-y-4">
        <SubmitError message={error} />
        <div className="bg-lo-surface-recessed p-4 rounded-xl flex items-center justify-between">
          <div>
            <span className="text-[11px] text-lo-text-subtle block uppercase tracking-wide">
              Plafon Siklus Aktif
            </span>
            <span className="text-base font-semibold text-lo-text-ink">{pos.name}</span>
            <span className="text-[12px] text-lo-text-subtle block mt-0.5">
              Plafon{' '}
              {formatCurrencyRaw(pos.plafon ?? pos.targetAmount ?? 0)} (batas aman)
            </span>
          </div>
          <div className="text-right">
            <span className="text-[11px] text-lo-text-subtle block uppercase tracking-wide">
              Saldo Pos Saat Ini
            </span>
            <span className="text-2xl font-semibold tabular-nums text-lo-text-ink">
              {formatCurrencyRaw(pos.amount)}
            </span>
          </div>
        </div>

        <div className="flex flex-col gap-2">
          <label className="text-xs font-medium text-lo-text-ink" htmlFor="inc-amount">
            Nominal Pemakaian Kali Ini <span className="text-lo-secondary">*</span>
          </label>
          <div className="grid grid-cols-4 gap-2">
            {QUICK_AMOUNTS.map((v) => (
              <button
                key={v}
                type="button"
                onClick={() =>
                  setAmountStr((prev) =>
                    formatNumberString(String(parseFormattedNumber(prev) + v))
                  )
                }
                className="py-2 rounded-lg text-[11px] bg-lo-surface-cream hover:bg-lo-accent-wash text-lo-text-ink border border-lo-border-hairline font-medium text-center transition-all cursor-pointer"
              >
                {quickAmountLabel(v)}
              </button>
            ))}
          </div>
          <div className="relative">
            <span className="absolute left-3.5 top-2.5 text-xs text-lo-text-subtle">Rp</span>
            <input
              id="inc-amount"
              type="text"
              inputMode="numeric"
              className={`${inputBase} pl-11 font-semibold tabular-nums text-base`}
              value={amountStr}
              onChange={(e) => setAmountStr(formatNumberString(e.target.value))}
              placeholder="0"
            />
          </div>
        </div>

        <SourceAccountPicker
          accounts={accounts}
          sourceAccountId={sourceAccountId}
          onChange={setSourceAccountId}
          id="inc-source-account"
          autoSelected={!!pos.defaultSourceAccountId && sourceAccountId === pos.defaultSourceAccountId}
        />
        {exceedsSource ? (
          <p className="text-[11px] text-lo-error -mt-2">
            Nominal {formatCurrencyRaw(amount)} melebihi saldo{' '}
            {formatCurrencyRaw(sourceBalance)} di {selectedSource?.name ?? 'sumber dana'}.
            Pilih sumber dana lain atau kurangi nominal.
          </p>
        ) : null}

        <div className="flex flex-col gap-1.5">
          <label className="text-xs font-medium text-lo-text-ink" htmlFor="inc-note">
            Keperluan / Catatan Transaksi
          </label>
          <input
            id="inc-note"
            type="text"
            className={inputBaseSm}
            value={note}
            onChange={(e) => setNote(e.target.value)}
            placeholder="Misal: makan siang warteg"
          />
        </div>

        {amount > 0 ? (
          <div className="p-3 rounded-xl bg-lo-accent-wash/60 border border-lo-secondary/20 text-xs text-lo-text-ink space-y-1">
            <div>
              Pemakaian{' '}
              <span className="font-semibold font-headline">{formatCurrencyRaw(amount)}</span>{' '}
              dicatat sebagai Expense penuh.
            </div>
            <div className="text-lo-text-subtle">
              Dari saldo pos:{' '}
              <span className="font-semibold font-headline text-lo-text-ink">
                {formatCurrencyRaw(fromPos)}
              </span>
              {shortfall > 0 ? (
                <>
                  {' · '}Sisa{' '}
                  <span className="font-semibold font-headline text-lo-text-ink">
                    {formatCurrencyRaw(shortfall)}
                  </span>{' '}
                  ditanggung uang yang belum dialokasikan
                  {selectedSource ? ` (dari ${selectedSource.name})` : ''}.
                </>
              ) : null}
            </div>
          </div>
        ) : null}
      </div>
    </Modal>
  );
}

/* ── Batch modal ── */

function BatchModal({
  pos,
  accounts,
  onClose,
  onSubmit,
}: {
  pos: PosItem;
  accounts: Account[];
  onClose: () => void;
  onSubmit: (cost: number, sourceAccountId: string, note?: string) => Promise<boolean>;
}) {
  const { saving, error, submit } = useSubmitState();
  const defaultCost = pos.plafon || pos.amount;
  const [amountStr, setAmountStr] = useState(formatNumberString(String(defaultCost)));
  const [note, setNote] = useState('');
  const [sourceAccountId, setSourceAccountId] = useState(() =>
    resolveManualSourceAccountId(pos, accounts)
  );
  const amount = parseFormattedNumber(amountStr);

  const sourceAccounts = sortedSourceAccounts(accounts);
  const selectedSource = sourceAccounts.find((a) => a.id === sourceAccountId);
  const sourceBalance = selectedSource?.availableBalance ?? 0;
  const exceedsSource = !!selectedSource && amount > sourceBalance;
  const canSubmit = amount > 0 && !!selectedSource && !exceedsSource && !pos.cycleExecuted;

  return (
    <Modal
      open
      onClose={onClose}
      title="Eksekusi Rutinitas Berkala (1x Pakai)"
      icon="event_repeat"
      footer={
        <>
          <button
            type="button"
            onClick={onClose}
            className="px-4 py-2 rounded-full text-xs text-lo-text-subtle hover:text-lo-text-ink transition-colors cursor-pointer"
          >
            Batal
          </button>
          <button
            type="button"
            disabled={!canSubmit || saving}
            onClick={() => void submit(() => onSubmit(amount, sourceAccountId, note || undefined))}
            className={`${btnPrimary} disabled:opacity-40`}
          >
            <Icon name="check" className="text-base" />
            <span>{saving ? 'Menyimpan…' : 'Konfirmasi Eksekusi Tuntas'}</span>
          </button>
        </>
      }
    >
      <div className="space-y-4">
        <SubmitError message={error} />
        {pos.cycleExecuted ? (
          <div className="rounded-xl border border-lo-secondary/30 bg-lo-accent-wash/60 p-3 text-xs text-lo-text-ink">
            Periode ini sudah direalisasikan. Sisa alokasi telah dilepas ke Uang Bebas; pos dapat
            digunakan kembali setelah periode berikutnya dimulai.
          </div>
        ) : null}
        <div className="bg-lo-surface-recessed p-4 rounded-xl flex items-center justify-between">
          <div>
            <span className="text-[11px] text-lo-text-subtle block uppercase tracking-wide">
              Rutinitas Periode Ini
            </span>
            <span className="text-base font-semibold text-lo-text-ink">{pos.name}</span>
            <span className="text-[12px] text-lo-text-subtle block mt-0.5">
              {pos.cycle || 'Bulanan'}
            </span>
          </div>
          <div className="text-right">
            <span className="text-[11px] text-lo-text-subtle block uppercase tracking-wide">
              Dana Disiapkan
            </span>
            <span className="text-2xl font-semibold tabular-nums text-lo-secondary">
              {formatCurrencyRaw(pos.amount)}
            </span>
          </div>
        </div>

        <div className="flex flex-col gap-1.5">
          <div className="flex items-center justify-between">
            <label className="text-xs font-medium text-lo-text-ink" htmlFor="batch-cost">
              Biaya Aktual Servis / Eksekusi (Rp)
            </label>
            <button
              type="button"
              onClick={() => setAmountStr(formatNumberString(String(defaultCost)))}
              className="text-[11px] text-lo-secondary hover:underline cursor-pointer font-medium"
            >
              Pas Plafon ({formatCurrencyRaw(defaultCost)})
            </button>
          </div>
          <div className="relative">
            <span className="absolute left-3.5 top-2.5 text-xs text-lo-text-subtle">Rp</span>
            <input
              id="batch-cost"
              type="text"
              inputMode="numeric"
              className={`${inputBase} pl-11 font-semibold tabular-nums text-base`}
              value={amountStr}
              onChange={(e) => setAmountStr(formatNumberString(e.target.value))}
              placeholder={formatNumberString(String(defaultCost))}
            />
          </div>
        </div>

        <SourceAccountPicker
          accounts={accounts}
          sourceAccountId={sourceAccountId}
          onChange={setSourceAccountId}
          id="batch-source-account"
          autoSelected={!!pos.defaultSourceAccountId && sourceAccountId === pos.defaultSourceAccountId}
        />
        {exceedsSource ? (
          <p className="text-[11px] text-lo-error -mt-2">
            Biaya {formatCurrencyRaw(amount)} melebihi saldo{' '}
            {formatCurrencyRaw(sourceBalance)} di {selectedSource?.name ?? 'sumber dana'}.
            Pilih sumber dana lain atau kurangi nominal.
          </p>
        ) : null}

        <div className="p-3.5 rounded-xl border border-lo-secondary/40 bg-lo-accent-wash/60">
          <div className="flex items-center justify-between">
            <span className="text-[11px] text-lo-text-subtle">Kondisi Realisasi:</span>
            <span className="text-xs font-semibold text-lo-secondary">
              {amount === defaultCost ? 'Sesuai Estimasi Pas Plafon' : 'Berbeda dari Estimasi'}
            </span>
          </div>
          <p className="text-[12px] text-lo-text-subtle mt-1">
            Biaya aktual {formatCurrencyRaw(amount)} dicatat sebagai pengeluaran. Sisa alokasi
            periode ini langsung dilepas ke Uang Bebas; biaya di atas alokasi memakai Uang Bebas.
          </p>
        </div>

        <div className="flex flex-col gap-1.5">
          <label className="text-xs font-medium text-lo-text-ink" htmlFor="batch-note">
            Catatan Bengkel / Vendor (Opsional)
          </label>
          <input
            id="batch-note"
            type="text"
            className={inputBaseSm}
            value={note}
            onChange={(e) => setNote(e.target.value)}
            placeholder="Contoh: Ganti oli Shell Advance + kampas rem depan"
          />
        </div>
      </div>
    </Modal>
  );
}

/* ── Single spend modal ── */

function SingleSpendModal({
  pos,
  accounts,
  onClose,
  onSubmit,
}: {
  pos: PosItem;
  accounts: Account[];
  onClose: () => void;
  onSubmit: (cost: number, sourceAccountId: string, note?: string) => Promise<boolean>;
}) {
  const { saving, error, submit } = useSubmitState();
  const defaultCost = pos.targetAmount || pos.amount;
  const [amountStr, setAmountStr] = useState(formatNumberString(String(defaultCost)));
  const [note, setNote] = useState('');
  const [sourceAccountId, setSourceAccountId] = useState(() =>
    resolveManualSourceAccountId(pos, accounts)
  );
  const amount = parseFormattedNumber(amountStr);

  const sourceAccounts = sortedSourceAccounts(accounts);
  const selectedSource = sourceAccounts.find((a) => a.id === sourceAccountId);
  const sourceBalance = selectedSource?.availableBalance ?? 0;
  const exceedsSource = !!selectedSource && amount > sourceBalance;
  const canSubmit = amount > 0 && !!selectedSource && !exceedsSource;

  return (
    <Modal
      open
      onClose={onClose}
      title="Realisasikan Belanja Non-Rutin"
      icon="shopping_cart_checkout"
      footer={
        <>
          <button
            type="button"
            onClick={onClose}
            className="px-4 py-2 rounded-full text-xs text-lo-text-subtle hover:text-lo-text-ink transition-colors cursor-pointer"
          >
            Batal
          </button>
          <button
            type="button"
            disabled={!canSubmit || saving}
            onClick={() => void submit(() => onSubmit(amount, sourceAccountId, note || undefined))}
            className={`${btnPrimary} disabled:opacity-40`}
          >
            <Icon name="archive" className="text-base" />
            <span>{saving ? 'Menyimpan…' : 'Belanjakan &amp; Arsipkan Pos'}</span>
          </button>
        </>
      }
    >
      <div className="space-y-4">
        <SubmitError message={error} />
        <div className="bg-lo-surface-recessed p-4 rounded-xl flex items-center justify-between">
          <div>
            <span className="text-[11px] text-lo-text-subtle block uppercase tracking-wide">
              Pos Belanja Sekali Pakai
            </span>
            <span className="text-base font-semibold text-lo-text-ink">{pos.name}</span>
            <span className="text-[12px] text-lo-text-subtle block mt-0.5">
              Pos sekali pakai · sisa alokasi dilepas setelah transaksi
            </span>
          </div>
          <div className="text-right">
            <span className="text-[11px] text-lo-text-subtle block uppercase tracking-wide">
              Alokasi Tersedia
            </span>
            <span className="text-2xl font-semibold tabular-nums text-lo-secondary">
              {formatCurrencyRaw(pos.amount)}
            </span>
          </div>
        </div>

        <div className="flex flex-col gap-1.5">
          <div className="flex items-center justify-between">
            <label className="text-xs font-medium text-lo-text-ink" htmlFor="single-cost">
              Harga Pembelian Aktual (Rp)
            </label>
            <div className="flex items-center gap-2">
              <button
                type="button"
                onClick={() =>
                  setAmountStr(formatNumberString(String(Math.round(defaultCost * 0.925))))
                }
                className="text-[11px] text-lo-secondary hover:underline cursor-pointer font-medium"
              >
                Hemat ({Math.round((defaultCost * 0.925) / 1000)}rb)
              </button>
              <span className="text-lo-border-hairline">·</span>
              <button
                type="button"
                onClick={() => setAmountStr(formatNumberString(String(defaultCost)))}
                className="text-[11px] text-lo-secondary hover:underline cursor-pointer font-medium"
              >
                Pas ({Math.round(defaultCost / 1000)}rb)
              </button>
            </div>
          </div>
          <div className="relative">
            <span className="absolute left-3.5 top-2.5 text-xs text-lo-text-subtle">Rp</span>
            <input
              id="single-cost"
              type="text"
              inputMode="numeric"
              className={`${inputBase} pl-11 font-semibold tabular-nums text-base`}
              value={amountStr}
              onChange={(e) => setAmountStr(formatNumberString(e.target.value))}
              placeholder={formatNumberString(String(defaultCost))}
            />
          </div>
        </div>

        <SourceAccountPicker
          accounts={accounts}
          sourceAccountId={sourceAccountId}
          onChange={setSourceAccountId}
          id="single-source-account"
          autoSelected={!!pos.defaultSourceAccountId && sourceAccountId === pos.defaultSourceAccountId}
        />
        {exceedsSource ? (
          <p className="text-[11px] text-lo-error -mt-2">
            Harga {formatCurrencyRaw(amount)} melebihi saldo{' '}
            {formatCurrencyRaw(sourceBalance)} di {selectedSource?.name ?? 'sumber dana'}.
            Pilih sumber dana lain atau kurangi nominal.
          </p>
        ) : null}

        <div className="p-3.5 rounded-xl border border-lo-secondary/40 bg-lo-accent-wash/60">
          <div className="flex items-center justify-between">
            <span className="text-[11px] text-lo-text-subtle">Hasil Belanja:</span>
            <span className="text-xs font-semibold text-lo-secondary">
              {amount <= defaultCost ? 'Pas / Hemat dengan Alokasi' : 'Melebihi Alokasi'}
            </span>
          </div>
          <p className="text-[12px] text-lo-text-subtle mt-1">
            Setelah transaksi dicatat, pos akan ditutup dan sisa alokasi dikembalikan ke Uang Bebas.
            Jika biaya melebihi alokasi, selisih memakai Uang Bebas.
          </p>
        </div>

        <div className="flex flex-col gap-1.5">
          <label className="text-xs font-medium text-lo-text-ink" htmlFor="single-note">
            Catatan / Toko (Opsional)
          </label>
          <input
            id="single-note"
            type="text"
            className={inputBaseSm}
            value={note}
            onChange={(e) => setNote(e.target.value)}
            placeholder="Misal: Optik Melawai Grand Indonesia"
          />
        </div>
      </div>
    </Modal>
  );
}
