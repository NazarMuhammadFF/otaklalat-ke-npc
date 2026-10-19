"""Jalankan matriks benchmark headless: metode x jumlah NPC x seed (x LOD untuk otak).

Pemakaian:
  set GODOT=D:\\Ruang game\\Godot\\Godot_v4.7.2-stable_mono_win64\\Godot_v4.7.2-stable_mono_win64_console.exe
  python tools/analysis/run_benchmarks.py [--n 10 50 100 200] [--seeds 1 2 3] [--duration 120]

Keluaran: bench_out/<metode>[_lod]_n<N>_s<seed>/ (dibaca analyze_bench.py)
"""
import argparse
import os
import shutil
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).parents[2]
DEFAULT_GODOT = r"D:\Ruang game\Godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe"


def run(godot, method, n, seed, duration, lod):
    name = f"{method}{'_lod' if lod else ''}_n{n}_s{seed}"
    out = ROOT / "bench_out"
    target = out / name
    if (target / "npcs.csv").exists():
        print(f"  {name}: sudah ada, dilewati")
        return
    # run LOD ditulis ke folder sementara sendiri agar tidak menimpa hasil run tanpa LOD
    run_out = out / "_lod_tmp" if lod else out
    args = [godot, "--headless", "--path", str(ROOT), "--fixed-fps", "60", "res://scenes/Arena.tscn", "--",
            f"--bench={method}", f"--n={n}", f"--seed={seed}", f"--duration={duration}", f"--lod={1 if lod else 0}",
            f"--out={run_out}"]
    t0 = time.time()
    res = subprocess.run(args, capture_output=True, text=True, encoding="utf-8", errors="replace")
    produced = run_out / f"{method}_n{n}_s{seed}"
    if res.returncode != 0 or not (produced / "npcs.csv").exists():
        print(res.stdout[-2000:], res.stderr[-2000:])
        raise SystemExit(f"benchmark gagal: {name}")
    if lod:
        if target.exists():
            shutil.rmtree(target)
        produced.rename(target)
        shutil.rmtree(run_out, ignore_errors=True)
    print(f"  {name}: {time.time() - t0:.0f} s")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--n", type=int, nargs="+", default=[10, 50, 100, 200])
    ap.add_argument("--seeds", type=int, nargs="+", default=[1, 2, 3])
    ap.add_argument("--duration", type=float, default=120)
    ap.add_argument("--lod", action="store_true", help="juga jalankan otak dengan brain-LOD")
    args = ap.parse_args()
    godot = os.environ.get("GODOT", DEFAULT_GODOT)
    if not Path(godot).exists():
        sys.exit(f"Godot tidak ditemukan: {godot} (set variabel GODOT)")
    for n in args.n:
        for seed in args.seeds:
            for method in ("fsm", "brain"):
                run(godot, method, n, seed, args.duration, lod=False)
            if args.lod:
                run(godot, "brain", n, seed, args.duration, lod=True)


if __name__ == "__main__":
    main()
