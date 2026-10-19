# Status Progres & Log Serah Terima Agen (PROGRESS.md)

Aturan proyek: [`AGENTS.md`](AGENTS.md). Rencana: [`PLAN.md`](PLAN.md). Dokumen ini adalah **buku catatan status hidup (*living progress log*)** yang digunakan bersama oleh **Claude Code** dan **Antigravity**. Setiap agen yang menyelesaikan pekerjaan wajib memperbarui dokumen ini sebelum mengakhiri sesi.

---

## 1. Status Milestone Proyek

| Fase | Deskripsi | Status | Keterangan |
|:---:|---|:---:|---|
| **Fase 1** | Pipeline Connectome & Mesin Spiking LIF C# | ✅ **Selesai** | 2.635 neuron, validasi sub-otak r≈0.9, 9/9 unit tests lulus 100%. |
| **Fase 2** | Pengadaan & Penataan Aset 3D Game | ✅ **Selesai** | Hewan (`assets/models/animals/`), Alam (`nature/`), FBX, dan Tekstur berhasil diimpor. |
| **Fase 3** | Environment 4 Kuadran & Pengujian Visual Manusia | ✅ **Selesai** | Model 3D hewan, 4 kuadran `Arena.cs`, kamera F1/F2/F3, skenario uji & live dashboard aktif. |
| **Fase 4** | Eksekusi Benchmark Final & Pembuatan Grafik Bab 4 | 🟡 **Siap Dijalankan** | Skrip `run_benchmarks.py` & `analyze_bench.py` siap memproduksi grafik PNG di `reports/figures/`. |

---

## 2. Rincian Checklist Pekerjaan

### A. Fondasi Sistem & Arsitektur
- [x] Ekstraksi dataset FlyWire FAFB v783 ke `data/brain/fly_npc_brain.json`
- [x] Engine Spiking LIF Shiu et al. (2024) di C# murni (`scripts/Brain/Core/`)
- [x] Preset 6 knob sifat (`data/npc_types/brain/*.tres`) dan FSM (`data/npc_types/fsm/*.tres`)
- [x] Multi-threading AI scheduler (`scripts/Npc/AiManager.cs`) dan Brain-LOD
- [x] Analisis authoring effort (`reports/authoring_effort.md`)

### B. Visualisasi & Evaluasi Manusia (Fase 3 - Selesai)
- [x] Impor aset 3D hewan (Deer, Cow, Fox, Donkey, dll.) dan alam (Bush, Trees, Rocks)
- [x] Pembuatan dokumen rancangan environment & pengujian (`PLAN.md`)
- [x] Pembuatan jembatan komunikasi agen (`AGENTS.md`, `CLAUDE.md`, `GEMINI.md`, `PROGRESS.md`)
- [x] Hubungkan model 3D hewan ke `NpcBase.cs` (menggantikan kapsul polos, lengkap dengan animasi `Idle`/`Walk`/`Gallop`/`Eating` dan koreksi orientasi hadap `-Z`).
- [x] Tambahkan cincin penanda visual kaki (Otak = Aura Biru, FSM = Aura Kuning/Oranye).
- [x] Tambahkan emotikon status reaktif di atas kepala (🏃💨 Kabur, 🍎 Makan, 🤢 Racun, ⚡ Bahaya, 🧭 Jelajah).
- [x] Bangun tata letak 4 Kuadran Terpadu di `scripts/World/Arena.cs`, `Edible.cs`, dan `DangerZone.cs`:
  - Kuadran 1: Feeding Grounds (`Bush_Large_Flowers`, `Bush_Small`, `Flower_*_Clump` & cincin aroma)
  - Kuadran 2: Danger Sanctuary (`DeadTree_*`, zona sengatan & batas aroma `OdorNeutral`)
  - Kuadran 3: Predator Runway (`Grass_Small`, koridor looming drone dengan proyeksi bayangan target)
  - Kuadran 4: Landmark Forest (`MapleTree_*`, batu bertekstur `Rocks.png` & kompas matahari)
