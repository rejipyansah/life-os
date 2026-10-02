export type NavDestination = 'beranda' | 'keuangan' | 'jurnal' | 'target';

export interface NavItem {
  id: NavDestination;
  label: string;
  available: boolean;
}

export const navItems: NavItem[] = [
  { id: 'beranda', label: 'Beranda', available: false },
  { id: 'keuangan', label: 'Keuangan', available: true },
  { id: 'jurnal', label: 'Jurnal', available: false },
  { id: 'target', label: 'Target', available: false },
];
