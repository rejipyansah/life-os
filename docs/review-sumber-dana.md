# Review Mendalam — Sumber Dana

**Tanggal review:** 11 Oktober 2026  
**Cakupan:** UI dan state frontend, API, service rekening, serta test terkait pada `frontend/LifeOS.Web` dan `backend/LifeOS.Api`.  
**Jenis review:** inspeksi implementasi dan verifikasi perubahan tindak lanjut.

## Ringkasan eksekutif

Modul memiliki fondasi backend yang baik untuk memperlakukan Sumber Dana sebagai lokasi uang: saldo dihitung dari ledger, data dibatasi per scope, nama duplikat divalidasi, dan pengarsipan mempertahankan riwayat serta mensyaratkan saldo nol. Test service juga mencakup beberapa invariants penting, termasuk isolasi scope, penolakan nama duplikat, dan larangan memakai rekening arsip.

Temuan awal terkait input yang tidak dipersistenkan ditangani dengan menghapus field tujuan rekening dan catatan transfer sesuai arahan dokumen/review. Modal kini menunggu hasil penyimpanan; validasi, aksesibilitas, dan tampilan juga diperbaiki. Pemodelan akun kredit tetap tertunda sesuai batas domain Finance v1.

## Temuan

### 1. [Tinggi] Tujuan rekening dapat diisi tetapi tidak tersimpan

**Lokasi:** `frontend/LifeOS.Web/src/components/finance/SumberDanaSection.tsx` (form akun); `frontend/LifeOS.Web/src/finance/useFinanceState.ts` (tambah dan edit); `backend/LifeOS.Api/Services/AccountCommands.cs`.

**Status: ditangani.** Field “Tujuan Sumber Dana” dihapus dari form dan view model karena `Purpose on Account` dicantumkan sebagai fitur tertunda di `docs/finance.md`.

**Dampak yang dihilangkan:** UI tidak lagi memberi kesan bahwa tujuan rekening akan disimpan.

Field ini tetap ditunda bersama dukungan domainnya.

### 2. [Tinggi] Catatan transfer tidak masuk ke transaksi

**Lokasi:** `frontend/LifeOS.Web/src/components/finance/SumberDanaSection.tsx` (form transfer); `frontend/LifeOS.Web/src/finance/useFinanceState.ts` (callback `transfer`).

**Status: ditangani.** Input catatan dihapus dari modal karena bukan data yang ingin dikumpulkan pada alur transfer ini.

**Dampak yang dihilangkan:** pengguna tidak lagi dapat mengisi catatan yang kemudian hilang.

Jika kebutuhan pencatatan muncul, dukung sebagai fitur tersendiri dengan persistensi deskripsi transaksi.

### 3. [Sedang] Modal menutup sebelum hasil mutation diketahui — ditangani

**Lokasi:** `frontend/LifeOS.Web/src/components/finance/SumberDanaSection.tsx` (handler submit akun dan transfer); `frontend/LifeOS.Web/src/finance/useFinanceState.ts` (mutation melalui `run`).

Modal kini menunggu Promise hasil mutation. Saat gagal, modal dan isian tetap terbuka; aksi submit menampilkan status proses dan pesan error. Confirm dialog arsip juga menunggu hasil mutation.

**Dampak yang diatasi:** pengguna dapat memperbaiki atau mengulang aksi tanpa mengisi ulang data setelah gagal.

### 4. [Sedang] Validasi nama UI tidak sejalan dengan aturan backend — sebagian ditangani

**Lokasi:** `frontend/LifeOS.Web/src/components/finance/SumberDanaSection.tsx` (validasi `nameValid`); `backend/LifeOS.Api/Services/AccountService.cs` (validasi create/update dan nama duplikat); `backend/LifeOS.Api/Data/ApplicationDbContext.cs` (konfigurasi indeks akun).

UI dan backend kini sama-sama menerima nama tidak kosong hingga 50 karakter; UI menampilkan batas karakter dan pesan validasi. Backend tetap menolak nama duplikat tanpa membedakan kapitalisasi. Pemeriksaan duplikat database tidak memiliki unique constraint case-insensitive, sehingga race condition request bersamaan masih menjadi tindak lanjut.

**Dampak:** ketidakcocokan panjang nama sudah diatasi. Request bersamaan dengan nama ekuivalen masih berpotensi berlomba melewati pemeriksaan duplikat aplikasi.

**Tindak lanjut:** evaluasi unique index case-insensitive yang kompatibel dengan provider database dan data lama.

### 5. [Sedang] Validasi transfer perlu mempertimbangkan saldo yang berubah dan nominal ekstrem — sebagian ditangani

**Lokasi:** `frontend/LifeOS.Web/src/components/finance/SumberDanaSection.tsx` (perhitungan `exceeds`, `canSubmit`, dan input nominal); `backend/LifeOS.Api/Services/TransactionService.cs` (validasi transaksi transfer).

