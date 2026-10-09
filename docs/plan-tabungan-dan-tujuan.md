# Plan — Tabungan dan Dana Tujuan

**Status:** Implementasi awal diterapkan untuk dana `Saving`; rincian sub-anggaran dan klasifikasi cadangan/tujuan tersendiri masih menjadi rencana.

![Diagram alur dana cadangan dan dana tujuan](tabungan-dana-tujuan-flow.svg)

## Ringkasan

Pisahkan dua maksud yang saat ini mudah tercampur:

1. **Menyisihkan uang**: mengurangi Uang Bebas dengan mengalokasikan dana ke sebuah pos. Ini fungsi top up dan withdraw saat ini.
2. **Membelanjakan uang yang sudah dialokasikan**: mencatat pengeluaran riil dari rekening dan mengurangi saldo pos yang menjadi sumber anggaran pengeluaran itu.

Top up/withdraw bukan transfer rekening dan bukan pemasukan/pengeluaran. Keduanya mengatur berapa banyak uang yang masih bebas dipakai.

## Model pengguna yang diusulkan

### A. Dana cadangan

Contoh: dana darurat atau cadangan kesehatan.

- Tujuannya menahan sebagian uang agar tidak dihitung sebagai Uang Bebas.
- Target tidak wajib; pos tanpa target tetap berguna.
- Top up menyisihkan lebih banyak Uang Bebas.
- Withdraw melepas sebagian alokasi kembali ke Uang Bebas.
- Jika dipakai untuk pengeluaran nyata, catat transaksi Expense dan kaitkan dengan pos. Pos tetap aktif untuk penggunaan berikutnya.

### B. Dana tujuan

Contoh: beli HP atau modal nikah.

- Mempunyai tujuan yang jelas dan dapat memiliki target total, misalnya modal nikah Rp50 juta.
- Pengeluaran untuk tujuan dicatat sebagai transaksi Expense yang mengurangi alokasi pos, bukan sebagai withdraw. Dengan demikian pemakaian nyata tetap tercatat pada rekening dan riwayat transaksi.
- Siklus menabung/top up bukan siklus belanja. Target total Rp50 juta tidak boleh ditafsirkan sebagai target bulanan.
- Tujuan dapat diselesaikan atau dibatalkan. Sisa saldo saat penutupan dilepas ke Uang Bebas, kecuali telah dibelanjakan melalui transaksi.

Perilaku saat belanja:

| Kasus | Perilaku yang diusulkan |
|---|---|
| Beli HP sekali | Catat Expense dari pos tujuan. Setelah transaksi, pengguna dapat menandai tujuan selesai; sisa dana kembali ke Uang Bebas saat pos ditutup. |
| Modal nikah bertahap | Catat Expense berkali-kali dari pos yang sama. Pos tetap aktif hingga pengguna menyelesaikan tujuan. |
| Pengeluaran melebihi saldo pos | Bagian dari pos tidak boleh melebihi saldo yang tersedia. Selisih dapat berasal dari Uang Bebas hanya jika pengguna menyetujuinya secara eksplisit dan Uang Bebas mencukupi. |

## Rincian anggaran tujuan

Modal nikah Rp50 juta dan “dekor Rp5 juta” adalah dua tingkat informasi berbeda:

- **Saldo tujuan** menjawab: berapa uang yang telah disisihkan dan masih tersisa untuk modal nikah secara keseluruhan?
- **Rincian anggaran** menjawab: berapa plafon dan realisasi untuk dekor, katering, dan kebutuhan lainnya?

### Rekomendasi tahap awal

Mulai dengan satu pos tujuan berulang-pakai. Pengeluaran dapat ditautkan ke pos dan diberi deskripsi/kategori transaksi. Jangan membuat sub-pos atau plafon kategori sebagai syarat agar model dasar berguna.

Ini mendukung pencatatan “dekor Rp5 juta” sebagai transaksi, tetapi belum menjamin plafon dekor Rp5 juta, sisa plafon dekor, atau pencegahan overspend per kebutuhan.

### Kandidat pengembangan

Jika pengguna perlu mengendalikan plafon tiap kebutuhan, tambahkan rincian anggaran di bawah tujuan induk, misalnya:

