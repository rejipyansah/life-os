import { useState } from 'react';

import {
  accountTypeLabel,
  formatCurrencyRaw,
  formatNumberString,
  parseFormattedNumber,
  quickAmountLabel,
  QUICK_AMOUNTS,
  type Account,
  type AccountType,
} from '../../finance';
import {
  btnPrimary,
  cardBase,
  ConfirmDialog,
  Icon,
  inputBase,
  Modal,
  selectBase,
} from './shared';

interface SumberDanaSectionProps {
  accounts: Account[];
  onAdd: (data: { name: string; type: AccountType }) => Promise<boolean>;
  onUpdate: (id: string, data: { name: string; type: AccountType }) => Promise<boolean>;
  onDelete: (id: string) => Promise<boolean>;
  onTransfer: (fromId: string, toId: string, amount: number) => Promise<boolean>;
}

type ModalMode =
  | null
  | { kind: 'account'; mode: 'create' | 'edit'; account?: Account }
  | { kind: 'transfer' }
  | { kind: 'delete'; account: Account };

export default function SumberDanaSection({
  accounts,
  onAdd,
  onUpdate,
  onDelete,
  onTransfer,
}: SumberDanaSectionProps) {
  const [modal, setModal] = useState<ModalMode>(null);

  return (
    <div className={`${cardBase} p-6 sm:p-7`}>
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 pb-5 border-b border-lo-border-hairline">
        <div className="space-y-1">
          <div className="flex items-center gap-2.5 flex-wrap">
            <h2 className="font-headline text-2xl font-medium text-lo-text-ink tracking-tight">
              Sumber Dana
            </h2>
            <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full bg-lo-surface-recessed text-lo-secondary text-xs font-medium border border-lo-border-hairline/60">
              <span className="w-1.5 h-1.5 rounded-full bg-lo-secondary" />
              <span>{accounts.length} Akun Aktif</span>
            </span>
          </div>
          <p className="text-xs text-lo-text-subtle">
            Tempat menyimpan dan menggunakan uang sehari-hari.
          </p>
        </div>
        <div className="flex items-center gap-2 shrink-0">
          <button
            type="button"
            onClick={() => setModal({ kind: 'transfer' })}
            className="px-3.5 py-2 rounded-full bg-lo-surface-recessed hover:bg-lo-border-hairline/60 text-lo-text-ink text-xs font-medium transition-all flex items-center gap-1.5 cursor-pointer border border-lo-border-hairline active:scale-95"
          >
            <Icon name="swap_horiz" className="text-[17px] text-lo-secondary" />
            <span>Transfer</span>
          </button>
          <button
            type="button"
            onClick={() => setModal({ kind: 'account', mode: 'create' })}
            className="px-3.5 py-2 rounded-full bg-lo-primary hover:bg-lo-secondary text-white text-xs font-medium transition-all flex items-center gap-1.5 cursor-pointer active:scale-95 shadow-xs"
          >
            <Icon name="add" className="text-[17px]" />
            <span>Tambah Akun</span>
          </button>
        </div>
      </div>

      <div className="space-y-2.5 mt-5">
        {accounts.length === 0 ? (
          <div className="py-8 px-4 text-center bg-lo-surface-cream rounded-xl border border-dashed border-lo-border-hairline text-lo-text-subtle text-xs space-y-2">
            <Icon name="account_balance_wallet" className="text-[28px] text-lo-text-subtle/50" />
            <p className="font-medium">Belum ada sumber dana yang tersimpan.</p>
            <button
              type="button"
              onClick={() => setModal({ kind: 'account', mode: 'create' })}
              className={btnPrimary}
            >
              + Tambah Akun
            </button>
          </div>
        ) : (
          accounts.map((acc) => (
            <div
              key={acc.id}
              className="px-3.5 py-3 rounded-xl border border-lo-border-hairline/60 bg-lo-surface-cream/50 hover:bg-lo-surface-cream hover:border-lo-secondary/30 transition-all flex flex-col sm:flex-row sm:items-center sm:justify-between gap-2 group"
            >
              <div className="flex items-center gap-3 min-w-0">
                <div className="w-9 h-9 rounded-lg bg-lo-accent-wash/80 flex items-center justify-center text-lo-primary shrink-0 border border-lo-border-hairline/60">
                  <Icon name={acc.icon} className="text-[18px]" />
                </div>
                <div className="min-w-0">
                  <div className="flex items-center gap-1.5">
                    <h3 className="font-medium text-sm text-lo-text-ink truncate tracking-tight">
                      {acc.name}
                    </h3>
                    <span className="px-1.5 py-0.5 rounded bg-lo-surface-recessed text-lo-text-subtle text-[10px] font-medium border border-lo-border-hairline/60 shrink-0">
                      {accountTypeLabel(acc.type)}
                    </span>
                  </div>
                </div>
              </div>
              <div className="flex w-full sm:w-auto items-center justify-between sm:justify-start gap-2 shrink-0">
                <span className="font-headline text-base font-medium text-lo-text-ink tabular-nums leading-tight">
                  {formatCurrencyRaw(acc.balance)}
                </span>
                <div className="flex items-center gap-0.5 opacity-60 group-hover:opacity-100 transition-opacity">
                  <button
                    type="button"
                    title="Edit Sumber Dana"
                    aria-label={`Edit Sumber Dana ${acc.name}`}
                    onClick={() => setModal({ kind: 'account', mode: 'edit', account: acc })}
                    className="w-8 h-8 rounded-md flex items-center justify-center text-lo-text-subtle hover:text-lo-text-ink hover:bg-lo-surface-recessed transition-all cursor-pointer"
                  >
                    <Icon name="edit" className="text-[16px]" />
                  </button>
                  <button
                    type="button"
                    title="Hapus"
                    aria-label={`Arsipkan Sumber Dana ${acc.name}`}
                    onClick={() => setModal({ kind: 'delete', account: acc })}
                    className="w-8 h-8 rounded-md flex items-center justify-center text-lo-text-subtle hover:text-lo-error hover:bg-red-50 transition-all cursor-pointer"
                  >
                    <Icon name="delete" className="text-[16px]" />
                  </button>
                </div>
              </div>
            </div>
          ))
        )}
      </div>

      <div className="pt-4 mt-4 border-t border-lo-border-hairline flex flex-col sm:flex-row items-start sm:items-center justify-between gap-2 text-xs text-lo-text-subtle">
        <span className="flex items-center gap-2">
          <span className="w-1.5 h-1.5 rounded-full bg-lo-secondary" />
          <span>Saldo akun diperbarui secara real-time</span>
        </span>
      </div>

      {/* Account modal */}
      {modal?.kind === 'account' ? (
        <AccountModal
          mode={modal.mode}
          account={modal.account}
          onClose={() => setModal(null)}
          onSubmit={(data) => {
            return modal.mode === 'edit' && modal.account
              ? onUpdate(modal.account.id, data)
              : onAdd(data);
          }}
          onSuccess={() => setModal(null)}
        />
      ) : null}

      {/* Transfer modal */}
      {modal?.kind === 'transfer' ? (
        <TransferModal
          accounts={accounts}
          onClose={() => setModal(null)}
          onSubmit={onTransfer}
          onSuccess={() => setModal(null)}
        />
      ) : null}

      {/* Delete confirm */}
      <ConfirmDialog
        open={modal?.kind === 'delete'}
        onClose={() => setModal(null)}
        onConfirm={() => {
          if (modal?.kind === 'delete') return onDelete(modal.account.id);
        }}
        title={`Hapus Sumber Dana "${modal?.kind === 'delete' ? modal.account.name : ''}"?`}
        description="Sumber dana ini akan diarsipkan dari daftar aktif. Saldo harus Rp 0 terlebih dahulu; Dana yang Disisihkan tidak menghalangi arsip karena alokasi bersifat scope-wide. Riwayat mutasi tetap tersimpan."
        icon="delete"
        actionLabel="Hapus"
        danger
      />
    </div>
  );
}

