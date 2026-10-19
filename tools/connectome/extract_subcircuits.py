"""Ekstraksi sub-otak lalat (FlyWire v783) untuk NPC -> data/brain/fly_npc_brain.json

Satu jaringan gabungan berisi 4 modul:
  escape  : LPLC2 + LC4 (looming) -> DNp01 (Giant Fiber), DNp02/DNp04/DNp11
  feeding : GRN gula & pahit -> MN9 (CB0701, proboscis; Shiu et al. 2024)
  cx      : EPG/PEN (central complex) -> PFL3 -> DNa01/DNa02 (belok)
  mb      : ALPN (3 bau) -> KC (sampel) -> MBON, + DAN (PAM/PPL1) + APL

Neuron dipilih bila berada pada jalur sensor -> output <= `hops` langkah (|sinapsis| >= THR).
Modifikasi yang disengaja (didokumentasikan untuk TA):
  * koneksi KC->KC dihapus (aksoaksonal; di model LIF membuat semua KC aktif dan
    menghapus selektivitas bau),
  * KC disampel; bobot KC->MBON dan KC->APL dikompensasi dengan faktor N_total/N_sampel,
  * sinapsis keluar DAN dihapus (dopamin sebagai neuromodulator; dipakai hanya untuk plastisitas).

Pemakaian:  python extract_subcircuits.py [--out ../../data/brain/fly_npc_brain.json]
"""
import argparse
import json
from pathlib import Path

import numpy as np
import scipy.sparse as sp

import flywire

THR = 5            # threshold sinapsis untuk penelusuran jalur (konvensi FlyWire)
KC_SAMPLE = 400    # jumlah KC yang dipertahankan
SEED = 7

# bau game -> glomerulus antennal lobe (ALPN uniglomerular)
ODORS = {
    # dipilih agar cakupan PN->KC seimbang (~20k sinapsis per bau); valensi dipelajari lewat MB
    "food":    r"^(DM1_lPN|DM4_adPN)$",   # ester buah
    "poison":  r"^(DC1_adPN|VA2_adPN)$",
    "neutral": r"^(DM2_lPN|DL1_adPN)$",
}

# valensi MBON (Aso et al. 2014 eLife 3:e04580): aktivasi -> menghindar / mendekat
MBON_AVOID = ["MBON01", "MBON02", "MBON03", "MBON04"]
MBON_APPROACH = ["MBON05", "MBON11", "MBON12", "MBON13", "MBON14"]


def paths(A, AT, S, O, k):
    """Neuron pada jalur S -> O dengan panjang <= k (BFS maju & mundur)."""
    def bfs(M, seeds):
        d = dict.fromkeys(map(int, seeds), 0)
        front = list(d)
        for h in range(1, k + 1):
            if not front:
                break
            nxt = set(M[front].indices.tolist()) - d.keys()
            for x in nxt:
                d[x] = h
            front = list(nxt)
        return d
    f, g = bfs(A, S), bfs(AT, O)
    return {x for x in f if x in g and f[x] + g[x] <= k}


