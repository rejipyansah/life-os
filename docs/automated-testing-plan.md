# Rencana Pematangan Automated Testing

## Tujuan

Setiap perubahan kode dapat diperiksa secara otomatis, sehingga pemilik aplikasi tidak perlu menjalankan skenario pengujian satu per satu secara manual. Test harus memberi umpan balik yang cepat, dapat diulang, dan berfokus pada pencegahan regresi—terutama kesalahan saldo, transaksi, isolasi data, dan alur utama pengguna.

## Kondisi Saat Ini

- Backend memiliki 385 test unit/service dan test HTTP/API di `backend/LifeOS.IntegrationTests`.
- Suite unit/service memakai SQLite in-memory untuk feedback cepat. Suite terpisah `backend/LifeOS.IntegrationTests` memakai PostgreSQL/Npgsql untuk migrasi dan concurrency.
- Frontend menggunakan Vitest dan sudah memiliki test untuk kalkulasi/mapping finansial serta sebagian komponen.
- Frontend memiliki skrip `test`, `build`, dan `lint`.
- Perintah root `npm test` menjalankan backend tests, HTTP/API integration tests, frontend tests, lint, build tool benchmark, dan production build. Jika `LIFEOS_TEST_POSTGRES_CONNECTION` tersedia, perintah ini juga menjalankan test provider PostgreSQL. Jika `LIFEOS_E2E_POSTGRES_CONNECTION` tersedia, perintah ini juga menjalankan E2E browser.
- Workflow `.github/workflows/automated-tests.yml` memuat semua pemeriksaan dan PostgreSQL/Playwright service, tetapi saat ini dinonaktifkan di GitHub. File test dan benchmark disetel manual-only agar tidak terus gagal karena billing; workflow benchmark tidak lagi terjadwal.
- Catatan review modul Dana yang Disisihkan mengidentifikasi gap seputar PostgreSQL, beberapa variasi alokasi pemasukan, kontrak HTTP/UI, dan benchmark proyeksi pada data besar.

## Prinsip

1. **Otomatiskan pemeriksaan yang berulang.** Developer cukup menjalankan satu perintah lokal; CI menjalankan pemeriksaan yang sama saat pull request.
2. **Utamakan risiko produk.** Prioritaskan invariants finansial, scope isolation, mutasi, reversal, dan migrasi.
3. **Gunakan test berlapis.** Test cepat untuk aturan dan komponen; test integrasi untuk kontrak dan perilaku database produksi.
4. **Data uji deterministik dan sintetis.** Jangan memakai data finansial pribadi. Dataset besar hanya dibuat untuk pengujian skala/performa yang memang membutuhkannya.
5. **Bug yang ditemukan menjadi regresi test.** Perbaikan tidak dianggap tuntas sampai test yang menangkap bug tersebut tersedia dan lulus.
6. **Coverage bukan sasaran tunggal.** Angka coverage dapat membantu menemukan area yang luput, tetapi kelulusan ditentukan terutama oleh cakupan risiko dan invariants.

## Status Implementasi

- **Selesai:** perintah test lokal terpadu dan skrip test frontend.
- **Dikonfigurasi, dijeda:** workflow manual untuk backend, HTTP/API, frontend, lint, build, PostgreSQL tests, dan Playwright E2E. GitHub Actions workflow disabled sementara di repo settings karena billing account lock.
- **Selesai:** test integrasi PostgreSQL untuk migrasi dari schema kosong dan konflik serialisasi yang memverifikasi retry serta refresh entity tracker.
- **Selesai:** test HTTP/API memakai TestServer memeriksa enum/nominal invalid, pemasukan dan expense lalu reversal, create/postpone agenda, `ScopeId` client diabaikan, serta data transaksi/pos tidak bisa dibaca guest scope lain.
- **Selesai:** test interaksi frontend mencakup pemilihan sumber dana wajib, pos opsional, submit pembayaran, postpone, dan tutup modal dengan Escape.
- **Diperkuat:** hasil “berhasil dicatat” hanya muncul setelah backend mengonfirmasi simpanan; kegagalan mempertahankan konfirmasi untuk retry dan tombol submit dikunci selama request pending untuk mencegah duplikasi.
- **Terverifikasi:** Playwright menjalankan alur browser guest → catat pemasukan → verifikasi proyeksi backend dengan PostgreSQL E2E yang terisolasi.
- **Diperkuat:** `SerializableCommandRunner` membersihkan tracked state setelah serialization failure agar retry membaca ulang nilai database terbaru.
- **Selesai:** regresi pemasukan teralokasi di atas shortfall dan histori dengan timestamp identik lintas cursor.
- **Selesai:** benchmark proyeksi PostgreSQL dengan generator data sintetis, SELECT counter, ukuran dataset/iterasi yang dapat diatur, cleanup schema otomatis, dan workflow mingguan/manual.
- **Selesai oleh pengguna:** ruleset aktif untuk `main` mewajibkan pull request dan melarang force-push/penghapusan branch.
- **CI PR #1:** GitHub menolak memulai job karena akun repository terkunci akibat masalah billing. Runner tidak mengeksekusi langkah/test; ini bukan kegagalan test kode.
- **Perubahan sementara:** workflow Actions dinonaktifkan dan dijadikan manual-only; required Actions status check dilepas dari ruleset. Test lokal `npm test` tetap berjalan penuh.
- **Berikutnya:** bila akses Actions pulih, aktifkan workflow, pulihkan pemicu PR, dan evaluasi kembali required status check. Tidak perlu menambahkan budget berbayar untuk test lokal.

