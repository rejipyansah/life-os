# Review Kualitas Kode — Security, Konsistensi, dan Performa

**Tanggal review:** 11 Oktober 2026  
**Cakupan:** inspeksi statis jalur autentikasi dan guest, API Finance dan interpretasi AI, akses data, integrasi provider, serta transport frontend.  
**Status:** rekomendasi review; temuan perlu dikonfirmasi terhadap konfigurasi deployment dan perilaku runtime sebelum dinyatakan sebagai kerentanan atau bottleneck terukur.

## Ringkasan

Backend sudah memiliki beberapa kontrol penting: token guest dibuat secara kriptografis dan disimpan sebagai hash, scope ditentukan di server, dan operasi finansial tertentu dijalankan secara transaksional. Pembatasan interpretasi AI, pemeriksaan origin mutasi, lifecycle guest 30+30 hari, error envelope tersanitasi, konfigurasi provider, dan agregasi projection sudah ditangani dalam tindak lanjut review. Verifikasi runtime pada deployment nyata dan pengelolaan HttpClient masih menjadi tindak lanjut.

## Security dan privasi

### 1. [Tinggi] Endpoint interpretasi belum tampak memiliki rate limit atau batas panjang input

**Lokasi:** `backend/LifeOS.Api/Program.cs:420–528`; `backend/LifeOS.Api/Services/InterpretEndpoint.cs:38–80`

Request yang lolos validasi memanggil provider AI eksternal. Pada jalur yang ditinjau tidak terlihat rate limiting ataupun batas panjang khusus untuk `Input`.

**Risiko:** penyalahgunaan dapat meningkatkan biaya provider dan beban API; input besar juga menambah biaya pemrosesan.

**Saran:** tetapkan batas panjang input, rate limit/kuota per scope atau sumber request, timeout, dan batas concurrency. Pastikan respons batas terlampaui konsisten dan tidak membocorkan informasi internal.

### 2. [Tinggi] Input pengguna dan nama akun dikirim ke layanan AI pihak ketiga

**Lokasi:** `backend/LifeOS.Api/Services/InterpretEndpoint.cs:45–73`; prompt di `backend/LifeOS.Api/Services/InterpreterPrompts.cs`

Request provider mencakup teks input dan daftar nama akun aktif. Teks bebas dapat memuat informasi pribadi atau finansial meskipun fitur ini hanya dimaksudkan untuk interpretasi transaksi.

**Risiko:** data yang dikirim keluar dari batas aplikasi bergantung pada provider dan konfigurasi retensinya.

**Saran:** minimalkan data prompt, dokumentasikan data yang dikirim dan provider yang digunakan, serta tetapkan kebijakan privasi/retensi yang sesuai. Hindari mengirim identifier atau informasi yang tidak dibutuhkan model.

### 3. [Sedang] Masa berlaku guest session belum tampak ditegakkan di server — ditangani setelah review

**Lokasi:** `backend/LifeOS.Api/Services/GuestTokenService.cs:47–75, 107–137`; `backend/LifeOS.Api/Models/GuestSession.cs:20–22`

Cookie berlaku satu tahun dan model menyimpan `LastActivityAt`. Pada jalur resolve yang ditinjau, timestamp aktivitas tidak digunakan untuk menolak sesi kedaluwarsa; mekanisme cleanup sesi lama juga belum ditemukan pada cakupan yang diperiksa.

**Risiko:** sesi/data guest dapat bertahan lebih lama dari kebijakan retensi yang diharapkan, dan record guest dapat terus bertambah.

**Saran:** definisikan expiry server-side berdasarkan aktivitas/umur, tegakkan saat resolve, dan buat mekanisme cleanup atau retensi data guest yang disepakati.

**Status tindak lanjut:** diterapkan dengan expiry setelah 30 hari tidak aktif, retensi 30 hari tambahan, update aktivitas ter-throttle, dan worker cleanup harian. Frontend juga menampilkan informasi ketika sandbox lama tidak tersedia. Test lifecycle dan cleanup ditambahkan; verifikasi PostgreSQL migration tetap perlu dijalankan pada database test.

### 4. [Sedang] Cookie lintas origin perlu ditinjau bersama perlindungan CSRF

**Lokasi:** `backend/LifeOS.Api/Program.cs:47–97`; `backend/LifeOS.Api/Services/GuestTokenService.cs:47–75`

