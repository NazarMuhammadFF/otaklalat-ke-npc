"""Peta retina 1D untuk neuron looming (LPLC2 + LC4) -> data/brain/retina_map.json

ASUMSI (dicatat untuk TA): anotasi FlyWire hanya menyediakan posisi soma, bukan dendrit di lobula, jadi
urutan posisi soma di sepanjang sumbu utama sebaran dipakai sebagai PROKSI urutan azimut di bidang pandang.
  * tiap mata & tiap tipe sel (LPLC2, LC4) diurutkan sepanjang PC1 sebaran soma (x,y), lalu dibagi K kuantil
    sama banyak, sehingga tiap sektor memuat kedua tipe sel dengan proporsi seimbang;
  * sektor 0 = ujung yang paling dekat garis tengah otak (pandangan depan), sektor K-1 = paling lateral (belakang).
Indeks neuron = indeks lokal di data/brain/fly_npc_brain.json.

Pemakaian:  python make_retina_map.py [--k 8]
"""
import argparse
import json
from pathlib import Path

import numpy as np
import pandas as pd

ROOT = Path(__file__).parents[2]


def build(k: int):
    brain = json.loads((ROOT / "data/brain/fly_npc_brain.json").read_text(encoding="utf-8"))
    ann = pd.read_csv(ROOT / "tools/connectome/raw/neuron_annotations.tsv", sep="\t", low_memory=False)
    ann["root_id"] = ann.root_id.astype(str)
    ann = ann.set_index("root_id")
    ids = [n["id"] for n in brain["neurons"]]
    xy = ann.loc[ids, ["soma_x", "soma_y"]].to_numpy(float)
    ctype = ann.loc[ids, "cell_type"].fillna("").to_numpy()
    mids = [xy[brain["groups"][f"in_loom_{s}"], 0].mean() for s in "LR"]
    midline = float(np.mean(mids))

    groups = {}
    for s in "LR":
        base = np.array(brain["groups"][f"in_loom_{s}"])
        sectors = {i: [] for i in range(k)}
        for t in ("LPLC2", "LC4"):
            idx = base[ctype[base] == t]
            P = xy[idx] - xy[idx].mean(0)
            axis = np.linalg.svd(P, full_matrices=False)[2][0]
            u = P @ axis
            # orientasi: ujung dengan |x - garis tengah| terkecil = sektor 0 (depan)
            near = np.abs(xy[idx, 0] - midline)
            if np.corrcoef(u, near)[0, 1] > 0:
                u = -u
            order = np.argsort(-u, kind="stable")
            for rank, j in enumerate(order):
                sectors[min(k - 1, rank * k // len(idx))].append(int(idx[j]))
        for i in range(k):
            groups[f"in_loom_{s}_s{i}"] = sorted(sectors[i])
    return {"k": k, "assumption": "posisi soma sebagai proksi retinotopi; sektor 0 = dekat garis tengah = pandangan depan",
            "groups": groups}


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--k", type=int, default=8)
    ap.add_argument("--out", type=Path, default=ROOT / "data/brain/retina_map.json")
    a = ap.parse_args()
    doc = build(a.k)
    a.out.write_text(json.dumps(doc, separators=(",", ":")), encoding="utf-8")
    print(f"-> {a.out}", {k: len(v) for k, v in doc["groups"].items()})
