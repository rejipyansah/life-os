# Review Mendalam — Dana yang Disisihkan

**Tanggal review:** 8 Oktober 2026  
**Cakupan:** implementasi yang ada di `backend/LifeOS.Api`, `frontend/LifeOS.Web`, dan test terkait. Dokumen ini adalah review implementasi dan UX, bukan spesifikasi produk yang sudah disepakati.  
**Status:** hasil review ulang kode dan test pada tanggal di atas. Temuan berlabel **BUG** adalah ketidaksesuaian yang dapat ditelusuri langsung; **GAP/RISIKO** memerlukan cakupan test, pengukuran, atau keputusan produk. Referensi baris di bagian lama adalah snapshot review sebelumnya dan perlu dibaca bersama lokasi kode terkini di bagian 8.

## Ringkasan eksekutif

Modul sudah punya pemisahan konsep yang kuat: Sumber Dana adalah lokasi uang, Dana yang Disisihkan adalah alokasi pada level scope, dan pengeluaran riil masuk ke ledger transaksi. Saldo pos diturunkan dari riwayat append-only; API memeriksa scope dan saldo; siklus tidak dinormalisasi diam-diam oleh GET. Ada test untuk banyak invariants penting, termasuk scope isolation, top-up, spend, rollover dan concurrency.

Review ulang mengonfirmasi fondasi ledger dan sebagian besar perbaikan lifecycle. Tiga bug yang ditemukan pada review ini sudah diperbaiki dan ditambahkan test reproduksi: income yang dialokasikan ke pos siklus sekarang mengisi shortfall dan reversalnya mengoreksi pendanaan; saldo historis tetap akurat lintas halaman cursor; nominal expense wajib sama dengan debit rekening. Pengujian integrasi PostgreSQL serta optimasi proyeksi masih menjadi gap.

1. **[Diperbaiki] Arsip, detail, dan histori pos tersedia dari UI** dengan pagination riwayat.
2. **[Diperbaiki] Metadata pos dapat diedit dari UI** dengan penjelasan dampak target/siklus.
3. **[Sebagian]** Histori telah dipaginasi dan pemindaian berulang proyeksi dikurangi; agregasi database-side menyeluruh dan benchmark masih belum ada.
4. **[Diperbaiki] Validasi enum API set-aside** diterapkan pada create/update/close.
5. **[Diperbaiki] Pemilihan rekening saat spend** hanya memakai preferensi tersimpan, ditandai eksplisit, atau meminta pengguna memilih.
6. **[Diperbaiki] Aturan siklus seluruh jalur mutasi sudah konsisten.** Periode transaksi mengikuti tanggal pencatatan; tanggal bisnis memakai WIB.
7. **[Diperbaiki] Siklus bulanan mengikuti tanggal kalender 1–akhir bulan**, termasuk pos lama dan pos yang dibuat di tengah bulan.
8. **[Diperbaiki] Shortfall siklus tidak mengurangi Uang Bebas yang belum ada** dan otomatis didanai berurutan berdasarkan waktu pembuatan pos saat pemasukan masuk.
9. **[Diperbaiki] Rekening default disimpan sebagai referensi**, bukan sumber debit atau batas saldo untuk top-up.
10. **[Diperbaiki] Pos Sekali Pakai, Rutinitas Berkala, nominal wajib positif, dan koreksi `UsedAmount` setelah reversal** mengikuti perilaku yang dijelaskan di UI.

Perbaikan lifecycle, aturan kalender dan WIB, pendanaan otomatis shortfall, input nominal, koreksi `UsedAmount`, UI arsip/edit, persistensi isian saat mutation gagal, serta tiga temuan review ulang sudah diterapkan. Optimasi proyeksi masih perlu pengukuran dan agregasi database-side.

### Hasil review ulang — temuan baru/masih terbuka

