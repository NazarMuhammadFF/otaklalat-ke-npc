"""Validasi sub-otak: bandingkan respons output subsirkuit dengan whole-brain (139k neuron).

Untuk tiap stimulus, laju spike grup output dihitung pada (a) model whole-brain Shiu et al.
dan (b) subsirkuit hasil extract_subcircuits.py. Hasil whole-brain di-cache di cache/.
Juga mengecek selektivitas bau di KC (subsirkuit) dan lateralisasi.

Pemakaian:  python validate_lif.py [--trials 3] [--ms 500]
Keluaran:   tabel di stdout + reports/validation.json
"""
import argparse
import json
from pathlib import Path

import numpy as np

import extract_subcircuits as ex
import flywire
import lif

HERE = Path(__file__).parent
CACHE = HERE / "cache"
REPORT = Path(__file__).parents[2] / "reports" / "validation.json"
ODOR_HZ = 100  # laju PN maksimum yang dipakai game (PN nyata ~50-100 Hz)

# stimulus: {grup_input: laju_Hz}; outputs: grup yang dibandingkan
STIMULI = {
    "gula_kiri":        ({"in_sugar_L": 100}, ["out_mn9"]),
    "gula+pahit_kiri":  ({"in_sugar_L": 100, "in_bitter_L": 100}, ["out_mn9"]),
    "looming_kiri":     ({"in_loom_L": 50}, ["out_gf", "out_loom_L", "out_loom_R"]),
    "looming_kanan":    ({"in_loom_R": 50}, ["out_gf", "out_loom_L", "out_loom_R"]),
    "cx_tonik+PEN_kiri": ({"in_epg": 40, "in_pen_L": 60}, ["out_turn_L", "out_turn_R"]),
    "cx_tonik+PEN_kanan": ({"in_epg": 40, "in_pen_R": 60}, ["out_turn_L", "out_turn_R"]),
}


def rates_for(W, n, groups, stim, ms, trials, seed):
    r = np.zeros(n)
    for g, hz in stim.items():
        r[groups[g]] = hz
    return lif.simulate(W, ms, r, trials=trials, seed=seed).mean(0) * (1000.0 / ms)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--trials", type=int, default=3)
    ap.add_argument("--ms", type=float, default=500)
    args = ap.parse_args()

    b = flywire.load()
    idx, Wsub, gl, neurons, D, _ = ex.build(b)
    gg = {k: idx[v] for k, v in gl.items()}          # indeks grup di whole-brain
    CACHE.mkdir(exist_ok=True)
    report = {"stimuli": {}, "mb": {}}
    rows = []
    all_full, all_sub = [], []

    for name, (stim, outs) in STIMULI.items():
        cf = CACHE / f"full_{name}_{args.trials}x{int(args.ms)}.npy"
        if cf.exists():
            full = np.load(cf)
        else:
            print(f"  whole-brain: {name} ...", flush=True)
            full = rates_for(b.W, b.n, gg, stim, args.ms, args.trials, seed=1)
            np.save(cf, full)
        sub = rates_for(Wsub, len(idx), gl, stim, args.ms, args.trials, seed=2)
        # korelasi laju seluruh neuron subsirkuit (non-input) vs neuron yang sama di whole-brain
        inp = np.concatenate([gl[g] for g in stim])
        m = np.ones(len(idx), bool); m[inp] = False
        act = m & ((full[idx] > 1) | (sub > 1))
        corr = float(np.corrcoef(full[idx][act], sub[act])[0, 1]) if act.sum() > 2 else float("nan")
        entry = {"corr_rates": corr, "n_active": int(act.sum()), "outputs": {}}
        for o in outs:
            f, s = float(full[gg[o]].mean()), float(sub[gl[o]].mean())
            entry["outputs"][o] = {"whole_brain_hz": f, "subcircuit_hz": s}
            rows.append((name, o, f, s))
        all_full.append(full[idx][act]); all_sub.append(sub[act])
        report["stimuli"][name] = entry
        print(f"{name:22s} r={corr:5.2f} aktif={act.sum():4d}  " +
              "  ".join(f"{o}: {e['whole_brain_hz']:6.1f} -> {e['subcircuit_hz']:6.1f} Hz"
                        for o, e in entry["outputs"].items()))

    # --- mushroom body: selektivitas bau di KC (subsirkuit) ---
    kc = gl["kc"]
    pats = {}
    for odor in ex.ODORS:
        stim = {f"in_odor_{odor}_L": ODOR_HZ, f"in_odor_{odor}_R": ODOR_HZ}
        r = rates_for(Wsub, len(idx), gl, stim, args.ms, 1, seed=3)
        pats[odor] = r[kc] > 2
        report["mb"][odor] = {
            "kc_active_frac": float(pats[odor].mean()),
            "mbon_avoid_hz": float(r[gl["out_mbon_avoid"]].mean()),
            "mbon_approach_hz": float(r[gl["out_mbon_approach"]].mean()),
        }
    names = list(pats)
    for i in range(len(names)):
        for j in range(i + 1, len(names)):
            p, q = pats[names[i]], pats[names[j]]
            jac = float((p & q).sum() / max(1, (p | q).sum()))
            report["mb"][f"jaccard_{names[i]}_{names[j]}"] = jac
    print("MB:", json.dumps(report["mb"], indent=1))

    report["overall_corr"] = float(np.corrcoef(np.concatenate(all_full), np.concatenate(all_sub))[0, 1])
    print(f"korelasi keseluruhan whole-brain vs subsirkuit: r={report['overall_corr']:.3f}")
    REPORT.parent.mkdir(parents=True, exist_ok=True)
    REPORT.write_text(json.dumps(report, indent=2))
    print("->", REPORT)


if __name__ == "__main__":
    main()
