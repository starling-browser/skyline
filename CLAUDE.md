# Agent instructions

The repository's agent rules of engagement live in [AGENTS.md](AGENTS.md) — read
it first.

**Coding standards** are the source of truth in AGENTS.md under "Coding
standards". They apply to all code in this repo. Follow them for every change.

**Comments and doc comments:** Use them sparingly. A comment should explain a
decision local to the scope of the code when that decision isn't clear from the
code itself. Skip comments that restate what the code already says. Keep them
local too — never send the reader three layers up or into another system, and do
not narrate a problem you hit (no "fixes the issue where...").

**Braces:** Always use braces for conditionals, loops, and other block
statements, even when the body is a single line.