1. **[DIPERBAIKI, P1] Income yang dialokasikan langsung ke pos siklus tidak mengurangi shortfall.** Jalur income dengan `SetAsideId` kini membagi alokasi: bagian yang memenuhi shortfall dicatat sebagai `CycleFunding`, sisanya sebagai `Added`. Tipe ledger ini memungkinkan reversal mengembalikan shortfall pada cycle yang sama. Implementasi di `SetAsideService.RecordTransactionIncomeAsync`; dipanggil oleh `TransactionService.ExecuteInTransactionAsync`.
2. **[DIPERBAIKI, P2] Saldo “setelah” pada halaman histori lanjutan tidak akurat.** API kini mengembalikan `BalanceAfter` per entry dengan memperhitungkan semua delta yang lebih baru dari entry pertama halaman cursor. UI menampilkan nilai dari API sehingga tidak mengulang kalkulasi dengan saldo terkini untuk tiap halaman. Implementasi di `SetAsideService.GetHistoryAsync`, DTO `SetAsideEntryProjection`, dan `YangDisisihkanSection.tsx`.
3. **[DIPERBAIKI, P1] Expense dengan `SetAsideId` dapat mencatat nominal transaksi berbeda dari debit rekening.** `ValidateExpenseAsync` kini mensyaratkan satu entry debit dengan nominal persis `-Transaction.Amount`, sebelum transaksi/alokasi disimpan. Validasi berlaku bagi semua expense, bukan hanya transaksi yang menggunakan pos.
4. **[GAP, P1] Belum ada bukti test concurrency pada PostgreSQL/Npgsql.** Operasi bergantung pada SERIALIZABLE dan retry untuk konflik; test service yang ditinjau memakai SQLite in-memory. SQLite tidak memverifikasi SQLSTATE serialization failure, retry, serta perilaku entity tracker Npgsql. Ini bukan bukti bug runtime, tetapi invariant uang utama belum diuji pada provider produksi.
5. **[GAP, P2] Daftar pos dan agregasi spend masih memuat seluruh histori usage ke memori.** Pengelompokan menghindari filter berulang per pos, tetapi list proyeksi tetap mengambil seluruh baris `Spent` untuk semua pos terpilih. Detail satu pos juga memuat semua entity pos scope untuk menentukan klaim rollover. Pagination history sendiri sudah memakai cursor. Rujukan: `SetAsideService.cs:760-778, 807-852`; benchmark/query count belum ditemukan.
6. **[UX, P2] Pesan sukses withdraw berpotensi menyiratkan perpindahan kas fisik.** UI menyebut dana “ditarik kembali ke kas” padahal withdraw hanya menambah Uang Bebas scope dan tidak mengubah akun. Toast perlu menyatakan bahwa saldo rekening tidak berubah. Rujukan: `useFinanceState.ts:462-475`; kontrak `docs/finance.md:181-187`.

Verifikasi regresi: `IncomeAllocatedToCyclingSetAsideFundsShortfall_AndReversalRestoresIt`, `History_CursorPagesKeepHistoricalBalanceAfterAccurate`, dan `Expense_RejectsTransactionAmountThatDiffersFromAccountDebit` lulus. Temuan #4–#6 adalah gap verifikasi/UX yang masih terbuka.

---

## 1. Model mental dan alur saat ini

### 1.1 Makna uang

- **Saldo aktual** = jumlah `TransactionEntry.Amount` per akun.
- **Saldo Dana yang Disisihkan** = jumlah `SetAsideEntry.Amount` untuk satu pos.
- **Total Dana yang Disisihkan aktif** = saldo semua pos aktif pada scope.
- **TotalAvailable** = total saldo aktual − total saldo pos aktif.
- **Uang Bebas** juga mengurangi komitmen rollover dan komitmen agenda terjadwal.
- Memindahkan uang antar akun tidak mengubah pos.
- Menambah/menarik alokasi bukan transaksi kas. `Spend` membuat transaksi expense dan mengurangi alokasi secara atomik.

Model ini konsisten dengan tujuan “alokasi, bukan rekening virtual”. Konsekuensinya perlu dibuat sangat terang di antarmuka: top-up/withdraw mengubah alokasi, tetapi **tidak** mengubah saldo rekening. Pengguna yang mengartikan “saya memindahkan uang ke rekening/tabungan lain” akan mendapat hasil yang berbeda dari pemindahan uang riil.

### 1.2 Jalur tindakan yang ditemukan

| Maksud pengguna | Jalur UI/backend | Efek yang diharapkan menurut kode |
|---|---|---|
| Buat tabungan biasa | Create set-aside dengan nominal awal 0 | Membuat pos, saldo aktual dan TotalAvailable tidak berubah |
| Buat rutinitas bertahap/berkala/sekali pakai | Create set-aside dengan saldo awal = target/plafon | Membuat entry alokasi awal; saldo rekening tidak berubah |
| Tambah alokasi | `AddAsync` | Tambah saldo pos dari Uang Bebas scope-wide; rekening referensi tidak membatasi atau didebit |
| Tarik alokasi | `WithdrawAsync` | Mengurangi saldo pos; tidak mengubah saldo rekening |
| Pakai pos | `SpendAsync` | Expense penuh dari satu akun; melepas paling banyak saldo pos; shortfall harus didukung Uang Bebas |
| Hapus/ tutup | `CloseAsync` | Menutup pos dan melepaskan semua saldo pos yang tersisa; bukan expense |
| Pakai pos dari transaksi umum | `CreateTransactionAsync(SetAsideId)` | Expense mengurangi pos; Income menambah pos |
| Reversal transaksi terkait pos | Reverse transaksi | Membuat transaksi reversal dan entry kompensasi alokasi |

