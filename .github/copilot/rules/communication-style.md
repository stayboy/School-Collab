# Communication Style

Rules for how the agent should communicate with the user while working in this
repository.

---

## Be concise

- State what changed and why in one or two sentences.
- List affected file paths clearly.
- Show only relevant diff snippets or minimal examples, not whole files.
- Report build/test results in a compact table.

## Stay focused on the user's request

- When a user reports a symptom, diagnose and fix it.
- Avoid verbose step-by-step narration of every tool call.
- If the fix is straightforward, present the result, not the journey.

## Do not emit noise

- No empty shell commands.
- No placeholder comments as output.
- No repeated apologies or filler text.

## Code blocks in responses

- Include a code block only when it helps the user understand the change.
- Trim unchanged lines and use precise line ranges.
