# Review Mendalam — Catat Transaksi

## Ruang lingkup

Review mencakup UI `CatatTransaksiSection`, parser transaksi natural-language, alur penyimpanan frontend, layanan ledger backend yang digunakan alur tersebut, serta test terkait. Implementasi tindak lanjut dan verifikasi test/build/lint dicatat pada bagian pembaruan.

## Ringkasan

Modul sudah memiliki alur dasar yang baik: sumber dana dipilih, hasil parsing ditampilkan untuk dikonfirmasi, proses simpan mencegah submit ganda, dan kegagalan yang dilaporkan sebagai `false` memungkinkan pengguna mencoba lagi. Backend juga memvalidasi scope, rekening aktif, saldo pengeluaran, dan menjalankan penulisan ledger secara transaksional.

Namun, sebagai fitur utama, pengalaman Catat Transaksi saat ini terasa lebih menuntut daripada Top Up/Tarik. Top Up/Tarik memberi kontrol terstruktur yang langsung terlihat—nominal cepat, saldo/batas yang relevan, dan tindakan yang jelas. Catat Transaksi meminta pengguna menyusun kalimat yang bisa dipahami parser, lalu mengoreksi hasil yang keliru dengan cara mengulang input. Rekomendasi arah produk adalah mempertahankan ketik bebas sebagai jalur cepat, sambil menambahkan koreksi langsung dan jalur terstruktur yang ringkas.

## Pembaruan implementasi

- Pemilihan POS manual di Catat Transaksi sudah dihapus. Catatan “sisihkan” diarahkan ke modul Dana yang Disisihkan; transaksi biasa tidak mengirim `SetAsideId`.
- Perilaku backend yang otomatis memakai Uang Bebas untuk menutup kekurangan pendanaan POS siklus dari pemasukan baru tetap dipertahankan. Toast pemasukan menjelaskan kemungkinan dampak tersebut.
- Preview sekarang memakai modal responsif (dialog desktop/bottom sheet mobile), bukan konten inline yang mengubah tinggi halaman. Detail edit dibuka lewat tombol **Edit transaksi**.
- Mode edit menggantikan ringkasan, sehingga informasi transaksi tidak tampil ganda. **Batal edit** membuang perubahan sementara; **Simpan Transaksi** dari mode edit langsung mencatat nilai yang sedang diedit tanpa langkah penerapan terpisah.
- Nominal pada input cepat dan mode edit menggunakan pemisah ribuan titik agar lebih mudah dibaca.
- Validasi nominal menolak nilai di bawah Rp1 dan di atas integer aman JavaScript (`9.007.199.254.740.991`). Teks transaksi/catatan dibatasi 512 karakter dan kategori 128 karakter; batas nominal dan panjang deskripsi/kategori juga ditegakkan backend.
- Kategori bersifat kondisional: pemasukan selalu memakai **Pendapatan** dan tidak dapat diubah; daftar kategori pengeluaran tidak memuat **Pendapatan**.
- Select Sumber Dana saat mode edit menampilkan saldo aktual tiap rekening pada opsi, agar pengguna dapat membandingkan nominal transaksi dengan saldo sumber yang akan dipakai. Saldo rekening tidak dicampuradukkan dengan Uang Bebas scope-wide.
- Validasi nominal menolak nilai di bawah Rp1 dan di atas integer aman JavaScript (`9.007.199.254.740.991`). Teks transaksi/catatan dibatasi 512 karakter dan kategori 128 karakter; batas nominal serta panjang deskripsi/kategori juga ditegakkan backend.
- Tombol Batal menutup dialog dan mengembalikan fokus ke kolom input.
- Test frontend, lint, build, serta test backend `IncomeAutomaticallyFundsCycleShortfallsInCreationOrder` sudah dijalankan setelah perubahan.
- Test tambahan memverifikasi batas atas nominal dan panjang deskripsi/kategori pada service backend.

## Temuan

### 1. [Tinggi] Hasil konfirmasi tidak disegarkan saat teks input berubah