function AccountModal({
  mode,
  account,
  onClose,
  onSubmit,
  onSuccess,
}: {
  mode: 'create' | 'edit';
  account?: Account;
  onClose: () => void;
  onSubmit: (data: { name: string; type: AccountType }) => Promise<boolean>;
  onSuccess: () => void;
}) {
  const [name, setName] = useState(account?.name || '');
  const [type, setType] = useState<AccountType>(account?.type || 'bank');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const nameValid = name.trim().length >= 1 && name.trim().length <= 50;
  const submit = async () => {
    if (!nameValid || saving) return;
    setSaving(true);
    setError('');
    try {
      if (await onSubmit({ name: name.trim(), type })) onSuccess();
      else setError('Sumber dana gagal disimpan. Periksa pesan kesalahan lalu coba lagi.');
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      open
      onClose={saving ? () => {} : onClose}
      title={mode === 'edit' ? 'Edit Sumber Dana' : 'Tambah Sumber Dana'}
      subtitle={
        mode === 'edit'
          ? 'Perbarui informasi sumber dana Anda'
          : 'Tambahkan tempat Anda menyimpan uang'
      }
      maxWidth="max-w-md"
      footer={
        <>
          <button
            type="button"
            onClick={onClose}
            disabled={saving}
            className="px-4 py-2 rounded-full bg-lo-surface-recessed hover:bg-lo-border-hairline/60 text-lo-text-ink text-xs font-medium border border-lo-border-hairline transition-colors cursor-pointer active:scale-95"
          >
            Batal
          </button>
          <button
            type="button"
            disabled={!nameValid || saving}
            onClick={() => void submit()}
            className={`${btnPrimary} disabled:opacity-40`}
          >
            <Icon name="check" className="text-[16px]" />
            <span>{saving ? 'Menyimpan…' : mode === 'edit' ? 'Simpan Perubahan' : 'Simpan'}</span>
          </button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="space-y-1.5">
          <label className="text-xs font-semibold text-lo-text-ink block" htmlFor="acc-name">
            Nama Sumber Dana
          </label>
          <input
            id="acc-name"
            type="text"
            className={inputBase}
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="Contoh: BCA Operasional, SeaBank, Tunai Dompet"
            maxLength={50}
            autoFocus
          />
          {!nameValid ? (
            <div className="flex items-center gap-1.5 text-xs text-lo-error mt-1">
              <Icon name="error" className="text-[15px]" />
              <span>Nama sumber dana wajib diisi dan maksimal 50 karakter.</span>
            </div>
          ) : null}
          <p className="text-[11px] text-lo-text-subtle text-right">{name.trim().length}/50</p>
        </div>

        <div className="space-y-1.5">
          <span className="text-xs font-semibold text-lo-text-ink block">Jenis Sumber Dana</span>
          <div role="group" aria-label="Jenis Sumber Dana" className="p-1 rounded-xl bg-lo-surface-recessed border border-lo-border-hairline flex items-center gap-1">
            {(['bank', 'ewallet', 'cash', 'credit'] as AccountType[]).map((t) => (
              <button
                key={t}
                type="button"
                onClick={() => setType(t)}
                aria-pressed={type === t}
                aria-label={accountTypeLabel(t)}
                className={`flex-1 py-1.5 px-2 rounded-lg text-xs font-medium text-center transition-all border ${
                  type === t
                    ? 'bg-white text-lo-text-ink shadow-xs border-lo-border-hairline'
                    : 'text-lo-text-subtle hover:text-lo-text-ink border-transparent'
                }`}
              >
                {accountTypeLabel(t)}
              </button>
            ))}
          </div>
        </div>

        {error ? <p role="alert" className="text-xs text-lo-error">{error}</p> : null}
      </div>
    </Modal>
  );
}

function TransferModal({
  accounts,
  onClose,
  onSubmit,
  onSuccess,
}: {
  accounts: Account[];
  onClose: () => void;
  onSubmit: (fromId: string, toId: string, amount: number) => Promise<boolean>;
  onSuccess: () => void;
}) {
  const [fromId, setFromId] = useState(accounts[0]?.id || '');
  const [toId, setToId] = useState(accounts[1]?.id || accounts[0]?.id || '');
  const [amountStr, setAmountStr] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const fromAcc = accounts.find((a) => a.id === fromId);
  const toAcc = accounts.find((a) => a.id === toId);
  const amount = parseFormattedNumber(amountStr);
  const exceeds = fromAcc ? amount > fromAcc.balance : false;
  const hasEnoughAccounts = accounts.length >= 2;
  const canSubmit = hasEnoughAccounts && Number.isSafeInteger(amount) && amount > 0 && !exceeds && fromAcc && toAcc && !saving;
  const submit = async () => {
    if (!canSubmit) return;
    setSaving(true);
    setError('');
    try {
      if (await onSubmit(fromId, toId, amount)) onSuccess();
      else setError('Transfer gagal disimpan. Periksa pesan kesalahan lalu coba lagi.');
    } finally {
      setSaving(false);
    }
  };

  const setQuick = (v: number | 'all') => {
    // Chip "+1k" menambah nominal yang sudah ada, bukan menimpa.
    const next =
      v === 'all'
        ? fromAcc?.balance ?? 0
        : parseFormattedNumber(amountStr) + v;
    setAmountStr(formatNumberString(String(next)));
  };

  const pickDifferentAccount = (excludeId: string) =>
    accounts.find((a) => a.id !== excludeId);

  const swap = () => {
    setFromId(toId);
    setToId(fromId);
  };

  return (
    <Modal
      open
      onClose={saving ? () => {} : onClose}
      title="Transfer Dana"
      subtitle="Pindahkan uang antar sumber dana tanpa mengubah total saldo."
      maxWidth="max-w-md"
      footer={
        <>
          <button
            type="button"
            onClick={onClose}
            disabled={saving}
            className="px-4 py-2 rounded-full bg-lo-surface-recessed hover:bg-lo-border-hairline/60 text-lo-text-ink text-xs font-medium border border-lo-border-hairline transition-colors cursor-pointer active:scale-95"
          >
            Batal
          </button>
          <button
            type="button"
            disabled={!canSubmit}
            onClick={() => void submit()}
            className={`${btnPrimary} disabled:opacity-40`}
          >
            <Icon name="check" className="text-[16px]" />
            <span>{saving ? 'Memproses…' : 'Transfer Dana'}</span>
          </button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="space-y-1.5">
          <div className="flex items-center justify-between">
            <label className="text-xs font-semibold text-lo-text-ink" htmlFor="tr-from">
              Dari Sumber Dana
            </label>
            <span className="text-[11px] text-lo-text-subtle tabular-nums">
              Saldo: {fromAcc ? formatCurrencyRaw(fromAcc.balance) : '-'}
            </span>
          </div>
          <select
            id="tr-from"
            className={`${selectBase} text-xs`}
            value={fromId}
            onChange={(e) => {
              const nextFromId = e.target.value;
              setFromId(nextFromId);
              if (nextFromId === toId) {
                const fallback = pickDifferentAccount(nextFromId);
                if (fallback) setToId(fallback.id);
              }
            }}
          >
            {accounts.map((a) => (
              <option key={a.id} value={a.id}>
                {a.name} ({formatCurrencyRaw(a.balance)})
              </option>
            ))}
          </select>
        </div>

        <div className="flex items-center justify-center -my-1">
          <button
            type="button"
            onClick={swap}
            title="Tukar Akun"
            className="h-7 px-3 rounded-full bg-lo-surface-recessed hover:bg-lo-accent-wash text-lo-secondary border border-lo-border-hairline flex items-center gap-1 text-[11px] font-medium transition-all active:scale-95 cursor-pointer"
          >
            <Icon name="swap_vert" className="text-[15px]" />
            <span>Tukar Posisi</span>
          </button>
        </div>

        <div className="space-y-1.5">
          <div className="flex items-center justify-between">
            <label className="text-xs font-semibold text-lo-text-ink" htmlFor="tr-to">
              Ke Sumber Dana
            </label>
            <span className="text-[11px] text-lo-text-subtle tabular-nums">
              Saldo: {toAcc ? formatCurrencyRaw(toAcc.balance) : '-'}
            </span>
          </div>
          <select
            id="tr-to"
            className={`${selectBase} text-xs`}
            value={toId}
            onChange={(e) => {
              const nextToId = e.target.value;
              if (nextToId === fromId) {
                const fallback = pickDifferentAccount(fromId);
                if (fallback) setToId(fallback.id);
              } else {
                setToId(nextToId);
              }
            }}
          >
            {accounts.map((a) => (
              <option key={a.id} value={a.id}>
                {a.name} ({formatCurrencyRaw(a.balance)})
              </option>
            ))}
          </select>
        </div>

        {!hasEnoughAccounts ? (
          <div className="flex items-center gap-1.5 text-xs text-lo-text-subtle bg-lo-surface-recessed p-2.5 rounded-xl border border-lo-border-hairline">
            <Icon name="info" className="text-[16px] shrink-0" />
            <span>Tambahkan sumber dana lain untuk melakukan transfer.</span>
          </div>
        ) : null}

        <div className="space-y-2 pt-1">
          <div className="flex items-center justify-between">
            <label className="text-xs font-semibold text-lo-text-ink" htmlFor="tr-amount">
              Nominal Transfer
            </label>
            <span className="text-[11px] text-lo-secondary font-medium tabular-nums">
              Tersedia: {fromAcc ? formatCurrencyRaw(fromAcc.balance) : 'Rp 0'}
            </span>
          </div>
          <div className="relative">
            <span className="absolute left-3.5 top-2.5 text-sm font-semibold text-lo-text-subtle">
              Rp
            </span>
            <input
              id="tr-amount"
              type="text"
              className={`${inputBase} pl-11 font-semibold tabular-nums`}
              value={amountStr}
              onChange={(e) => setAmountStr(formatNumberString(e.target.value))}
              placeholder="0"
            />
          </div>
          <div className="flex items-center gap-1.5 flex-wrap pt-0.5">
            {QUICK_AMOUNTS.map((v) => (
              <button
                key={v}
                type="button"
                onClick={() => setQuick(v)}
                className="px-2.5 py-1 rounded-full bg-lo-surface-recessed hover:bg-lo-border-hairline/60 text-lo-text-subtle text-[11px] font-medium border border-lo-border-hairline transition-colors cursor-pointer active:scale-95"
              >
                {quickAmountLabel(v)}
              </button>
            ))}
            <button
              type="button"
              onClick={() => setQuick('all')}
              className="px-2.5 py-1 rounded-full bg-lo-accent-wash hover:bg-lo-secondary-container text-lo-secondary text-[11px] font-medium border border-lo-secondary/30 transition-colors cursor-pointer active:scale-95 ml-auto"
            >
              Pindahkan Semua
            </button>
          </div>
          {exceeds ? (
            <div className="flex items-center gap-1.5 text-xs text-lo-error">
              <Icon name="error" className="text-[15px] shrink-0" />
              <span>Nominal melebihi saldo yang tersedia</span>
            </div>
          ) : null}
        </div>

        {error ? <p role="alert" className="text-xs text-lo-error">{error}</p> : null}
      </div>
    </Modal>
  );
}
