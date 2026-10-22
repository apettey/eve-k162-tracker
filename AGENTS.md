# Working agreements for agents

Rules for any AI agent (Claude Code or otherwise) making changes in this repository. The
focus here is **clean commits**: a history someone can read a year from now and trust, with
nothing checked in that shouldn't be.

---

## 1. One commit, one coherent change

- A commit does **one** thing. Don't fold an unrelated rename, a formatting sweep, and a bug
  fix into a single commit — split them.
- Don't leave debris: no commented-out blocks, no stray `Console.WriteLine`/`Debug.WriteLine`,
  no `TODO` you just invented, no dead code from an approach you abandoned.
- The working tree is clean when you're done: `git status` shows nothing you didn't mean to
  leave. Reformatting churn that isn't part of the change does not belong in the diff.

## 2. Commit messages explain *why*

The diff already shows *what* changed. The message exists for the *why*.

- Subject line: imperative mood, ≤ ~72 chars, no trailing period
  (`Fix Wake Watch dropping kills after the last pilot leaves`).
- Body (wrap ~72): the reason for the change and anything non-obvious about the approach.
  Reference systems/APIs by name where it helps a future reader.
- End every commit message with the attribution line the session specifies.

## 3. Never commit to `main`; branch and open a PR

- Branch, commit, push, open a PR. Do not push directly to `main`.
- PR descriptions get the same *why*-first treatment, and end with the session's PR
  attribution line.

## 4. Never commit generated output, secrets, or local state

`.gitignore` already covers the usual .NET output — keep it that way:

- **Build output:** `bin/`, `obj/`, `publish/`, and the release `dist`/Velopack output. Never
  force-add them.
- **Secrets and per-user state:** never commit anything from `%APPDATA%\K162FleetIntel` or
  `%LOCALAPPDATA%\K162FleetIntel` — especially `tokens.dat` (DPAPI-encrypted SSO refresh
  tokens), `settings.json` (holds the user's ESI client id), or the `cache\` folder. No real
  ESI client id, character id, or token ever goes into source, tests, or fixtures — use
  obvious fakes.
- **The one generated file that *is* tracked on purpose:** `src/K162.App/Assets/Data/wormholes.json`
  is baked from anoik.is static data at build time and committed deliberately. Regenerate it
  with the transform rather than hand-editing, and commit the regeneration on its own.

## 5. Line endings are CRLF

Files in this repo are **CRLF**. `sed -i` under Git Bash on Windows strips carriage returns
and turns a one-line edit into a whole-file diff — use an editor that preserves them (the
Edit tool does). If a diff looks 10× bigger than your change, this is why; fix it before
committing.

## 6. Tests must pass before you commit

```
dotnet test tests/K162.Tests/K162.Tests.csproj
```

New behaviour in `K162.Core` needs a test. `K162.Core` is a platform-neutral `net10.0`
library with the parsing, caching, intel, SSO/PKCE and Wake Watch logic — keep the
Windows-only code (WPF, DPAPI, native calls) in `K162.App`.

## 7. The EVE EULA boundary is not casually widened

`docs/EULA-COMPLIANCE.md` and `tests/K162.Tests/EulaComplianceTests.cs` encode promises about
what this program can and cannot do: it reads CCP's authenticated ESI (read-only scopes) and
the player's own chat logs (read-only), and it cannot read the screen, read memory, or send
input to the game. The test enforces that in CI — a prohibited API name, a new native
`DllImport`, a write-mode open of an EVE-owned file, or an unrecognised outbound host all
fail the build.

If a change legitimately needs to cross one of those lines:

1. Decide deliberately whether it belongs, and say so **in the commit message**.
2. Update the relevant allow-list in `EulaComplianceTests.cs` **with a comment explaining why
   it is safe** (see the `FlashWindowEx` entry for the tone).
3. Update the API table and prose in `docs/EULA-COMPLIANCE.md`, and the README if the data
   sources changed.

Never widen an allow-list just to get a build green. Input synthesis (`SendInput`,
`keybd_event`, `PostMessage`, `SendMessage`, hooks), screen capture, and process-memory
access stay banned — those are what the EULA actually prohibits.

## 8. Changes that need none of the above

Typo fixes in comments, whitespace, and edits to this file need no test and no ceremony —
still keep them in their own focused commit.
