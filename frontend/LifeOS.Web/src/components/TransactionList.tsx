import type { TransactionProjection } from '../types';
import TransactionItem from './TransactionItem';

interface TransactionListProps {
  transactions: TransactionProjection[];
  onSelect?: (transaction: TransactionProjection) => void;
}

function groupByDate(transactions: TransactionProjection[]): TransactionProjection[][] {
  const groups: TransactionProjection[][] = [];
  let current: TransactionProjection[] = [];

  for (const tx of transactions) {
    if (current.length > 0 && current[0].occurredOn !== tx.occurredOn) {
      groups.push(current);
      current = [];
    }
    current.push(tx);
  }
  if (current.length > 0) groups.push(current);

  return groups;
}

function formatGroupDate(dateStr: string): string {
  const today = new Date();
  const todayKey = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`;

  const yesterday = new Date(today);
  yesterday.setDate(yesterday.getDate() - 1);
  const yesterdayKey = `${yesterday.getFullYear()}-${String(yesterday.getMonth() + 1).padStart(2, '0')}-${String(yesterday.getDate()).padStart(2, '0')}`;

  if (dateStr === todayKey) return 'Hari Ini';

  if (dateStr === yesterdayKey) return 'Kemarin';

  const [y, m, d] = dateStr.split('-');
  const monthNames = ['Jan', 'Feb', 'Mar', 'Apr', 'Mei', 'Jun', 'Jul', 'Agu', 'Sep', 'Okt', 'Nov', 'Des'];
  return `${parseInt(d)} ${monthNames[parseInt(m) - 1]} ${y}`;
}

export default function TransactionList({ transactions, onSelect }: TransactionListProps) {
  if (transactions.length === 0) {
    return (
      <div className="empty-view-container" style={{ padding: '24px 0' }}>
        <p style={{ color: 'var(--color-text-secondary)', fontSize: 13.5, lineHeight: 1.55 }}>
          Belum ada transaksi.
        </p>
        <p style={{ color: 'var(--color-text-muted)', fontSize: 12.5, marginTop: 4 }}>
          Ceritakan aktivitasmu di atas, atau gunakan Catat Manual.
        </p>
      </div>
    );
  }

  const groups = groupByDate(transactions);

  return (
    <div className="transaction-list">
      {groups.map(group => (
        <div key={group[0].occurredOn} className="date-group">
          <div className="date-group-header">{formatGroupDate(group[0].occurredOn)}</div>
          {group.map(tx => (
            <TransactionItem key={tx.id} transaction={tx} onSelect={onSelect} />
          ))}
        </div>
      ))}
    </div>
  );
}
