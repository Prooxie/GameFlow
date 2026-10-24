Rules for agents that write or change code in GameFlow.
If a rule does not answer your question, ask the user.

## Language

Write all agent-facing text in ASD-STE100 Simplified Technical English.
This includes comments, commit messages, issues, specs, and reports.

- Use active voice and simple tenses.
- Write one instruction per sentence.
- Keep instructions to 20 words or fewer. Keep descriptions to 25 words or fewer.
- Use one word for one meaning. Use the terms in `CONTEXT.md`.
- Use a list for three or more steps or conditions.
- Use a full stop, not a semicolon.

## Workflow: TDD

Write the test first. Then write the code.
Obey all rules in `docs/agents/TDD.md`. Read it before you write or change a test.

## Coding standard

Obey all rules in `docs/agents/CODING.md`. Read it before you write or change code.

## Git

Obey all rules in `docs/agents/GIT.md`. Read it before you commit or change branches.


## Domain docs

- Read `CONTEXT.md` before you explore the code.