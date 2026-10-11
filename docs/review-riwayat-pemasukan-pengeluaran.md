# Review Pemasukan/Pengeluaran — daftar, pencatatan, dan riwayat

Dokumen ini mencatat aturan UX serta perilaku daftar rencana dan transaksi riil untuk pemasukan/pengeluaran. Aturan saldo dan ledger tetap mengacu ke [`finance.md`](finance.md); dokumen ini berfokus pada pengalaman pengguna, edge case, dan status implementasi.

## Perilaku dan keputusan UX

- Agenda adalah rencana; agenda baru memengaruhi transaksi/saldo setelah direalisasikan.
- Kartu agenda menyebut arah transaksi secara eksplisit, menggunakan ikon/aksen yang berbeda, dan tetap menampilkan nominal bertanda. Warna bukan satu-satunya pembeda.
- Pengeluaran memakai tanggal dan kategori; pemasukan memakai kategori tetap Pendapatan dan dapat fleksibel tanpa tanggal.
- Sumber Dana Default merupakan saran. Pengguna tetap memilih sumber dana aktual saat merealisasikan agenda.
- Riwayat lengkap mengambil daftar transaksi dari `GET /api/finance/transactions`; read model `/api/finance/state` hanya menyediakan maksimal 20 transaksi terbaru untuk aktivitas ringkas.
- Riwayat mengurutkan transaksi terbaru lebih dahulu, menyediakan pencarian, filter arah, dan filter sumber dana. Pembatalan ditampilkan sebagai reversal agar jejak audit tetap ada.
- Transfer tetap berlabel Transfer dan tidak masuk filter pemasukan/pengeluaran walaupun mutasi rekening penerima bertanda positif.
- Filter periode aktivitas: Hari Ini mengikuti tanggal bisnis Jakarta; Minggu Ini berarti tujuh hari kalender terakhir termasuk hari ini; Bulan Ini berarti bulan kalender berjalan.

## Edge case yang harus dipertahankan

- Filter harus tetap benar di pergantian hari, minggu, bulan, tahun, dan zona waktu; tanggal tanpa waktu dibandingkan sebagai tanggal kalender.
- Riwayat kosong, gagal dimuat, atau gagal pencarian harus memiliki pesan yang berbeda dan tidak mengesankan hasil parsial sebagai riwayat lengkap.
- Riwayat dari akun yang sudah diarsipkan tetap dapat dicari dan ditampilkan.
- Pemasukan, pengeluaran, pembatalan/reversal, dan transfer tidak boleh tertukar arah. Reversal tidak ditawarkan untuk dibatalkan lagi.
- Klik simpan berulang dan request gagal tidak boleh menghasilkan transaksi ganda atau menghilangkan draft tanpa pemberitahuan.
- Akun default yang sudah diarsipkan tidak boleh dipakai untuk transaksi baru; UI harus meminta pilihan akun aktif.
- Pada layar sempit, aksi kartu agenda harus membungkus dengan urutan yang tetap jelas dan tombol utama tetap mudah dijangkau.

## Status implementasi

- [x] Kartu agenda menampilkan label Pemasukan/Pengeluaran, ikon arah, aksen visual, tanda nominal, dan tata letak aksi responsif.
- [x] Riwayat lengkap memakai endpoint transaksi seluruhnya, tidak terbatas pada 20 transaksi ringkasan.
- [x] Filter aktivitas Hari Ini, Minggu Ini, dan Bulan Ini menggunakan tanggal ISO kalender.
- [x] Riwayat mempertahankan urutan terbaru yang disediakan API dan label footer menjelaskan urutan.
- [x] Baris transaksi membedakan arah melalui nominal, ikon/aksen, dan label; reversal tetap memiliki status khusus.
- [x] Dokumen API menjelaskan batas 20 item pada Finance state dan endpoint riwayat lengkap.
- [ ] Tambahkan pagination server-side jika ukuran riwayat aktual menimbulkan biaya memori atau waktu muat yang bermakna.
- [ ] Uji visual manual pada beberapa ukuran layar dan kontras tema aktual.

## Batasan yang diketahui

Endpoint riwayat saat ini memuat semua transaksi untuk satu scope dalam satu request. Hal ini memastikan hasil pencarian benar-benar lengkap, tetapi pagination server-side tetap diperlukan bila dataset tumbuh besar. Riwayat dimuat ulang ketika modal dibuka kembali setelah transaksi dibatalkan.
