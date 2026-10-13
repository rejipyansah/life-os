import type { ParsedTransaction } from './types';

export const TRANSACTION_CATEGORIES = [
  'Makan & Minum',
  'Transportasi',
  'Tagihan & Utilitas',
  'Belanja',
  'Kesehatan',
  'Hiburan',
  'Pendidikan',
  'Pendapatan',
  'Lainnya',
] as const;

export const MAX_TRANSACTION_AMOUNT = Number.MAX_SAFE_INTEGER;
export const MAX_TRANSACTION_TEXT_LENGTH = 512;
export const MAX_TRANSACTION_DESCRIPTION_LENGTH = 512;
export const MAX_TRANSACTION_CATEGORY_LENGTH = 128;

const ACCOUNT_KEYWORDS: Array<{ match: string; label: string }> = [
  { match: 'seabank', label: 'SeaBank' },
  { match: 'bca', label: 'BCA Operasional' },
  { match: 'mandiri', label: 'Bank Mandiri' },
  { match: 'tunai', label: 'Dompet Fisik / Tunai' },
  { match: 'cash', label: 'Dompet Fisik / Tunai' },
  { match: 'dompet', label: 'Dompet Fisik / Tunai' },
];

const CATEGORY_KEYWORDS: Array<{ category: (typeof TRANSACTION_CATEGORIES)[number]; keywords: string[] }> = [
  { category: 'Makan & Minum', keywords: ['kopi', 'makan', 'jajan', 'restoran', 'sarapan', 'makan siang', 'makan malam'] },
  { category: 'Transportasi', keywords: ['transport', 'bensin', 'parkir', 'tol', 'ojek', 'grab', 'gojek', 'bus', 'kereta'] },
  { category: 'Tagihan & Utilitas', keywords: ['wifi', 'internet', 'listrik', 'air', 'pulsa', 'tagihan'] },
  { category: 'Belanja', keywords: ['belanja', 'beli', 'marketplace', 'supermarket', 'sabun', 'pakaian'] },
  { category: 'Kesehatan', keywords: ['obat', 'dokter', 'klinik', 'rumah sakit', 'vitamin'] },
  { category: 'Hiburan', keywords: ['bioskop', 'film', 'game', 'hiburan', 'konser'] },
  { category: 'Pendidikan', keywords: ['kursus', 'buku', 'sekolah', 'kuliah', 'pendidikan'] },
];

const INCOME_KEYWORDS = [
  'gaji', 'gajian', 'gajia', 'gajih', 'terima', 'menerima', 'dapat', 'dapet', 'pemasukan',
  'pendapatan', 'bonus', 'freelance', 'honor', 'komisi', 'upah', 'income',
  'salary', 'cair', 'pencairan', 'masuk', 'kiriman', 'refund', 'cashback', 'reimburse',
];

const EXPENSE_KEYWORDS = [
  'bayar', 'membayar', 'pembayaran', 'beli', 'belanja', 'jajan', 'pengeluaran',
  'keluar', 'expense', 'spending',
];

const INCOME_ACTIONS = ['terima', 'menerima', 'dapat', 'dapet', 'masuk', 'cair', 'pencairan'];
const EXPENSE_ACTIONS = ['bayar', 'membayar', 'pembayaran', 'beli', 'belanja', 'jajan', 'keluar'];

const hasPhrase = (text: string, phrase: string) =>
  new RegExp(`(^|[^\\p{L}\\p{N}])${phrase.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}([^\\p{L}\\p{N}]|$)`, 'iu').test(text);

function editDistanceAtMostOne(left: string, right: string): boolean {
  if (Math.abs(left.length - right.length) > 1) return false;
  let i = 0;
  let j = 0;
  let edits = 0;
  while (i < left.length && j < right.length) {
    if (left[i] === right[j]) {
      i++;
      j++;
      continue;
    }
    if (++edits > 1) return false;
    if (left.length > right.length) i++;
    else if (right.length > left.length) j++;
    else {
      i++;
      j++;
    }
  }
  if (i < left.length || j < right.length) edits++;
  return edits <= 1;
}

function findIntentKeyword(text: string, keywords: string[]): number {
  const tokens = [...text.matchAll(/[\p{L}]+/gu)];
  let firstMatch = Number.POSITIVE_INFINITY;
  for (const token of tokens) {
    const word = token[0].toLowerCase();
    if (keywords.some((keyword) =>
      word === keyword || (word.length >= 6 && keyword.length >= 6 && editDistanceAtMostOne(word, keyword))
    )) {
      firstMatch = Math.min(firstMatch, token.index ?? firstMatch);
    }
  }
  return firstMatch;
}