- [x] Pasang controller multi-kamera (F1: FPS Pemain, F2: Bird's-Eye Top Down dengan Pan/Zoom, F3: Follow NPC) + tombol skenario uji satu-klik & statistik komparatif di `DebugOverlay.cs`.

### C. Data & Grafik Tugas Akhir (Fase 4 - Aktif)
- [ ] **[Task Selanjutnya]** Jalankan benchmark matriks headless (`run_benchmarks.py`) dan hasilkan grafik PNG di `reports/figures/` (`analyze_bench.py`).
- [ ] Buat file batch satu-klik (`jalankan_pengujian.bat`) bila diperlukan pengguna.

---

## 3. Log Serah Terima Pekerjaan (Handover History)

### [2026-10-19] - Antigravity (Gemini)
- **Tindakan**:
  1. Mengorganisir seluruh aset dari `D:\Ruang game\Asset\Untuk TA` ke dalam struktur folder terstandar: `assets/models/animals/`, `assets/models/nature/`, `assets/models/fbx/`, dan `assets/textures/`.
  2. Menjalankan proses impor Godot headless; 132 file aset (non-`.import`) berada di `assets/`, 96 di antaranya punya `.import`; sisanya (mis. `.bin`) tidak perlu impor.
  3. Menyusun rencana implementasi komprehensif di `PLAN.md` (dan salinan di folder `docs/`).
  4. Membuat jembatan komunikasi antar-agen: `CLAUDE.md` dan `PROGRESS.md` (kini aturan dipusatkan di `AGENTS.md`).
- **Status Proyek Saat Ini**:
  - Kode C# bersih (0 warning, 0 error), 9/9 unit test pass.
  - Aset 3D siap dihubungkan ke entitas NPC dan arena.
- **Tugas Berikutnya untuk Agen Selanjutnya (atau sesi ini)**:
  - Buka [`scripts/Npc/NpcBase.cs`](scripts/Npc/NpcBase.cs) dan pasang instansiasi model glTF hewan (`Deer.gltf` untuk Rusa, `Cow.gltf` untuk Babi, `Fox.gltf` untuk Kucing, dll.) menggantikan mesh kapsul primitif.
  - Pasang billboard emotikon status dan cincin aura kaki pembeda Otak vs FSM.

### [2026-10-19] - Claude Code
- **Tindakan**:
  1. Membuat `AGENTS.md` sebagai sumber aturan tunggal; `CLAUDE.md` (via `@AGENTS.md`) dan `GEMINI.md` hanya merujuk ke sana.
  2. Menghapus duplikat `docs/desain_environment_dan_pengujian_sifat_alami.md` (identik dengan `PLAN.md`); `PLAN.md` kini rencana resmi.
  3. Mengganti link `file:///D:/...` menjadi path relatif; menambah tabel pemetaan tipe NPC → model di `AGENTS.md`.
- **Verifikasi (2026-10-19)**: `dotnet build otakLalat-npc.csproj -c Debug` 0 warning/0 error; `dotnet test tests/BrainTests -c Release` 9/9 lulus.
- **Tugas Berikutnya**: sama seperti di atas (model hewan di `NpcBase.cs`). Kriteria selesai: tiap tipe NPC tampil sebagai model pada tabel `AGENTS.md`, build bersih, test tetap 9/9.

### [2026-10-19] - Antigravity (Gemini)
- **Tindakan**:
  1. Memperbarui [`scripts/Npc/NpcBase.cs`](scripts/Npc/NpcBase.cs): memuat model glTF hewan sesuai tabel `AGENTS.md` (`Deer.gltf` skala 0.35, `Cow.gltf` skala 0.26, `Fox.gltf` skala 0.30, `Donkey.gltf` skala 0.34, rotasi Y 180° agar menghadap `-Z`), menyinkronkan `AnimationPlayer` (`Idle`, `Walk`, `Gallop`, `Eating`), menambahkan cincin aura kaki (Biru = Otak, Kuning/Oranye = FSM), serta emotikon status reaktif (`🏃💨`, `🍎`, `🤢`, `⚡`, `🧭`) pada `Label3D`.
  2. Memperbarui [`scripts/World/Edible.cs`](scripts/World/Edible.cs), [`scripts/World/DangerZone.cs`](scripts/World/DangerZone.cs), [`scripts/World/Drone.cs`](scripts/World/Drone.cs), dan [`scripts/World/Arena.cs`](scripts/World/Arena.cs): membangun Environment 4 Kuadran Terpadu menggunakan aset 3D (`Bush_Large_Flowers.gltf`, `Bush_Small.gltf`, `DeadTree_*.gltf`, `MapleTree_*.gltf`, `Flower_*_Clump.gltf`, `Grass_Small.gltf`, tekstur `Rocks.png`), visualisasi radius aroma & batas zona bahaya, papan nama 3D per kuadran, serta proyeksi bayangan target drone dan metode `DiveAt()`.
  3. Memperbarui [`scripts/World/Player.cs`](scripts/World/Player.cs) dan [`scripts/UI/DebugOverlay.cs`](scripts/UI/DebugOverlay.cs): menambahkan 3 mode kamera (`F1` FirstPerson, `F2` TopDown Bird's-Eye sebagai default saat mulai agar seluruh 4 kuadran langsung terlihat, `F3` FollowNpc), tombol skenario uji satu-klik (`Uji Looming`, `Uji Makan vs Racun`, `Uji Belajar Bahaya`), dan tabel statistik rata-rata real-time (Otak vs FSM).
- **Verifikasi (2026-10-19)**:
  - `dotnet build otakLalat-npc.csproj -c Debug` -> Build succeeded (0 Warning, 0 Error).
  - `dotnet test tests/BrainTests -c Release` -> Passed 9/9 (0 Failed).
  - `& "D:\Ruang game\Godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe" --headless --path . --fixed-fps 60 res://scenes/Arena.tscn -- --bench=brain --n=10 --seed=1 --duration=3 --lod=0 --out=bench_out/_verify_env` -> Selesai bersih tanpa error (kode keluar 0).
  - `.venv\Scripts\python.exe tools/analysis/count_effort.py` -> Tetap 0 LOC perilaku tulisan tangan untuk Otak (6 knob vs 13 parameter FSM).
- **Tugas Berikutnya**:
  - Jalankan matriks benchmark headless (`tools/analysis/run_benchmarks.py`) dan analisis plot (`tools/analysis/analyze_bench.py`) untuk menghasilkan grafik kuantitatif di `reports/figures/`.

### [2026-10-19] - Claude Code (#2: pemisahan map)
- **Tindakan**:
  1. `scripts/World/MapBase.cs` (baru): basis map (lingkungan, AI, pemain, overlay, API spawn `SpawnNpc/ClearNpcs`). `Arena.cs` kini turunan `MapBase` (layout lama, auto-spawn 24 NPC, mode benchmark).
  2. 4 map di `scenes/maps/Map_*.tscn` + `scripts/World/Maps/`: Makan vs Racun, Bahaya & Belajar, Koridor Predator, Hutan Navigasi. Map mulai kosong.
  3. `DebugOverlay`: tombol pindah map + `Ctrl+1..4`, panel spawn (tipe, Otak/FSM, Spawn, Hapus semua, klik lantai), tombol skenario dan statistik hanya yang relevan per map. Hotkey spawn lama 1-4 dihapus. `scripts/UI/MapRegistry.cs` menyimpan daftar map.
- **Verifikasi**: build 0 warning/0 error; test 9/9; tangkapan layar tiap map via `--write-movie`; benchmark headless `--bench=brain --n=10 --duration=5` menghasilkan csv. Klik spawn dan pindah map via UI **belum diuji interaktif**.
### [2026-10-19] - Antigravity (Gemini) (#3: adaptasi ukuran layar & launcher satu-klik)
- **Tindakan**:
  1. Menyesuaikan konfigurasi window di [`project.godot`](project.godot): `viewport_width=1280`, `viewport_height=720`, `mode=2` (Maximized secara otomatis saat mulai agar pas penuh dengan monitor pengguna), `resizable=true`, `initial_position_type=2` (Centered), dengan stretch mode `canvas_items` dan aspect `expand`.
  2. Memastikan `run/main_scene="res://scenes/maps/Map_MakanRacun.tscn"` dan memulihkan scene `scenes/Arena.tscn` (membersihkan duplikat di `data/Arena.tscn`).
  3. Memperbarui [`scripts/UI/DebugOverlay.cs`](scripts/UI/DebugOverlay.cs): menambahkan tombol `⛶ Layar Penuh (F11)` serta shortcut keyboard `F11` dan `Alt+Enter` untuk toggle instan antara Maximized dan Fullscreen.
  4. Membuat file launcher satu-klik [`JALANKAN_GAME.bat`](JALANKAN_GAME.bat) di direktori utama proyek yang mengeksekusi Godot Mono Windows secara langsung, mempermudah pengguna/penguji menjalankan prototype tanpa perlu mengetik di terminal.
- **Verifikasi**:
  - `dotnet build otakLalat-npc.csproj -c Debug` -> 0 Warning(s), 0 Error(s).
  - `dotnet test tests/BrainTests -c Release` -> Passed 9/9 (0 Failed).
### [2026-10-19] - Antigravity (Gemini) (#4: Bento Grid UI System)
- **Tindakan**:
  1. Merefaktor arsitektur antarmuka pada [`scripts/UI/DebugOverlay.cs`](scripts/UI/DebugOverlay.cs) menjadi **Bento Grid Modular** dengan 2 kolom mengambang terpisah (Kiri dan Kanan), membebaskan area tengah 3D untuk pengamatan visual.
  2. Membagi fungsi ke dalam 7 Bento Tiles terisolasi dengan border aksen warna khusus:
     - 🟦 *Card 1*: 🗺️ Map & Kamera (`FPS`, `AI Tick ms`, `Kamera F1/F2/F3`, `Fullscreen F11`, Pindah 4 Map).
     - 🟩 *Card 2*: 🐾 Spawner Populasi (Dropdown Tipe & AI, `+ Spawn`, centang `Klik lantai`, `🗑️ Hapus Semua`).
     - 🟧 *Card 3*: 🎯 Skenario Uji Khusus (Tombol pemicu kontekstual map: Looming, Makan vs Racun, Belajar Bahaya).
     - 🟪 *Card 4*: 📊 Skor Komparatif Real-Time (Tabel head-to-head Otak vs FSM untuk metrik makan, racun, kabur, bahaya, jarak).
     - 🩵 *Card 5*: 🔬 Inspeksi Entitas Terpilih (Status emotikon, output motorik terurai, laju tembak neuron motorik GF/DNa/MN9/MBON).
     - 🟨 *Card 6*: 🧬 6 Knob Sifat Biologis (6 slider interaktif dilengkapi pembacaan angka desimal real-time).
     - 🔴 *Card 7*: ⚡ Raster Spike Neuron (Visualisasi 2.635 neuron LIF: sensorik, memori/navigasi, motorik).
  3. Menambahkan fitur *collapsible* (tombol `−`/`+` per kartu) serta mode Zen / sembunyikan HUD instan via tombol shortcut **`H`**.
- **Verifikasi**:
  - `dotnet build otakLalat-npc.csproj -c Debug` -> 0 Warning(s), 0 Error(s).
  - `dotnet test tests/BrainTests -c Release` -> Passed 9/9 (0 Failed).
  - Verifikasi headless scene `Map_MakanRacun.tscn` -> exited code 0, inisialisasi UI bersih tanpa runtime exception.
  - Verifikasi headless benchmark `Arena.tscn` -> exited code 0, 10 NPC simulasi selesai tanpa regresi.
- **Tugas Berikutnya**:
  - Pengujian langsung UI oleh pengguna via `JALANKAN_GAME.bat`.
  - Eksekusi benchmark headless final (`tools/analysis/run_benchmarks.py`) dan pembuatan visualisasi grafik data (`tools/analysis/analyze_bench.py`) untuk Bab 4 Tugas Akhir.

## 4. Keputusan yang Sudah Diambil / Pertanyaan Terbuka

Keputusan:
- Bahasa C# (.NET 8) tanpa GDScript, 6 knob biologis murni tanpa hardcoded behaviors di `BrainNpc`.
- 4 map uji terpisah 40×40 m (`scenes/maps/`), NPC di-spawn manual lewat overlay.
- Window game di-set auto-maximized (`mode=2`, `resizable=true`) + dukungan `F11` fullscreen toggle.
- File batch launcher disediakan sebagai `JALANKAN_GAME.bat`.
- Antarmuka pengujian menggunakan arsitektur Bento Grid 7-kartu modular dengan Zen Mode (`H`).

Belum diputuskan (jawab lalu catat di sini):
- Visualisasi aroma: cincin transparan di lantai atau partikel kabut?
- Skala benchmark: ringan (N=10-24) atau matriks penuh (N=10/50/100/200)?


