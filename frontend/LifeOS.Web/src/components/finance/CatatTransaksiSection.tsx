import { useRef, useState } from 'react';

import {
  formatCurrency,
  formatCurrencyRaw,
  formatNumberString,
  parseFormattedNumber,
  parseTransactionText,
  TRANSACTION_CATEGORIES,
  MAX_TRANSACTION_AMOUNT,
  MAX_TRANSACTION_CATEGORY_LENGTH,
  MAX_TRANSACTION_DESCRIPTION_LENGTH,
  MAX_TRANSACTION_TEXT_LENGTH,
  QUICK_AMOUNTS,
  quickAmountLabel,
  type Account,
  type ParsedTransaction,
  type Transaction,
} from '../../finance';
import {
  cardBase,
  Icon,
  inputBase,
  Modal,
  selectBase,
} from './shared';

interface CatatTransaksiSectionProps {
  accounts: Account[];
  onSave: (parsed: ParsedTransaction) => Promise<boolean>;
}

const QUICK_PHRASES = [
  'bayar wifi 340rb',
  'jajan kopi 25rb',
  'terima freelance desain 750rb',
  'beli makan siang 45rb',
];
const EXPENSE_CATEGORIES = TRANSACTION_CATEGORIES.filter((category) => category !== 'Pendapatan');

function validateTransactionInput(transaction: ParsedTransaction, accounts: Account[]): string | null {
  const amount = transaction.amount;
  if (amount === undefined || !Number.isFinite(amount) || !Number.isInteger(amount) || amount < 1) {
    return 'Nominal minimum Rp 1.';
  }
  if (amount > MAX_TRANSACTION_AMOUNT) {
    return `Nominal maksimum Rp ${formatNumberString(String(MAX_TRANSACTION_AMOUNT))}.`;
  }
  if (!transaction.category?.trim()) return 'Pilih kategori transaksi.';
  if (transaction.type === 'Pemasukan' && transaction.category !== 'Pendapatan') {
    return 'Kategori pemasukan harus Pendapatan.';
  }
  if (transaction.type === 'Pengeluaran' && transaction.category === 'Pendapatan') {
    return 'Kategori Pendapatan hanya berlaku untuk pemasukan.';
  }
  if (transaction.category.length > MAX_TRANSACTION_CATEGORY_LENGTH) {
    return `Kategori maksimal ${MAX_TRANSACTION_CATEGORY_LENGTH} karakter.`;
  }
  if ((transaction.description?.length ?? 0) > MAX_TRANSACTION_DESCRIPTION_LENGTH) {
    return `Catatan maksimal ${MAX_TRANSACTION_DESCRIPTION_LENGTH} karakter.`;
  }
  if (!accounts.some((account) => account.name === transaction.account)) {
    return 'Pilih sumber dana aktif.';
  }
  if (transaction.type !== 'Pengeluaran' && transaction.type !== 'Pemasukan') {
    return 'Jenis transaksi tidak didukung pada form ini.';
  }
  return null;
}