def build(b: flywire.Brain):
    a = b.ann
    ct = a.cell_type.fillna("")
    cls = a.cell_class.fillna("")
    side = a.side.fillna("").map({"left": "L", "right": "R"}).fillna("C").to_numpy()

    def sel(mask, s=None):
        m = np.asarray(mask)
        if s:
            m = m & (side == s)
        return np.flatnonzero(m)

    adj = {}

    def adjacency(thr):
        if thr not in adj:
            A = (abs(b.W) >= thr).astype(np.int8).tocsr()
            adj[thr] = (A, A.T.tocsr())
        return adj[thr]

    groups = {}
    keep = set()

    def module(name, sensors, outputs, hops, thr=THR):
        S = np.concatenate([v for v in sensors.values()])
        O = np.concatenate([v for v in outputs.values()])
        p = paths(*adjacency(thr), S, O, hops)
        keep.update(p, S.tolist(), O.tolist())
        groups.update(sensors)
        groups.update(outputs)
        print(f"[{name}] sensor={len(S)} output={len(O)} jalur={len(p)}")

    looming = ct.isin(["LPLC2", "LC4"])
    module("escape",
           {"in_loom_L": sel(looming, "L"), "in_loom_R": sel(looming, "R")},
           {"out_gf": sel(ct == "DNp01"),
            "out_loom_L": sel(ct.isin(["DNp02", "DNp04", "DNp11"]), "L"),
            "out_loom_R": sel(ct.isin(["DNp02", "DNp04", "DNp11"]), "R")},
           hops=2)

    sub = a.cell_sub_class.fillna("")
    sugar = sub.isin(["sugar", "sugar/low_salt"])
    module("feeding",
           {"in_sugar_L": sel(sugar, "L"), "in_sugar_R": sel(sugar, "R"),
            "in_bitter_L": sel(sub == "bitter", "L"), "in_bitter_R": sel(sub == "bitter", "R")},
           {"out_mn9": sel(ct == "CB0701")},
           hops=3)

    pen = ct.str.startswith("PEN")
    module("cx",
           {"in_epg": sel(ct == "EPG"), "in_pen_L": sel(pen, "L"), "in_pen_R": sel(pen, "R")},
           {"out_turn_L": sel(ct.isin(["DNa01", "DNa02"]), "L"),
            "out_turn_R": sel(ct.isin(["DNa01", "DNa02"]), "R")},
           hops=4, thr=10)
    groups["in_er_L"] = sel(ct.str.match(r"^ER\d"), "L")    # ring neuron (GABA): landmark
    groups["in_er_R"] = sel(ct.str.match(r"^ER\d"), "R")
    keep.update(groups["in_er_L"].tolist(), groups["in_er_R"].tolist())

    # --- mushroom body ---
    alpn = cls == "ALPN"
    for odor, pat in ODORS.items():
        for s in "LR":
            groups[f"in_odor_{odor}_{s}"] = sel(alpn & ct.str.match(pat), s)
    pns = np.concatenate([groups[f"in_odor_{o}_{s}"] for o in ODORS for s in "LR"])
    kc_all = sel(cls == "Kenyon_Cell")
    # KC sampel: utamakan KC yang menerima input dari PN bau terpilih, sisanya acak
    pn_in = np.asarray(abs(b.W[pns][:, kc_all]).sum(axis=0)).ravel()
    rng = np.random.default_rng(SEED)
    order = np.lexsort((rng.random(len(kc_all)), -pn_in))
    kc = np.sort(kc_all[order[:KC_SAMPLE]])
    groups["kc"] = kc
    groups["mbon"] = sel(cls == "MBON")
    groups["out_mbon_avoid"] = sel(ct.isin(MBON_AVOID))
    groups["out_mbon_approach"] = sel(ct.isin(MBON_APPROACH))
    groups["in_dan_reward"] = sel(ct.str.startswith("PAM"))
    groups["in_dan_punish"] = sel(ct.str.startswith("PPL1"))
    groups["apl"] = sel(ct == "APL")
    keep.update(pns.tolist(), kc.tolist(), groups["mbon"].tolist(), groups["apl"].tolist(),
                groups["in_dan_reward"].tolist(), groups["in_dan_punish"].tolist())
    kc_scale = len(kc_all) / len(kc)

    # jangan ikut KC yang tidak disampel (bisa masuk lewat penelusuran jalur)
    keep -= set(kc_all.tolist()) - set(kc.tolist())

    idx = np.array(sorted(keep))
    W = b.W[idx][:, idx].tocsr().astype(np.float32)
    local = {g: np.searchsorted(idx, v) for g, v in groups.items()}
    is_kc = np.zeros(len(idx), bool); is_kc[local["kc"]] = True
    W = W.tocoo()
    pre_kc, post_kc = is_kc[W.row], is_kc[W.col]
    keep_e = ~(pre_kc & post_kc)                              # hapus KC->KC
    data = W.data.copy()
    data[pre_kc & ~post_kc] *= kc_scale                       # kompensasi sampling KC
    W = sp.csr_matrix((data[keep_e], (W.row[keep_e], W.col[keep_e])), shape=(len(idx),) * 2)
    W.eliminate_zeros()

    # bobot DAN -> MBON (penentu kompartemen plastisitas)
    dan = np.concatenate([local["in_dan_reward"], local["in_dan_punish"]])
    D = abs(W[dan][:, local["mbon"]]).toarray()               # (dan x mbon)
    # dopamin = neuromodulator (GPCR), bukan transmisi cepat: sinapsis keluar DAN dihapus dari jaringan
    # (di model Shiu dopamin dihitung eksitatorik). Efeknya hanya lewat plastisitas KC->MBON.
    keep_rows = np.ones(len(idx), bool); keep_rows[dan] = False
    W = (sp.diags(keep_rows.astype(np.float32)) @ W).tocsr()
    W.eliminate_zeros()

    neurons = [{"id": str(b.ids[i]), "type": ct.iloc[i], "class": cls.iloc[i], "side": side[i]} for i in idx]
    return idx, W, local, neurons, D, kc_scale


def export(out: Path):
    b = flywire.load()
    idx, W, groups, neurons, D, kc_scale = build(b)
    W.sort_indices()
    doc = {
        "meta": {
            "source": "FlyWire FAFB v783 (Dorkenwald et al. 2024; Schlegel et al. 2024); "
                      "konektivitas bertanda & model LIF dari Shiu et al. 2024. Lisensi data CC-BY-NC 4.0.",
            "n_neurons": int(len(idx)), "n_synapses": int(W.nnz), "threshold": THR,
            "kc_sample": KC_SAMPLE, "kc_scale": kc_scale, "odors": ODORS,
            "mbon_avoid": MBON_AVOID, "mbon_approach": MBON_APPROACH,
        },
        "neurons": neurons,
        "groups": {k: v.tolist() for k, v in groups.items()},
        # CSR (baris = presinaps), bobot = jumlah sinapsis bertanda
        "csr": {"indptr": W.indptr.tolist(), "indices": W.indices.tolist(),
                "weights": [round(float(x), 3) for x in W.data]},
        # dan_mbon[d][m]: jumlah sinapsis DAN ke-d (urutan reward lalu punish) ke MBON ke-m
        "dan_mbon": D.round(1).tolist(),
    }
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(doc, separators=(",", ":")))
    print(f"-> {out}  neuron={len(idx)} sinapsis={W.nnz}  ({out.stat().st_size/1e6:.1f} MB)")
    return b, idx, W, groups


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", type=Path, default=Path(__file__).parents[2] / "data/brain/fly_npc_brain.json")
    export(ap.parse_args().out)
