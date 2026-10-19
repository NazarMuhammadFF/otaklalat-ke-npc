"""Loader data FlyWire v783 (snapshot publik).

Sumber (lihat README.md di folder ini):
- Connectivity_783.parquet, Completeness_783.csv : Shiu et al. 2024 (github.com/philshiu/Drosophila_brain_model)
- neuron_annotations.tsv : Schlegel et al. 2024 (github.com/flyconnectome/flywire_annotations)
"""
from dataclasses import dataclass
from pathlib import Path

import numpy as np
import pandas as pd
import scipy.sparse as sp

RAW = Path(__file__).parent / "raw"


@dataclass
class Brain:
    ids: np.ndarray          # flywire root_id per indeks neuron (urutan Completeness_783)
    ann: pd.DataFrame        # anotasi, di-index menurut indeks neuron (bisa NaN)
    W: sp.csr_matrix         # W[pre, post] = jumlah sinapsis bertanda (+ eksitasi, - inhibisi)

    @property
    def n(self):
        return len(self.ids)

    def idx_of(self, root_ids):
        lut = pd.Series(np.arange(self.n), index=self.ids)
        return lut.reindex(root_ids).dropna().astype(int).to_numpy()

    def select(self, cell_type=None, cell_class=None, super_class=None, side=None, regex=False):
        m = np.ones(self.n, dtype=bool)
        for col, val in (("cell_type", cell_type), ("cell_class", cell_class), ("super_class", super_class)):
            if val is None:
                continue
            s = self.ann[col].fillna("")
            if regex:
                m &= s.str.contains(val, regex=True).to_numpy()
            elif isinstance(val, (list, tuple, set)):
                m &= s.isin(val).to_numpy()
            else:
                m &= (s == val).to_numpy()
        if side is not None:
            m &= (self.ann["side"].fillna("") == side).to_numpy()
        return np.flatnonzero(m)


def load(raw: Path = RAW) -> Brain:
    comp = pd.read_csv(raw / "Completeness_783.csv", index_col=0)
    ids = comp.index.to_numpy(dtype=np.int64)
    con = pd.read_parquet(raw / "Connectivity_783.parquet",
                          columns=["Presynaptic_Index", "Postsynaptic_Index", "Excitatory x Connectivity"])
    n = len(ids)
    W = sp.csr_matrix((con["Excitatory x Connectivity"].to_numpy(np.float32),
                       (con["Presynaptic_Index"].to_numpy(), con["Postsynaptic_Index"].to_numpy())),
                      shape=(n, n))
    ann = pd.read_csv(raw / "neuron_annotations.tsv", sep="\t", low_memory=False,
                      usecols=["root_id", "super_class", "cell_class", "cell_sub_class", "cell_type",
                               "hemibrain_type", "top_nt", "side"])
    ann = ann.drop_duplicates("root_id").set_index("root_id").reindex(ids).reset_index(drop=True)
    return Brain(ids=ids, ann=ann, W=W)