Template ruleset aktif tersimpan di [`github-main-branch-ruleset.json`](github-main-branch-ruleset.json). Ruleset menargetkan `main`, mewajibkan pull request, serta mencegah force-push dan penghapusan branch. Required status check tidak dipasang selama Actions dijeda.

Jalankan semua pemeriksaan lokal dengan `npm test` dari root. Test endpoint HTTP berjalan dengan TestServer dan SQLite in-memory. Untuk suite PostgreSQL/Npgsql, atur `LIFEOS_TEST_POSTGRES_CONNECTION` ke database test terisolasi. Untuk E2E browser, instal Chromium (`npx playwright install chromium`) dan atur `LIFEOS_E2E_POSTGRES_CONNECTION` ke database khusus yang namanya mengandung `test` atau `e2e`; script akan menolak nama database lain dan menerapkan migrasi sebelum menjalankan browser.

Suite regresi kini juga menguji pemasukan teralokasi yang melebihi shortfall siklus dan histori dengan timestamp identik lintas halaman cursor. Tool benchmark menggunakan PostgreSQL, membuat schema sementara sendiri, lalu menghapus schema tersebut setelah pengukuran. Benchmark dapat dijalankan manual melalui `.github/workflows/projection-benchmark.yml`; hasilnya melaporkan waktu proyeksi, SELECT count, dan alokasi memori untuk dataset sintetis. Hasil benchmark diunggah sebagai artifact selama 30 hari.

Verifikasi lokal lengkap dengan Docker berjalan: 385 test unit/service, 2 test integrasi (HTTP/API dan PostgreSQL/Npgsql), 72 test frontend (3 skipped), lint, build, dan 1 Playwright browser E2E lulus. Benchmark lokal pada 100 set-aside dan 50.000 ledger entry mencatat mean 91.8 ms, 11 SELECT, serta 4.14 MiB allocated. Angka ini dicatat sebagai baseline pengamatan dan belum menjadi batas gagal CI.

Untuk benchmark lokal, set `LIFEOS_TEST_POSTGRES_CONNECTION` ke database PostgreSQL khusus benchmark, lalu jalankan:

```sh
dotnet run --project backend/FinanceProjectionBenchmark/FinanceProjectionBenchmark.csproj --configuration Release -- --set-asides 100 --entries 50000 --iterations 5
```

## Tahapan

### Tahap 1 — Satu alur test lokal

**Hasil yang diharapkan:** pemeriksaan inti dapat dijalankan dengan satu perintah dan hasilnya mudah dipahami.

- Tambahkan skrip `test` frontend yang menjalankan Vitest dalam mode non-interaktif/sekali jalan.
- Sediakan satu perintah dari root untuk menjalankan backend tests, frontend tests, lint, dan build.
- Pisahkan test cepat dari test integrasi yang memerlukan PostgreSQL agar iterasi lokal tetap cepat.
- Tandai test live/smoke yang memerlukan server atau konfigurasi eksternal, sehingga tidak membuat test biasa bergantung pada layanan yang sedang berjalan.
- Pastikan kegagalan di salah satu langkah menghasilkan exit code gagal.

**Kriteria selesai:** seluruh pemeriksaan inti dapat dijalankan berulang secara lokal tanpa langkah manual tersembunyi; hasil gagal menunjukkan komponen yang bermasalah.

### Tahap 2 — Lengkapi regresi untuk aturan berisiko tinggi

Tambahkan atau lengkapi test pada gap yang telah dicatat di review:

- **Ledger dan alokasi:** pemasukan yang dialokasikan langsung ke pos dengan shortfall parsial/penuh, pemasukan melebihi shortfall, beberapa pemasukan, rollover, dan reversal setelah pergantian siklus.
- **Histori:** urutan deterministik dan saldo histori untuk timestamp identik, termasuk batas pagination/cursor.
- **Transaksi dan siklus:** konsistensi saldo dan status setelah spend, close, reversal, serta aksi ganda atau kegagalan mutasi.
- **Scope isolation:** pastikan owner dan guest tidak dapat membaca atau mengubah data scope lain.
- **API:** validasi request pada batas HTTP, enum atau nilai invalid, status code, dan bentuk respons.
- **Frontend:** alur submit/mutation penting, respons error atau timeout, pilihan rekening, histori lintas halaman, serta aksesibilitas keyboard untuk kontrol utama.

