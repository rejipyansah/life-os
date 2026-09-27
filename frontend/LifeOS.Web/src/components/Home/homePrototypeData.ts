/**
 * PROTOTYPE DATA ONLY
 *
 * This file contains representative hardcoded data for the Home/Beranda experience.
 * It is NOT live user data. It is NOT connected to any backend.
 *
 * This data exists solely to validate the Home information architecture,
 * interaction model, and visual design before real domain concepts are defined.
 *
 * After Home UX is validated, real domain concepts will be analyzed separately.
 */

export interface ObservationData {
  label: string;
  period: string;
  statement: string;
  detail: string;
  currentMonth: { label: string; amount: string };
  previousMonth: { label: string; amount: string };
  difference: { label: string; value: string; direction: 'up' | 'down' };
  ctaLabel: string;
  ctaTarget: 'finance';
  statusLabel: string;
}

export interface PresentRealityItem {
  id: string;
  title: string;
  subtitle: string;
  amount: string;
  statusLabel: string;
  statusTone: 'warning' | 'active';
  primaryAction: { label: string; variant: 'primary' | 'secondary' };
  secondaryAction: { label: string; variant: 'ghost' | 'subtle' };
}

export interface ProgressItem {
  id: string;
  categoryLabel: string;
  statement: string;
  detail: string;
  currentAmount: string;
  targetAmount: string;
  percentage: number;
}

export interface RecentMemoryItem {
  id: string;
  description: string;
  dateLabel: string;
  amount: string;
  amountTone: 'positive' | 'neutral' | 'negative';
  metaLabel: string;
}

export const prototypeObservation: ObservationData = {
  label: 'Pengamatan Bulan Ini',
  period: 'Bulan ini',
  statement: 'Pengeluaranmu turun Rp450.000 dibanding bulan lalu.',
  detail:
    'Total pengeluaran tercatat Rp3.420.000, dibandingkan Rp3.870.000 pada bulan sebelumnya.',
  currentMonth: { label: 'Bulan ini', amount: 'Rp3.420.000' },
  previousMonth: { label: 'Bulan lalu', amount: 'Rp3.870.000' },
  difference: { label: 'Selisih', value: '-11.6%', direction: 'down' },
  ctaLabel: 'Lihat di Keuangan',
  ctaTarget: 'finance',
  statusLabel: 'Tercatat rapi',
};

export const prototypePresentRealities: PresentRealityItem[] = [
  {
    id: 'wifi-bill',
    title: 'Tagihan WiFi Rumah',
    subtitle: 'Tenggat hari ini (Mandiri)',
    amount: 'Rp340.440',
    statusLabel: 'Belum dicatat',
    statusTone: 'warning',
    primaryAction: { label: 'Catat Pengeluaran', variant: 'primary' },
    secondaryAction: { label: 'Lewati', variant: 'ghost' },
  },
  {
    id: 'motor-service',
    title: 'Alokasi Servis Motor',
    subtitle: 'Jadwal berkala bulan ini',
    amount: 'Rp500.000',
    statusLabel: 'Masih aktif',
    statusTone: 'active',
    primaryAction: { label: 'Periksa Pos', variant: 'secondary' },
    secondaryAction: { label: 'Simpan Nanti', variant: 'subtle' },
  },
];

export const prototypeProgress: ProgressItem[] = [
  {
    id: 'emergency-fund',
    categoryLabel: 'Dana Darurat',
    statement: 'Target tabunganmu tinggal Rp800.000 lagi.',
    detail:
      'Tabungan terkumpul Rp9.200.000 dari target Rp10.000.000 (bertambah Rp800.000 di bulan ini).',
    currentAmount: 'Rp9.200.000',
    targetAmount: 'Rp10.000.000',
    percentage: 92,
  },
];

export const prototypeRecentMemory: RecentMemoryItem[] = [
  {
    id: 'tx-1',
    description: 'Jajan sore',
    dateLabel: 'Kemarin 16:30',
    amount: '-Rp18.000',
    amountTone: 'negative',
    metaLabel: 'Tunai',
  },
  {
    id: 'tx-2',
    description: 'Gaji September',
    dateLabel: '25 Sep',
    amount: '+Rp5.000.000',
    amountTone: 'positive',
    metaLabel: 'BCA',
  },
  {
    id: 'tx-3',
    description: 'Transfer ke BCA',
    dateLabel: '24 Sep',
    amount: 'Rp100.000',
    amountTone: 'neutral',
    metaLabel: 'Selesai',
  },
];
