"""Exploratory local replay of the isolated-worker command history.

Usage: python3 -I replay.py <runs-per-arm>   (see README.md for the build and environment)

Not an acceptance run. One fresh worker process per run, pinned to one CPU, replaying the
policy in benchmarks/CsvPipeABProbe/run-isolated.ps1 (pilot calibration to 100 ms, warmup of
>=10 batches and >=10 s, post-warmup calibration to 125 ms, 30 measured batches) for each
transport in the original order. Records per-parse milliseconds for every measured batch and the
JIT summary (tier + code size per method) so code shape can be compared with timing.
"""
import hashlib
import json
import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
# Output (runs.ndjson, jit/) goes to STUDY_OUT so the repository stays clean; the worker build
# defaults to ./bin and can be overridden with STUDY_WORKER_DIR.
OUT = os.environ.get("STUDY_OUT", HERE)
WORKER = os.path.join(os.environ.get("STUDY_WORKER_DIR", os.path.join(HERE, "bin")), "CsvPipeABProbe.dll")
CPU = os.environ.get("STUDY_CPU", "2")
ARMS = {
    "default": {},
    "nopgo": {"DOTNET_TieredPGO": "0"},
}
POLICY = "replay-policy-v1"
# Runtime/JIT knobs inherited from the caller would silently change the "default" arm, so every
# DOTNET_*, COMPlus_* and CORECLR_* variable is dropped except these host-location and CLI
# settings. The names that were dropped are recorded with every process.
KEPT_ENV = {
    "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT_X86", "DOTNET_ROOT_ARM64",
    "DOTNET_CLI_TELEMETRY_OPTOUT", "DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "DOTNET_NOLOGO",
    "DOTNET_MULTILEVEL_LOOKUP",
}
RUNTIME_PREFIXES = ("DOTNET_", "COMPlus_", "CORECLR_")


def clean_environment():
    """Return (environment, sorted names of dropped runtime variables)."""
    env, dropped = {}, []
    for name, value in os.environ.items():
        if name.startswith(RUNTIME_PREFIXES) and name not in KEPT_ENV:
            dropped.append(name)
        else:
            env[name] = value
    return env, sorted(dropped)


def fingerprint():
    """Identity of everything that must be identical for rows to be comparable."""
    worker_dir = os.path.dirname(WORKER)
    hashes = {}
    for name in ("CsvPipeABProbe.dll", "HeroParser.dll", "CsvPipeABModels.dll"):
        with open(os.path.join(worker_dir, name), "rb") as f:
            hashes[name] = hashlib.sha256(f.read()).hexdigest()
    payload = {"policy": POLICY, "cpu": CPU, "arms": ARMS, "dlls": hashes}
    return hashlib.sha256(json.dumps(payload, sort_keys=True).encode()).hexdigest()


def run_one(arm, index, out, print_fingerprint, known_runtimes):
    jit = os.path.join(OUT, "jit", f"{arm}-{index:03d}.txt")
    os.makedirs(os.path.dirname(jit), exist_ok=True)
    if os.path.exists(jit):
        os.remove(jit)
    env, dropped = clean_environment()
    env.update({
        "HERO_PARSER_WORKER_PROTOCOL": "csv-pipe-isolated-v3",
        "DOTNET_JitStdOutFile": jit,
        "DOTNET_JitDisasmSummary": "1",
    })
    env.update(ARMS[arm])
    proc = subprocess.Popen(
        ["taskset", "--cpu-list", CPU, "dotnet", WORKER, "--rows", "2000"],
        stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, env=env)
    seq = 0

    def read():
        line = proc.stdout.readline()
        if not line:
            raise RuntimeError("worker exited: " + proc.stderr.read()[:500])
        return json.loads(line)

    def req(op, transport="", repeats=0):
        nonlocal seq
        seq += 1
        proc.stdin.write(json.dumps({"Id": seq, "Operation": op, "Transport": transport, "Repeats": repeats}) + "\n")
        proc.stdin.flush()
        r = read()
        if r["Id"] != seq or r["pid"] != proc.pid:
            raise RuntimeError("mismatched response")
        return r

    def calibrate(transport, repeats, target):
        while True:
            b = req("batch", transport, repeats)
            if b["batchMs"] >= target:
                return repeats
            if repeats >= 65536:
                raise RuntimeError("calibration cap")
            repeats = min(repeats * 2, 65536)

    env0 = read()
    known_runtimes.add(env0["runtime"])
    if len(known_runtimes) != 1:
        proc.kill()
        raise SystemExit(f"Runtime changed within one data set: {sorted(known_runtimes)}")
    started = time.time()
    record = {"arm": arm, "index": index, "pid": proc.pid, "cpu": CPU, "runtime": env0["runtime"],
              "processors": env0["processors"], "fingerprint": print_fingerprint,
              "droppedRuntimeEnv": dropped, "armEnv": ARMS[arm], "transports": {}}
    try:
        req("verify")
        for transport in ("Contiguous", "Segmented128", "Stream4096"):
            req("prepare", transport)
            repeats = calibrate(transport, 1, 100)
            warm_start = time.time()
            warmed = 0
            while warmed < 10 or time.time() - warm_start < 10:
                req("batch", transport, repeats)
                warmed += 1
            repeats = calibrate(transport, repeats, 125)
            samples = []
            for _ in range(30):
                b = req("batch", transport, repeats)
                samples.append(b["measurement"]["Milliseconds"])
            record["transports"][transport] = {"repeats": repeats, "warmed": warmed, "perParseMs": samples}
        req("stop")
        proc.wait(timeout=30)
        record["exit"] = proc.returncode
    finally:
        if proc.poll() is None:
            proc.kill()
        proc.wait()
    record["seconds"] = round(time.time() - started, 1)
    record["jitFile"] = os.path.basename(jit)
    out.write(json.dumps(record) + "\n")
    out.flush()


def main():
    runs = int(sys.argv[1])
    arms = os.environ.get("STUDY_ARMS", "default,nopgo").split(",")
    if not arms or any(a not in ARMS for a in arms):
        raise SystemExit("STUDY_ARMS must list arms from: " + ", ".join(ARMS))
    path = os.path.join(OUT, "runs.ndjson")
    current = fingerprint()
    done, known_runtimes = set(), set()
    if os.path.exists(path):
        for line in open(path):
            r = json.loads(line)
            if r.get("fingerprint") != current:
                raise SystemExit("Existing data in STUDY_OUT has a different (or missing) fingerprint: "
                                 "the worker build, CPU, arms or policy changed. Use a fresh STUDY_OUT.")
            done.add((r["arm"], r["index"]))
            known_runtimes.add(r["runtime"])
    os.makedirs(OUT, exist_ok=True)
    with open(path, "a") as out:
        for i in range(runs):
            # interleave arms with alternating order so drift hits both equally
            order = tuple(arms) if i % 2 == 0 else tuple(reversed(arms))
            for arm in order:
                if (arm, i) in done:
                    continue
                run_one(arm, i, out, current, known_runtimes)
                seg = json.loads(open(path).read().splitlines()[-1])["transports"]["Segmented128"]["perParseMs"]
                print(f"run {i:03d} {arm:8s} seg median {sorted(seg)[15]:.4f} ms", flush=True)


if __name__ == "__main__":
    main()