- Modal nikah — target Rp50 juta
  - Dekor — plafon Rp5 juta
  - Katering — plafon Rp20 juta
  - Busana — plafon Rp8 juta

Perlu diputuskan apakah rincian tersebut hanya kategori/plafon analitik atau sub-pos alokasi sungguhan. Sub-pos alokasi sungguhan membutuhkan aturan agar uang tidak terhitung ganda antara tujuan induk dan anak, serta aturan untuk pengeluaran bersama dan pemindahan plafon. Karena itu, rekomendasi awal adalah menunda sub-pos alokasi sampai kebutuhan nyata terkonfirmasi.

## Pemetaan terhadap sistem saat ini

Finance saat ini memakai `SetAside` sebagai pool alokasi scope-wide. Saldo berasal dari jumlah `SetAsideEntry`; akun hanya dipilih untuk transaksi riil. Top up dan withdraw tidak mengubah saldo akun.

- `Saving` saat ini paling dekat dengan dana cadangan dan juga dapat dipakai untuk tujuan berulang-pakai.
- `SingleSpend` saat ini menutup pos setelah satu Expense. Ini cocok untuk tujuan sekali bayar, tetapi penyelesaian tujuan dan pengeluaran adalah satu kejadian otomatis.
- `TargetAmount` tersedia, tetapi bukan target periodik jika `CycleKind` bernilai `None`.
- Belum ada entitas tujuan induk/sub-pos anggaran, plafon per kategori, atau realisasi kategori.

### Sudah diterapkan

- Aksi `Saving` membedakan **Tambah Alokasi**, **Lepas Alokasi**, dan **Catat Pengeluaran**.
- Pengeluaran dari modal `Saving` menjadi transaksi Expense riil dari rekening yang dipilih, mengurangi saldo alokasi, dan pos tetap aktif untuk pemakaian berikutnya.
- Aksi pengeluaran UI membatasi nominal pada saldo pos dan saldo rekening agar tidak mencampur dana bebas tanpa persetujuan eksplisit.
- Tujuan dapat diselesaikan manual; sisa alokasi dilepas ke Uang Bebas tanpa mengubah saldo rekening.
- Copy UI dan notifikasi top up/withdraw menjelaskan bahwa saldo rekening tidak berubah.
- Formulir dana tidak meminta rekening referensi. Target dibuat opsional dengan checkbox yang membuka input nominal dan area ketuk lebar untuk layar mobile.
- Kelola Dana memakai tab ringkas Top Up, Withdraw, dan Belanja. Opsi Semua berada sebaris dengan nominal cepat pada tab Withdraw dan Belanja.
- Form nominal tampil sebelum preset nominal, rekening sumber, dan catatan; tab Top Up menampilkan jumlah Uang Bebas tersedia secara jelas di atas nominal.

Implementasi ini memakai `Saving` yang sudah ada; belum menambah klasifikasi data cadangan vs tujuan, dan belum membuat sub-anggaran seperti plafon dekor/katering.

Usulan semantik produk yang lebih jelas:

1. Pertahankan alokasi sebagai konsep terpisah dari rekening dan transaksi.
2. Bedakan **cadangan fleksibel** dan **tujuan** berdasarkan maksud pengguna; jangan menjadikan “sekali bayar” satu-satunya tipe tujuan.
3. Tujuan sekali bayar dan tujuan bertahap sama-sama dapat memakai saldo alokasi; perbedaannya adalah penyelesaian: otomatis setelah satu transaksi atau manual setelah beberapa transaksi.
4. Untuk MVP, pengeluaran tujuan berulang-pakai tidak menutup pos otomatis. Penyelesaian manual melepaskan saldo tersisa.
5. Pertahankan `SingleSpend` untuk perilaku legacy/khusus yang memang ingin otomatis selesai setelah satu Expense, atau migrasikan hanya setelah keputusan kompatibilitas dibuat.

## Alur yang diharapkan

### Menyisihkan

1. Pengguna membuat dana cadangan atau tujuan.
2. Pengguna memasukkan nominal alokasi awal, atau mulai dari nol.
3. Sistem memvalidasi nominal terhadap Uang Bebas.
4. Sistem menambahkan ledger alokasi; saldo rekening tidak berubah.

