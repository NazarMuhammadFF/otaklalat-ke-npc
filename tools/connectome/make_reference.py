"""Buat referensi laju spike (numpy) untuk unit test LifSimulator C#.

Memakai data/brain/fly_npc_brain.json (hasil extract_subcircuits.py) dan dt game.
Juga mencetak selisih dt=0.1 ms (referensi Shiu) vs dt game untuk melihat galat diskretisasi.

Pemakaian:  python make_reference.py  -> tests/reference/ref_rates.json
"""
import json
from pathlib import Path

import numpy as np
import scipy.sparse as sp

import lif

ROOT = Path(__file__).parents[2]
DT_GAME = 0.5
STIMULI = {
    "sugar_L": {"in_sugar_L": 100},
    "loom_L": {"in_loom_L": 50},
    "cx_tonic": {"in_epg": 40},
    "odor_food": {"in_odor_food_L": 100, "in_odor_food_R": 100},
}
OUTPUTS = ["out_mn9", "out_gf", "out_loom_L", "out_loom_R", "out_turn_L", "out_turn_R",
           "out_mbon_approach", "out_mbon_avoid"]


def load_json(path):
    d = json.loads(Path(path).read_text())
    c = d["csr"]
    n = d["meta"]["n_neurons"]
    W = sp.csr_matrix((np.array(c["weights"], np.float32), c["indices"], c["indptr"]), shape=(n, n))
    return d, W


def main():
    d, W = load_json(ROOT / "data/brain/fly_npc_brain.json")
    g = d["groups"]
    n = W.shape[0]
    ref = {"dt": DT_GAME, "ms": 1000, "trials": 8, "stimuli": {}}
    for name, stim in STIMULI.items():
        r = np.zeros(n)
        for k, hz in stim.items():
            r[g[k]] = hz
        res = {}
        for dt in (0.1, DT_GAME):
            c = lif.simulate(W, 1000, r, dt=dt, trials=8, seed=11).mean(0)
            res[dt] = {o: float(c[g[o]].mean()) for o in OUTPUTS}
        ref["stimuli"][name] = {"input": stim, "rates_hz": res[DT_GAME]}
        print(name, " ".join(f"{o}={res[0.1][o]:.1f}/{res[DT_GAME][o]:.1f}" for o in OUTPUTS if res[0.1][o] or res[DT_GAME][o]))
    out = ROOT / "tests/reference/ref_rates.json"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(ref, indent=2))
    print("->", out, "(format: dt0.1/dt%.1f Hz)" % DT_GAME)


if __name__ == "__main__":
    main()
