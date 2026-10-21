# Panduan Proyek & Konvensi Bersama (AGENTS.md)

Dokumen ini adalah **satu-satunya sumber aturan** (`CLAUDE.md` dan `GEMINI.md` hanya merujuk ke sini). Ia adalah **pedoman resmi bersama** bagi semua AI Agent (Claude Code, Antigravity, dll.) yang bekerja pada repositori ini. Semua agen wajib membaca dan mematuhi aturan ini sebelum mengubah kode.

---

## 1. Ringkasan & Konteks Riset
- **Topik Tugas Akhir**: Prototype NPC berbasis subsirkuit connectome otak lalat (*Drosophila melanogaster* - FlyWire FAFB v783) menggunakan model spiking LIF (*Shiu et al. 2024*).
- **Tujuan Utama**: Menunjukkan bahwa variasi kepribadian dan perilaku kompleks NPC (makan, kabur, navigasi kompas, pembelajaran asosiatif) dapat muncul secara alami (*emergent*) hanya dari **6 knob sifat biologis**, tanpa menulis aturan perilaku *hardcoded*.
- **Pembanding**: Baseline FSM tradisional (13 parameter, 5 state, 7 transisi manual).

---

## 2. Aturan Ketat Arsitektur & Teknologi

1. **Bahasa & Engine**:
   - Engine: **Godot 4.7.2 Mono (win64)**.
   - Bahasa: **C# (.NET 8)**.
   - **DILARANG MENGGUNAKAN GDSCRIPT** untuk logika game atau AI.
   - Executable Godot Mono: `D:\Ruang game\Godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe` (atau `_console.exe`).

2. **Pemisahan Lapisan (Layer Separation)**:
   - `scripts/Brain/Core/`: Murni pustaka C# matematika/saraf, **TIDAK BOLEH BERGANTUNG PADA GODOT** (`using Godot;` dilarang di Core). Hal ini agar simulasi saraf bisa diuji secara headless dan di-benchmark independen.
   - `scripts/Npc/`: Implementasi tubuh NPC bersama (`NpcBase`), NPC Otak (`BrainNpc`), NPC FSM (`BaselineNpc`), dan manajer penjadwalan AI (`AiManager`).
   - `scripts/World/`: Lingkungan 3D, makanan/racun, bahaya, drone predator, dan pemain. `MapBase.cs` = basis semua map; `Maps/` = 4 map uji; `Arena.cs` = arena gabungan khusus **benchmark headless**.
   - `scenes/maps/`: satu scene per map uji interaktif (Ctrl+1..4). NPC tidak di-spawn otomatis; pengguna menaruhnya lewat overlay.
   - `scripts/UI/`: Debug overlay, raster spike real-time, dan slider knob sifat.

3. **Integritas 6 Knob Sifat (Traits)**:
   - NPC Otak HANYA boleh diatur lewat 6 knob: `Fearfulness`, `Appetite`, `Pickiness`, `Curiosity`, `LearningRate`, `NeuralNoise`.
   - **JANGAN PERNAH** menambahkan *if-else statement* perilaku manual di `BrainNpc.cs`. Semua perilaku harus berupa respons murni dari motor output neuron (Giant Fiber, MN9, DNa01/DNa02, MBON).

4. **Struktur Aset 3D & Pemetaan Tipe NPC**:
   - Hewan 3D: `res://assets/models/animals/` (`Deer.gltf`, `Cow.gltf`, `Fox.gltf`, dll.)
   - Alam & Lingkungan: `res://assets/models/nature/` (`Bush_*.gltf`, `MapleTree_*.gltf`, `DeadTree_*.gltf`, dll.)
   - Tekstur: `res://assets/textures/`

   | Tipe NPC (`data/npc_types/*/`) | Model | Catatan |
   |---|---|---|
   | `rusa` | `animals/Deer.gltf` | Pemilih, penakut |
   | `sapi` | `animals/Cow.gltf` | Herbivora rakus (nafsu makan tinggi) |
   | `rubah` | `animals/Fox.gltf` | Lincah, penasaran/keingintahuan tinggi |
   | `keledai` | `animals/Donkey.gltf` | Netral / rata-rata |

   Skala dan animasi (idle/walk/run) per model belum diverifikasi; cek di Godot sebelum dipakai. Model FBX (Snake, Frog, Rat, Wasp, Spider) belum dipetakan.

