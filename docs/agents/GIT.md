# GIT.md

Rules for Git work and commit messages in GameFlow.
The sources are the Prooxie github wiki pages "Rules for commit messages" and "Mandatory software versioning and source code management rules".
If a rule does not answer your question, ask the user.

## Agent rules

- Commit only when the user tells you to.
- Add no `Co-Authored-By` or other trailer lines.
- Ask the user before each command that changes the remote: `push`, remote branch delete, and tag push.
- Ask the user before each `merge` into `master`, each `rebase`, and each `tag`.
- Never force-push to `master` or `dev`.

## Repository

- `master` contains only working, confirmed, and delivered versions.
- Do all development in a separate branch. `dev` is the default branch for new features.
- Use a separate `bugfix` branch for fixes after a release.
- If more than one order is in progress, give each branch a name that tells its purpose.
- Start each task from a known and confirmed base version. Do not guess the base.

## Daily work

- Commit and push developed and tested work to `origin` each day.
- Make each commit one isolated change that you tested.
- Do not commit features that are not implemented yet.
- Split a large change into small, independent steps. Each step must fit into one day.
- If other developers use the same branch, run `git pull --rebase origin` before you push.
  A pull request merges with rebase. A merge commit on the source branch causes rebase conflicts.

## Release

Before a test version:

1. Merge or rebase the latest `master` into the development branch.
2. Resolve all conflicts.

Exception: the project manager tells you to keep features in different versions.

After the project manager confirms the version:

1. Merge the development branch into `master`.
2. Tag the release commit in `master` with `major.minor.build`, for example `1.7.0`.
3. For a hotfix, add a suffix without a space, for example `1.7.0-HF1`. Git does not allow spaces in tags.
4. Make sure that the release version in the project settings is correct for all build targets.
5. Push `master` and the tag to `origin`.
6. Delete the development branch on `origin`.

Example:

```
git fetch origin
git checkout dev
git rebase origin/master
git add <resolved_file>
git rebase --continue
git checkout master
git merge dev
git tag 1.7.0
git push origin master --tags
```

## Commit messages

Format: `type: Description`

| Part | Description |
|---|---|
| `Description` | A short summary in the imperative mood. |

The `commit-msg` hook enforces this format. The full summary line is 80 characters or fewer.

### Description

- Start with a verb in the imperative mood: `Add`, not `Added`. `Fix`, not `Fixed`.
- Keep it to one short, specific line.
- Do not end it with a full stop.

### Examples

```
Add user authentication via OAuth2
Correct null pointer in payment processing
Add unit tests for payment service edge cases
Update API endpoint descriptions in README
Extract validation logic into separate service
Move config files to /env directory
```
