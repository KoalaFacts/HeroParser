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
function setupEnabled(event: string, selected: Record<string, boolean | string> = {}): boolean {
    const condition = job('setup').match(/^    if: (.+)$/m);
    assert.ok(condition, 'Ordinary CI must have an explicit condition');
    const inputs = new Proxy(selected, { get: (target, key: string) => target[key] ?? false });
    // Execute the checked-in Actions boolean expression with no IO globals.
    return Boolean(vm.runInNewContext(condition[1], { github: { event_name: event }, inputs }, { timeout: 1000 }));
}

test('the retired timing experiment has no workflow job or dispatch inputs', () => {
    assert.doesNotMatch(workflow, /^  pipe-ab:/m);
    assert.doesNotMatch(workflow, /^      pipe_(?:ab|controls):/m);
    assert.doesNotMatch(workflow, /inputs\.pipe_(?:ab|controls)\b/);
    assert.doesNotMatch(workflow, /run-isolated\.ps1|Timing acceptance remains FAILED/);
});
for (const event of ['pull_request', 'push', 'workflow_dispatch']) {
    test(`${event} retains the ordinary current-code checks`, () => {
        assert.equal(setupEnabled(event), true);
        assert.match(job('benchmark'), /needs: setup/);
        assert.match(job('pipe-correctness'), /needs: setup/);
    });
}
test('independent manual diagnostics remain separate from ordinary CI', () => {
    for (const mode of ['pipe_profile', 'pipe_bias', 'pipe_bias_jit_only', 'pipe_native_profile',
        'pipe_native_replay_run', 'pipe_native_history_probe', 'pipe_history', 'pipe_history_replay_run',
        'pipe_history_jit_control', 'pipe_history_same_run']) {
        assert.equal(setupEnabled('workflow_dispatch', { [mode]: true }), false, mode);
    }
    for (const id of ['pipe-profile', 'pipe-bias', 'pipe-history', 'pipe-native-profile']) {
        assert.match(job(id), /if: github\.event_name == 'workflow_dispatch'/);
    }
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
    assert.match(text, /name: Report current correctness\n\s+if: always\(\)/);
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

test('current reports do not keep an archived experiment as a live obligation', () => {
    const result = report();
    assert.equal(result.schemaVersion, 2);
    assert.equal(result.checkPassed, true);
    assert.equal(result.current.correctness, 'PASS');
    assert.equal(result.current.policy, 'PASS');
    assert.equal(result.current.timing, 'INCONCLUSIVE');
    assert.equal(result.current.newTimingSamples, 0);
    assert.equal(result.current.speedupClaimAllowed, false);
    assert.equal(Object.hasOwn(result, 'historical'), false);
    assert.doesNotMatch(JSON.stringify(result), /36732093817|1\.052845684|Historical timing|remainingSamplingBudget/);
    assert.doesNotMatch(result.markdown, /FAILED|INCONCLUSIVE/);
    assert.match(result.markdown, /not performance acceptance/i);
});
for (const stage of ['policy', 'controls', 'build', 'tests', 'evidence']) {
    test(`a failed ${stage} cannot be hidden by current reporting`, () => {
        const outcomes = { policy: 'success', controls: 'success', build: 'success', tests: 'success', evidence: 'success' };
        outcomes[stage] = 'failure';
        const result = report({ outcomes });
        assert.equal(result.checkPassed, false);
        assert.ok(result.failedPrerequisites.includes(stage));
        assert.equal(result.current[stage === 'policy' || stage === 'controls' ? 'policy' : 'correctness'], 'FAIL');
        assert.equal(Object.hasOwn(result, 'historical'), false);
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
        const section = text.slice(text.indexOf('      - name: Report current correctness'));
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
        assert.equal(Object.hasOwn(result, 'historical'), false);
        assert.equal(result.checkPassed, testOutcome === 'success');
        assert.equal(failure.length > 0, testOutcome === 'failure');
        assert.equal(markdown, result.markdown);
    });
}

test('a new revision queues without cancelling an active benchmark capture', () => {
    assert.match(workflow, /^concurrency:\n/m);
    assert.match(workflow, /^  group: .*github\.workflow.*github\.ref.*github\.event_name/m);
    assert.match(workflow, /^  cancel-in-progress: false$/m);
});