UI memblokir nominal nol, nominal melebihi saldo yang ditampilkan, akun asal/tujuan yang sama, serta nominal integer di luar rentang aman JavaScript. Backend memvalidasi ulang saldo dan struktur transfer saat request diterima. Error server dilaporkan lewat toast dan pesan di modal.

**Dampak tersisa:** batas rentang frontend lebih konservatif daripada decimal backend; belum ada batas maksimum bisnis eksplisit.

**Tindak lanjut:** sepakati batas nominal maksimum produk dan gunakan aturan yang sama di UI serta backend.

### 6. [Sedang] Aksesibilitas tombol dan pengelolaan fokus modal belum lengkap — ditangani

**Lokasi:** `frontend/LifeOS.Web/src/components/finance/SumberDanaSection.tsx` (tombol aksi akun dan pilihan jenis); `frontend/LifeOS.Web/src/components/finance/shared.tsx` (komponen `Modal`).

Tombol aksi sekarang memiliki label aksesibel, tombol jenis mengekspos `aria-pressed` dalam group berlabel, dan modal memindahkan fokus ke dalamnya, membatasi Tab, lalu mengembalikan fokus setelah ditutup.

**Dampak yang diatasi:** aksi dan state pilihan lebih mudah dikenali melalui keyboard dan teknologi asistif.

### 7. [Rendah] Teks edit mengandung tanda kutip yang tidak berpasangan — ditangani

**Lokasi:** `frontend/LifeOS.Web/src/components/finance/SumberDanaSection.tsx`, subtitle modal edit.

Tanda kutip penutup yang tidak berpasangan telah dihapus.

### 8. [Rendah] Tata letak rekening perlu diuji dengan konten panjang — diperbaiki

**Lokasi:** `frontend/LifeOS.Web/src/components/finance/SumberDanaSection.tsx`, baris kartu rekening.

Pada viewport sempit, informasi identitas kini berada pada baris tersendiri dan saldo/aksi mengambil baris penuh. Nama tetap dipotong untuk mencegah overflow.

Layout responsif ini tetap perlu diverifikasi secara visual pada perangkat sasaran.

### 9. [Keputusan produk] Semantik saldo akun kredit belum cukup jelas

**Lokasi:** `frontend/LifeOS.Web/src/components/finance/SumberDanaSection.tsx` (opsi jenis `credit` dan label saldo); `backend/LifeOS.Api/Services/AccountService.cs` (saldo aktual dari ledger); `frontend/LifeOS.Web/src/finance/apiMapping.ts` (label ikon/jenis).

Modul mendukung jenis `credit`, sedangkan saldo rekening berasal dari penjumlahan entri ledger dan dipakai pada tampilan saldo. Dari cakupan kode yang ditinjau belum jelas apakah akun kredit mewakili limit tersedia, utang berjalan, atau sumber dana biasa. Dampaknya terasa pada saldo negatif, batas transaksi, dan cara menamai angka yang ditampilkan.

**Rekomendasi:** tetapkan semantik akun kredit, termasuk arti saldo, pembayaran kartu, transaksi yang diperbolehkan, serta perilaku saldo negatif. Sesuaikan label UI dan validasi backend dengan keputusan tersebut.

## Kekuatan yang sudah ada

- Saldo aktual rekening dihitung dari ledger; tidak ada saldo tersimpan yang bisa menyimpang dari transaksi.
- API menyelesaikan scope di server dan membatasi operasi rekening ke scope tersebut.
- Backend memvalidasi nama, jenis rekening, dan duplikasi nama.
- Pengarsipan mempertahankan jejak historis dan hanya diizinkan ketika saldo aktual nol.
- Rekening arsip tidak dapat digunakan untuk transaksi baru; test service mencakup beberapa variasi lifecycle rekening.
- Transfer UI menampilkan saldo asal/tujuan, menyediakan aksi “Pindahkan Semua”, dan mencegah nominal nol atau melebihi saldo yang saat itu diketahui UI.

## Urutan tindak lanjut yang disarankan

1. Evaluasi unique index case-insensitive untuk nama akun per scope.
2. Sepakati batas nominal maksimum transfer dan tegakkan konsisten di frontend/backend.
3. Pemodelan akun kredit tetap ditunda sesuai `docs/finance.md`; pertahankan semantik yang ada sampai ada spesifikasi produk.
4. Verifikasi layout secara visual pada perangkat sasaran.

## Status verifikasi

**Verifikasi tindak lanjut:** `npm run build`, `npm test` (101 lulus, 3 skip), `npm run lint`, dan `dotnet test backend/LifeOS.Tests/LifeOS.Tests.csproj --configuration Release` (409 lulus) lulus. Test PostgreSQL memerlukan `LIFEOS_TEST_POSTGRES_CONNECTION`; satu test migrasi gagal dijalankan karena koneksi database tersebut belum dikonfigurasi.
