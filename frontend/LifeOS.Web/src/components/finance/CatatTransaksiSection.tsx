import { useRef, useState } from 'react';

import {
  formatCurrencyRaw,
  parseTransactionText,
  type Account,
  type ParsedTransaction,
  type PosItem,
  type Transaction,
} from '../../finance';
import {
  cardBase,
  Icon,
  inputBase,
  selectBase,
} from './shared';

interface CatatTransaksiSectionProps {
  accounts: Account[];
  posItems: PosItem[];
  onSave: (parsed: ParsedTransaction) => Promise<boolean>;
}

const QUICK_PHRASES = [
  'bayar wifi 340rb',
  'jajan kopi 25rb',
  'terima freelance desain 750rb',
  'beli makan siang 45rb',
];

export default function CatatTransaksiSection({
  accounts,
  posItems,
  onSave,
}: CatatTransaksiSectionProps) {
  const [input, setInput] = useState('');
  const [activeAccountId, setActiveAccountId] = useState('');
  const [activePosId, setActivePosId] = useState('');
  const [guidance, setGuidance] = useState<string | null>(null);
  const [parsed, setParsed] = useState<ParsedTransaction | null>(null);
  const [savedMsg, setSavedMsg] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const savingRef = useRef(false);

  const selectorAccounts = accounts.filter((a) => !a.archived);
  // Tanpa fallback ke akun pertama — user harus memilih eksplisit,
  // atau nama akun harus cocok dengan parsed account.
  const selectedAccount = selectorAccounts.find((a) => a.id === activeAccountId);
  const activePos = posItems.filter((p) => !p.archived);
  const selectedPos = activePos.find((p) => p.id === activePosId);

  const handleAccountClick = (id: string) => {
    setActiveAccountId(id);
    const account = selectorAccounts.find((a) => a.id === id);
    if (account && parsed && parsed.status === 'SUCCESS') {
      setParsed({ ...parsed, account: account.name });
    }
  };

  const handlePosChange = (id: string) => {
    setActivePosId(id);
    if (parsed && parsed.status === 'SUCCESS') {
      const pos = activePos.find((p) => p.id === id);
      setParsed({
        ...parsed,
        setAsideId: pos?.id,
        setAsideLabel: pos?.name,
      });
    }
  };

  const handleInputChange = (value: string) => {
    setInput(value);
    setGuidance(null);
    setSavedMsg(null);
  };

  const clearInput = () => {
    setInput('');
    setGuidance(null);
    setParsed(null);
    setSavedMsg(null);
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
    // "Alokasi Pos" membuat pos baru — pilihan pos yang ada tidak relevan di sini.
    if (
      selectedPos &&
      (result.type === 'Pengeluaran' || result.type === 'Pemasukan')
    ) {
      next.setAsideId = selectedPos.id;
      next.setAsideLabel = selectedPos.name;
    }
    setGuidance(null);
    setSavedMsg(null);
    setParsed(next);
    return next;
  };

  const handleSubmit = () => {
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
    buildParsed(result);
  };

  const handleConfirm = async () => {
    if (!parsed || parsed.status !== 'SUCCESS' || savingRef.current) return;
    savingRef.current = true;
    setSaving(true);
    try {
      const saved = await onSave(parsed);
      if (!saved) return;

      const amount = parsed.amount ?? 0;
      setSavedMsg(parsed.type === 'Alokasi Pos'
        ? `Alokasi berhasil dicatat: ${parsed.category} (${formatCurrencyRaw(amount)})`
        : `Transaksi berhasil dicatat: ${parsed.category} (${formatCurrencyRaw(amount)}) melalui ${parsed.account}${
            parsed.setAsideLabel ? ` · pos "${parsed.setAsideLabel}"` : ''
          }`);
      setInput('');
      setParsed(null);
      window.setTimeout(() => setSavedMsg(null), 4000);
    } finally {
      savingRef.current = false;
      setSaving(false);
    }
  };

  const handleCancel = () => {
    setParsed(null);
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
          <span className="text-[11px] text-lo-text-subtle">
            Aktif:{' '}
            <strong className="text-lo-primary font-medium">
              {selectedAccount?.name ?? 'Belum dipilih'}
            </strong>
          </span>
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
        <div className="flex flex-col sm:flex-row items-stretch sm:items-center gap-2.5">
          <div className="flex-1 relative flex items-center">
            <input
              type="text"
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
        </div>

        {/* Dana yang Disisihkan — pilihan tambahan, ringkas. Hanya tampil bila ada pos aktif. */}
        {activePos.length > 0 ? (
          <div className="mt-2.5 flex items-center gap-2 flex-wrap">
            <label
              htmlFor="catat-pos"
              className="text-[11px] text-lo-text-subtle flex items-center gap-1 shrink-0"
            >
              <Icon name="savings" className="text-[13px]" />
              Dana yang Disisihkan
            </label>
            <select
              id="catat-pos"
              className={`${selectBase} w-auto min-w-[11rem] py-1.5`}
              value={activePosId}
              onChange={(e) => handlePosChange(e.target.value)}
            >
              <option value="">Tidak ada</option>
                {activePos.map((p) => (
                  <option
                    key={p.id}
                    value={p.id}
                    disabled={p.category === 'routine_batch' && p.cycleExecuted}
                  >
                  {p.name} — {formatCurrencyRaw(p.amount)}
                </option>
              ))}
            </select>
          </div>
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
          <div className="mt-3.5 rounded-2xl bg-[#f8fbf7] border border-lo-border-hairline p-5 shadow-sm animate-[fadeInSlide_0.25s_cubic-bezier(0.16,1,0.3,1)]">
            <div className="flex flex-col sm:flex-row sm:items-start justify-between gap-3 sm:gap-4">
              <div className="flex items-start gap-3">
                <span
                  className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-full text-xs font-semibold shrink-0 mt-0.5 ${
                    parsed.type === 'Pengeluaran'
                      ? 'bg-[#ffdbd0]/80 text-[#76321b] border border-[#ffb59e]/70'
                      : parsed.type === 'Pemasukan'
                        ? 'bg-[#b5f0c4]/80 text-[#195130] border border-[#9ad4a9]/70'
                        : 'bg-lo-accent-wash text-lo-secondary border border-lo-border-hairline'
                  }`}
                >
                  <Icon
                    name={
                      parsed.type === 'Pengeluaran'
                        ? 'arrow_outward'
                        : parsed.type === 'Pemasukan'
                          ? 'arrow_downward'
                          : 'sync_alt'
                    }
                    className="text-[14px]"
                  />
                  <span>{parsed.type}</span>
                </span>
                <div className="min-w-0">
                  <h3 className="font-semibold text-lo-text-ink text-base sm:text-lg leading-snug break-words">
                    {parsed.category}
                  </h3>
                  <p className="text-xs text-lo-text-subtle font-normal mt-1 flex items-center gap-1">
                    Sumber Dana:{' '}
                    <span className="text-lo-text-ink font-medium" id="verify-account-display">
                      {parsed.account}
                    </span>
                  </p>
                  <p className="text-xs text-lo-text-subtle font-normal mt-0.5 flex items-center gap-1">
                    Dana yang Disisihkan:{' '}
                    <span className="text-lo-text-ink font-medium">
                      {parsed.setAsideLabel ?? 'Tidak ada'}
                    </span>
                  </p>
                </div>
              </div>
              <div className="text-left sm:text-right shrink-0 pl-10 sm:pl-0">
                <span className="font-headline font-medium text-lo-text-ink text-xl sm:text-2xl tracking-tight block tabular-nums">
                  {formatCurrencyRaw(parsed.amount ?? 0)}
                </span>
              </div>
            </div>
            <div className="border-t border-lo-border-hairline/60 my-3.5" />
            <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
              <div className="flex items-center gap-1.5 text-xs text-lo-text-subtle font-normal">
                <Icon name="verified_user" className="text-[16px] text-lo-secondary" />
                <span>Periksa kembali mutasi sebelum disimpan</span>
              </div>
              <div className="flex items-center justify-end gap-2 shrink-0">
                <button
                  type="button"
                  onClick={handleCancel}
                  className="px-4 py-2 text-xs font-medium text-lo-text-subtle hover:text-lo-text-ink rounded-full hover:bg-black/5 transition-colors cursor-pointer"
                >
                  Batal
                </button>
                <button
                  type="button"
                  disabled={saving}
                  onClick={handleConfirm}
                  className="px-5 py-2 rounded-full bg-lo-primary text-lo-primary-hover text-white hover:bg-lo-primary-hover text-xs font-medium shadow-sm transition-all flex items-center gap-1.5 cursor-pointer active:scale-[0.98]"
                >
                  <Icon name="check" className="text-[16px]" />
                  <span>{saving ? 'Menyimpan…' : 'Simpan Transaksi'}</span>
                </button>
              </div>
            </div>
          </div>
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
                buildParsed(result);
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