function inferTransactionType(text: string): 'Pemasukan' | 'Pengeluaran' {
  if (hasPhrase(text, 'dana awal') || hasPhrase(text, 'saldo awal')) return 'Pemasukan';
  const incomeAt = findIntentKeyword(text, INCOME_KEYWORDS);
  const expenseAt = findIntentKeyword(text, EXPENSE_KEYWORDS);
  if (incomeAt === Number.POSITIVE_INFINITY) return 'Pengeluaran';
  if (expenseAt === Number.POSITIVE_INFINITY) return 'Pemasukan';

  const incomeActionAt = findIntentKeyword(text, INCOME_ACTIONS);
  const expenseActionAt = findIntentKeyword(text, EXPENSE_ACTIONS);
  if (incomeActionAt !== Number.POSITIVE_INFINITY && expenseActionAt === Number.POSITIVE_INFINITY) return 'Pemasukan';
  if (expenseActionAt !== Number.POSITIVE_INFINITY && incomeActionAt === Number.POSITIVE_INFINITY) return 'Pengeluaran';
  if (incomeActionAt !== Number.POSITIVE_INFINITY && expenseActionAt !== Number.POSITIVE_INFINITY) {
    return incomeActionAt < expenseActionAt ? 'Pemasukan' : 'Pengeluaran';
  }
  return incomeAt < expenseAt ? 'Pemasukan' : 'Pengeluaran';
}

export function detectAccountFromText(text: string): string | null {
  for (const kw of ACCOUNT_KEYWORDS) {
    if (hasPhrase(text, kw.match)) return kw.label;
  }
  return null;
}

function parseAmount(text: string): { amount: number; ambiguous: boolean } {
  const unitMatch = text.match(/(\d+(?:[.,]\d+)?)\s*(rb|k|ribu|jt|juta)\b/i);
  if (unitMatch) {
    const multiplier = /^(rb|k|ribu)$/i.test(unitMatch[2]) ? 1_000 : 1_000_000;
    return { amount: Number(unitMatch[1].replace(',', '.')) * multiplier, ambiguous: false };
  }

  const rawMatch = text.match(/(?:rp\.?\s*)?(\d[\d.,]*)/i);
  if (!rawMatch) return { amount: 0, ambiguous: false };
  const token = rawMatch[1];
  if (/[.,]/.test(token)) {
    // Only accept punctuation as a thousands separator when groups are complete.
    if (/^\d{1,3}(?:[.,]\d{3})+$/.test(token)) {
      return { amount: Number(token.replace(/[.,]/g, '')), ambiguous: false };
    }
    return { amount: 0, ambiguous: true };
  }
  return { amount: Number(token), ambiguous: false };
}

function cleanDescription(raw: string): string {
  return raw
    .replace(/\b(?:bayar|beli|jajan|terima|dapat|sisihkan|transfer|kirim|ke|dari|di)\b/gi, ' ')
    .replace(/\b(?:\d+[\d.,]*\s*(?:rb|k|ribu|jt|juta)?|rp\.?\s*\d+[\d.,]*)\b/gi, ' ')
    .replace(/\b(?:bca|seabank|mandiri|bri|bni|jago|gopay|ovo|dana|shopeepay|cash|tunai|dompet)\b/gi, ' ')
    .replace(/\s+/g, ' ')
    .trim();
}

export function parseTransactionText(raw: string): ParsedTransaction {
  if (!raw || !raw.trim()) return { status: 'EMPTY' };

  const text = raw.trim().toLowerCase();
  const { amount, ambiguous } = parseAmount(text);
  if (ambiguous) return { status: 'AMBIGUOUS_AMOUNT' };
  if (!amount || !Number.isFinite(amount)) return { status: 'NO_AMOUNT' };
  if (amount > MAX_TRANSACTION_AMOUNT) return { status: 'AMOUNT_OUT_OF_RANGE' };

  const account = detectAccountFromText(text) ?? undefined;
  let type: ParsedTransaction['type'] = 'Pengeluaran';
  let category: string = 'Lainnya';

  if (/\b(?:transfer|pindah|kirim)\b/i.test(text)) {
    type = 'Transfer Kas';
  } else if (/\b(?:sisihkan|tabung|investasi|darurat)\b/i.test(text)) {
    type = 'Alokasi Pos';
  } else {
    type = inferTransactionType(text);
    if (type === 'Pemasukan') {
      category = 'Pendapatan';
    } else {
      const match = CATEGORY_KEYWORDS.find(({ keywords }) => keywords.some((keyword) => hasPhrase(text, keyword)));
      if (match) category = match.category;
    }
  }

  const description = cleanDescription(raw);
  if (type === 'Alokasi Pos') category = description || 'Pos Tabungan';

  return {
    status: 'SUCCESS',
    amount: Math.round(amount),
    type,
    category,
    description: description || category,
    account,
  };
}
