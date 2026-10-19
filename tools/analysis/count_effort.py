"""Hitung biaya authoring perilaku: otak lalat vs FSM -> reports/authoring_effort.{json,md}

Dibedakan:
  per tipe NPC  : jumlah parameter yang harus ditentukan desainer per tipe (.tres)
  logika perilaku: LOC, state, transisi, ambang/timer yang ditulis tangan untuk perilaku
  infrastruktur : kode bersama (tubuh, dunia, indera) TIDAK dihitung karena sama untuk keduanya
"""
import json
import re
from pathlib import Path

ROOT = Path(__file__).parents[2]
NON_PARAM = {"script", "DisplayName", "BodyColor"}


def loc(path):
    n = 0
    in_block = False
    for line in Path(path).read_text(encoding="utf-8").splitlines():
        s = line.strip()
        if in_block:
            in_block = "*/" not in s
            continue
        if not s or s.startswith("//") or s in ("{", "}", "};"):
            continue
        if s.startswith("/*"):
            in_block = "*/" not in s
            continue
        n += 1
    return n


def tres_params(path):
    keys = [l.split("=")[0].strip() for l in Path(path).read_text(encoding="utf-8").splitlines()
            if "=" in l and not l.startswith("[")]
    return [k for k in keys if k not in NON_PARAM]


def main():
    brain_types = sorted((ROOT / "data/npc_types/brain").glob("*.tres"))
    fsm_types = sorted((ROOT / "data/npc_types/fsm").glob("*.tres"))
    fsm_src = (ROOT / "scripts/Npc/BaselineNpc.cs").read_text(encoding="utf-8")
    fsm_states = len(re.search(r"enum State \{([^}]*)\}", fsm_src)[1].split(","))
    fsm_transitions = len(re.findall(r"Current = State\.\w+", fsm_src))
    fsm_conditions = len(re.findall(r"\bif\b|\belse if\b|\?", fsm_src))

    brain_glue = ["scripts/Brain/Core/SensorEncoder.cs", "scripts/Brain/Core/MotorDecoder.cs"]
    brain_engine = ["scripts/Brain/Core/LifSimulator.cs", "scripts/Brain/Core/Connectome.cs", "scripts/Brain/Core/Plasticity.cs",
                    "scripts/Brain/Core/FlyBrain.cs", "scripts/Brain/Core/Rng.cs", "scripts/Brain/Core/Traits.cs"]
    brain_pipeline = ["tools/connectome/flywire.py", "tools/connectome/extract_subcircuits.py", "tools/connectome/lif.py"]
    res = {
        "brain": {
            "params_per_type": len(tres_params(brain_types[0])),
            "types": {p.stem: len(tres_params(p)) for p in brain_types},
            "behavior_logic_loc": 0,
            "behavior_states": 0, "behavior_transitions": 0,
            "glue_loc (encoder/decoder, sekali untuk semua tipe)": sum(loc(ROOT / f) for f in brain_glue),
            "engine_loc (generik, dapat dipakai ulang)": sum(loc(ROOT / f) for f in brain_engine),
            "pipeline_loc (offline)": sum(loc(ROOT / f) for f in brain_pipeline),
        },
        "fsm": {
            "params_per_type": len(tres_params(fsm_types[0])),
            "types": {p.stem: len(tres_params(p)) for p in fsm_types},
            "behavior_logic_loc": loc(ROOT / "scripts/Npc/BaselineNpc.cs") + loc(ROOT / "scripts/Npc/FsmProfile.cs"),
            "behavior_states": fsm_states, "behavior_transitions": fsm_transitions, "behavior_conditions": fsm_conditions,
        },
    }
    out = ROOT / "reports"
    out.mkdir(exist_ok=True)
    (out / "authoring_effort.json").write_text(json.dumps(res, indent=2, ensure_ascii=False))
    b, f = res["brain"], res["fsm"]
    md = f"""# Biaya authoring perilaku

| | Otak lalat | FSM |
|---|---:|---:|
| Parameter per tipe NPC | {b['params_per_type']} | {f['params_per_type']} |
| Total parameter untuk {len(brain_types)} tipe | {b['params_per_type'] * len(brain_types)} | {f['params_per_type'] * len(fsm_types)} |
| LOC logika perilaku tulisan tangan | {b['behavior_logic_loc']} | {f['behavior_logic_loc']} |
| State / transisi eksplisit | 0 / 0 | {f['behavior_states']} / {f['behavior_transitions']} |
| Kondisi (if/ternary) di logika perilaku | 0 | {f['behavior_conditions']} |
| LOC lem indera/gerak (sekali, semua tipe) | {b['glue_loc (encoder/decoder, sekali untuk semua tipe)']} | (termasuk dalam logika) |
| LOC mesin simulasi generik | {b['engine_loc (generik, dapat dipakai ulang)']} | - |
| LOC pipeline data offline | {b['pipeline_loc (offline)']} | - |

Catatan: kode tubuh/indera/dunia (`NpcBase`, `World/*`) dipakai kedua metode dan tidak dihitung.
Menambah tipe NPC baru: otak = {b['params_per_type']} angka; FSM = {f['params_per_type']} angka, dan perilaku baru
(mis. belajar menghindari bau baru) membutuhkan state/transisi baru di FSM tetapi tidak pada otak.
"""
    (out / "authoring_effort.md").write_text(md, encoding="utf-8")
    print(md)


if __name__ == "__main__":
    main()
