"""Analisis hasil benchmark (bench_out/) -> reports/bench_summary.{json,md} + reports/figures/*.png

Metrik:
  runtime   : ms CPU keputusan per tick, us per NPC per tick, ms wall per tick, memori terkelola
  perilaku  : makan, racun, kena predator, keberhasilan kabur & latensi, zona bahaya
  variasi   : entropi aksi, indeks kelurusan jalur, sebaran sudut belok, CV antar-individu
  sifat     : perbedaan antar tipe NPC (eta^2) untuk metrik kunci
  belajar   : waktu dekat zona bahaya sebelum vs sesudah pertama kali disakiti di sana

Pemakaian: python tools/analysis/analyze_bench.py
"""
import json
import re
from pathlib import Path

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np
import pandas as pd

ROOT = Path(__file__).parents[2]
BENCH = ROOT / "bench_out"
REPORT = ROOT / "reports"
FIG = REPORT / "figures"

# palet referensi (dataviz skill, slot 1-4, mode terang) + token teks/permukaan
COLORS = {"fsm": "#2a78d6", "brain": "#eb6834", "brain_lod": "#1baf7a"}
TYPE_COLORS = ["#2a78d6", "#eb6834", "#1baf7a", "#eda100"]
LABELS = {"fsm": "FSM (tradisional)", "brain": "Otak lalat", "brain_lod": "Otak lalat + LOD"}
SURFACE, TEXT, TEXT2, GRID = "#fcfcfb", "#0b0b0b", "#52514e", "#e4e3df"
DANGER = (-4.0, -2.0, 3.5)
NEAR_DANGER = 7.0

plt.rcParams.update({
    "figure.facecolor": SURFACE, "axes.facecolor": SURFACE, "savefig.facecolor": SURFACE,
    "axes.edgecolor": GRID, "axes.labelcolor": TEXT2, "xtick.color": TEXT2, "ytick.color": TEXT2,
    "text.color": TEXT, "axes.grid": True, "grid.color": GRID, "grid.linewidth": 0.6,
    "axes.spines.top": False, "axes.spines.right": False, "font.size": 10, "lines.linewidth": 2,
})


def load_runs():
    runs = []
    for d in sorted(BENCH.glob("*_n*_s*")):
        m = re.match(r"(brain_lod|brain|fsm)_n(\d+)_s(\d+)$", d.name)
        if not m or not (d / "npcs.csv").exists():
            continue
        runs.append(dict(method=m[1], n=int(m[2]), seed=int(m[3]), dir=d))
    return runs


def entropy(row):
    t = row[["act_walk", "act_turn", "act_eat", "act_escape", "act_poison"]].to_numpy(float)
    p = t[t > 0] / t.sum()
    return float(-(p * np.log2(p)).sum())


def path_metrics(path):
    """Per NPC: kelurusan jalur (jendela 5 s), sebaran laju belok, waktu dekat zona bahaya sebelum/sesudah."""
    out = {}
    for nid, p in path.groupby("id"):
        x, z, t = p.x.to_numpy(), p.z.to_numpy(), p.t.to_numpy()
        step = np.hypot(np.diff(x), np.diff(z))
        heading = np.unwrap(np.arctan2(np.diff(z), np.diff(x)))
        moving = step > 0.05
        turn = np.diff(heading)[moving[1:] & moving[:-1]] / 0.1   # rad/s
        w = 50  # 5 s
        straight = []
        for i in range(0, len(x) - w, w):
            length = step[i:i + w].sum()
            if length > 1:
                straight.append(np.hypot(x[i + w] - x[i], z[i + w] - z[i]) / length)
        d = np.hypot(x - DANGER[0], z - DANGER[1])
        inside = d < DANGER[2]
        first = np.argmax(inside) if inside.any() else None
        rec = dict(straightness=np.mean(straight) if straight else np.nan,
                   turn_std=np.std(turn) if len(turn) > 10 else np.nan,
                   turn_kurtosis=(pd.Series(turn).kurt() if len(turn) > 10 else np.nan))
        if first is not None and first > 50 and len(x) - first > 300:
            # keluar dari zona setelah masuk pertama
            after = np.argmax(~inside[first:]) + first
            rec["near_before"] = float((d[:first] < NEAR_DANGER).mean())
            rec["near_after"] = float((d[after:] < NEAR_DANGER).mean())
        out[nid] = rec
    return pd.DataFrame.from_dict(out, orient="index")


