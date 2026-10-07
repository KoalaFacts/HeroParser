"""Statistics quoted in README.md, computed from runs.ndjson and summary.json.

Usage: python3 -I stats.py          (reads the files next to this script, or STUDY_OUT)
"""
import json
import os
import statistics as st

HERE = os.environ.get("STUDY_OUT", os.path.dirname(os.path.abspath(__file__)))
ARMS = ("default", "nopgo")
SEG = "Segmented128"


def load():
    rows = [json.loads(line) for line in open(os.path.join(HERE, "runs.ndjson"))]
    summ = json.load(open(os.path.join(HERE, "summary.json")))
    assert [(r["arm"], r["index"]) for r in rows] == [(s["arm"], s["index"]) for s in summ]
    return rows, summ


def quantile(values, p):
    s = sorted(values)
    return s[int(round((len(s) - 1) * p))]


def spearman(a, b):
    def rank(v):
        order = sorted(range(len(v)), key=lambda i: v[i])
        r = [0] * len(v)
        for k, i in enumerate(order):
            r[i] = k
        return r

    ra, rb = rank(a), rank(b)
    n = len(a)
    ma, mb = sum(ra) / n, sum(rb) / n
    num = sum((x - ma) * (y - mb) for x, y in zip(ra, rb))
    den = (sum((x - ma) ** 2 for x in ra) * sum((y - mb) ** 2 for y in rb)) ** 0.5
    return num / den if den else float("nan")


def samples(row, transport=SEG):
    return row["transports"][transport]["perParseMs"]


def main():
    rows, summ = load()
    by_arm = {arm: [(r, s) for r, s in zip(rows, summ) if r["arm"] == arm] for arm in ARMS}

    print("1. Code shape per process (final optimized tier of the two hot methods)")
    for arm in ARMS:
        combos = {(s["callerFinal"], s["readerFinal"]) for _, s in by_arm[arm]}
        callers = sorted({s["callerFinal"] for _, s in by_arm[arm]})
        readers = sorted({s["readerFinal"] for _, s in by_arm[arm]})
        counts = {}
        for _, s in by_arm[arm]:
            key = (s["callerFinal"], s["readerFinal"])
            counts[key] = counts.get(key, 0) + 1
        print(f"   {arm:8s} n={len(by_arm[arm])} distinct (caller, reader) = {len(combos)}; "
              f"most common combination covers {max(counts.values())}/{len(by_arm[arm])}")
        print(f"            caller sizes {callers}")
        print(f"            reader sizes {readers}")

    print("\n2. Noise floor: max/min of the 30 measured Segmented128 batches inside ONE process")
    for arm in ARMS:
        d = [max(samples(r)) / min(samples(r)) for r, _ in by_arm[arm]]
        print(f"   {arm:8s} median {st.median(d):.3f}  max {max(d):.3f}")

    print("\n3. Spread of Segmented128 across processes (max/min of a per-process statistic)")
    for arm in ARMS:
        for name, fn in (("min", min), ("p10", lambda v: quantile(v, 0.1)), ("median", st.median)):
            vals = [fn(samples(r)) for r, _ in by_arm[arm]]
            print(f"   {arm:8s} per-process {name:6s}: {min(vals):.3f} .. {max(vals):.3f}  max/min {max(vals) / min(vals):.3f}")

    print("\n4. Is a slow process slow on every transport? (Spearman across processes)")
    for arm in ARMS:
        g = lambda t: [st.median(samples(r, t)) for r, _ in by_arm[arm]]
        print(f"   {arm:8s} seg~contiguous {spearman(g(SEG), g('Contiguous')):+.2f}  "
              f"seg~stream {spearman(g(SEG), g('Stream4096')):+.2f}  "
              f"contiguous~stream {spearman(g('Contiguous'), g('Stream4096')):+.2f}")

    print("\n5. Does code shape explain speed? (default arm, Spearman with size, n=%d)" % len(by_arm["default"]))
    sel = by_arm["default"]
    for stat_name, fn in (("min", min), ("median", st.median)):
        vals = [fn(samples(r)) for r, _ in sel]
        print(f"   {stat_name:6s}: caller size {spearman(vals, [s['callerFinal'] for _, s in sel]):+.2f}  "
              f"reader size {spearman(vals, [s['readerFinal'] for _, s in sel]):+.2f}")
    mins = [min(samples(r)) for r, _ in sel]
    groups = {}
    for m, (_, s) in zip(mins, sel):
        groups.setdefault(s["callerFinal"], []).append(m)
    for size, vals in sorted(groups.items()):
        print(f"   caller={size}: n={len(vals)} median of per-process minima {st.median(vals):.3f} "
              f"range {min(vals):.3f}-{max(vals):.3f}")

    print("\n6. Dynamic PGO effect (per-process minima, paired by run index)")
    dm = [min(samples(r)) for r, _ in by_arm["default"]]
    nm = [min(samples(r)) for r, _ in by_arm["nopgo"]]
    slower = sum(1 for a, b in zip(dm, nm) if b > a)
    print(f"   median of minima: default {st.median(dm):.3f}  TieredPGO=0 {st.median(nm):.3f}  "
          f"ratio {st.median(nm) / st.median(dm):.3f};  TieredPGO=0 slower in {slower}/{len(dm)} pairs")


if __name__ == "__main__":
    main()
