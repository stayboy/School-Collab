/**
 * orchestrate-fix — orchestrator-worker-reviewer slash command
 *
 * A thin command wrapper around the `orchestrator-worker-reviewer` skill.
 * Usage:
 *   /orchestrate-fix <task description>
 *   /orchestrate-fix --orchestrator ollama/glm-5.3-flash:cloud --worker ollama/deepseek-v4-flash:0731-cloud --reviewer ollama/kimi-k2.7-code:cloud --tester ollama/minimax-m3:cloud <task>
 *
 * The command parses optional --orchestrator / --worker / --reviewer / --tester
 * model overrides (exact provider/id strings) and a free-text task, then injects a
 * priming user message that tells the parent agent to follow the local skill
 * at .pi/skills/orchestrator-worker-reviewer/SKILL.md and run the workflow
 * (plan → implement → review → accept, then UI-tester pass for UI rounds) with
 * those agents.
 *
 * The actual multi-agent execution is driven by the installed `pi-subagents`
 * extension's machinery (workflowScript, runs.run, runs.all, intercom). This
 * extension only seeds the turn; it does not spawn processes itself.
 */

import type { ExtensionAPI } from "@earendil-works/pi-coding-agent";

interface ParsedArgs {
	task: string;
	orchestrator?: string;
	worker?: string;
	reviewer?: string;
	tester?: string;
}