### 1.3 Arsitektur yang sudah baik

- Saldo pos tidak disimpan sebagai counter mutable; ledger entry append-only menjadi sumber kebenaran.
- API menetapkan scope dari konteks server, bukan mempercayai `ScopeId` dari request.
- Pengeluaran khusus pos dan perubahan saldo alokasi berjalan dalam transaksi database yang sama.
- `SpendAsync` memisahkan rekening tempat uang benar-benar keluar dari pos yang dipakai.
- Ada validasi akun arsip/lintas-scope pada rekening referensi dan rekening transaksi riil, batas nominal positif, larangan menarik melebihi saldo pos, serta pengecekan Uang Bebas untuk alokasi.
- Query GET tidak mengubah data hanya karena tanggal rollover lewat.
- Test unit mencakup banyak alur dasar. Ini fondasi yang layak dipertahankan.

---

## 2. Temuan implementasi — prioritas tinggi

### 2.1 “Belanjakan & Arsipkan Pos” tidak mengarsipkan pos — DIPERBAIKI

**Perbaikan:** expense untuk `SingleSpend` kini menutup pos dan melepas saldo tersisa secara atomik. Reversal expense terkait mengaktifkan kembali pos dan membalik entry penutupan/alokasi. Validasi backend serta test ditambahkan.

**Verifikasi:** penutupan dilakukan dalam transaksi yang sama dengan expense. Test memastikan reversal mengaktifkan kembali pos dan memulihkan saldo alokasi.

### 2.2 Rutinitas berkala tidak memiliki status “sudah dieksekusi periode ini” — DIPERBAIKI

**Keputusan dan perbaikan:** hanya `RoutineBatch` dibatasi satu eksekusi per periode; biaya aktual boleh lebih kecil atau lebih besar dari estimasi. Sisa saldo dilepas ke Uang Bebas saat realisasi, dan kelebihan memerlukan uang bebas yang cukup. Status cycle diturunkan dari transaksi spend aktif (transaksi yang direversal tidak mengunci cycle), diproyeksikan ke UI, dan ditegakkan pada dedicated spend, transaksi biasa, serta realisasi agenda.

**Verifikasi:** status selesai diturunkan dari entry pemakaian aktif dalam rentang cycle; reversal tidak mengunci cycle. Test mencakup pemakaian sekali, pelepasan sisa, penolakan pemakaian kedua, reversal, dan expense lewat transaksi umum.

### 2.3 Nominal kosong/`0` berubah menjadi nominal default tanpa penjelasan — DIPERBAIKI

**Perbaikan:** input aktual Batch dan Sekali Pakai memakai nilai input apa adanya; nominal kosong/nol tidak lagi fallback ke target/plafon dan tombol submit nonaktif sampai nominal positif. Nilai awal tetap diisi dengan estimasi yang dapat diedit.

**Verifikasi:** nilai awal tetap berisi estimasi yang dapat diedit, tetapi input kosong atau nol kini menghasilkan nominal nol dan submit dinonaktifkan.

### 2.4 Aturan siklus belum konsisten di seluruh jalur mutasi — DIPERBAIKI

**Perbaikan:** expense khusus pos, transaksi umum, dan realisasi agenda menggunakan aturan siklus yang sama. Top-up, withdraw, dan close menormalisasi rollover sebelum menerapkan mutasi. Periode ditentukan berdasarkan tanggal pencatatan WIB, termasuk untuk transaksi yang `OccurredOn`-nya di-backdate.

**Verifikasi:** test mencakup batas satu eksekusi `RoutineBatch`, pelepasan sisa, reversal, expense melalui transaksi umum dengan `OccurredOn` lampau, serta normalisasi sebelum top-up, withdraw, dan close. Expense ber-backdate tetap memakai tanggal pencatatan WIB untuk memilih cycle.

### 2.5 Reversal membetulkan saldo, tetapi tidak membetulkan `UsedAmount` — DIPERBAIKI

