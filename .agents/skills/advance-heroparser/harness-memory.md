# Harness memory — HeroParser

Why each change to `advance-heroparser`, `verify-heroparser`, the gate or a friction-removing script was made, and whether it worked (agent-harness autonomous-dev §8.1). Organised by root cause. Read it whole every run; never delete an attempt.

Created 2026-11-26. Attempts before that date are retroactive: their predictions were written after the change.

## RC-1 Runs end early instead of using their budget
Status: open
Inherited: agent-harness RC-1 — firm progress rule 2 names two non-stops: too little time left (take a sub-step, push the started branch) and a hosted check still running after the local gate and review (prepare the next item while it runs). HeroParser was one of the tasks cited there (35-minute run on 2026-11-24 that stopped with "hosted CI on the new head is still queued or running" on #215).
- A1 · this PR · 2026-11-26 · claude-opus-5-5
  Change: none in the repo yet; this run applies the inherited rule (pushes #215's flake fix, then keeps working while hosted CI runs). Why: the 2026-11-24 run of #215 stopped on a running check. Predict: the next 3 HeroParser runs each run at least 40 minutes or quote a hard-limit / collision stop reason in `report_run`.
  Observed: pending (check 2026-11-29).

## RC-2 The tracker cannot read the repo's progress
Status: open
Inherited: agent-harness RC-5 — `advance-<repo>` must declare `ledger:`; HeroParser was one of the four repos missing it on 2026-11-25.
- A1 · PR #218 · 2026-11-25 · model not recorded (retroactive)
  Change: `ledger: CLAUDE.md#maintenance-ledger` in both copies of `advance-heroparser`'s frontmatter. Why: the dashboard showed "advance skill 未声明 ledger". Predict: `get_overview` shows no ledger amber for heroparser within a day of the merge (2026-11-26) and none returns within two weeks.
  Observed: pending (check 2026-12-09).
