import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';

import EntryScreen from './EntryScreen';

describe('EntryScreen guest session expiry', () => {
  it('explains expiry and retention when the previous guest sandbox is unavailable', () => {
    const html = renderToStaticMarkup(
      <EntryScreen
        onEnter={() => {}}
        onRequestLogin={() => {}}
        guestSessionExpired
      />,
    );

    expect(html).toContain('Sesi demo sebelumnya tidak tersedia lagi.');
    expect(html).toContain('30 hari tidak aktif');
    expect(html).toContain('masa retensi 30 hari');
    expect(html).toContain('Mulai sesi demo baru');
  });
});