Cookie Identity production menggunakan `SameSite=None`; cookie guest juga menggunakan `SameSite=None` pada HTTPS. CORS membatasi origin yang diizinkan, tetapi CORS sendiri bukan mekanisme perlindungan CSRF.

**Catatan:** endpoint JSON dan preflight dapat membatasi sebagian request lintas situs, tetapi inspeksi statis ini tidak membuktikan semua jalur mutasi terlindungi. Konfigurasi reverse proxy dan origin produksi juga memengaruhi perilaku browser.

**Saran:** petakan endpoint mutasi berbasis cookie dan verifikasi proteksi CSRF end-to-end. Pertimbangkan token anti-CSRF atau kontrol `Origin`/`Referer` yang sesuai arsitektur, dan uji dengan origin yang tidak dipercaya.

## Konsistensi

### 5. [Sedang] Konfigurasi provider Groq tidak dipakai pada request — ditangani

**Lokasi:** `backend/LifeOS.Api/Services/GroqInterpreter.cs:23–38, 73–96`

Awalnya constructor membaca model, reasoning effort, dan endpoint dari konfigurasi, tetapi request menggunakan nilai konstan.

**Dampak:** operator dapat mengubah konfigurasi tanpa mengubah perilaku aktual; field konfigurasi juga menjadi misleading.

**Saran:** teruskan nilai konfigurasi ke request builder atau hapus pengaturan yang tidak didukung. Tambahkan test untuk memastikan konfigurasi provider digunakan.

**Status tindak lanjut:** model, reasoning effort, dan endpoint Groq sekarang memakai nilai konfigurasi; test memastikan nilai tersebut dikirim sesuai konfigurasi.

### 6. [Sedang] Penanganan respons API frontend tidak seragam

**Lokasi:** `frontend/LifeOS.Web/src/api.ts:30–73, 83–132`

Sebagian request memakai helper `request`, sementara fungsi autentikasi melakukan fetch dan parsing sendiri. Format error, parsing body, dan perlakuan terhadap status tertentu tersebar di beberapa pola.

**Saran:** tetapkan satu kebijakan transport dan error response; pertahankan pengecualian hanya untuk kebutuhan endpoint yang jelas.

**Status tindak lanjut:** `api.ts` kini menghasilkan `ApiRequestError` bertipe dengan status, code, trace ID, dan pesan aman; guest expiry tetap memiliki tipe/event khusus. Backend mengembalikan envelope aditif dengan pesan Indonesia serta generic 500 yang tidak membocorkan exception.

### 7. [Rendah] Tipe response frontend tidak memvalidasi JSON saat runtime

**Lokasi:** `frontend/LifeOS.Web/src/api.ts:48–60`

Generic `request<T>` melakukan cast body ke `T`, bukan validasi bentuk payload. Perubahan response backend dapat lolos compile dan memunculkan masalah baru saat UI mengakses field.

**Saran:** pertimbangkan schema runtime untuk read model utama atau contract test yang mengikat response backend dan frontend.

## Performa dan skala

### 8. [Sedang] Query read model finance memuat koleksi yang tumbuh bersama data — sudah diukur

**Lokasi:** `backend/LifeOS.Api/Services/FinanceStateService.cs:48–116`; `backend/LifeOS.Api/Services/SetAsideService.cs:834–1033`; `backend/LifeOS.Api/Services/TransactionService.cs:523–579`

Finance state mengambil account dan upcoming event scope, lalu memuat proyeksi set-aside dan transaksi terbaru. Proyeksi set-aside memuat spent entries untuk selected set-asides guna menghitung pemakaian. Endpoint daftar transaksi tanpa `limit` dapat mengembalikan seluruh transaksi.

**Risiko:** latency dan penggunaan memori meningkat seiring volume akun, agenda, pos, dan ledger.

**Saran:** ukur dengan dataset representatif. Pertimbangkan pagination pada endpoint riwayat/daftar, query agregasi database untuk data yang tidak perlu dirender satu per satu, serta indeks komposit berdasarkan pola filter dan sort yang terbukti dominan.