**Perbaikan:** proyeksi `UsedAmount` mengabaikan expense yang direversal; reversal atas realisasi `RoutineBatch` juga membuka kembali status eksekusi pada periode aktif.

**Verifikasi:** proyeksi mengabaikan transaksi spend yang mempunyai reversal; test memastikan `UsedAmount` kembali nol dan status cycle dapat dieksekusi kembali.

---

## 3. Temuan implementasi — prioritas sedang/rendah

### 3.1 Arsip dan riwayat pos — DIPERBAIKI, saldo histori lintas halaman masih salah

**Status: DIPERBAIKI.** UI kini memiliki daftar Arsip, detail pos, alasan penutupan, serta histori ledger bertahap dengan cursor. Jenis entry dan catatan sistem dilokalkan, nominal menunjukkan arah perubahan dan saldo setelah entry, sementara transaksi terkait dapat dibuka untuk melihat ringkasannya tanpa menampilkan UUID mentah.

**Bukti terkini:** `YangDisisihkanSection.tsx:137-143, 190-197, 229-234` menyediakan tab aktif/arsip dan membuka riwayat dari pos arsip; `:438-540` memuat riwayat bertahap. Endpoint cursor ada di `FinanceEndpoints.cs:284-302`. API mengembalikan saldo entry lintas halaman melalui `SetAsideService.GetHistoryAsync`; lihat temuan review ulang #2 yang kini diperbaiki.

**Dampak tersisa:** arsip/detail dan saldo setelah lintas halaman tersedia. Tidak ditemukan tampilan total jumlah entry atau nominal agregat yang dilepas secara eksplisit.

**Saran lanjutan:** pertimbangkan menampilkan ringkasan jumlah entry dan nominal yang dilepas ketika tutup.

### 3.2 Edit metadata pos — DIPERBAIKI

**Status: DIPERBAIKI.** Aksi edit metadata tersedia terpisah dari aksi saldo. Field dan label mengikuti jenis pos: siklus hanya untuk Rutinitas Bertahap/Berkala, dan target diberi makna sesuai jenis dana. Preview menjelaskan saldo yang tidak berubah serta dampak target/siklus dan normalisasi cycle lama.

**Bukti terkini:** `YangDisisihkanSection.tsx:261-267, 544-609, 782-785` menampilkan modal edit metadata; API `PATCH` ada di `FinanceEndpoints.cs:304-325` dan backend menormalisasi cycle lama sebelum update di `SetAsideService.cs:118-183`.

**Dampak tersisa:** metadata dapat diperbaiki melalui UI. Perubahan target/cycle tetap memiliki dampak normalisasi/shortfall yang kompleks dan belum ada test integrasi UI untuk seluruh variasinya.

**Saran lanjutan:** pertahankan preview dan tambah test perubahan target/cycle di batas rollover, termasuk kasus target dihapus dan target menjadi lebih rendah dari saldo.

### 3.3 Fallback rekening saldo terbesar — DIPERBAIKI

**Status: DIPERBAIKI.** Fallback diam-diam ke rekening saldo tertinggi dihapus. Hanya preferensi rekening yang tersimpan yang di-preselect dan UI memberi label bahwa pilihan tersebut otomatis.

**Bukti terkini:** `resolveManualSourceAccountId` hanya memilih default yang tersimpan dan masih aktif; jika tidak ada, hasilnya kosong (`YangDisisihkanSection.tsx:94-105`). Pemilihan akun tetap wajib untuk pengeluaran riil.

**Dampak tersisa:** fallback ranking saldo sudah dihapus. Pastikan rekening preferensi otomatis tetap diberi penanda yang mudah dikenali di setiap modal dan pilihan berubah bila rekening diarsipkan.

**Saran lanjutan:** pertahankan tanpa tebakan rekening; tampilkan ringkasan debit rekening pada konfirmasi sebelum transaksi.

### 3.4 Top-up dan withdraw adalah perubahan alokasi, bukan perpindahan uang fisik — DIPUTUSKAN

**Bukti:** create/add/withdraw hanya membuat `SetAsideEntry`, tanpa `TransactionEntry`, sebagaimana di `SetAsideService` dan dokumentasi arsitektur.

**Risiko UX:** pengguna dapat memahami “top-up dari rekening” sebagai debit rekening. Yang dilakukan sistem adalah menandai sebagian uang scope-wide sebagai teralokasi; saldo akun tetap sama. Kolom sumber dana opsional dapat memperkuat kesan bahwa transfer fisik tercatat padahal tidak.

