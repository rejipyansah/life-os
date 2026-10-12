# Review Kualitas Kode — Clean Code

**Tanggal review:** 11 Oktober 2026  
**Cakupan:** inspeksi statis struktur backend, frontend, endpoint, service, test, dan script proyek.  
**Status:** rekomendasi review; tindak lanjut implementasi belum dilakukan sebagai bagian dari review ini.

## Ringkasan

Struktur proyek sudah memisahkan frontend React dan API ASP.NET Core, memiliki service domain, serta test unit dan integrasi. Area utama untuk meningkatkan maintainability adalah memecah kelas/hook besar dan mengurangi pengulangan alur yang sama. Temuan ini merupakan risiko maintainability, bukan klaim adanya bug runtime.

## Temuan

### 1. [Tinggi] `SetAsideService` menggabungkan banyak tanggung jawab

**Lokasi:** `backend/LifeOS.Api/Services/SetAsideService.cs:33–1161`

Satu service menangani lifecycle pos, operasi uang, normalisasi siklus, penghitungan alokasi, riwayat, serta pembentukan proyeksi. Bagian `ProjectForScopeAsync` juga memuat pengambilan data dan kalkulasi dalam metode panjang.

**Dampak:** perubahan aturan satu alur lebih sulit ditinjau secara terisolasi dan dapat meningkatkan risiko regresi pada alur lain.

**Saran:** pisahkan secara bertahap tanggung jawab command/mutasi, query/proyeksi, dan kalkulasi siklus. Pertahankan aturan domain di satu tempat yang jelas dan tambahkan test regresi pada batas perilaku yang dipisah.

**Status tindak lanjut:** query/list/detail/history dan pembentukan projection dipindahkan ke `SetAsideProjectionService`; `SetAsideService` tetap menjadi façade command yang mendelegasikan query untuk hasil mutasi. Test siklus, spend, dan reversal tetap lulus.

### 2. [Tinggi] Entry point aplikasi terlalu panjang dan mencampur beberapa lapisan

**Lokasi:** `backend/LifeOS.Api/Program.cs:26–944`

File ini memuat konfigurasi hosting, Identity/cookie/CORS, endpoint auth dan guest, interpretasi, serta command CLI untuk provisioning dan reset. Konfigurasi Identity juga diulang pada beberapa jalur CLI.

**Saran:** ekstrak registrasi layanan, endpoint auth/guest, dan command administratif ke modul/file terpisah. Gunakan satu konfigurasi Identity bersama untuk host aplikasi dan command CLI.

**Status tindak lanjut:** auth, guest session, interpret routes dipetakan oleh endpoint extensions; owner CLI dipindahkan ke `OwnerAdministrationCommands` dengan konfigurasi Identity/database bersama. Entry point kini berfokus pada composition root.

### 3. [Tinggi] Hook finance frontend memiliki banyak tanggung jawab

**Lokasi:** `frontend/LifeOS.Web/src/finance/useFinanceState.ts:149–782`

Hook memuat proyeksi, state pagination/filter, toast, serta aksi akun, transaksi, pos, dan agenda. Pola request, refresh, dan notifikasi diulang pada banyak callback.

**Saran:** kelompokkan aksi berdasarkan domain dan pisahkan pengelolaan read state/notifikasi bila pemisahan membuat dependensi lebih jelas. Ekstrak helper mutation bersama untuk pola sukses/gagal/refresh yang benar-benar seragam.

**Status tindak lanjut:** callback mutation dikelompokkan dalam factory domain di `financeActions.ts`; `useFinanceState` kini memegang read model, filter/pagination, toast, dan komposisi action.

### 4. [Sedang] Endpoint Finance mengulang pola penanganan yang sama

**Lokasi:** `backend/LifeOS.Api/Services/FinanceEndpoints.cs`

Route berulang kali menyelesaikan scope, memeriksa body, memanggil service, dan memetakan exception menjadi response.

**Saran:** buat helper untuk bagian yang identik dan gunakan pola error yang konsisten. Hindari abstraksi generik yang menyamarkan perbedaan validasi antar route.

**Status tindak lanjut:** resolusi Owner/Guest scope sekarang dijalankan satu kali oleh endpoint filter route group; handler menggunakan scope yang sudah terselesaikan. Error response memakai factory bersama, sementara validasi body dan perbedaan status not-found/validation tetap dekat route agar semantik domain tidak berubah.

**Status tindak lanjut:** scope kini di-resolve satu kali oleh endpoint filter pada route group Finance. Penanganan body dan validasi domain tetap berada dekat route karena tipe command dan status yang dihasilkan berbeda-beda; boilerplate ini masih dapat dirapikan lebih lanjut bila ada pola yang terbukti sama.

### 5. [Sedang] Kegagalan memuat proyeksi finance tidak disampaikan ke UI

**Lokasi:** `frontend/LifeOS.Web/src/finance/useFinanceState.ts:165–177`

`catch` mengabaikan error dan mempertahankan proyeksi terakhir. Pada kondisi awal, UI juga tidak membedakan data kosong dari kegagalan request.

**Saran:** representasikan status loading/error secara eksplisit agar pengguna dapat memahami dan mencoba kembali ketika pemuatan gagal.

### 6. [Rendah] Lint frontend dikonfigurasi dengan aturan yang terbatas

**Lokasi:** `frontend/LifeOS.Web/.oxlintrc.json:1–8`

Konfigurasi saat ini hanya menetapkan beberapa aturan React. Lint yang lulus karena itu tidak mewakili pemeriksaan gaya dan pola kode yang luas.

**Saran:** tambah aturan secara bertahap sesuai konvensi project; jaga agar signal/noise tetap baik dan jalankan lint di CI.

## Kekuatan yang sudah ada

- Service domain memisahkan banyak aturan backend dari definisi endpoint.
- Test unit, integrasi, frontend, dan E2E sudah memiliki tempat yang jelas.
- Banyak aturan domain dan keputusan desain dijelaskan melalui komentar serta dokumentasi.
- API frontend sudah memiliki fungsi transport bersama untuk sebagian besar endpoint.

## Urutan tindak lanjut yang disarankan

1. Rancang pemisahan `SetAsideService` berdasarkan command, query/proyeksi, dan siklus.
2. Ekstrak konfigurasi dan endpoint dari `Program.cs`, termasuk menghilangkan konfigurasi Identity berulang.
3. Pecah `useFinanceState` berdasarkan tanggung jawab dan kelompok aksi.
4. Kurangi boilerplate endpoint setelah pola error dan validasi disepakati.
5. Tambahkan status error pemuatan data pada frontend dan perluas lint secara bertahap.

## Verifikasi

Review ini berdasarkan pembacaan kode dan struktur repository. Test, build, dan lint tidak dijalankan sebagai bagian dari inspeksi ini. Jalankan `npm test` setelah perubahan implementasi terkait.