**Hasil dan tindak lanjut benchmark:** pada dataset sama (500 set-aside, 250.000 entry, 50.000 transaksi dasar + 2.500 reversal, 10.000 agenda, 60 akun), agregasi spend dipindahkan ke database-side dan indeks usage/reversal disesuaikan. Baseline algoritme lama 10 iterasi: proyeksi set-aside 710,3 ms/10 `SELECT`/53,23 MiB dan Finance penuh 563,4 ms/18 `SELECT`/62,47 MiB. Sesudah optimasi: 440,8 ms/8 `SELECT`/14,55 MiB dan 451,4 ms/16 `SELECT`/24,11 MiB. Pada run tersebut waktu turun sekitar 38%/20% dan alokasi 73%/61%. Ini observasi PostgreSQL disposable lokal, bukan SLA; evaluasi workload produksi representatif tetap diperlukan.

### 9. [Sedang] Proyeksi set-aside mengolah entry dan siklus berulang di memori — ditangani sebagian

**Lokasi saat review:** `backend/LifeOS.Api/Services/SetAsideService.cs:881–997`; query/projection kini berada di `SetAsideProjectionService.cs`.

Entry terpakai dikelompokkan dan dihitung per set-aside, lalu funding rollover disimulasikan dalam loop. Ini memberi kontrol logika yang eksplisit, tetapi biayanya bergantung pada jumlah entry serta pos aktif.

**Saran:** pertahankan hasil sebagai baseline yang benar; gunakan benchmark/profiling terlebih dahulu, kemudian optimalkan hanya bagian yang terbukti dominan. Pastikan hasil tetap konsisten dengan test aturan cycle.

**Status tindak lanjut:** agregasi Spend kini difilter dan dihitung database-side; test mencakup jendela siklus dan reversal. Loop alokasi rollover masih diproses di memory karena mempertahankan urutan klaim domain.

### 10. [Rendah] Groq menggunakan `HttpClient` yang dibuat langsung oleh service

**Lokasi:** `backend/LifeOS.Api/Services/GroqInterpreter.cs:23–38`

Service membuat `HttpClient` sendiri dan menyimpannya di instance. Ini menghindari pola pengelolaan client melalui `IHttpClientFactory` dan menyulitkan konfigurasi/observability transport terpusat. Dampaknya perlu dinilai berdasarkan lifetime service dan volume request aktual.

**Saran:** gunakan typed/named client dari `IHttpClientFactory` agar lifetime handler, timeout, dan kebijakan transport dikelola konsisten.

### 11. [Rendah] Resume guest menulis `LastActivityAt` ke database — ditangani

**Lokasi saat review:** `backend/LifeOS.Api/Services/GuestTokenService.cs`; resolusi session sekarang diterapkan oleh guest endpoints dan Finance endpoints.

Sebelumnya activity hanya diperbarui pada resume; pola write perlu seimbang dengan pemakaian timestamp untuk expiry/retensi.

**Status tindak lanjut:** pencatatan kini berlaku untuk request guest valid dan di-throttle maksimal satu write per 15 menit per sesi.

**Saran:** setelah kebijakan expiry ditetapkan, ukur frekuensi dan dampaknya. Bila perlu, batasi update berdasarkan interval atau lakukan update hanya ketika melewati ambang waktu tertentu.

## Hal yang sudah baik

- Token guest dibuat menggunakan cryptographic random dan yang disimpan adalah hash token.
- Endpoint Finance menyelesaikan Owner/Guest scope di server, bukan menerima scope sebagai otoritas dari client.
- Cookie Identity ditandai `HttpOnly`; cookie produksi mengikuti HTTPS.
- Error provider AI dipetakan ke pesan umum pada endpoint interpretasi, bukan dikembalikan sebagai detail internal kepada pengguna.
- Sebagian operasi mutasi ledger/set-aside memakai serializable transaction untuk menghadapi concurrent request.

## Urutan tindak lanjut yang disarankan

1. Verifikasi error trace dan latency pada deployment production.
2. Migrasikan `HttpClient` Groq ke `IHttpClientFactory`.
3. Evaluasi paginasi agenda hanya bila workload production-representative menunjukkan agenda sebagai bottleneck.

## Verifikasi dan batas review

Review awal dilakukan secara statis. Tindak lanjut diverifikasi dengan unit/service tests, HTTP dan PostgreSQL integration tests, frontend tests, lint/build, mobile browser simulation, serta benchmark lokal. Ini bukan penetration test atau audit konfigurasi provider/deployment; performa produksi tetap perlu diukur pada workload dan infrastruktur sebenarnya.
