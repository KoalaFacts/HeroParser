'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { test } = require('node:test');

const workflow = fs.readFileSync(path.join(__dirname, '../..', '.github/workflows/benchmarks.yml'), 'utf8');
function job(id: string): string {
    const match = workflow.match(new RegExp(`^  ${id}:\\n[\\s\\S]*?(?=^  [a-z][a-z0-9-]*:\\n|(?![\\s\\S]))`, 'm'));
    assert.ok(match, `Missing workflow job: ${id}`);
    return match[0];
}
function timingEnabled(event: string, selected: Record<string, boolean | string> = {}): boolean {
    const condition = job('pipe-ab').match(/^    if: (.+)$/m);
    assert.ok(condition, 'Timing job must have an explicit condition');
    const inputs = new Proxy(selected, { get: (target, key: string) => target[key] ?? false });
    // Evaluate only the checked-in, single-line Actions boolean expression, with no IO globals.
    return Boolean(vm.runInNewContext(condition[1], { github: { event_name: event }, inputs }, { timeout: 1000 }));
}

for (const event of ['pull_request', 'push']) {
    test(`${event} never enters the historical timing job`, () => {
        assert.equal(timingEnabled(event), false);
        assert.equal(timingEnabled(event, { pipe_controls: true }), false);
        assert.equal(timingEnabled(event, { pipe_ab: true }), false);
    });
}
test('manual timing remains opt-in and does not mix with diagnostics', () => {
    assert.equal(timingEnabled('workflow_dispatch'), false);
    assert.equal(timingEnabled('workflow_dispatch', { pipe_controls: true }), true);
    assert.equal(timingEnabled('workflow_dispatch', { pipe_ab: true }), true);
    for (const mode of ['pipe_profile', 'pipe_bias', 'pipe_bias_jit_only', 'pipe_native_profile',
        'pipe_native_replay_run', 'pipe_native_history_probe', 'pipe_history', 'pipe_history_replay_run',
        'pipe_history_jit_control', 'pipe_history_same_run']) {
        assert.equal(timingEnabled('workflow_dispatch', { pipe_controls: true, [mode]: true }), false, mode);
    }
});
test('manual timing retains the frozen workload and suspended A/B guards', () => {
    const text = job('pipe-ab');
    assert.match(text, /Timing acceptance remains FAILED at run 36732093817/);
    assert.match(text, /Segmented128 1\.052845684 > 1\.05/);
    assert.match(text, /git diff --quiet 89c06810e76c4623ebad3cfc89c4bcdef41acd59 HEAD/);
    assert.match(text, /if \(\$LASTEXITCODE -eq 0\) \{ throw/);
    assert.match(text, /if \(\$LASTEXITCODE -ne 1\) \{ throw/);
    assert.match(text, /A\/B is suspended/);
});
test('automatic correctness uses real MTP tests, all configured frameworks and non-empty evidence', () => {
    const text = job('pipe-correctness');
    assert.match(text, /needs: setup/);
    assert.match(text, /framework: \$\{\{ fromJSON\(needs\.setup\.outputs\.frameworks\) \}\}/);
    assert.match(text, /fail-fast: false/);
    assert.match(text, /dotnet test tests\/HeroParser\.Tests/);
    assert.match(text, /--filter-class "\*CsvPipe\*"/);
    assert.match(text, /"HeroParser\.Tests\.PipeReaderTests"/);
    assert.match(text, /--minimum-expected-tests 1/);
    assert.match(text, /--fail-skips on/);
    assert.match(text, /validate-test-results\.ps1/);
    assert.doesNotMatch(text, /run-isolated\.ps1|run-series\.ps1|--throughput|continue-on-error/);
    assert.match(text, /test-control\.ps1/);
    assert.match(text, /test-ci-status\.ts/);
    assert.match(text, /SOURCE_SHA: \$\{\{ github\.event\.pull_request\.head\.sha \|\| github\.sha \}\}/);
    assert.match(text, /if-no-files-found: error/);
});
test('reporting and upload still run on failure, but never hide failed prerequisites', () => {
    const text = job('pipe-correctness');
    assert.match(text, /name: Report correctness and timing separately\n\s+if: always\(\)/);
    assert.match(text, /name: Upload current correctness evidence\n\s+if: always\(\)/);
    for (const prerequisite of ['policy', 'controls', 'build', 'tests', 'evidence']) {
        assert.ok(text.includes(`steps.${prerequisite}.outcome`), prerequisite);
    }
    assert.match(text, /if \(!report\.checkPassed\) core\.setFailed/);
});

function report(overrides: Record<string, unknown> = {}) {
    const modulePath = path.join(__dirname, 'ci-status.ts');
    assert.ok(fs.existsSync(modulePath), 'A current-head status reporter must exist');
    const { createCiStatus } = require(modulePath);
    return createCiStatus({ sourceSha: '1'.repeat(40), framework: 'net10.0', runId: '42', runAttempt: '1',
        outcomes: { policy: 'success', controls: 'success', build: 'success', tests: 'success', evidence: 'success' }, ...overrides });
}

test('correctness success is not historical acceptance or a current speedup claim', () => {
    const result = report();
    assert.equal(result.checkPassed, true);
    assert.equal(result.current.correctness, 'PASS');
    assert.equal(result.current.policy, 'PASS');
    assert.equal(result.current.timing, 'INCONCLUSIVE');
    assert.equal(result.current.newTimingSamples, 0);
    assert.equal(result.current.speedupClaimAllowed, false);
    assert.equal(result.historical.status, 'FAILED');
    assert.equal(result.historical.runId, '36732093817');
    assert.equal(result.historical.ratio, 1.052845684);
    assert.equal(result.historical.maxRatio, 1.05);
    assert.equal(result.historical.remainingSamplingBudget, 0);
    assert.match(result.markdown, /INCONCLUSIVE/);
    assert.match(result.markdown, /FAILED/);
    assert.match(result.markdown, /not performance acceptance/i);
});
for (const stage of ['policy', 'controls', 'build', 'tests', 'evidence']) {
    test(`a failed ${stage} cannot be hidden by historical INCONCLUSIVE reporting`, () => {
        const outcomes = { policy: 'success', controls: 'success', build: 'success', tests: 'success', evidence: 'success' };
        outcomes[stage] = 'failure';
        const result = report({ outcomes });
        assert.equal(result.checkPassed, false);
        assert.ok(result.failedPrerequisites.includes(stage));
        assert.equal(result.current[stage === 'policy' || stage === 'controls' ? 'policy' : 'correctness'], 'FAIL');
        assert.equal(result.historical.status, 'FAILED');
    });
}
for (const outcome of ['skipped', 'cancelled', '', undefined, 'unknown']) {
    test(`missing/unusable tests (${String(outcome)}) never become green`, () => {
        const result = report({ outcomes: { policy: 'success', controls: 'success', build: 'success', tests: outcome, evidence: 'success' } });
        assert.equal(result.checkPassed, false);
        assert.equal(result.current.correctness, 'INCONCLUSIVE');
        assert.ok(result.failedPrerequisites.includes('tests'));
    });
}
test('an empty prerequisite set does not pass vacuously', () => {
    assert.equal(report({ outcomes: {} }).checkPassed, false);
});
test('mutating a report cannot rewrite frozen history for the next run', () => {
    report().historical.status = 'PASS';
    assert.equal(report().historical.status, 'FAILED');
});
test('unsupported provenance is rejected rather than recorded as verified', () => {
    for (const overrides of [{ sourceSha: 'main' }, { framework: 'net11.0' }, { runId: 'x' }, { runAttempt: '0' }]) {
        assert.throws(() => report(overrides), /Invalid/);
    }
});

test('MTP receives both class selectors under one multi-value option', () => {
    const text = job('pipe-correctness');
    assert.match(text, /--filter-class "\*CsvPipe\*" "HeroParser\.Tests\.PipeReaderTests"/);
    assert.equal((text.match(/--filter-class/g) ?? []).length, 1);
});

for (const testOutcome of ['success', 'failure']) {
    test(`actual workflow reporter preserves ${testOutcome} and writes independent evidence`, async () => {
        const text = job('pipe-correctness');
        const section = text.slice(text.indexOf('      - name: Report correctness and timing separately'));
        const match = section.match(/          script: \|\n((?:            [^\n]*\n)+)/);
        assert.ok(match, 'Missing executable status reporter');
        const script = match[1].replace(/^            /gm, '');
        const writes = new Map<string, string>();
        let markdown = '';
        let failure = '';
        const summary = { addRaw: (text: string) => { markdown = text; return summary; }, write: async () => {} };
        const env = {
            GITHUB_WORKSPACE: path.join(__dirname, '../..'),
            SOURCE_SHA: '2'.repeat(40), FRAMEWORK: 'net8.0', RUN_ID: '123', RUN_ATTEMPT: '1',
            POLICY_OUTCOME: 'success', CONTROLS_OUTCOME: 'success', BUILD_OUTCOME: 'success',
            TESTS_OUTCOME: testOutcome, EVIDENCE_OUTCOME: 'success',
        };
        const io = { mkdirSync: () => {}, writeFileSync: (file: string, content: string) => writes.set(file, content) };
        await vm.runInNewContext(`(async () => {\n${script}\n})()`, {
            require: (id: string) => id === 'node:fs' ? io : require(id),
            process: { env }, core: { summary, setFailed: (message: string) => { failure = message; } },
        }, { timeout: 1000 });
        const result = JSON.parse(writes.get('BenchmarkDotNet.Artifacts/pipe-ci/status.json') ?? 'null');
        assert.ok(result, 'The real workflow must publish JSON even after test failure');
        assert.equal(result.current.sourceSha, env.SOURCE_SHA);
        assert.equal(result.current.correctness, testOutcome === 'success' ? 'PASS' : 'FAIL');
        assert.equal(result.current.timing, 'INCONCLUSIVE');
        assert.equal(result.historical.status, 'FAILED');
        assert.equal(result.checkPassed, testOutcome === 'success');
        assert.equal(failure.length > 0, testOutcome === 'failure');
        assert.equal(markdown, result.markdown);
    });
}