**Keputusan:** top-up mengurangi Uang Bebas scope-wide dan withdraw mengembalikannya. Keduanya tidak mengubah saldo rekening; top-up hanya bisa dilakukan jika Uang Bebas cukup. Pemindahan fisik antar rekening tetap dicatat melalui fitur Transfer.

### 3.5 Informasi rekening default kini disimpan sebagai referensi — DIPERBAIKI

**Perbaikan:** `DefaultSourceAccountId` disimpan meski saldo awal pos Rp0. Rekening ini hanya menjadi referensi rekening yang biasanya dipakai; saldonya tidak membatasi top-up dan tidak didebit.

**Dampak/perilaku:** alokasi top-up memakai Uang Bebas total scope, sehingga tetap bisa dilakukan bila rekening referensi kosong tetapi ada Uang Bebas di rekening lain. Bila Uang Bebas tidak cukup, top-up ditolak.

**Verifikasi:** test memastikan rekening referensi bersaldo Rp0 dapat dipakai ketika Uang Bebas dari rekening lain mencukupi; saldo rekening referensi tidak berubah.

### 3.6 Siklus bulanan mengikuti kalender — DIPERBAIKI

**Perbaikan:** siklus `Monthly` selalu dimulai tanggal 1 dan berakhir pada hari terakhir bulan. Pos yang dibuat tanggal 8 Oktober langsung memakai periode 1–31 Oktober; aturan ini juga berlaku bagi pos bulanan yang sudah ada.

**Dampak:** anchor lama pada tanggal 29–31 tidak lagi menggeser periode setelah bulan pendek.

**Verifikasi:** test mencakup pembuatan di tengah bulan, pergantian bulan, Februari, dan perhitungan periode dari anchor lama.

### 3.7 Tanggal operasi menggunakan WIB — DIPERBAIKI

**Perbaikan:** tanggal bisnis backend dan default tanggal frontend dihitung dengan zona WIB (UTC+7), sementara timestamp audit tetap disimpan dalam UTC.

**Dampak:** tanggal default transaksi dan batas siklus konsisten dengan kalender pengguna WIB.

**Verifikasi:** batas siklus dan transaksi menggunakan tanggal WIB; entry ledger yang timestamp-nya UTC dipetakan ke jendela hari WIB.

### 3.8 Validasi enum API set-aside — DIPERBAIKI

**Status: DIPERBAIKI.** Create memvalidasi `Kind` dan `CycleKind`, update memvalidasi `CycleKind`, dan close memvalidasi `Reason`; enum numerik di luar nilai yang dikenal ditolak.

**Bukti terkini:** `CreateInternalAsync` memvalidasi `Kind`/`CycleKind` (`SetAsideService.cs:51-56`), update memvalidasi cycle (`:123-124`), dan close memvalidasi reason (`:509-510`). Test invalid enum tersedia di `SetAsideServiceTests.cs:725-756`.

**Dampak:** risiko enum numerik tidak dikenal ditangani pada jalur yang ditinjau; string enum tidak dikenal bergantung pada deserialisasi request dan belum diverifikasi melalui test endpoint.

**Saran lanjutan:** pertahankan validasi dan tambahkan test endpoint untuk deserialisasi string/nilai numerik invalid serta respons 422.

### 3.9 Shortfall siklus dan auto-funding income — DIPERBAIKI

**Perbaikan:** shortfall target disimpan dan ditampilkan terpisah, tetapi tidak mengurangi Uang Bebas. Saat pemasukan dicatat, sistem otomatis mengisi shortfall aktif sesuai Uang Bebas.

**Prioritas:** bila pemasukan belum cukup untuk semua pos, pendanaan berjalan dari pos yang dibuat lebih dahulu; kekurangan pos berikutnya tetap terlihat. Reversal pemasukan membalik pendanaan otomatis pada cycle yang sama.

**Verifikasi:** FreeCash tidak menjadi negatif hanya karena target siklus belum terpenuhi; test mencakup pemasukan parsial, prioritas creation order, dan reversal.

### 3.10 Pagination histori dan kompleksitas proyeksi — DIPERBAIKI SEBAGIAN

**Status: DIPERBAIKI SEBAGIAN.** Histori memakai cursor pagination; detail hanya mengambil 20 entry terbaru; proyeksi membatasi query usage pada pos terpilih dan mengelompokkan entry sekali agar tidak memindai seluruh daftar per pos. Query masih memuat baris spend untuk menghitung usage, sehingga agregasi database-side dan benchmark dataset besar tetap menjadi pekerjaan lanjutan.