### Menambah alokasi kemudian

1. Pengguna memilih top up dan nominal.
2. Sistem memeriksa Uang Bebas scope-wide.
3. Sistem menambah saldo alokasi; akun tidak didebit.

### Membelanjakan alokasi

1. Pengguna mencatat transaksi Expense riil dan memilih rekening sumber.
2. Pengguna memilih dana tujuan/cadangan yang dipakai, bila relevan.
3. Sistem mencatat debit rekening dan pengurangan alokasi secara atomik.
4. Tujuan bertahap tetap aktif. Tujuan sekali bayar dapat diselesaikan otomatis sesuai tipe yang dipilih atau ditutup manual sesuai keputusan UX.

### Menyelesaikan atau membatalkan tujuan

- Menyelesaikan tidak membuat transaksi Expense dengan sendirinya.
- Sisa saldo alokasi dilepas ke Uang Bebas melalui entry penutupan.
- Pengeluaran nyata yang sudah terjadi tetap berada di ledger transaksi.
- Reversal transaksi terkait harus mengompensasi entry alokasi sesuai aturan reversal yang sudah berlaku.

## Aturan istilah UI

- **Top up / Tambah alokasi**: “Mengurangi Uang Bebas; saldo rekening tidak berubah.”
- **Withdraw / Lepas alokasi**: “Mengembalikan dana ke Uang Bebas; saldo rekening tidak berubah.”
- **Pakai dana / Catat pengeluaran**: transaksi Expense nyata dari rekening, sekaligus mengurangi alokasi yang dipilih.
- **Selesaikan tujuan**: menutup pos dan melepas saldo alokasi yang tersisa; bukan transaksi belanja.

Hindari label “pindah ke tabungan” untuk top up karena dapat dipahami sebagai transfer antar-rekening.

## Keputusan yang perlu dikonfirmasi sebelum implementasi

1. Apakah dana cadangan dan dana tujuan perlu menjadi jenis eksplisit di UI/data, atau cukup menjadi tujuan/label pada pos yang sama?
2. Untuk tujuan sekali bayar, apakah pos otomatis selesai setelah Expense pertama, atau pengguna harus menekan “Selesaikan tujuan” agar sisa dana tidak terlepas tanpa sengaja?
3. Apakah Expense boleh memakai sebagian dari pos dan sebagian dari Uang Bebas? Jika boleh, apakah konfirmasi eksplisit wajib?
4. Pada tahap awal, apakah rincian seperti dekor Rp5 juta cukup sebagai deskripsi/kategori transaksi, atau perlu plafon dan sisa anggaran terpisah?
5. Apakah penutupan tujuan dengan saldo tersisa selalu melepas sisanya ke Uang Bebas, atau pengguna perlu memilih tujuan alokasi baru?

## Tahapan implementasi yang disarankan

1. **Tetapkan semantik dan istilah:** konfirmasi keputusan di atas, khususnya pemakaian parsial, penyelesaian tujuan sekali bayar, dan rincian kategori.
2. **Rapikan UX tanpa mengubah ledger:** jelaskan efek top up/withdraw, pisahkan aksi catat Expense dari aksi lepas alokasi, dan sediakan penyelesaian manual untuk tujuan berulang-pakai.
3. **Sesuaikan model hanya bila perlu:** tambah tipe tujuan atau status penyelesaian hanya setelah kompatibilitas data lama dan arti `SingleSpend` diputuskan.
4. **Tambahkan rincian anggaran sebagai tahap terpisah:** mulai dari kategori/plafon analitik; gunakan sub-pos alokasi hanya jika aturan saldo induk-anak dapat dibuat tanpa penghitungan ganda.
5. **Verifikasi invariants:** Uang Bebas, saldo rekening, saldo alokasi, penutupan, scope isolation, konkurensi, dan reversal harus konsisten di backend, UI, serta test.

## Batas cakupan plan ini

Dokumen ini mencatat rencana produk/domain dan status implementasi awal. `docs/finance.md` tetap sumber normatif untuk perilaku yang sudah diterapkan. Setelah keputusan lanjutan disepakati, perbarui spesifikasi Finance, API/DTO bila terpengaruh, UI, migration, dan regression tests bersama implementasinya.
