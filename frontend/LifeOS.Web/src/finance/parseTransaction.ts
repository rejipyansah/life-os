import type { ParsedTransaction } from './types';

const ACCOUNT_KEYWORDS: Array<{ match: string; label: string }> = [
  { match: 'seabank', label: 'SeaBank' },
  { match: 'bca', label: 'BCA Operasional' },
  { match: 'mandiri', label: 'Bank Mandiri' },
  { match: 'tunai', label: 'Dompet Fisik / Tunai' },
  { match: 'cash', label: 'Dompet Fisik / Tunai' },
  { match: 'dompet', label: 'Dompet Fisik / Tunai' },
];

export function detectAccountFromText(text: string): string | null {
  const lower = text.toLowerCase();
  for (const kw of ACCOUNT_KEYWORDS) {
    if (lower.includes(kw.match)) return kw.label;
  }
  return null;
}

export function parseTransactionText(raw: string): ParsedTransaction {
  if (!raw || !raw.trim()) {
    return { status: 'EMPTY' };
  }

  const text = raw.toLowerCase().trim();

  let amount = 0;
  const kMatch = text.match(/(\d+[.,]?\d*)\s*(rb|k|ribu)/i);
  const jtMatch = text.match(/(\d+[.,]?\d*)\s*(jt|juta)/i);
  const rawNumMatch = text.match(/(?:rp\.?\s*)?(\d+(?:[.,]\d{3})*|\d+)/i);

  if (kMatch) {
    amount = parseFloat(kMatch[1].replace(',', '.')) * 1000;
  } else if (jtMatch) {
    amount = parseFloat(jtMatch[1].replace(',', '.')) * 1_000_000;
  } else if (rawNumMatch) {
    const clean = rawNumMatch[1].replace(/\./g, '').replace(',', '.');
    amount = parseFloat(clean) || 0;
  }

  if (!amount || amount === 0) {
    return { status: 'NO_AMOUNT' };
  }

  const detectedAcc = detectAccountFromText(text);
  // Sumber Dana = uang fisik aktual yang ditentukan user.
  // Tanpa penyebutan akun dalam teks, tidak ada fallback —
  // UI meminta user memilih Sumber Dana secara eksplisit.
  const finalAccount = detectedAcc ?? undefined;

  let type: ParsedTransaction['type'] = 'Pengeluaran';
  let category = 'Rutin & Harian';

  if (
    text.includes('terima') ||
    text.includes('gaji') ||
    text.includes('dapat') ||
    text.includes('masuk') ||
    text.includes('freelance') ||
    text.includes('bonus')
  ) {
    type = 'Pemasukan';
    category = text.includes('freelance') ? 'Freelance Desain' : 'Pemasukan Kas';
  } else if (
    text.includes('sisihkan') ||
    text.includes('tabung') ||
    text.includes('investasi') ||
    text.includes('darurat')
  ) {
    type = 'Alokasi Pos';
    category = 'Pos Tabungan';
  } else if (
    text.includes('transfer') ||
    text.includes('pindah') ||
    text.includes('kirim')
  ) {
    type = 'Transfer Kas';
    category = 'Pemindahan Kas';
  } else if (text.includes('wifi') || text.includes('internet')) {
    category = 'Wifi Bulanan';
  } else if (text.includes('kopi')) {
    category = 'Jajan Kopi';
  } else if (text.includes('makan')) {
    category = 'Makan Siang';
  }

  const cleaned = raw
    .replace(/bayar|beli|jajan|terima|dapat|sisihkan|transfer|kirim|ke|dari|di/gi, '')
    .replace(/\d+[.,]?\d*\s*(rb|k|ribu|jt|juta)?/gi, '')
    .replace(
      /bca|seabank|mandiri|bri|bni|jago|gopay|ovo|dana|shopeepay|cash|tunai|dompet/gi,
      ''
    )
    .trim();

  if (cleaned.length > 1) {
    category = cleaned
      .split(' ')
      .filter(Boolean)
      .map((s) => s.charAt(0).toUpperCase() + s.slice(1))
      .join(' ');
  }

  return {
    status: 'SUCCESS',
    amount: Math.round(amount),
    type,
    category: category || (type === 'Pemasukan' ? 'Pemasukan Kas' : 'Pengeluaran Harian'),
    account: finalAccount,
  };
}