**Bukti terkini:** `GetHistoryAsync` memakai cursor dan batas 1–100 (`SetAsideService.cs:684-748`); detail memuat maksimal 20 entry terkini (`:925-959`). Namun proyeksi tetap memuat seluruh entry spend untuk pos terpilih ke memori dan semua pos scope untuk proyeksi alokasi rollover (`:760-778, 807-852`).

**Dampak:** pemindaian berulang O(pos × usage) sudah dikurangi dengan grouping, tetapi biaya query/memori tetap tumbuh terhadap jumlah usage dan pos. Belum ada benchmark dataset besar.

**Saran:** pindahkan agregat pemakaian per pos/window ke SQL bila didukung provider, optimalkan query detail dengan tetap menjaga alokasi scope-wide, dan ukur query/time/memori pada dataset besar.

---

## 4. Edge-case dan human-error yang perlu diuji

Checklist ini mencakup kasus yang belum cukup terlihat terwakili di UI/test; bukan berarti seluruhnya sudah menjadi bug yang terkonfirmasi.

### Siklus dan waktu

- Pos bulanan dibuat tanggal 29/30/31; Februari pendek; tahun kabisat; beberapa bulan/tahun terlewati tanpa ada write.
- Pengguna membuat pemakaian pada tanggal lokal berbeda dengan UTC; request terjadi dekat tengah malam.
- `OccurredOn` transaksi sengaja di-backdate sebelum/ke siklus sebelumnya, sementara write terjadi hari ini.
- Edit cycle/target pada hari rollover: pastikan normalisasi memakai konfigurasi lama dan pengguna melihat ringkasan dampaknya.
- Banyak cycle terlewati: pastikan desain memang hanya menormalkan saldo satu kali ke target saat ini, bukan membuat satu entry per periode yang terlewat.

### Nominal dan saldo

- Input kosong, `0`, angka sangat besar, pemisah ribuan, paste teks, dan nominal yang melebihi safe integer JavaScript.
- Pengguna memilih akun dengan saldo kecil tetapi total scope punya dana di akun lain; jelaskan mengapa aksi ditolak bila sumber akun terpilih memang menjadi batas fisik.
- Saldo pos 0, saldo pos tepat sama dengan biaya, biaya sedikit di atas saldo pos, serta overspend lebih besar dari Uang Bebas.
- TotalAvailable atau Uang Bebas negatif akibat koreksi/reversal/legacy data; create/top-up/spend harus menampilkan kekurangan yang dapat dipahami.
- Beberapa pos rollover pending berebut saldo; pastikan prioritas creation order terlihat atau dapat diprediksi user.
- Top-up dengan hint sumber akun arsip/hilang; hint harus dibersihkan atau dialihkan dengan pemberitahuan.

### Riwayat, koreksi, dan duplikasi

- Tap/double click submit, refresh browser setelah timeout, atau retry jaringan setelah server menyimpan tetapi respons hilang (idempotency).
- Dua tab/perangkat melakukan spend, withdraw, close, atau create bersamaan.
- Reversal expense terkait pos; reversal sesudah cycle berganti; reversal setelah pos ditutup.
- Expense dengan `OccurredOn` yang di-backdate tetap mengikuti tanggal pencatatan untuk penentuan cycle.
- Reversal atas transaksi income yang mengisi pos; pastikan saldo, `UsedAmount`, dan nama pos pada aktivitas benar.
- Reversal atas transaksi yang sudah punya reversal; penolakan harus jelas dan konsisten.
- Tutup pos dengan saldo nol, saldo positif, rollover pending, atau request penutupan berulang.
- Pengguna menutup pos dengan alasan `Spent` tanpa transaksi belanja: UI/API harus membedakan “menutup alokasi” dari “mencatat pengeluaran”.

### Interaksi manusia dan UX

