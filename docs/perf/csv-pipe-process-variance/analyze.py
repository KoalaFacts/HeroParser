"""Extract tier and code size of the two hot methods from each JIT summary log and write
summary.json next to runs.ndjson.  Needs the jit/ directory produced by replay.py."""
import json
import os
import re
import statistics as st
import sys

HERE = os.environ.get("STUDY_OUT", os.path.dirname(os.path.abspath(__file__)))
LINE = re.compile(r"JIT compiled (?P<m>.+?) \[(?P<tier>[^,\]]+?)(?: @0x[0-9a-f]+)?(?: with [^,\]]+)?, IL size=(?P<il>\d+), code size=(?P<code>\d+)\]")
TARGETS = {
    "caller": "<MoveNextSlowAsync>d__31:MoveNext()",
    "reader": "HeroParser.Csv:TryReadRow(",
}


def jit_shape(path):
    """Return {key: [(tier, code size), ...]} for the target methods, in compile order."""
    shape = {k: [] for k in TARGETS}
    for line in open(path, errors="replace"):
        m = LINE.search(line)
        if not m:
            continue
        for key, needle in TARGETS.items():
            if needle in m["m"]:
                shape[key].append((m["tier"], int(m["code"])))
    return shape


def final_optimized(versions):
    """Last fully optimized (non-OSR) version, falling back to last OSR, else None."""
    full = [c for t, c in versions if t.startswith("Tier1") and "OSR" not in t]
    if full:
        return full[-1]
    osr = [c for t, c in versions if "OSR" in t]
    return osr[-1] if osr else None


def main():
    rows = [json.loads(line) for line in open(os.path.join(HERE, "runs.ndjson"))]
    out = []
    for r in rows:
        shape = jit_shape(os.path.join(HERE, "jit", r["jitFile"]))
        t = r["transports"]
        seg = t["Segmented128"]["perParseMs"]
        out.append({
            "arm": r["arm"], "index": r["index"], "seconds": r["seconds"],
            "segMedian": st.median(seg), "segMin": min(seg), "segMax": max(seg),
            "contMedian": st.median(t["Contiguous"]["perParseMs"]),
            "streamMedian": st.median(t["Stream4096"]["perParseMs"]),
            "callerFinal": final_optimized(shape["caller"]),
            "readerFinal": final_optimized(shape["reader"]),
            "callerVersions": shape["caller"], "readerVersions": shape["reader"],
        })
    json.dump(out, open(os.path.join(HERE, "summary.json"), "w"), indent=1)
    for arm in ("default", "nopgo"):
        sel = [o for o in out if o["arm"] == arm]
        if not sel:
            continue
        seg = [o["segMedian"] for o in sel]
        print(f"\n== {arm}: n={len(sel)}  Segmented128 per-parse ms (process medians)")
        print(f"   min {min(seg):.4f}  median {st.median(seg):.4f}  max {max(seg):.4f}  max/min {max(seg)/min(seg):.4f}")
        print("   sorted:", " ".join(f"{x:.3f}" for x in sorted(seg)))
        shapes = {}
        for o in sel:
            shapes.setdefault((o["callerFinal"], o["readerFinal"]), []).append(o["segMedian"])
        for (c, rd), vals in sorted(shapes.items(), key=lambda kv: str(kv[0])):
            print(f"   caller={c} reader={rd}: n={len(vals)} seg median of medians {st.median(vals):.4f} range {min(vals):.4f}-{max(vals):.4f}")


if __name__ == "__main__":
    main()
