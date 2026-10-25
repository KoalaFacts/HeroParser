'use strict';

// Read-only reporting for ordinary CI. This module never authorizes or runs timing.
// Native TypeScript: the pinned actions/github-script v9 action uses Node 24.
type CiInput = {
    sourceSha: string;
    framework: string;
    runId: string;
    runAttempt: string;
    outcomes?: Record<string, unknown>;
};
type Verdict = 'PASS' | 'FAIL' | 'INCONCLUSIVE';

const STAGES = ['policy', 'controls', 'build', 'tests', 'evidence'] as const;

function verdict(outcomes: Record<string, unknown>, stages: readonly string[]): Verdict {
    if (stages.some(stage => outcomes[stage] === 'failure')) return 'FAIL';
    return stages.every(stage => outcomes[stage] === 'success') ? 'PASS' : 'INCONCLUSIVE';
}

function createCiStatus(input: CiInput) {
    if (!input || typeof input.sourceSha !== 'string' || !/^[0-9a-f]{40}$/.test(input.sourceSha)) {
        throw new Error('Invalid source SHA: require the exact checked-out commit.');
    }
    if (!['net8.0', 'net9.0', 'net10.0'].includes(input.framework)) {
        throw new Error('Invalid correctness framework.');
    }
    for (const field of ['runId', 'runAttempt'] as const) {
        if (typeof input[field] !== 'string' || !/^[1-9][0-9]*$/.test(input[field])) {
            throw new Error(`Invalid ${field}.`);
        }
    }
    // Normalize missing/unknown outcomes instead of accepting a vacuous empty set.
    const supplied = input.outcomes ?? {};
    const outcomes = Object.fromEntries(STAGES.map(stage => [stage,
        ['success', 'failure', 'cancelled', 'skipped'].includes(String(supplied[stage]))
            ? supplied[stage] : 'missing',
    ]));
    const failedPrerequisites = STAGES.filter(stage => outcomes[stage] !== 'success');
    const current = {
        sourceSha: input.sourceSha,
        framework: input.framework,
        runId: input.runId,
        runAttempt: input.runAttempt,
        correctness: verdict(outcomes, ['build', 'tests', 'evidence']),
        policy: verdict(outcomes, ['policy', 'controls']),
        timing: 'INCONCLUSIVE',
        reason: 'Ordinary CI runs correctness and synthetic policy tests only; no timing samples are collected.',
        newTimingSamples: 0,
        speedupClaimAllowed: false,
    };
    const markdown = [
        `## CSV Pipe current correctness (${input.framework})`,
        '',
        `Source: \`${input.sourceSha}\`; run ${input.runId}, attempt ${input.runAttempt}.`,
        '',
        '| Independent result | State |',
        '| --- | --- |',
        `| Current parser correctness and TRX evidence | ${current.correctness} |`,
        `| CI reporting and synthetic control-policy tests | ${current.policy} |`,
        '',
        'A green correctness job is not performance acceptance and cannot support a speedup claim.',
        ...(failedPrerequisites.length ? ['', `Unsuccessful prerequisites: ${failedPrerequisites.join(', ')}.`] : []),
        '',
    ].join('\n');
    return { schemaVersion: 2, checkPassed: failedPrerequisites.length === 0,
        failedPrerequisites, outcomes, current, markdown };
}

module.exports = { createCiStatus };
