# CSV PipeReader paired A/B probe

This opt-in probe compares two independently built HeroParser assemblies in the **same process**. Build the baseline from the commit being compared (for example, in a separate Git worktree) with a distinct assembly name:

```sh
dotnet build src/HeroParser/HeroParser.csproj -c Release -f net10.0 -p:AssemblyName=HeroParser.Baseline
```

Then run the probe from the candidate worktree, passing the absolute path to that baseline DLL:

```sh
dotnet run -c Release --project benchmarks/CsvPipeABProbe -p:BaselineDll=BASELINE_DLL_PATH
```

The probe alternates baseline-first and candidate-first pairs, warms up both implementations, and checks the parsed cell count. Each sample reads 100,000 unquoted rows ten times, for four and eight columns. It reports the candidate/baseline elapsed-time ratio; values below 1 favor the candidate. Repeat the run, especially on laptops with heterogeneous cores or changing power limits. Set `HERO_PARSER_AB_AFFINITY` to a hexadecimal CPU affinity mask if pinning to one core is useful (for example, `10` for the fifth logical processor on Windows).

Do not replace the baseline with a wrapper around the candidate's slow path: that adds an extra async state machine and can exaggerate the improvement.