**Lokasi:** `frontend/LifeOS.Web/src/components/finance/CatatTransaksiSection.tsx:72–76`

`handleInputChange` hanya memperbarui teks input, pesan panduan, dan pesan sukses. State `parsed` tidak dibersihkan atau dihitung ulang. Akibatnya, pengguna dapat memparse satu transaksi, mengubah teks input, lalu tetap melihat kartu konfirmasi lama. Jika tombol **Simpan Transaksi** ditekan, data yang disimpan adalah hasil parse lama, bukan teks yang sedang tampil.

**Dampak:** transaksi dapat tercatat dengan nominal, kategori, atau jenis yang berbeda dari catatan yang terlihat saat konfirmasi.

**Rekomendasi:** batalkan hasil konfirmasi setiap kali teks berubah, atau parse ulang dan minta konfirmasi atas hasil terbaru. Pastikan teks yang dikonfirmasi selalu sama dengan teks yang akan disimpan.

### 2. [Tinggi] Interpretasi nominal tanpa satuan dapat menghasilkan nominal yang keliru

**Lokasi:** `frontend/LifeOS.Web/src/finance/parseTransaction.ts:27–42`

Untuk angka tanpa akhiran `rb`, `k`, `ribu`, `jt`, atau `juta`, parser menghapus seluruh titik sebelum mengubahnya menjadi angka. Misalnya, `bayar makan 1.5` menjadi nominal `15`, bukan `1,5`. Pemisah koma dan format angka lainnya juga bisa ambigu. Nilai hasil parsing kemudian dibulatkan dan dapat disimpan sebagai transaksi.

**Dampak:** nominal transaksi bisa berbeda dari maksud pengguna.

**Rekomendasi:** tetapkan dan dokumentasikan aturan format nominal tanpa satuan; jangan menebak ketika input ambigu. Tampilkan pesan klarifikasi/validasi sebelum penyimpanan dan pertimbangkan batas nominal yang wajar.

### 3. [Tinggi] Kategori hasil tebakan sulit diperbaiki dan taksonomi belum jelas

**Lokasi:** `frontend/LifeOS.Web/src/finance/parseTransaction.ts:87–102`

Pembersihan memakai penggantian substring global tanpa batas kata, termasuk kata pendek seperti `di`. Rangkaian karakter tersebut dapat terhapus dari dalam kata lain. Selain itu, hasil teks bersih selalu dapat menimpa kategori yang sudah ditentukan dari kata kunci; misalnya kategori `Wifi Bulanan` bisa berubah menjadi `Wifi`. Strategi kategori saat ini juga mencampur kategori transaksi dan deskripsi spesifik, sehingga sulit menentukan daftar konstanta yang konsisten.

**Dampak:** kategori/deskripsi bisa terpotong atau tidak konsisten. Jika tebakan kategori salah, pengguna harus mengubah kalimat dan mencoba menebak kata kunci yang dikenali parser; parser dapat memberi hasil salah yang sama lagi. Ini membuat fitur utama terasa kurang menarik dan lebih lambat daripada alur terstruktur lain.

**Rekomendasi:** pisahkan tiga hal: kategori inti, deskripsi transaksi, dan kata kunci/alias parser.

- Mulai dengan taksonomi inti yang kecil dan stabil; jangan jadikan setiap jenis belanja atau merchant sebagai kategori terpisah. Contoh awal: Makan & Minum, Transportasi, Tagihan & Utilitas, Belanja, Kesehatan, Hiburan, Pendapatan, dan Lainnya. Ini titik awal untuk diuji, bukan daftar final yang harus dikunci.
- Gunakan pemetaan kata/frasa umum ke kategori inti. Tambahkan alias dari pola penggunaan nyata secara bertahap; teks yang belum dikenal tetap dapat memakai kategori umum.
- Tampilkan kategori sebagai kontrol yang bisa diedit langsung di kartu konfirmasi. Pengguna harus bisa mengganti kategori tanpa mengulang kalimat. Sediakan pula cara mengubah jenis transaksi dan nominal bila tebakan terkait salah.
- Tokenisasi dan hapus kata/frasa secara terarah, bukan substring sembarang. Simpan teks bersih sebagai deskripsi dan jangan biarkan deskripsi menimpa kategori yang sudah berhasil dipetakan.

