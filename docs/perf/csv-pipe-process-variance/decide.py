"""Predeclared decision rule for the CI process-variance experiment.

The thresholds and the order of the checks below are fixed BEFORE the experiment is dispatched
and are not inputs of the workflow. A valid run always ends in exactly one outcome; only an
invalid run (incomplete or malformed data) exits non-zero. Nothing here relaxes, waives or
replaces the failed timing gate in benchmarks.yml.

Usage: python3 -I decide.py [--expected N]   (STUDY_OUT selects the data directory)
"""
import json
import os
import statistics as st
import sys

HERE = os.environ.get("STUDY_OUT", os.path.dirname(os.path.abspath(__file__)))

# ---- predeclared constants -------------------------------------------------------------
EXPECTED_PROCESSES = 20      # fresh `default`-arm processes, one dispatch, no retry
BATCHES_PER_TRANSPORT = 30
MAX_NOISE = 1.30             # median over processes of max/min of the 30 Segmented128 batches
IRRELEVANT_SPREAD = 0.03     # p90/p10 - 1 of per-process minima: at most this => shape irrelevant
CAUSAL_SPREAD = 0.05         # p90/p10 - 1 of per-process minima: more than this is needed for causal
MIN_SHAPES = 4               # "irrelevant" needs MORE than 3 distinct (caller, reader) shapes
CAUSAL_RHO = 0.5             # |Spearman| of per-process minimum with caller or reader size
# -----------------------------------------------------------------------------------------

SEG = "Segmented128"


def quantile(values, p):
    s = sorted(values)
    return s[int(round((len(s) - 1) * p))]


def average_ranks(values):
    """1-based ranks with tied values sharing their average rank (order independent)."""
    order = sorted(range(len(values)), key=lambda i: values[i])
    ranks = [0.0] * len(values)
    i = 0
    while i < len(values):
        j = i
        while j + 1 < len(values) and values[order[j + 1]] == values[order[i]]:
            j += 1
        for k in range(i, j + 1):
            ranks[order[k]] = (i + j) / 2 + 1
        i = j + 1
    return ranks


def spearman(a, b):
    """Spearman rho: Pearson correlation of average ranks, so ties cannot make it order dependent."""
    ra, rb = average_ranks(a), average_ranks(b)
    n = len(a)
    ma, mb = sum(ra) / n, sum(rb) / n
    num = sum((x - ma) * (y - mb) for x, y in zip(ra, rb))
    den = (sum((x - ma) ** 2 for x in ra) * sum((y - mb) ** 2 for y in rb)) ** 0.5
    return num / den if den else 0.0

def invalid(reason, details):
    result = {"decision": "invalid", "reason": reason, "details": details}
    return result, 2


def evaluate(rows, summ, expected):
    if len(rows) != expected:
        return invalid("process-count", f"{len(rows)} of {expected} processes completed")
    if len(summ) != len(rows):
        return invalid("summary-mismatch", "summary.json does not match runs.ndjson")
    for r, s in zip(rows, summ):
        if r["arm"] != "default" or (r["arm"], r["index"]) != (s["arm"], s["index"]):
            return invalid("arm-or-order", f"unexpected arm/index at {r['arm']} {r['index']}")
        if r.get("exit") != 0:
            return invalid("worker-exit", f"process {r['index']} exited with {r.get('exit')}")
        for t in ("Contiguous", SEG, "Stream4096"):
            if len(r["transports"][t]["perParseMs"]) != BATCHES_PER_TRANSPORT:
                return invalid("batch-count", f"process {r['index']} {t} lacks {BATCHES_PER_TRANSPORT} batches")
        if s["callerFinal"] is None or s["readerFinal"] is None:
            return invalid("no-optimized-version", f"process {r['index']} has no optimized hot-method version")
    seg = [r["transports"][SEG]["perParseMs"] for r in rows]
    mins = [min(v) for v in seg]
    noise = st.median(max(v) / min(v) for v in seg)
    spread = quantile(mins, 0.9) / quantile(mins, 0.1) - 1
    shapes = {(s["callerFinal"], s["readerFinal"]) for s in summ}
    rho_caller = spearman(mins, [s["callerFinal"] for s in summ])
    rho_reader = spearman(mins, [s["readerFinal"] for s in summ])
    rho = max(abs(rho_caller), abs(rho_reader))
    metrics = {
        "processes": len(rows), "noiseMedianMaxOverMin": noise, "spreadP90OverP10Minus1": spread,
        "distinctShapes": len(shapes), "rhoCaller": rho_caller, "rhoReader": rho_reader, "maxAbsRho": rho,
        "minimaSorted": sorted(mins),
    }
    if noise > MAX_NOISE:
        decision = "runner-too-noisy-inconclusive"
    elif spread <= IRRELEVANT_SPREAD and len(shapes) >= MIN_SHAPES:
        decision = "shape-irrelevant-at-this-precision"
    elif spread > CAUSAL_SPREAD and rho >= CAUSAL_RHO:
        decision = "shape-plausibly-causal-candidate"
    else:
        decision = "inconclusive"
    return {"decision": decision, "metrics": metrics, "rule": {
        "MAX_NOISE": MAX_NOISE, "IRRELEVANT_SPREAD": IRRELEVANT_SPREAD, "CAUSAL_SPREAD": CAUSAL_SPREAD,
        "MIN_SHAPES": MIN_SHAPES, "CAUSAL_RHO": CAUSAL_RHO, "EXPECTED_PROCESSES": expected}}, 0


def main():
    expected = EXPECTED_PROCESSES
    if "--expected" in sys.argv:
        expected = int(sys.argv[sys.argv.index("--expected") + 1])
    rows = [json.loads(line) for line in open(os.path.join(HERE, "runs.ndjson"))]
    summ = json.load(open(os.path.join(HERE, "summary.json")))
    result, code = evaluate(rows, summ, expected)
    json.dump(result, open(os.path.join(HERE, "decision.json"), "w"), indent=1)
    print(json.dumps(result, indent=1))
    return code


if __name__ == "__main__":
    sys.exit(main())
