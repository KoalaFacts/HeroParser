#!/usr/bin/env python3
"""Symbolize perf samples of a .NET process against its perf map, without perf's JIT support.

Inputs:
  map      /tmp/perf-<pid>.map written by DOTNET_PerfMapEnabled (lines: START SIZE NAME, hex)
  ips      one sample instruction pointer per line (hex), from `perf script -F ip`
  codedir  optional directory of <start>.bin code dumps taken from /proc/<pid>/mem while the process
           ran; a dump named for a method's start address enables a per-instruction listing via objdump

Output (stdout): a method-level histogram, then for the hottest methods a per-instruction listing
(when a code dump exists) or a per-offset histogram (when it does not).
"""
import bisect
import collections
import os
import subprocess
import sys


def load_map(path):
    entries = []
    with open(path, encoding="utf-8", errors="replace") as f:
        for line in f:
            parts = line.rstrip("\n").split(" ", 2)
            if len(parts) < 3:
                continue
            try:
                start = int(parts[0], 16)
                size = int(parts[1], 16)
            except ValueError:
                continue
            entries.append((start, size, parts[2]))
    entries.sort()
    return entries


def resolve(entries, starts, ip):
    i = bisect.bisect_right(starts, ip) - 1
    if i < 0:
        return None
    start, size, name = entries[i]
    if ip < start + size:
        return entries[i]
    return None


def main():
    map_path, ips_path = sys.argv[1], sys.argv[2]
    code_dir = sys.argv[3] if len(sys.argv) > 3 else None
    top_methods = int(sys.argv[4]) if len(sys.argv) > 4 else 6

    entries = load_map(map_path)
    starts = [e[0] for e in entries]

    per_symbol = collections.Counter()
    per_ip = collections.Counter()
    total = 0
    unknown = 0
    with open(ips_path, encoding="utf-8", errors="replace") as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            try:
                ip = int(line, 16)
            except ValueError:
                continue
            total += 1
            entry = resolve(entries, starts, ip)
            if entry is None:
                unknown += 1
                per_symbol["[not in perf map: native runtime, libc, kernel]"] += 1
                continue
            per_symbol[entry] += 1
            per_ip[(entry, ip)] += 1

    print(f"samples: {total}  in JIT code: {total - unknown}  elsewhere: {unknown}")
    print()
    print("=== methods (share of all samples)")
    for entry, count in per_symbol.most_common(40):
        name = entry if isinstance(entry, str) else entry[2]
        print(f"{100.0 * count / total:6.2f}%  {count:6d}  {name}")

    hot = [e for e, _ in per_symbol.most_common() if not isinstance(e, str)][:top_methods]
    for entry in hot:
        start, size, name = entry
        method_samples = per_symbol[entry]
        print()
        print(f"=== {name}")
        print(f"    start 0x{start:x} size 0x{size:x}  {method_samples} samples ({100.0 * method_samples / total:.2f}% of all)")
        offsets = collections.Counter()
        for (e, ip), c in per_ip.items():
            if e == entry:
                offsets[ip - start] += c

        dump = os.path.join(code_dir, f"{start:x}.bin") if code_dir else None
        if dump and os.path.exists(dump):
            listing = subprocess.run(
                ["objdump", "-D", "-b", "binary", "-m", "i386:x86-64", "-M", "intel", f"--adjust-vma={start}", dump],
                capture_output=True, text=True, check=False).stdout
            print("    per-instruction samples (% of this method), instructions with samples plus context:")
            lines = []
            for line in listing.splitlines():
                stripped = line.strip()
                if ":" not in stripped:
                    continue
                addr_text = stripped.split(":", 1)[0].strip()
                try:
                    addr = int(addr_text, 16)
                except ValueError:
                    continue
                c = offsets.get(addr - start, 0)
                lines.append((c, addr - start, stripped))
            # Print every instruction with a sample plus two instructions before and after it.
            keep = set()
            for i, (c, _, _) in enumerate(lines):
                if c > 0:
                    keep.update(range(max(0, i - 2), min(len(lines), i + 3)))
            last = -1
            for i in sorted(keep):
                if last >= 0 and i != last + 1:
                    print("    ...")
                c, off, text = lines[i]
                pct = 100.0 * c / method_samples if method_samples else 0.0
                marker = f"{pct:6.2f}%" if c else "       "
                print(f"    {marker}  +0x{off:04x}  {text.split(':', 1)[1].strip()[:110]}")
                last = i
        else:
            print("    per-offset samples (% of this method), no code dump for this method:")
            for off, c in sorted(offsets.items(), key=lambda kv: -kv[1])[:40]:
                print(f"    {100.0 * c / method_samples:6.2f}%  +0x{off:04x}")


if __name__ == "__main__":
    main()