- Nama pos mirip/duplikat; pengguna salah memilih pos dari dropdown transaksi/agenda.
- Rekening terpilih otomatis berubah karena saldo ranking berubah atau rekening default diarsipkan.
- Pengguna mengira top-up mengurangi rekening atau withdraw menambah saldo rekening.
- Pengguna mengira target adalah dana yang sudah tersedia; pengguna tidak memahami bahwa target cycle dapat shortfall.
- Pengguna mengira realisasi sekali pakai/batch mengarsipkan/menandai selesai, padahal pos tetap aktif.
- Pengguna tidak sengaja klik delete dan tidak dapat mencari kembali pos/histori sesudahnya.
- Aksesibilitas: periksa keyboard/focus, nama aksesibel untuk tombol ikon, kontras, serta pembacaan error dan ringkasan nominal oleh screen reader. Tombol ikon pada kartu sekarang memiliki `aria-label`, tetapi seluruh dialog belum diverifikasi dengan pengujian aksesibilitas.
- Modal mutation kini menutup hanya setelah aksi berhasil dan mempertahankan isian ketika gagal (`YangDisisihkanSection.tsx:374-389` dan callback modal). Tetap verifikasi kasus timeout ketika server sukses tetapi respons hilang; perlindungan idempotensi belum tampak.
- **Sudah diuji:** pagination histori >1 halaman memeriksa saldo “setelah” untuk entry lama, selain kelanjutan cursor/no duplicate.
- **Sudah diuji:** income bertarget `SetAsideId` saat shortfall aktif dan reversal; perlu pertahankan cakupan untuk kondisi income parsial dan pemasukan berikutnya.
- **Sudah diuji:** expense saat `Transaction.Amount` tidak sama dengan debit entry ditolak sebelum transaksi/alokasi tersimpan.

---

## 5. Gap test yang disarankan

Test suite backend saat ini mencakup banyak perilaku dasar, kalender, rollover, siklus batch, reversal, dan invariant concurrency pada SQLite. Prioritas gap tersisa:

1. **Income allocation:** perluas test income langsung ke pos dengan shortfall parsial, nominal income lebih besar dari shortfall, rollover, pendanaan income berikutnya, dan reversal sesudah pergantian cycle.
2. **History balance:** tambahkan kasus beberapa entry dengan timestamp identik dan periksa determinisme urutan/ saldo across cursor (test dasar positif/negatif lintas page sudah tersedia).
3. **Concurrency di PostgreSQL:** paksa serialization failure dan uji retry/state ORM dengan SQLSTATE Npgsql. Test SQLite saat ini tidak memvalidasi provider produksi.
4. **Perubahan metadata UI/API:** update target di bawah saldo, target dihapus, cycle diubah saat rollover, dan validasi enum pada batas HTTP.
5. **Frontend interaksi/accessibility:** respons timeout ambigu, pemilihan rekening otomatis vs manual, penjelasan alokasi tanpa pergerakan rekening, navigasi keyboard/screen reader, serta histori lintas page.
6. **Performance regression:** benchmark proyeksi dengan banyak pos dan entry; ukur query count, waktu, dan alokasi memori.

---

## 6. Rekomendasi UX dan kontrak domain

### Prioritas P0 — akurasi uang dan cegah salah catat

- Satu aksi submit harus menunjukkan: **nominal transaksi**, **rekening yang didebit**, **bagian dari saldo pos**, **bagian dari uang belum dialokasikan**, dan **saldo pos setelah aksi**.
- **Sudah diterapkan:** hilangkan fallback nominal; angka yang tampak harus sama dengan angka yang dikirim. Pertahankan regresi test untuk nominal kosong/nol.
- **Sudah diterapkan:** konsolidasikan aturan spend/cycle lintas jalur; pertahankan test untuk transaksi umum dan agenda.
- **Sudah diterapkan:** state arsip dan periode selesai berasal dari backend; pertahankan test agar toast/UI tetap selaras.
- **Sebagian diterapkan:** reversal mengoreksi saldo dan `UsedAmount`; uji tambahan untuk income alokasi langsung masih dibutuhkan.
- Validasi bahwa nominal expense sama dengan debit akun sebelum memproses alokasi.
- Setelah double-submit/network ambiguity, gunakan idempotency key atau mekanisme deduplikasi agar satu gesture tidak menghasilkan dua transaksi.

### Prioritas P1 — transparansi dan pemulihan

- **Sudah diterapkan:** detail, histori, arsip, dan edit metadata tersedia. Perbaiki saldo histori lintas pagination dan lengkapi preview dampak shortfall.
- Ganti istilah yang berpotensi disalahartikan: “Top-up” → “Tambah alokasi” jika tidak ada debit akun; “Hapus” → “Tutup pos”; sebutkan secara eksplisit bahwa uang rekening tidak bergerak.
- **Sudah diterapkan sebagian:** modal mempertahankan input dan menampilkan error; uji timeout ambigu dan idempotensi masih diperlukan.
- Untuk sumber dana yang otomatis dipilih, tampilkan ringkasan konfirmasi yang nyata dan jangan menyebutnya “dari pos”; rekening adalah lokasi dana, pos adalah alokasi.