Untuk skenario finansial, test sebaiknya memeriksa keadaan akhir ledger dan proyeksi—bukan hanya memastikan metode selesai tanpa exception.

**Kriteria selesai:** gap prioritas tinggi dalam review memiliki test regresi; setiap bug finansial yang diperbaiki punya test yang gagal sebelum perbaikan dan lulus sesudahnya.

### Tahap 3 — Test integrasi PostgreSQL/Npgsql

**Hasil yang diharapkan:** perilaku yang bergantung pada database produksi diuji menggunakan provider produksi.

- Jalankan PostgreSQL khusus test, misalnya melalui service container pada CI atau Docker lokal.
- Pastikan database test terisolasi, dapat dibuat ulang, dan tidak pernah menunjuk ke database pengguna/produksi.
- Jalankan migrasi dari skema kosong sebagai bagian dari verifikasi integrasi.
- Uji transaksi SERIALIZABLE, konflik dengan SQLSTATE serialization failure, kebijakan retry, serta penggunaan ulang DbContext/entity tracker setelah konflik.
- Uji operasi finansial konkuren yang harus menjaga invariant saldo dan tidak menggandakan mutasi.
- Pertahankan SQLite in-memory untuk mayoritas test service cepat; gunakan suite PostgreSQL untuk kontrak provider dan integrasi.

**Kriteria selesai:** jalur migrasi dan skenario concurrency prioritas lulus terhadap versi PostgreSQL yang dipakai aplikasi; pengujian tidak bergantung pada database eksternal yang dipakai bersama.

### Tahap 4 — CI wajib pada perubahan kode

Tambahkan workflow GitHub Actions (atau CI yang digunakan proyek) untuk menjalankan:

1. Restore/build dan backend unit/service tests.
2. Test integrasi PostgreSQL.
3. Instalasi dependency frontend, Vitest, lint, dan build.
4. Pelaporan hasil yang memudahkan melihat suite dan test yang gagal.

Workflow berjalan pada pull request dan perubahan ke branch utama. Agar benar-benar wajib sebelum merge, aktifkan branch protection/ruleset GitHub untuk branch `main` dan jadikan check `Automated tests / test` required. Workflow tidak dapat mengaktifkan setting repository tersebut sendiri. Cache dependency boleh digunakan, tetapi tidak boleh mengorbankan reproducibility.

**Kriteria selesai:** pull request mendapat status otomatis; kegagalan test/build/lint mencegah status pemeriksaan menjadi lulus.

### Tahap 5 — Dataset sintetis dan benchmark performa

Tahap ini dilakukan setelah test fungsional dan integrasi inti stabil.

- Sediakan generator seed sintetis untuk jumlah pos dan entry histori yang dapat dikendalikan.
- Gunakan fixture kecil untuk test fungsional dan dataset lebih besar hanya untuk benchmark/query regression.
- Ukur durasi, jumlah query, dan penggunaan memori pada proyeksi finansial/histori.
- Jalankan benchmark sebagai job terpisah atau terjadwal pada awalnya, bukan pada setiap test lokal.
- Tentukan ambang performa setelah baseline diukur; jangan menetapkan batas arbitrer sebelum ada data pembanding.

**Kriteria selesai:** benchmark dapat direproduksi dan memberikan baseline yang berguna untuk mengidentifikasi regresi performa.

## Bentuk pemeriksaan akhir

Setelah tahapan di atas diterapkan, pemeriksaan normal untuk perubahan kode idealnya mencakup:

- Backend unit/service tests.
- Backend PostgreSQL integration tests.
- Frontend Vitest.
- Frontend lint dan production build.
- Benchmark PostgreSQL terpisah sesuai jadwal atau ketika perubahan menyentuh query/proyeksi data besar.

Pemeriksaan tersebut mengotomatiskan regresi yang dapat diprediksi. Review manusia tetap berguna untuk keputusan UX/produk dan hal-hal yang belum dinyatakan sebagai aturan eksplisit.

## Urutan Prioritas

1. Satukan perintah test lokal dan tambahkan skrip test frontend.
2. Lengkapi regresi finansial/API yang berisiko tinggi.
3. Tambahkan test integrasi PostgreSQL/Npgsql.
4. Pasang CI dan jadikan pemeriksaan utama sebagai required check.
5. Tambahkan generator dataset sintetis dan benchmark performa.

Urutan ini memberi manfaat awal dengan cepat tanpa menunggu dataset besar atau benchmark tersedia.
