# otakLalat-npc: NPC berbasis sub-otak lalat (FlyWire) di Godot 4.7

Prototype Tugas Akhir. Perilaku NPC dijalankan oleh **subsirkuit connectome otak *Drosophila***
(FlyWire FAFB v783) yang disimulasikan sebagai jaringan spiking LIF (model Shiu et al. 2024). Setiap tipe NPC
cukup didefinisikan dengan **6 knob sifat**. Sebagai pembanding ada baseline **FSM tradisional** yang semua
sifatnya ditulis manual.

## Isi

| Bagian | Lokasi |
|---|---|
| Pipeline data connectome (Python, offline) | `tools/connectome/` |
| Mesin otak (C#, bebas Godot, teruji) | `scripts/Brain/Core/` |
| Integrasi Godot (aset, profil sifat) | `scripts/Brain/` |
| NPC: tubuh bersama, NPC otak, NPC FSM, manajer AI | `scripts/Npc/` |
| Dunia: arena, makanan/racun, zona bahaya, drone, pemain | `scripts/World/` |
| Overlay debug (slider sifat, raster spike) | `scripts/UI/` |
| Benchmark headless | `scripts/Benchmark/`, `tools/analysis/` |
| Preset tipe NPC | `data/npc_types/brain/*.tres` (6 knob), `data/npc_types/fsm/*.tres` (13 parameter) |
| Sub-otak hasil ekstraksi | `data/brain/fly_npc_brain.json` |
| Unit test & uji perilaku | `tests/BrainTests/` |
| Laporan hasil | `reports/` |

## Sub-otak yang dipakai (2.635 neuron, ~136 rb koneksi)

| Modul | Sensor (input) | Output | Dipakai untuk |
|---|---|---|---|
| Escape | LPLC2, LC4 (looming, kiri/kanan) | DNp01 (Giant Fiber), DNp02/04/11 | kabur dari ancaman yang mendekat |
| Feeding | GRN gula & pahit | MN9 (CB0701) | makan / menolak |
| Central complex | EPG (tonik), PEN (rotasi diri), ring neuron (landmark) | DNa01/DNa02 kiri/kanan | menjelajah, belok |
| Mushroom body | ALPN 3 bau → 400 KC → MBON, DAN PAM/PPL1, APL | MBON approach/avoid | valensi bau yang **dipelajari** |

Neuron dipilih secara otomatis sebagai neuron pada jalur sensor → output (≤ 2–4 hop, ≥ 5 sinapsis).

## Knob sifat (per tipe NPC)

| Knob | Yang diubah di otak |
|---|---|
| `Fearfulness` | gain input looming |
| `Appetite` | gain GRN gula (+ laju lapar) |
| `Pickiness` | gain GRN pahit |
| `Curiosity` | drive tonik central complex |
| `LearningRate` | kekuatan plastisitas dopamin KC→MBON |
| `NeuralNoise` | besar fluktuasi drive central complex |

Perilaku seperti ambang lapar, jarak kabur, preferensi bau, dan penghindaran zona yang pernah menyakiti
tidak ditulis sebagai aturan. Semuanya muncul dari connectome dan pengalaman.

## Menjalankan

Kebutuhan: Godot 4.7.2 **.NET**, .NET SDK 8, Python 3.11 (`uv`).

```bash
# 1. data (sekali): unduh snapshot publik v783 ke tools/connectome/raw/
#    - Connectivity_783.parquet, Completeness_783.csv  (github.com/philshiu/Drosophila_brain_model)
#    - neuron_annotations.tsv = Supplemental_file1_neuron_annotations.tsv (github.com/flyconnectome/flywire_annotations)
uv venv .venv && uv pip install --python .venv/Scripts/python.exe pandas pyarrow numpy scipy matplotlib
cd tools/connectome
../../.venv/Scripts/python.exe extract_subcircuits.py   # -> data/brain/fly_npc_brain.json
../../.venv/Scripts/python.exe validate_lif.py          # whole-brain vs subsirkuit -> reports/validation.json
../../.venv/Scripts/python.exe make_reference.py        # referensi untuk unit test C#
cd ../..

# 2. test
dotnet test tests/BrainTests -c Release

# 3. main (buka di editor Godot .NET, atau)
"<Godot_mono_console.exe>" --path .
```

Kontrol: WASD bergerak, Shift lari (mendekat cepat = looming bagi NPC), klik kanan + geser untuk menoleh,
klik kiri untuk memilih NPC (muncul slider sifat dan raster spike), F menaruh makanan, 1–4 spawn NPC otak
(Shift+1–4 untuk FSM), L untuk brain-LOD, Tab untuk label.

### Benchmark

```bash
.venv/Scripts/python.exe tools/analysis/run_benchmarks.py --n 10 50 100 200 --seeds 1 2 3 --duration 120 --lod
.venv/Scripts/python.exe tools/analysis/analyze_bench.py     # -> reports/bench_summary.md + reports/figures/
.venv/Scripts/python.exe tools/analysis/count_effort.py      # -> reports/authoring_effort.md
```

## Keputusan pemodelan (perlu disebut di TA)

1. **Model neuron** mengikuti Shiu et al. 2024 (LIF, bobot = jumlah sinapsis × tanda NT × 0,275 mV). Langkah
   game dt = 0,5 ms (referensi 0,1 ms). Selisih laju output ±5–25%, dan unit test memastikan C# cocok dengan numpy.
2. **Subsirkuit vs whole-brain**: korelasi laju neuron r ≈ 0,9 (`reports/validation.json`). Lateralisasi
   looming dan supresi MN9 oleh rasa pahit tetap terjaga.
3. **KC→KC dihapus**. Koneksi aksoaksonal ini pada model LIF membuat ~80% KC aktif untuk bau apa pun.
   **KC disampel** 400 dari 5.177 dengan kompensasi bobot. Hasilnya kode bau sparse (4–23% KC) dengan overlap ~1%.
4. **Dopamin = neuromodulator**: sinapsis cepat keluar dari DAN dihapus dan hanya dipakai sebagai peta
   kompartemen plastisitas. Plastisitas hanya terpicu oleh burst DAN (> 65 Hz, dihaluskan 0,3 s), sehingga
   aktivitas DAN akibat bau saja tidak mengubah memori.
5. **Variasi** tidak berasal dari noise membran (pada model ini noise membran memicu loop eksitasi
   CB0216↔MN9). Variasi berasal dari spike Poisson dan fluktuasi Ornstein–Uhlenbeck drive central complex.
6. **Antennal lobe**: respons PN memakai normalisasi divisif c/(c+K) (Olsen et al. 2010). ORN memproyeksi
   bilateral dengan bias ipsilateral 60% (Gaudry et al. 2013).
7. **"Masa kecil"**: semua NPC otak menjalani 3 detik bau buah + hadiah saat spawn. Ini setara dengan
   pengetahuan bawaan FSM bahwa bau makanan itu baik. Jalur lateral horn (valensi bawaan lalat) tidak dimodelkan.
8. **Tubuh/VNC abstrak** (`MotorDecoder`): laju DN dibaca menjadi gerak, dengan konstanta yang sama untuk semua
   tipe. Contohnya arah kabur kontralateral dari sisi DN looming yang lebih aktif dan klinotaksis =
   valensi MB × gradien bau. Refleks menghindar dinding ada di tubuh dan dipakai juga oleh FSM.

## Sitasi data & model

- Dorkenwald, S. et al. (2024). Neuronal wiring diagram of an adult brain. *Nature* 634, 124–138.
- Schlegel, P. et al. (2024). Whole-brain annotation and multi-connectome cell typing of *Drosophila*. *Nature* 634, 139–152.
- Shiu, P. K. et al. (2024). A *Drosophila* computational brain model reveals sensorimotor processing. *Nature* 634, 210–219.
- Aso, Y. et al. (2014). Mushroom body output neurons encode valence and guide memory-based action selection. *eLife* 3, e04580.

Data FlyWire berlisensi CC-BY-NC 4.0 (non-komersial).