### Prioritas P2 — skalabilitas dan operasional

- Definisikan timezone scope dan gunakan `TimeProvider`/clock abstraction agar batas hari/siklus deterministik dan testable.
- History pos sudah dipaginasi; lanjutkan agregasi usage database-side dan pagination/query transaksi sesuai skala yang diukur.
- Tambahkan log terstruktur untuk cycle funding/shortfall, close, spend, reversal dan konflik concurrent tanpa menaruh data sensitif berlebihan.
- Monitor jumlah serialization retry/failure, durasi endpoint proyeksi, ukuran respons, serta error mutation.

---

## 7. Keputusan produk

Keputusan yang sudah dikonfirmasi:

1. **Rutinitas Berkala** hanya dapat dieksekusi satu kali per periode.
2. **Sekali Pakai** ditutup setelah expense pertama yang berhasil dicatat; reversal membuka kembali pos.
3. Transaksi lampau menggunakan cycle berdasarkan tanggal pencatatan, bukan `OccurredOn`; tanggal bisnis memakai WIB.
4. Top-up/withdraw hanya mengubah alokasi scope-wide; rekening default adalah referensi, bukan sumber debit. Perpindahan rekening dicatat lewat Transfer.
5. Shortfall cycle tidak mengurangi Uang Bebas sampai dana tersedia; pemasukan baru otomatis mengisi kekurangan aktif sesuai urutan pos dibuat.
6. **Belum diputuskan:** apakah nama pos boleh duplikat; jika boleh, dropdown transaksi perlu membedakan pos dengan kategori/saldo.

---

## 8. Lokasi kode utama

- Domain: `backend/LifeOS.Api/Models/SetAside.cs`, `SetAsideEntry.cs`
- Commands dan projection: `backend/LifeOS.Api/Services/SetAsideCommands.cs`
- Mutasi, rollover, proyeksi dan histori: `backend/LifeOS.Api/Services/SetAsideService.cs`
- Aritmetika siklus: `backend/LifeOS.Api/Services/SetAsideCycle.cs`
- Perubahan alokasi dari transaksi umum/reversal: `backend/LifeOS.Api/Services/TransactionService.cs`
- Perubahan alokasi saat realisasi agenda: `backend/LifeOS.Api/Services/UpcomingEventService.cs`
- Total keuangan: `backend/LifeOS.Api/Services/BalanceCalculator.cs`, `FinanceStateService.cs`
- UI pos: `frontend/LifeOS.Web/src/components/finance/YangDisisihkanSection.tsx`
- Aksi dan refresh UI: `frontend/LifeOS.Web/src/finance/useFinanceState.ts`
- Mapping API/UI: `frontend/LifeOS.Web/src/finance/apiMapping.ts`
- Test backend: `backend/LifeOS.Tests/SetAsideServiceTests.cs`, `TransactionReversalTests.cs`
- Kontrak domain saat ini: `docs/finance.md`

### Berkas yang diperiksa pada review ulang

- Endpoint/API contract: `backend/LifeOS.Api/Services/FinanceEndpoints.cs`, `SetAsideCommands.cs`
- Model dan mapping EF/index: `backend/LifeOS.Api/Models/SetAside.cs`, `SetAsideEntry.cs`, `backend/LifeOS.Api/Data/ApplicationDbContext.cs`
- Saldo dan proyeksi scope: `backend/LifeOS.Api/Services/BalanceCalculator.cs`, `FinanceStateService.cs`, `SetAsideService.cs`
- Arsitektur cycle dan transaksi lintas jalur: `SetAsideCycle.cs`, `TransactionService.cs`, `UpcomingEventService.cs`
- UI, pemetaan dan mutation: `frontend/LifeOS.Web/src/components/finance/YangDisisihkanSection.tsx`, `frontend/LifeOS.Web/src/finance/apiMapping.ts`, `useFinanceState.ts`
- Test: `backend/LifeOS.Tests/SetAsideServiceTests.cs`, `SetAsideCycleTests.cs`, `TransactionReversalTests.cs`, `UpcomingEventServiceTests.cs`

Verifikasi implementasi: `dotnet test backend/LifeOS.Tests/LifeOS.Tests.csproj` lulus (383 test); `npm run build` pada `frontend/LifeOS.Web` lulus; `git diff --check` lulus. Test PostgreSQL/Npgsql dan benchmark proyeksi besar yang direkomendasikan di bagian 5 belum dijalankan/tersedia.
