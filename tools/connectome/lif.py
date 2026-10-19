"""Simulator LIF referensi (numpy), model Shiu et al. 2024 (Nature 634:210).

    dv/dt = (v0 - v + g) / t_mbr      (tidak diintegrasi saat refraktori)
    dg/dt = -g / tau
    spike: v > v_th -> v = v_rst, g = 0, refraktori t_rfc
    sinapsis: g_post += w_syn * n_sinapsis_bertanda, setelah delay t_dly
    input Poisson: v += w_syn * f_poi (praktis selalu memicu spike)

Skema diskret di sini SAMA PERSIS dengan scripts/Brain/LifSimulator.cs, sehingga
hasilnya bisa dipakai sebagai referensi unit test C#.
"""
import numpy as np
import scipy.sparse as sp

PARAMS = dict(v0=-52.0, v_rst=-52.0, v_th=-45.0, t_mbr=20.0, tau=5.0, t_rfc=2.2, t_dly=1.8,
              w_syn=0.275, f_poi=250.0)


def simulate(W, t_ms, rates_hz, dt=0.1, trials=1, seed=0, p=PARAMS, silence=None, stim_until_ms=None, count_from_ms=0.0):
    """W: csr (pre x post) jumlah sinapsis bertanda. rates_hz: array (n,) laju Poisson per neuron.

    stim_until_ms: input Poisson dimatikan setelah waktu ini (uji persistensi/latch).
    count_from_ms: spike hanya dihitung mulai waktu ini.
    Mengembalikan matriks jumlah spike (trials x n).
    """
    rng = np.random.default_rng(seed)
    n = W.shape[0]
    Ww = (W * p["w_syn"]).astype(np.float32).tocsr()
    if silence is not None and len(silence):
        Ww = Ww.tolil(); Ww[silence, :] = 0; Ww = Ww.tocsr()
    steps = int(round(t_ms / dt))
    D = max(1, int(round(p["t_dly"] / dt)))
    R = int(round(p["t_rfc"] / dt))
    decay_g = np.float32(np.exp(-dt / p["tau"]))
    k_v = np.float32(dt / p["t_mbr"])
    poi_w = np.float32(p["w_syn"] * p["f_poi"])
    rates = np.asarray(rates_hz, dtype=np.float32)
    driven = np.flatnonzero(rates > 0)
    p_spk = rates[driven] * dt * 1e-3

    v = np.full((trials, n), p["v0"], np.float32)
    g = np.zeros((trials, n), np.float32)
    rfc = np.zeros((trials, n), np.int32)
    is_poi = np.zeros(n, bool); is_poi[driven] = True   # target Poisson tanpa refraktori (seperti Shiu)
    buf = np.zeros((D, trials, n), np.float32)
    counts = np.zeros((trials, n), np.int64)

    for t in range(steps):
        slot = t % D
        g += buf[slot]; buf[slot] = 0
        active = rfc <= 0
        v = np.where(active, v + k_v * (p["v0"] - v + g), v)
        g = np.where(active, g * decay_g, g)
        if len(driven) and (stim_until_ms is None or t * dt < stim_until_ms):
            hit = rng.random((trials, len(driven))) < p_spk
            v[:, driven] += hit * poi_w
        spk = v > p["v_th"]
        rfc -= 1
        if spk.any():
            if t * dt >= count_from_ms:
                counts += spk
            v[spk] = p["v_rst"]; g[spk] = 0
            rfc[spk] = np.where(np.broadcast_to(is_poi, spk.shape)[spk], 0, R)
            S = sp.csr_matrix(spk.astype(np.float32))
            buf[slot] += (S @ Ww).toarray()   # tiba D langkah kemudian (slot ini dibaca lagi di t+D)
    return counts