export default function CatatTransaksiSection({
  accounts,
  onSave,
}: CatatTransaksiSectionProps) {
  const [input, setInput] = useState('');
  const inputRef = useRef<HTMLInputElement>(null);
  const [activeAccountId, setActiveAccountId] = useState(() => {
    try {
      return window.localStorage.getItem('lifeos:last-transaction-account') ?? '';
    } catch {
      return '';
    }
  });
  const [guidance, setGuidance] = useState<string | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [parsed, setParsed] = useState<ParsedTransaction | null>(null);
  const [savedMsg, setSavedMsg] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [quickMode, setQuickMode] = useState(false);
  const [quickType, setQuickType] = useState<'Pengeluaran' | 'Pemasukan'>('Pengeluaran');
  const [quickAmount, setQuickAmount] = useState('');
  const [quickAmountError, setQuickAmountError] = useState<string | null>(null);
  const [quickCategory, setQuickCategory] = useState<string>('Makan & Minum');
  const [editDetails, setEditDetails] = useState(false);
  const [editDraft, setEditDraft] = useState<ParsedTransaction | null>(null);
  const savingRef = useRef(false);

  const selectorAccounts = accounts.filter((a) => !a.archived);
  // Gunakan rekening terakhir, dan pilih sumber aktif pertama pada penggunaan awal.
  const selectedAccount = selectorAccounts.find((a) => a.id === activeAccountId)
    ?? selectorAccounts[0];
  const dialogValidationError = parsed
    ? validateTransactionInput(editDetails && editDraft ? editDraft : parsed, selectorAccounts)
    : null;
  const quickValidationError = quickAmountError
    ?? (quickAmount.length > 0 && Number(quickAmount) < 1 ? 'Nominal minimum Rp 1.' : null);

  const handleAccountClick = (id: string) => {
    setActiveAccountId(id);
    const account = selectorAccounts.find((a) => a.id === id);
    if (account && parsed && parsed.status === 'SUCCESS') {
      setParsed({ ...parsed, account: account.name });
    }
  };

  const handleInputChange = (value: string) => {
    setInput(value);
    setParsed(null);
    setGuidance(null);
    setSavedMsg(null);
    setSaveError(null);
  };

  const clearInput = () => {
    setInput('');
    setGuidance(null);
    setParsed(null);
    setSavedMsg(null);
    setSaveError(null);
  };

  /** Resolve Sumber Dana: pilihan eksplisit user, atau nama akun yang cocok
   *  dengan hasil parser. Tanpa keduanya → panduan, bukan fallback diam-diam. */
  const resolveAccount = (result: ParsedTransaction): Account | undefined => {
    if (selectedAccount) return selectedAccount;
    const wanted = result.account?.trim().toLowerCase();
    if (!wanted) return undefined;
    return selectorAccounts.find((a) => a.name.toLowerCase() === wanted);
  };

  const buildParsed = (result: ParsedTransaction): ParsedTransaction | null => {
    const account = resolveAccount(result);
    if (!account) {
      setGuidance(
        'Pilih Sumber Dana terlebih dahulu — tempat uang keluar/masuk. Tidak ada nama akun pada catatan yang cocok.'
      );
      setParsed(null);
      return null;
    }
    const next: ParsedTransaction = {
      ...result,
      account: account.name,
    };
    setGuidance(null);
    setSavedMsg(null);
    setEditDetails(false);
    setEditDraft(null);
    setParsed(next);
    return next;
  };

  const handleSubmit = () => {
    setQuickMode(false);
    const result = parseTransactionText(input);
    if (result.status === 'EMPTY') {
      setGuidance('Masukkan catatan transaksi terlebih dahulu.');
      setParsed(null);
      return;
    }
    if (result.status === 'NO_AMOUNT') {
      setGuidance('Sertakan nominal transaksi (contoh: 25rb, 340rb, atau 1.5jt).');
      setParsed(null);
      return;
    }
    if (result.status === 'AMBIGUOUS_AMOUNT') {
      setGuidance('Nominal belum jelas. Gunakan angka utuh (contoh: 15000), 15rb, atau 1.5jt.');
      setParsed(null);
      return;
    }
    if (result.status === 'AMOUNT_OUT_OF_RANGE') {
      setGuidance(`Nominal maksimum Rp ${formatNumberString(String(MAX_TRANSACTION_AMOUNT))}.`);
      setParsed(null);
      return;
    }
    if (result.type === 'Alokasi Pos') {
      setGuidance('Catat Transaksi hanya untuk uang masuk atau keluar. Kelola alokasi di modul Dana yang Disisihkan.');
      setParsed(null);
      return;
    }
    if (result.type === 'Transfer Kas') {
      setGuidance('Transfer memerlukan sumber dana tujuan. Gunakan menu Transfer di bagian Sumber Dana.');
      setParsed(null);
      return;
    }
    buildParsed(result);
  };

  const handleQuickAmountChange = (value: string) => {
    const digits = value.replace(/\D/g, '');
    if (digits.length > String(MAX_TRANSACTION_AMOUNT).length || Number(digits || 0) > MAX_TRANSACTION_AMOUNT) {
      const error = `Nominal maksimum Rp ${formatNumberString(String(MAX_TRANSACTION_AMOUNT))}.`;
      setQuickAmountError(error);
      setGuidance(error);
      return;
    }
    setQuickAmountError(null);
    setGuidance(null);
    setQuickAmount(digits);
  };

  const handleEditedAmountChange = (value: string) => {
    const digits = value.replace(/\D/g, '');
    if (digits.length > String(MAX_TRANSACTION_AMOUNT).length || Number(digits || 0) > MAX_TRANSACTION_AMOUNT) {
      setSaveError(null);
      if (editDraft) setEditDraft({ ...editDraft, amount: MAX_TRANSACTION_AMOUNT + 1 });
      return;
    }
    setSaveError(null);
    if (editDraft) setEditDraft({ ...editDraft, amount: parseFormattedNumber(value) });
  };

  const handleQuickReview = () => {
    if (quickValidationError) {
      setGuidance(quickValidationError);
      return;
    }
    const amount = Number(quickAmount.replace(/\D/g, ''));
    if (amount < 1) {
      setGuidance('Nominal minimum Rp 1.');
      return;
    }
    if (!Number.isSafeInteger(amount) || amount > MAX_TRANSACTION_AMOUNT) {
      setGuidance(`Nominal maksimum Rp ${formatNumberString(String(MAX_TRANSACTION_AMOUNT))}.`);
      return;
    }
    if (!selectedAccount) {
      setGuidance('Tambahkan sumber dana aktif terlebih dahulu.');
      return;
    }
    setGuidance(null);
    setInput('');
    setEditDetails(false);
    setEditDraft(null);
    setParsed({
      status: 'SUCCESS',
      amount,
      type: quickType,
      category: quickType === 'Pemasukan' ? 'Pendapatan' : quickCategory,
      description: quickType === 'Pemasukan' ? 'Pendapatan' : quickCategory,
      account: selectedAccount.name,
    });
  };

  const handleConfirm = async (transaction: ParsedTransaction | null = parsed) => {
    if (!transaction || transaction.status !== 'SUCCESS' || savingRef.current) return;
    const inputError = validateTransactionInput(transaction, selectorAccounts);
    if (inputError) {
      setSaveError(inputError);
      return;
    }
    setSaveError(null);
    savingRef.current = true;
    setSaving(true);
    try {
      const saved = await onSave(transaction);
      if (!saved) {
        setSaveError('Transaksi belum berhasil disimpan. Periksa nominal dan saldo sumber dana, lalu coba lagi.');
        return;
      }

      const transactionAccount = selectorAccounts.find((account) => account.name === transaction.account);
      if (transactionAccount) {
        setActiveAccountId(transactionAccount.id);
        try {
          window.localStorage.setItem('lifeos:last-transaction-account', transactionAccount.id);
        } catch {
          // Penyimpanan lokal opsional; transaksi yang berhasil tetap dipertahankan.
        }
      }

      const amount = transaction.amount ?? 0;
      setSavedMsg(`Transaksi berhasil dicatat: ${transaction.category} (${formatCurrencyRaw(amount)}) melalui ${transaction.account}.`);
      setInput('');
      setParsed(null);
      setEditDraft(null);
      setEditDetails(false);
      setSaveError(null);
      setQuickAmount('');
      window.requestAnimationFrame(() => inputRef.current?.focus());
      window.setTimeout(() => setSavedMsg(null), 4000);
    } catch {
      setSaveError('Transaksi belum berhasil disimpan. Periksa koneksi lalu coba lagi.');
    } finally {
      savingRef.current = false;
      setSaving(false);
    }
  };

  const handleCancel = () => {
    setParsed(null);
    setEditDraft(null);
    setEditDetails(false);
    setSaveError(null);
    window.requestAnimationFrame(() => inputRef.current?.focus());
  };

  const startEditing = () => {
    if (!parsed) return;
    setEditDraft({ ...parsed });
    setSaveError(null);
    setEditDetails(true);
  };

  const cancelEditing = () => {
    setEditDraft(null);
    setEditDetails(false);
  };

  return (
    <div className={`${cardBase} p-5 sm:p-7`}>
      {/* Header */}
      <div className="flex items-center gap-3 pb-4 mb-4 border-b border-lo-border-hairline/80">
        <div className="w-10 h-10 rounded-xl bg-lo-surface-recessed flex items-center justify-center text-lo-secondary border border-lo-border-hairline shrink-0">
          <Icon name="edit_note" className="text-[22px]" />
        </div>
        <div>
          <h2 className="font-headline text-xl font-medium text-lo-text-ink leading-tight">
            Catat Transaksi
          </h2>
          <p className="text-xs text-lo-text-subtle font-normal mt-0.5">
            Ketik transaksi sehari-hari, sistem otomatis mendeteksi nominal &amp; kategori
          </p>
        </div>
      </div>

      {/* Account selector */}
      <div className="mb-4">
        <div className="flex items-center justify-between mb-2">
          <label className="text-xs font-semibold text-lo-text-ink flex items-center gap-1.5">
            <Icon name="account_balance_wallet" className="text-[16px] text-lo-secondary" />
            Sumber Dana:
          </label>
          <div className="text-right text-[11px] text-lo-text-subtle">
            <span>Aktif: </span>
            <strong className="text-lo-primary font-medium">
              {selectedAccount?.name ?? 'Belum dipilih'}
            </strong>
          </div>
        </div>
        <div className="grid grid-cols-2 sm:grid-cols-4 gap-2">
          {selectorAccounts.length === 0 ? (
            <p className="col-span-2 sm:col-span-4 text-xs text-lo-text-subtle px-1">
              Belum ada sumber dana aktif. Tambahkan sumber dana di bagian Sumber Dana.
            </p>
          ) : (
            selectorAccounts.map((acc) => {
              const isActive = acc.id === selectedAccount?.id;
              return (
                <button
                  key={acc.id}
                  type="button"
                  onClick={() => handleAccountClick(acc.id)}
                  aria-pressed={isActive}
                  className={`px-3 py-2 rounded-xl text-xs font-medium border text-center transition-all cursor-pointer ${
                    isActive
                      ? 'bg-lo-primary text-white border-lo-primary shadow-xs'
                      : 'bg-lo-surface-recessed text-lo-text-ink border-lo-border-hairline hover:bg-lo-accent-wash'
                  }`}
                >
                  {acc.name.replace(' Operasional', '').replace('Dompet Fisik / ', '')}
                </button>
              );
            })
          )}
        </div>
      </div>

      {/* Input + submit — alur utama: pilih Sumber Dana → tulis → catat */}
      <div className="relative">
        <div className="flex items-center justify-between gap-3 mb-2">
          <span className="text-[11px] text-lo-text-subtle">Ketik catatan atau gunakan input cepat.</span>
          <button
            type="button"
            aria-pressed={quickMode}
            onClick={() => {
              setQuickMode((value) => !value);
              setParsed(null);
              setGuidance(null);
            }}
            className="text-xs font-medium text-lo-secondary hover:underline cursor-pointer"
          >
            {quickMode ? 'Pakai catatan bebas' : 'Input cepat'}
          </button>
        </div>
        {quickMode ? (
          <div className="rounded-xl border border-lo-border-hairline bg-lo-surface-recessed p-3 mb-3 space-y-3">
            <div className="grid grid-cols-2 gap-2" role="group" aria-label="Jenis transaksi">
              {(['Pengeluaran', 'Pemasukan'] as const).map((type) => (
                <button key={type} type="button" aria-pressed={quickType === type}
                  onClick={() => {
                    setQuickType(type);
                    if (type === 'Pengeluaran' && quickCategory === 'Pendapatan') setQuickCategory('Lainnya');
                  }}
                  className={`rounded-lg border px-3 py-2 text-xs font-medium ${quickType === type ? 'bg-lo-primary text-white border-lo-primary' : 'bg-white border-lo-border-hairline'}`}>
                  {type}
                </button>
              ))}
            </div>
            <div className="grid grid-cols-2 sm:grid-cols-4 gap-2">
              {QUICK_AMOUNTS.slice(0, 4).map((amount) => (
                <button key={amount} type="button"
                  onClick={() => {
                    const next = Number(quickAmount || 0) + amount;
                    if (!Number.isSafeInteger(next) || next > MAX_TRANSACTION_AMOUNT) {
                      const error = `Nominal maksimum Rp ${formatNumberString(String(MAX_TRANSACTION_AMOUNT))}.`;
                      setQuickAmountError(error);
                      setGuidance(error);
                    } else {
                      setQuickAmountError(null);
                      setQuickAmount(String(next));
                    }
                  }}
                  className="rounded-lg border border-lo-border-hairline bg-white px-2 py-2 text-xs hover:bg-lo-accent-wash">
                  {quickAmountLabel(amount)}
                </button>
              ))}
            </div>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
              <label className="text-xs font-medium">Nominal (Rp)
                <input type="text" inputMode="numeric" maxLength={24} min={1} max={MAX_TRANSACTION_AMOUNT}
                  aria-invalid={!!quickAmountError || (quickAmount.length > 0 && Number(quickAmount) < 1)}
                  value={formatNumberString(quickAmount)}
                  onChange={(event) => handleQuickAmountChange(event.target.value)}
                  placeholder="Contoh: 25000" className={`${inputBase} mt-1`} />
              </label>
              <label className="text-xs font-medium">Kategori
                {quickType === 'Pemasukan' ? (
                  <input aria-label="Kategori pemasukan" readOnly value="Pendapatan" className={`${inputBase} mt-1`} />
                ) : (
                  <select required aria-label="Kategori transaksi" value={quickCategory}
                    onChange={(event) => setQuickCategory(event.target.value)} className={`${selectBase} mt-1 w-full`}>
                    {EXPENSE_CATEGORIES.map((category) => <option key={category}>{category}</option>)}
                  </select>
                )}
              </label>
            </div>
            {quickValidationError ? <p role="alert" className="text-xs text-lo-error">{quickValidationError}</p> : null}
            <button type="button" onClick={handleQuickReview}
              disabled={!!quickValidationError || Number(quickAmount || 0) < 1 || Number(quickAmount || 0) > MAX_TRANSACTION_AMOUNT}
              className="w-full rounded-xl bg-lo-primary px-4 py-2.5 text-sm font-medium text-white hover:bg-lo-primary-hover disabled:opacity-50">
              Tinjau Transaksi
            </button>
          </div>
        ) : null}
        {!quickMode ? <div className="flex flex-col sm:flex-row items-stretch sm:items-center gap-2.5">
          <div className="flex-1 relative flex items-center">
            <input
              ref={inputRef}
              type="text"
              maxLength={MAX_TRANSACTION_TEXT_LENGTH}
              value={input}
              onChange={(e) => handleInputChange(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter') {
                  e.preventDefault();
                  handleSubmit();
                }
              }}
              autoComplete="off"
              placeholder="Contoh: bayar wifi 340rb, jajan kopi 25rb, atau terima bonus 1.5jt..."
              className={inputBase}
            />
            {input.length > 0 ? (
              <button
                type="button"
                onClick={clearInput}
                title="Hapus teks"
                className="absolute right-3 text-lo-text-subtle hover:text-lo-text-ink transition-colors p-0.5 cursor-pointer"
              >
                <Icon name="backspace" className="text-[18px]" />
              </button>
            ) : null}
          </div>
          <button
            type="button"
            onClick={handleSubmit}
            className="px-5 py-2.5 rounded-xl bg-lo-primary text-white hover:bg-lo-primary-hover text-xs sm:text-sm font-medium whitespace-nowrap shadow-sm hover:shadow transition-all flex items-center justify-center gap-1.5 cursor-pointer active:scale-[0.98]"
          >
            <span>Catat Transaksi</span>
            <Icon name="arrow_forward" className="text-[18px]" />
          </button>
        </div> : null}
        {!quickMode && input.length > 0 ? (
          <p className="mt-1 text-right text-[10px] text-lo-text-subtle">{input.length}/{MAX_TRANSACTION_TEXT_LENGTH} karakter</p>
        ) : null}

        {/* Guidance */}
        {guidance ? (
          <div className="mt-3 px-4 py-3 rounded-xl bg-lo-surface-cream border border-lo-border-hairline flex items-center justify-between gap-3 text-xs animate-[fadeInSlide_0.25s_cubic-bezier(0.16,1,0.3,1)]">
            <div className="flex items-center gap-2 text-lo-text-subtle">
              <Icon name="info" className="text-[18px] text-lo-warning shrink-0" />
              <span className="text-lo-text-ink">{guidance}</span>
            </div>
            <button
              type="button"
              onClick={() => setGuidance(null)}
              className="text-lo-text-subtle hover:text-lo-text-ink p-1 cursor-pointer"
            >
              <Icon name="close" className="text-[16px]" />
            </button>
          </div>
        ) : null}

        {/* Verification card */}
        {parsed && parsed.status === 'SUCCESS' ? (
          <Modal
            open
            onClose={handleCancel}
            title={editDetails ? 'Edit detail transaksi' : 'Tinjau transaksi'}
            subtitle={editDetails ? 'Periksa perubahan, lalu simpan transaksi.' : 'Pastikan detail transaksi sudah benar sebelum disimpan.'}
            icon={editDetails ? 'edit' : 'receipt_long'}
            maxWidth="max-w-2xl"
            footer={(
              <>
                {editDetails ? (
                  <>
                    <button type="button" onClick={cancelEditing}
                      className="px-4 py-2 text-xs font-medium text-lo-text-subtle hover:text-lo-text-ink rounded-full hover:bg-black/5 transition-colors cursor-pointer">
                      Batal edit
                    </button>
                    <button type="button" onClick={() => void handleConfirm(editDraft)} disabled={saving || !editDraft || !!validateTransactionInput(editDraft, selectorAccounts)}
                      className="px-5 py-2 rounded-full bg-lo-primary text-white hover:bg-lo-primary-hover text-xs font-medium shadow-sm transition-all flex items-center justify-center gap-1.5 cursor-pointer disabled:opacity-50">
                      <Icon name="check" className="text-[16px]" />
                      {saving ? 'Menyimpan…' : 'Simpan Transaksi'}
                    </button>
                  </>
                ) : (
                  <>
                    <button type="button" onClick={handleCancel}
                      className="px-4 py-2 text-xs font-medium text-lo-text-subtle hover:text-lo-text-ink rounded-full hover:bg-black/5 transition-colors cursor-pointer">
                      Batal
                    </button>
                    <button type="button" onClick={startEditing}
                      className="px-4 py-2 rounded-full border border-lo-border-hairline bg-white text-lo-text-ink hover:bg-lo-surface-recessed text-xs font-medium transition-colors cursor-pointer inline-flex items-center justify-center gap-1.5">
                      <Icon name="edit" className="text-[15px]" />
                      Edit transaksi
                    </button>
                    <button type="button" disabled={saving || !!validateTransactionInput(parsed, selectorAccounts)} onClick={() => void handleConfirm(parsed)}
                      className="px-5 py-2 rounded-full bg-lo-primary text-white hover:bg-lo-primary-hover text-xs font-medium shadow-sm transition-all flex items-center justify-center gap-1.5 cursor-pointer active:scale-[0.98] disabled:opacity-50">
                      <Icon name="check" className="text-[16px]" />
                      <span>{saving ? 'Menyimpan…' : 'Simpan Transaksi'}</span>
                    </button>
                  </>
                )}
              </>
            )}
          >
          <div className="rounded-xl border border-lo-border-hairline bg-white/70 p-4">
            {saveError || dialogValidationError ? (
              <p role="alert" className="mb-3 rounded-lg border border-lo-error/30 bg-red-50 px-3 py-2 text-xs text-lo-error">
                {saveError ?? dialogValidationError}
              </p>
            ) : null}
            {editDetails && editDraft?.status === 'SUCCESS' ? (
              <div className="space-y-4">
                <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                  <label className="text-xs font-medium text-lo-text-subtle">Jenis transaksi
                    <select aria-label="Jenis transaksi hasil" value={editDraft.type}
                      onChange={(event) => {
                        const type = event.target.value as 'Pengeluaran' | 'Pemasukan';
                        setSaveError(null);
                        setEditDraft({
                          ...editDraft,
                          type,
                          category: type === 'Pemasukan'
                            ? 'Pendapatan'
                            : editDraft.category === 'Pendapatan' ? 'Lainnya' : editDraft.category,
                        });
                      }} className={`${selectBase} mt-1`}>
                      <option>Pengeluaran</option><option>Pemasukan</option>
                    </select>
                  </label>
                  <label className="text-xs font-medium text-lo-text-subtle">Kategori
                    {editDraft.type === 'Pemasukan' ? (
                      <input aria-label="Kategori transaksi" readOnly value="Pendapatan" className={`${inputBase} mt-1`} />
                    ) : (
                      <select required aria-label="Kategori transaksi" value={editDraft.category ?? 'Lainnya'}
                        onChange={(event) => { setSaveError(null); setEditDraft({ ...editDraft, category: event.target.value }); }}
                        className={`${selectBase} mt-1`}>
                        {EXPENSE_CATEGORIES.map((category) => <option key={category}>{category}</option>)}
                      </select>
                    )}
                  </label>
                  <label className="text-xs font-medium text-lo-text-subtle">Catatan
                    <input aria-label="Catatan transaksi" maxLength={MAX_TRANSACTION_DESCRIPTION_LENGTH} value={editDraft.description ?? ''}
                      onChange={(event) => { setSaveError(null); setEditDraft({ ...editDraft, description: event.target.value }); }}
                      className={`${inputBase} mt-1`} />
                    <span className="mt-1 block text-right text-[10px] font-normal text-lo-text-subtle">
                      {editDraft.description?.length ?? 0}/{MAX_TRANSACTION_DESCRIPTION_LENGTH}
                    </span>
                  </label>
                  <label className="text-xs font-medium text-lo-text-subtle">
                    Sumber Dana
                    <select aria-label="Sumber Dana transaksi" value={selectorAccounts.find((account) => account.name === editDraft.account)?.id ?? ''}
                      onChange={(event) => {
                        const account = selectorAccounts.find((candidate) => candidate.id === event.target.value);
                        if (account) { setSaveError(null); setEditDraft({ ...editDraft, account: account.name }); }
                      }} className={`${selectBase} mt-1`}>
                      {selectorAccounts.map((account) => (
                        <option key={account.id} value={account.id}>
                          {account.name} — saldo {formatCurrency(account.availableBalance)}
                        </option>
                      ))}
                    </select>
                  </label>
                  <label className="text-xs font-medium text-lo-text-subtle sm:col-span-2">Nominal (Rp)
                    <input aria-label="Nominal transaksi" inputMode="numeric" maxLength={24} min={1} max={MAX_TRANSACTION_AMOUNT}
                      aria-invalid={!!validateTransactionInput(editDraft, selectorAccounts)}
                      value={formatNumberString(String(editDraft.amount ?? ''))}
                      onChange={(event) => handleEditedAmountChange(event.target.value)}
                      className={`${inputBase} mt-1`} />
                  </label>
                </div>
                <p className="flex items-start gap-1.5 text-xs text-lo-text-subtle">
                  <Icon name="info" className="text-[15px] text-lo-secondary shrink-0" />
                  Menyimpan dari sini langsung mencatat transaksi dengan detail yang sedang ditampilkan.
                </p>
              </div>
            ) : (
              <div className="flex items-start justify-between gap-4">
                <div className="min-w-0">
                  <span className={`inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-xs font-semibold ${
                    parsed.type === 'Pengeluaran'
                      ? 'bg-[#ffdbd0]/80 text-[#76321b] border border-[#ffb59e]/70'
                      : 'bg-[#b5f0c4]/80 text-[#195130] border border-[#9ad4a9]/70'
                  }`}>
                    <Icon name={parsed.type === 'Pemasukan' ? 'arrow_downward' : 'arrow_outward'} className="text-[14px]" />
                    {parsed.type}
                  </span>
                  <h3 className="mt-2 font-semibold text-lo-text-ink text-base">{parsed.category}</h3>
                  {parsed.description ? <p className="text-xs text-lo-text-subtle mt-0.5">{parsed.description}</p> : null}
                  <p className="text-xs text-lo-text-subtle mt-2">
                    Sumber Dana: <strong className="font-medium text-lo-text-ink">{parsed.account}</strong>
                  </p>
                </div>
                <div className="text-right shrink-0">
                  <span className="block text-[11px] text-lo-text-subtle">Nominal</span>
                  <strong className="block mt-1 font-headline text-xl tabular-nums text-lo-text-ink">
                    {formatCurrencyRaw(parsed.amount ?? 0)}
                  </strong>
                </div>
              </div>
            )}
          </div>
          </Modal>
        ) : null}

        {/* Saved confirmation */}
        {savedMsg ? (
          <div className="mt-3.5 px-4 py-3 rounded-2xl bg-lo-surface-cream border border-lo-border-hairline flex items-center justify-between gap-3 text-xs animate-[fadeInSlide_0.25s_cubic-bezier(0.16,1,0.3,1)]">
            <div className="flex items-center gap-2.5">
              <Icon name="check_circle" className="text-[20px] text-lo-secondary shrink-0" />
              <p className="text-lo-text-ink font-normal leading-relaxed">{savedMsg}</p>
            </div>
            <button
              type="button"
              onClick={() => setSavedMsg(null)}
              className="text-lo-text-subtle hover:text-lo-text-ink p-1 cursor-pointer shrink-0"
              title="Tutup"
            >
              <Icon name="close" className="text-[16px]" />
            </button>
          </div>
        ) : null}
      </div>

      {/* Quick format chips */}
      <div className="flex flex-wrap items-center gap-1.5 pt-3.5 border-t border-lo-border-hairline/60 mt-4">
        <span className="text-xs text-lo-text-subtle font-medium mr-1 flex items-center gap-1">
          <Icon name="lightbulb" className="text-[15px] text-lo-secondary" />
          Contoh format:
        </span>
        {QUICK_PHRASES.map((phrase) => (
          <button
            key={phrase}
            type="button"
            onClick={() => {
              setInput(phrase);
              setGuidance(null);
              setSavedMsg(null);
              const result = parseTransactionText(phrase);
              if (result.status === 'SUCCESS') {
                if (result.type === 'Alokasi Pos') {
                  setGuidance('Kelola alokasi di modul Dana yang Disisihkan.');
                  setParsed(null);
                } else {
                  buildParsed(result);
                }
              } else if (result.status === 'NO_AMOUNT') {
                setGuidance('Sertakan nominal transaksi (contoh: 25rb, 340rb, atau 1.5jt).');
                setParsed(null);
              } else {
                setParsed(null);
              }
            }}
            className="px-2.5 py-1 rounded-lg bg-lo-surface-recessed hover:bg-lo-accent-wash text-lo-text-ink border border-lo-border-hairline transition-colors cursor-pointer text-xs font-normal"
          >
            {phrase}
          </button>
        ))}
      </div>
    </div>
  );
}

export type { Transaction };