### 4. [Sedang] Klasifikasi jenis dan deteksi rekening memakai pencocokan substring

**Lokasi:** `frontend/LifeOS.Web/src/finance/parseTransaction.ts:12–17, 54–78`

Deteksi jenis transaksi dan rekening memakai `includes`. Kata kunci pemasukan diperiksa sebelum alokasi dan transfer, sehingga teks dengan beberapa kata kunci dapat diklasifikasikan menurut kata kunci pertama, bukan maksud frasa. Deteksi rekening juga dapat cocok pada bagian teks yang bukan nama rekening.

**Dampak:** jenis transaksi atau sumber dana yang terdeteksi bisa tidak sesuai dengan catatan pengguna.

**Rekomendasi:** gunakan pencocokan kata/frasa yang spesifik dan tentukan prioritas eksplisit untuk frasa bertumpang tindih. Selalu tampilkan hasil deteksi untuk dikonfirmasi, seperti alur UI saat ini.

### 5. [Sedang] Pemilihan sumber dana menambah friksi dan state aktif kurang aksesibel

**Lokasi:** `frontend/LifeOS.Web/src/components/finance/CatatTransaksiSection.tsx:203–214`

Pengguna harus memilih sumber dana pada setiap sesi, sementara nama rekening yang panjang memenuhi ruang pada deretan tombol. Pilihan aktif dibedakan dengan class visual, tetapi tombol tidak mengekspos state tersebut melalui atribut semantik seperti `aria-pressed`. Nama pada tombol juga dapat disingkat, sedangkan teks status aktif menggunakan nama penuh.

**Dampak:** pemilihan berulang menambah langkah pada pencatatan harian; pengguna teknologi asistif juga dapat kesulitan mengetahui sumber dana yang sedang dipilih.

**Rekomendasi:** ingat sumber dana terakhir yang digunakan dan tampilkan sebagai pilihan aktif pada kunjungan berikutnya. Untuk penggunaan pertama tanpa riwayat, pilih default awal yang disepakati produk atau minta pilihan sekali. Pastikan sumber dana aktif tetap jelas dan mudah diganti; ekspos state dengan atribut aksesibilitas seperti `aria-pressed`, serta pastikan nama panjang tetap dapat dikenali.

### 6. [Rendah] Exception dari proses simpan tidak ditampilkan sebagai error lokal

**Lokasi:** `frontend/LifeOS.Web/src/components/finance/CatatTransaksiSection.tsx:136–156`

`handleConfirm` menggunakan `try/finally`, tetapi tidak menangkap exception dari `onSave`. Hook saat ini tampaknya menangani error dan mengembalikan `false`, tetapi komponen bergantung pada kontrak tersebut. Jika implementasi callback melempar exception, kartu tidak memberi pesan error lokal.

**Dampak:** kegagalan simpan yang tidak ditangani oleh callback berpotensi membingungkan pengguna.

**Rekomendasi:** pertimbangkan state error pada komponen atau pastikan kontrak `onSave` secara konsisten menangkap exception dan menampilkan umpan balik.

## Hal yang sudah tertangani dengan baik

- Pengguna perlu memilih sumber dana secara eksplisit; tidak ada fallback diam-diam ke rekening pertama.
- Hasil parsing ditampilkan untuk dikonfirmasi sebelum disimpan.
- Tombol konfirmasi dinonaktifkan saat penyimpanan berlangsung, dengan guard tambahan untuk mencegah submit ganda.
- Bila callback mengembalikan `false`, kartu konfirmasi tetap tersedia agar penyimpanan dapat dicoba lagi.
- Transfer tanpa rekening tujuan diarahkan ke alur Transfer pada modul Sumber Dana.
- Backend memvalidasi scope dan status rekening, memeriksa saldo untuk pengeluaran, dan membungkus pembuatan ledger dalam command transaksional.