def eta_squared(df, col):
    g = df.groupby("type")[col]
    grand = df[col].mean()
    ss_b = sum(len(v) * (v.mean() - grand) ** 2 for _, v in g)
    ss_t = ((df[col] - grand) ** 2).sum()
    return float(ss_b / ss_t) if ss_t > 0 else 0.0


def main():
    runs = load_runs()
    if not runs:
        raise SystemExit("bench_out kosong: jalankan run_benchmarks.py dulu")
    FIG.mkdir(parents=True, exist_ok=True)
    rt_rows, npc_frames, att_frames, paths_for_plot = [], [], [], {}
    for r in runs:
        ticks = pd.read_csv(r["dir"] / "ticks.csv").iloc[60:]   # buang 1 s pertama (pemanasan)
        rt_rows.append(dict(method=r["method"], n=r["n"], seed=r["seed"],
                            cpu_ms=ticks.decide_cpu_ms.mean(), wall_ms=ticks.decide_wall_ms.mean(),
                            wall_p95=ticks.decide_wall_ms.quantile(0.95), mem_mb=ticks.managed_mb.max(),
                            us_per_npc=ticks.decide_cpu_ms.mean() * 1000 / r["n"]))
        npcs = pd.read_csv(r["dir"] / "npcs.csv")
        path = pd.read_csv(r["dir"] / "path.csv")
        pm = path_metrics(path)
        npcs = npcs.join(pm, on="id")
        npcs["entropy"] = npcs.apply(entropy, axis=1)
        npcs["method"] = r["method"]; npcs["n_run"] = r["n"]; npcs["seed"] = r["seed"]
        npc_frames.append(npcs)
        att = pd.read_csv(r["dir"] / "attacks.csv")
        att["method"] = r["method"]; att["n"] = r["n"]
        att_frames.append(att)
        if r["n"] == 10 and r["seed"] == 1:
            paths_for_plot[r["method"]] = path
    rt = pd.DataFrame(rt_rows)
    npcs = pd.concat(npc_frames, ignore_index=True)
    att = pd.concat(att_frames, ignore_index=True)

    # ---------------- ringkasan ----------------
    summary = {"runtime": {}, "behavior": {}, "variability": {}, "traits": {}, "learning": {}}
    rt_g = rt.groupby(["method", "n"]).agg(cpu_ms=("cpu_ms", "mean"), wall_ms=("wall_ms", "mean"),
                                           wall_p95=("wall_p95", "mean"), us_per_npc=("us_per_npc", "mean"),
                                           mem_mb=("mem_mb", "mean")).reset_index()
    summary["runtime"] = rt_g.round(4).to_dict(orient="records")
    for m, g in npcs.groupby("method"):
        a = att[att.method == m]
        summary["behavior"][m] = dict(
            food_s=g.food_s.mean(), ate_frac=(g.food_s > 0).mean(), poison_bites=g.poison_bites.mean(),
            predator_hits=g.predator_hits.mean(), danger_entries=g.danger_entries.mean(),
            time_in_danger=g.time_in_danger.mean(), distance=g.distance.mean(),
            escape_success=float(((a.latency >= 0) & (a.hit == 0)).mean()),
            escape_latency=float(a.latency[a.latency >= 0].mean()))
        summary["variability"][m] = dict(
            action_entropy=g.entropy.mean(), straightness=g.straightness.mean(),
            turn_std=g.turn_std.mean(), turn_kurtosis=g.turn_kurtosis.mean(),
            cv_distance=float(g.groupby(["n_run", "seed", "type"]).distance.agg(lambda s: s.std() / s.mean()).mean()),
            cv_food=float(g.groupby(["n_run", "seed", "type"]).food_s.agg(lambda s: s.std() / s.mean() if s.mean() > 0 else np.nan).mean()))
        summary["traits"][m] = {
            col: dict(eta2=eta_squared(g, col), per_type=g.groupby("type")[col].mean().round(3).to_dict())
            for col in ["escapes", "food_s", "distance", "predator_hits", "turn_std"]}
        lg = g.dropna(subset=["near_before", "near_after"])
        summary["learning"][m] = dict(n_npc=int(len(lg)), near_before=float(lg.near_before.mean()) if len(lg) else None,
                                      near_after=float(lg.near_after.mean()) if len(lg) else None)

    def clean(o):
        if isinstance(o, dict): return {k: clean(v) for k, v in o.items()}
        if isinstance(o, list): return [clean(v) for v in o]
        if isinstance(o, (np.floating, float)): return None if np.isnan(o) else round(float(o), 4)
        if isinstance(o, np.integer): return int(o)
        return o
    summary = clean(summary)
    (REPORT / "bench_summary.json").write_text(json.dumps(summary, indent=2, ensure_ascii=False))

    # ---------------- grafik ----------------
    methods = [m for m in ("fsm", "brain", "brain_lod") if m in rt.method.unique()]

    fig, axes = plt.subplots(1, 2, figsize=(10, 4))
    for m in methods:
        g = rt_g[rt_g.method == m]
        axes[0].plot(g.n, g.wall_ms, marker="o", ms=6, color=COLORS[m], label=LABELS[m])
        axes[1].plot(g.n, g.us_per_npc, marker="o", ms=6, color=COLORS[m], label=LABELS[m])
        axes[0].annotate(f"{g.wall_ms.iloc[-1]:.2f} ms", (g.n.iloc[-1], g.wall_ms.iloc[-1]), textcoords="offset points", xytext=(6, 0), color=TEXT2, fontsize=8, va="center")
        axes[1].annotate(f"{g.us_per_npc.iloc[-1]:.1f} µs", (g.n.iloc[-1], g.us_per_npc.iloc[-1]), textcoords="offset points", xytext=(6, 0), color=TEXT2, fontsize=8, va="center")
    axes[0].set(title="Waktu keputusan AI per tick (wall, paralel)", xlabel="Jumlah NPC", ylabel="ms / tick")
    axes[1].set(title="Biaya CPU per NPC per tick", xlabel="Jumlah NPC", ylabel="µs CPU / NPC / tick", yscale="log")
    axes[0].axhline(16.7, color=TEXT2, lw=1, ls="--"); axes[0].text(rt.n.min(), 16.7, " anggaran 1 frame @60 FPS", color=TEXT2, fontsize=8, va="bottom")
    axes[0].legend(frameon=False)
    fig.tight_layout(); fig.savefig(FIG / "runtime.png", dpi=160); plt.close(fig)

    beh = pd.DataFrame(summary["behavior"]).T.loc[methods]
    keys = [("ate_frac", "NPC yang makan (fraksi)"), ("escape_success", "Serangan dihindari (fraksi)"),
            ("predator_hits", "Kena predator / NPC"), ("time_in_danger", "Detik di zona bahaya / NPC")]
    fig, axes = plt.subplots(1, 4, figsize=(12, 3.2))
    for ax, (k, title) in zip(axes, keys):
        vals = beh[k].astype(float)
        bars = ax.bar(range(len(methods)), vals, color=[COLORS[m] for m in methods], width=0.6, edgecolor=SURFACE, linewidth=2)
        for b, v in zip(bars, vals):
            ax.text(b.get_x() + b.get_width() / 2, v, f"{v:.2f}", ha="center", va="bottom", fontsize=8, color=TEXT2)
        ax.set_xticks(range(len(methods)), [LABELS[m].split(" (")[0] for m in methods], fontsize=8)
        ax.set_title(title, fontsize=9)
    fig.tight_layout(); fig.savefig(FIG / "behavior.png", dpi=160); plt.close(fig)

    # perbedaan antar tipe (sifat) - otak vs FSM
    types = sorted(npcs.type.unique())
    fig, axes = plt.subplots(1, 3, figsize=(12, 3.4))
    for ax, (col, title) in zip(axes, [("escapes", "Kejadian kabur / NPC"), ("food_s", "Detik makan / NPC"), ("turn_std", "Sebaran laju belok (rad/s)")]):
        width = 0.8 / len(types)
        for i, ty in enumerate(types):
            vals = [npcs[(npcs.method == m) & (npcs.type == ty)][col].mean() for m in ("fsm", "brain")]
            ax.bar(np.arange(2) + (i - (len(types) - 1) / 2) * width, vals, width=width * 0.9, color=TYPE_COLORS[i], label=ty, edgecolor=SURFACE, linewidth=1)
        ax.set_xticks(range(2), [LABELS["fsm"].split(" (")[0], LABELS["brain"]])
        ax.set_title(title, fontsize=9)
    axes[0].legend(frameon=False, fontsize=8)
    fig.tight_layout(); fig.savefig(FIG / "traits.png", dpi=160); plt.close(fig)

    # distribusi laju belok (naturalness)
    fig, ax = plt.subplots(figsize=(6, 3.4))
    for m in ("fsm", "brain"):
        if m not in paths_for_plot: continue
        p = paths_for_plot[m]
        tr = []
        for _, q in p.groupby("id"):
            h = np.unwrap(np.arctan2(np.diff(q.z), np.diff(q.x)))
            st = np.hypot(np.diff(q.x), np.diff(q.z))
            mv = (st[1:] > 0.05) & (st[:-1] > 0.05)
            tr.append(np.diff(h)[mv] / 0.1)
        tr = np.concatenate(tr)
        ax.hist(np.clip(tr, -6, 6), bins=60, density=True, histtype="step", lw=2, color=COLORS[m], label=LABELS[m])
    ax.set(title="Distribusi laju belok (N=10, seed 1)", xlabel="rad/s", ylabel="densitas", yscale="log")
    ax.legend(frameon=False)
    fig.tight_layout(); fig.savefig(FIG / "turn_distribution.png", dpi=160); plt.close(fig)

    # contoh lintasan
    fig, axes = plt.subplots(1, 2, figsize=(10, 5))
    for ax, m in zip(axes, ("fsm", "brain")):
        if m not in paths_for_plot: continue
        p = paths_for_plot[m]
        ax.add_patch(plt.Circle(DANGER[:2], DANGER[2], color="#e34948", alpha=0.15))
        for i, (_, q) in enumerate(p.groupby("id")):
            q = q[q.t < 60]
            ax.plot(q.x, q.z, lw=1, color=COLORS[m], alpha=0.35 + 0.06 * (i % 10))
        ax.set(title=f"{LABELS[m]}: lintasan 60 s pertama", xlim=(-20, 20), ylim=(-20, 20), aspect="equal")
    fig.tight_layout(); fig.savefig(FIG / "trajectories.png", dpi=160); plt.close(fig)

    write_markdown(summary, rt_g, methods)
    print(f"-> {REPORT / 'bench_summary.md'} ; grafik di {FIG}")