---

## 3. Perintah Standar (Commands)

| Keperluan | Perintah Terminal (PowerShell) |
|---|---|
| **Build C# Project** | `dotnet build otakLalat-npc.csproj -c Debug` |
| **Jalankan Unit Tests** | `dotnet test tests/BrainTests -c Release` |
| **Jalankan Game (GUI)** | `& "D:\Ruang game\Godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" --path .` |
| **Headless Benchmark** | `.venv/Scripts/python.exe tools/analysis/run_benchmarks.py --n 10 50 --seeds 1 2 --duration 60` (tambah `--retina` untuk otak dengan penglihatan retinotopik) |
| **Analisis & Plot Data** | `.venv/Scripts/python.exe tools/analysis/analyze_bench.py` |

---

## 4. Protokol Kerjasama Antar-Agen (Handover Protocol)

Setiap agen yang menerima perintah dari pengguna wajib:
1. **Sebelum bekerja**: Baca [`PROGRESS.md`](PROGRESS.md), [`PLAN.md`](PLAN.md), dan (untuk UI/benchmark) [`docs/rencana_pengujian_visual_dan_data.md`](docs/rencana_pengujian_visual_dan_data.md) untuk memahami status terakhir dan langkah kerja yang telah disetujui.
2. **Saat bekerja**: Pastikan kode C# selalu lolos compile (`dotnet build`) dan unit tests tetap 100% pass (`dotnet test`). Kerjakan **satu tugas per sesi** dan jangan mengedit file yang sama bersamaan dengan agen lain (cek log terakhir di `PROGRESS.md` dulu).
3. **Setelah selesai bekerja**: Wajib memperbarui bagian **Log Serah Terima** di [`PROGRESS.md`](PROGRESS.md) (apa yang diubah, file apa yang terpengaruh, dan apa tugas berikutnya) agar agen lain dapat melanjutkan tanpa jeda. Cantumkan perintah verifikasi yang benar-benar dijalankan beserta hasilnya, lalu `git commit` pekerjaan Anda sesuai [aturan Git](#5-aturan-git).

---

## 5. Aturan Git

1. **Pesan commit**: **Bahasa Indonesia**, **satu baris singkat** (maks. ~70 karakter), tanpa isi panjang. Format: `<tipe>: <ringkasan>`, dengan tipe `fitur`, `perbaikan`, `refaktor`, `uji`, `dokumen`, atau `rapikan`. Contoh: `fitur: FOV retina per spesies`, `perbaikan: NPC tidak lagi menempel ke dinding`. Rincian pekerjaan ditulis di `PROGRESS.md`, bukan di pesan commit.
2. **Satu tugas = satu commit** (atau beberapa commit kecil per tugas). Commit hanya file yang terkait tugas itu; jangan campur perubahan agen lain yang belum selesai (cek `git status` dulu).
3. **Sebelum commit**: `dotnet build` 0 error dan `dotnet test` lolos. Jangan commit file sementara/uji (skrip `Tmp*`, `bench_out/`, tangkapan layar uji) maupun data mentah FlyWire (`tools/connectome/raw/` sudah di-ignore).
4. **Push hanya atas permintaan pengguna.** Agen tidak menambah remote, tidak mengubah akses repo, dan tidak memakai akun sendiri; push memakai identitas Git pengguna di mesin ini. Jangan `--force`, jangan `--no-verify`, dan jangan mengubah (`--amend`/rebase) commit yang sudah di-push.
5. Cabang kerja saat ini adalah `master`. Buat cabang baru hanya bila diminta pengguna.