## Rekomendasi arah UX

Tujuan desain: membuat pencatatan transaksi harian terasa cepat seperti Top Up/Tarik tanpa menghilangkan input kalimat bebas sebagai pembeda fitur. Revisi UX lanjutan:

- **Input natural tetap tersedia** dengan kategori inti yang ditebak dari kata/frasa umum, sedangkan deskripsi transaksi dipisahkan agar nama merchant/keterangan tidak tercampur dengan kategori.
- **Ringkasan dan editor adalah dua mode yang saling menggantikan.** Koreksi jenis transaksi, kategori, catatan, sumber dana, dan nominal tidak menggandakan informasi; perubahan bisa dibatalkan atau langsung disimpan sebagai transaksi. Edit teks asli membatalkan preview lama agar data lama tidak bisa tersimpan tanpa disadari.
- **Nominal memakai format ribuan lokal** seperti `25.000` di input cepat dan mode edit.
- **Jalur Input cepat** menyediakan jenis transaksi, nominal angka, tombol penambah nominal, dan kategori sebagai alternatif ringkas.
- **Sumber dana terakhir diingat** melalui local storage; saat belum ada preferensi, sumber dana aktif pertama menjadi default. Pilihan tetap tampak dan bisa diganti.
- **Nominal ambigu ditolak dengan panduan**: angka desimal tanpa satuan (seperti `1.5`) tidak diterka, sedangkan akhiran satuan (`1.5jt`) dan pengelompokan ribuan (`1.500`) didukung.
- **POS tidak dipilih manual dari Catat Transaksi.** Modul ini mencatat transaksi riil; alokasi/penarikan POS dikelola dari modul Dana yang Disisihkan.
- **Auto-funding shortfall siklus tetap dipertahankan.** Pemasukan baru tetap dapat mendanai kekurangan pos siklus yang di-reset menggunakan Uang Bebas, sesuai aturan backend yang sudah ada. Ini aturan sistem, bukan pilihan POS yang perlu diisi pengguna pada form.
- **Preview dipindah ke modal/bottom sheet.** Dialog responsif menjaga halaman di belakang tetap stabil saat Batal atau Simpan dan membatasi scroll pada konten dialog.

Kategori inti awal yang digunakan: Makan & Minum, Transportasi, Tagihan & Utilitas, Belanja, Kesehatan, Hiburan, Pendidikan, Pendapatan, dan Lainnya. Daftar ini merupakan baseline yang bisa dikembangkan dari pola penggunaan, bukan konstanta final yang menuntut semua deskripsi cocok sempurna.

Fokus skenario utama adalah pencatatan pengeluaran sehari-hari, sambil tetap mendukung pemasukan dan alur lain yang sudah ada. Kategori dan alias parser sebaiknya dievaluasi berdasarkan catatan nyata; koreksi manual saat ini menjadi fallback langsung bagi pengguna.

## Cakupan test dan area yang perlu ditambahkan

Test yang tersedia mencakup parsing nominal bersatuan/ambigu/di luar batas, pengelompokan ribuan, pemetaan kategori/deskripsi, batas kata untuk deteksi rekening, retry saat simpan gagal, pencegahan submit ganda, pembatalan preview usang, koreksi kategori/nominal, jalur input cepat, kategori kondisional sesuai jenis transaksi, format nominal bertitik, validasi minimum/maksimum dan batas panjang, serta transisi mode tinjau/edit dan pembatalan perubahan edit.

Area yang masih perlu diuji atau dipantau:

- konflik frasa kata kunci jenis transaksi yang saling bertumpang tindih;
- pemulihan pilihan sumber dana terakhir setelah reload dan ketika rekening tersebut diarsipkan;
- perubahan kategori/alias berdasarkan pola transaksi nyata dan umpan balik pengguna.