function parseArgs(raw: string): ParsedArgs {
	const tokens = raw.match(/(?:[^\s"]+|"[^"]*")+/g) ?? [];
	const result: ParsedArgs = { task: "" };
	const taskParts: string[] = [];

	for (let i = 0; i < tokens.length; i++) {
		const tok = tokens[i];
		if (tok === "--orchestrator" || tok === "--worker" || tok === "--reviewer" || tok === "--tester") {
			const next = tokens[i + 1];
			if (next) {
				result[tok.slice(2) as "orchestrator" | "worker" | "reviewer" | "tester"] = next.replace(/^"|"$/g, "");
				i++;
			}
			continue;
		}
		if (tok.startsWith("--orchestrator=")) result.orchestrator = tok.slice("--orchestrator=".length).replace(/^"|"$/g, "");
		else if (tok.startsWith("--worker=")) result.worker = tok.slice("--worker=".length).replace(/^"|"$/g, "");
		else if (tok.startsWith("--reviewer=")) result.reviewer = tok.slice("--reviewer=".length).replace(/^"|"$/g, "");
		else if (tok.startsWith("--tester=")) result.tester = tok.slice("--tester=".length).replace(/^"|"$/g, "");
		else taskParts.push(tok.replace(/^"|"$/g, ""));
	}

	result.task = taskParts.join(" ").trim();
	return result;
}

const DEFAULT_ORCHESTRATOR = "ollama/glm-5.3-flash:cloud";
const DEFAULT_WORKER = "ollama/deepseek-v4-flash:0731-cloud";
const DEFAULT_REVIEWER = "ollama/kimi-k2.7-code:cloud";
const DEFAULT_TESTER = "ollama/minimax-m3:cloud";

function buildKickoff(parsed: ParsedArgs): string {
	const orchestrator = parsed.orchestrator ?? DEFAULT_ORCHESTRATOR;
	const worker = parsed.worker ?? DEFAULT_WORKER;
	const reviewer = parsed.reviewer ?? DEFAULT_REVIEWER;
	const tester = parsed.tester ?? DEFAULT_TESTER;
	const task = parsed.task || "(no task provided — ask the user what to fix before proceeding)";

	const overrides = [];
	if (parsed.orchestrator) overrides.push(`orchestrator=${parsed.orchestrator}`);
	if (parsed.worker) overrides.push(`worker=${parsed.worker}`);
	if (parsed.reviewer) overrides.push(`reviewer=${parsed.reviewer}`);
	if (parsed.tester) overrides.push(`tester=${parsed.tester}`);
	const overrideNote = overrides.length ? `\nAgent overrides applied: ${overrides.join(", ")}.` : "";

	return `Follow the orchestrator-worker-reviewer skill in this repo at .pi/skills/orchestrator-worker-reviewer/SKILL.md and run a full fix round for the task below.

Agents to use (exact provider/id strings, copied from the session model registry):
- Orchestrator (document owner; agent definition: delegate): ${orchestrator}
- Worker (implements the plan; agent definition: worker): ${worker}
- Reviewer (verifies against the plan + source specs; agent definition: reviewer): ${reviewer}
- UI Tester (bug-hunts delivered UI work after acceptance; agent definition: worker): ${tester}

Task:
${task}
${overrideNote}

Execute the skill's procedure end to end:
1. Orchestrator reads the relevant specs/review docs, writes a plan to a documents/specs/plan-*.md file, and authors the worker + reviewer task text. Only the orchestrator edits the plan/acceptance doc.
2. Worker implements exactly the orchestrator's plan, runs build + affected tests, and returns a changed-files + build/test report. It does NOT edit the plan/acceptance doc.
3. Reviewer verifies the worker's diffs against the plan and source specs, runs build + tests independently (if it lacks shell tools, it returns the full report inline for you to persist), and writes a review doc. The reviewer task MUST also include a best-coding-practices check: (a) NO OVERWRITES — the diff must not rewrite or delete pre-existing code outside the plan's scope (flag unrelated deletions/reformatting/unrequested improvements as P1); (b) REPO SKILLS HONORED — where the touched surface has an applicable installed skill (dotnet-best-practices, dialog-ui, blazor-css-isolation, fluentui-*, author-component, collect-user-input), the implementation must follow it; (c) READABILITY & MAINTENANCE — concise naming per repo conventions, minimal focused diff, no dead/duplicated code. Violations are P1 (destructive overwrites, ignored mandated skills) / P2 (readability nits) with file+line evidence.
4. Orchestrator acceptance pass: appends an acceptance verdict (CLOSED / REMAINING P1) to its owned doc. When CLOSED and the round touched UI, the SAME acceptance pass authors the UI-tester scope handover: from the worker's changed-files report (and git diff --name-only) it enumerates every affected UI surface — changed .razor/.razor.css files, the pages/landing pages/dialogs that render them, the ApiClient methods they call, navigation entry points — and appends that closed list with a one-line rationale per entry to its owned doc.
5. UI-tester pass (only for UI rounds after a CLOSED verdict): pass the orchestrator's scope handover VERBATIM into the tester task — the tester neither derives nor expands its own scope. Within that scope it bug-hunts for swallowed errors, perpetual spinners, missing state rendering, invisible validation, wrong bindings, silent no-ops. It must NOT wander into unrelated UI pages/components; out-of-scope observations go back to the parent as notes, not rework. It returns P1/P2/pass with file+line evidence inline.
6. If the tester reports P1, loop: orchestrator-rework appends a rework plan + emits a focused worker task + folds the rework diff into the handover scope → worker fixes → tester re-verifies. Max ~2 rework iterations, then surface residuals to the user.

Run all phases as ONE workflowScript call with async:true using runs.run(...) sequentially (orchestrator → worker → reviewer → orchestrator-accept, then ui-tester for UI rounds), passing each phase's output into the next. If the reviewer raises P1 gaps the worker can fix, loop once more before the acceptance pass.

After the workflow settles, from the parent, run \`dotnet build SchoolCollab.sln -c Debug --nologo -v q\` and the affected \`dotnet test\` projects yourself to get authoritative numbers, persist any reviewer/tester report the agents could not write, merge the numbers into the orchestrator's acceptance doc, then report back to me with: per-agent status, build/test counts, reviewer + tester findings, rework iterations, and the acceptance-doc path.`;
}

export default function orchestrateFixExtension(pi: ExtensionAPI) {
	pi.registerCommand("orchestrate-fix", {
		description: "Run an orchestrator-worker-reviewer fix round for a task (plan → implement → review → accept, + UI-tester pass for UI rounds). Optional: --orchestrator/--worker/--reviewer/--tester <provider/id>.",
		getArgumentCompletions: (prefix) => {
			const flags = ["--orchestrator=", "--worker=", "--reviewer=", "--tester="];
			const matched = flags.filter((f) => f.startsWith(prefix));
			return matched.length > 0 ? matched.map((f) => ({ value: f, label: f })) : null;
		},
		handler: async (args, ctx) => {
			const parsed = parseArgs(args ?? "");

			if (!parsed.task) {
				ctx.ui.notify(
					"Usage: /orchestrate-fix <task>  [--orchestrator <id>] [--worker <id>] [--reviewer <id>] [--tester <id>]",
					"warning",
				);
				return;
			}

			const kickoff = buildKickoff(parsed);
			ctx.ui.notify(
				`Starting orchestrator-worker-reviewer round${parsed.orchestrator || parsed.worker || parsed.reviewer || parsed.tester ? " (custom agents)" : ""}…`,
				"info",
			);
			// sendUserMessage lives on the top-level ExtensionAPI (pi), not on the
			// command ctx, and returns void (triggers a turn immediately).
			pi.sendUserMessage(kickoff);
		},
	});
}