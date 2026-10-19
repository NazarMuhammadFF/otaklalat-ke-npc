# Biaya authoring perilaku

| | Otak lalat | FSM |
|---|---:|---:|
| Parameter per tipe NPC | 6 | 13 |
| Total parameter untuk 4 tipe | 24 | 52 |
| LOC logika perilaku tulisan tangan | 0 | 110 |
| State / transisi eksplisit | 0 / 0 | 5 / 7 |
| Kondisi (if/ternary) di logika perilaku | 0 | 30 |
| LOC lem indera/gerak (sekali, semua tipe) | 109 | (termasuk dalam logika) |
| LOC mesin simulasi generik | 340 | - |
| LOC pipeline data offline | 277 | - |

Catatan: kode tubuh/indera/dunia (`NpcBase`, `World/*`) dipakai kedua metode dan tidak dihitung.
Menambah tipe NPC baru: otak = 6 angka; FSM = 13 angka, dan perilaku baru
(mis. belajar menghindari bau baru) membutuhkan state/transisi baru di FSM tetapi tidak pada otak.