def write_markdown(s, rt_g, methods):
    L = ["# Ringkasan benchmark", "", "Dihasilkan otomatis oleh `tools/analysis/analyze_bench.py`.", "",
         "## Runtime (rata-rata seed)", "", "| Metode | N | ms wall/tick | p95 | ms CPU/tick | µs CPU/NPC/tick | memori MB |", "|---|---:|---:|---:|---:|---:|---:|"]
    for _, r in rt_g.iterrows():
        L.append(f"| {LABELS[r.method]} | {r.n} | {r.wall_ms:.3f} | {r.wall_p95:.3f} | {r.cpu_ms:.3f} | {r.us_per_npc:.1f} | {r.mem_mb:.0f} |")
    L += ["", "## Perilaku (rata-rata per NPC, semua run)", "", "| Metrik | " + " | ".join(LABELS[m] for m in methods) + " |", "|---|" + "---:|" * len(methods)]
    for k in s["behavior"][methods[0]]:
        L.append(f"| {k} | " + " | ".join(f"{s['behavior'][m][k]}" for m in methods) + " |")
    L += ["", "## Variasi perilaku", "", "| Metrik | " + " | ".join(LABELS[m] for m in methods) + " |", "|---|" + "---:|" * len(methods)]
    for k in s["variability"][methods[0]]:
        L.append(f"| {k} | " + " | ".join(f"{s['variability'][m][k]}" for m in methods) + " |")
    L += ["", "## Diferensiasi sifat antar tipe (eta², 0..1: seberapa besar tipe NPC menjelaskan variasi)", "",
          "| Metrik | " + " | ".join(LABELS[m] for m in methods) + " |", "|---|" + "---:|" * len(methods)]
    for k in s["traits"][methods[0]]:
        L.append(f"| {k} | " + " | ".join(f"{s['traits'][m][k]['eta2']}" for m in methods) + " |")
    L += ["", "Rata-rata per tipe:", ""]
    for m in methods:
        L.append(f"- **{LABELS[m]}**: " + "; ".join(f"{k}: {v['per_type']}" for k, v in s["traits"][m].items() if k in ("escapes", "food_s")))
    L += ["", "## Belajar (zona bahaya)", "", f"Fraksi waktu dalam {NEAR_DANGER} m dari zona, sebelum masuk pertama vs sesudah keluar:", ""]
    for m in methods:
        v = s["learning"][m]
        L.append(f"- **{LABELS[m]}**: n={v['n_npc']} NPC, sebelum {v['near_before']}, sesudah {v['near_after']}")
    L += ["", "## Grafik", "", "![runtime](figures/runtime.png)", "![perilaku](figures/behavior.png)",
          "![sifat](figures/traits.png)", "![belok](figures/turn_distribution.png)", "![lintasan](figures/trajectories.png)", ""]
    (REPORT / "bench_summary.md").write_text("\n".join(L), encoding="utf-8")


if __name__ == "__main__":
    main()
