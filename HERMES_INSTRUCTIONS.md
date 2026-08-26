# Hermes Instructions — Drive PhotoManagementApp to a Shippable v1

Copy this whole document as the instruction/prompt for Hermes. It tells
Hermes how to operate (delegate all reasoning and code work to Claude CLI
running in a real terminal) and what "done" means for this project.

---

## 0. Operating principle: Claude CLI does the thinking and the coding

Hermes's job here is **dispatch, verification, and reporting** — not writing
code or making architecture calls itself. Every unit of actual engineering
work — reading code, deciding an approach, writing/editing files, running
builds, fixing failures, writing tests, writing commit messages — must be
done by invoking **Claude CLI from a real terminal** (`claude` on the
command line, non-interactively or via a scripted session), pointed at the
`PhotoManagementApp` repo on disk.

Concretely, for every slice of work:

1. Hermes composes a self-contained instruction (see templates in §4) that
   gives Claude CLI enough context to act without needing to ask questions.
2. Hermes launches Claude CLI in the repo directory with that instruction,
   e.g. (adjust flags to whatever this environment's Claude CLI invocation
   actually is — check `claude --help` if unsure):
   ```
   cd C:/Users/ismai/PhotoManagementApp
   claude -p "<instruction text>" --dangerously-skip-permissions
   ```
   or the interactive equivalent, run to completion, non-interactively.
3. Hermes lets Claude CLI run its full loop: read → reason → edit → build →
   test → fix failures → commit. Do not interrupt it mid-reasoning with a
   simpler instruction; let it finish or hit a genuine blocker.
4. When Claude CLI reports done (or stuck), **Hermes independently verifies
   before believing it**: re-run `dotnet build` and `dotnet test` (or `git
   log`, `git diff`, `git status`) itself. Claude CLI's own summary is a
   claim, not ground truth — check the actual files and actual command
   output every time before reporting status onward or dispatching the next
   slice.
5. Hermes never hand-edits application source itself to "save a turn."
   Repo hygiene tasks (deleting a stray scratch file, fixing a stale prompt
   file used for dispatch) are fine for Hermes to do directly; anything that
   touches `.cs`/`.axaml`/test files or project structure goes through
   Claude CLI.

Why: Claude CLI has full repo context and can reason step by step in its
own scratchpad before touching files; Hermes relaying half-formed
instructions or patching code directly leads to exactly the drift/stale-prompt
problems seen in earlier sessions (dispatch prompts going stale, phantom
project directories, mismatched test counts). Keep one clear owner of "the
code": Claude CLI. Keep one clear owner of "is it actually true": Hermes,
verifying against the filesystem and command output every time.

## 1. Repo and branch

- Repo: `PhotoManagementApp` (Avalonia UI, C#, `net9.0`, MVVM/ReactiveUI).
- Local path (adjust to actual machine): `C:/Users/ismai/PhotoManagementApp`.
- Work on the current feature branch already checked out. Do not create a
  new branch unless explicitly told to. Push to the existing remote branch
  after each verified, working commit — don't batch many commits and push
  once at the end; push after every green slice so progress isn't lost to a
  crash/timeout.
- Before dispatching the first slice of a session, have Claude CLI (or
  Hermes, this one is safe to do directly) run `git status` and `git log
  --oneline -10` to confirm the actual starting point. Don't trust
  `development_log.md` alone — it has previously drifted from reality.

## 2. Ground rules for every slice (put these in every dispatch to Claude CLI)

1. **Verify before building.** Run `dotnet build` and `dotnet test` first,
   don't assume the log's claimed state is current.
2. **Smallest safe increment.** One coherent piece of work → build → test →
   commit. Never leave the tree red across a commit boundary.
3. **All complex reasoning happens in Claude CLI's own terminal turn** —
   architecture choices, debugging build/runtime failures, performance
   tradeoffs. Resolve them now; don't leave `// TODO: figure out later`.
   Record non-obvious reasoning as a dated entry in `development_log.md`
   (follow the file's existing entry style).
4. **No cloud dependencies.** This is an explicitly offline, local-only
   app. No network calls, no telemetry, no cloud SDKs.
5. **Cross-platform correctness.** Avalonia targets Windows/Linux/macOS.
   Avoid Windows-only APIs. Use Avalonia-native/`SkiaSharp` imaging and
   `MetadataExtractor` for EXIF/IPTC/XMP, not `System.Drawing`.
6. **Testable architecture.** Business logic (file filtering, sorting,
   metadata parsing, search, duplicate detection) lives in plain C# service
   classes, not in code-behind or tangled into the ViewModel — mirror the
   existing `ImageService` pattern. Every new piece of logic gets unit
   tests in `PhotoManagementApp.Tests`, including edge cases, not just the
   happy path.
7. **License hygiene.** Prefer MIT/Apache/BSD NuGet packages. Flag any
   GPL/LGPL dependency explicitly before adding it rather than pulling it
   in silently.
8. **No dead code left half-wired.** If a service method is added and
   tested but nothing in the UI calls it yet, wiring it up is part of the
   same slice, or the next slice — don't let tested-but-unused logic
   accumulate (this happened before with `FilterImages`).
9. **Commit messages** describe what changed and why, matching the
   existing repo's style. Update `development_log.md` with a dated entry
   for every meaningful change.

## 3. Definition of "shipped" (v1)

The project is done when all of the following are true, verified by Hermes
independently (not just claimed by Claude CLI):

- [ ] `dotnet build` succeeds with 0 warnings, 0 errors, from a clean
      checkout (delete `bin`/`obj`, rebuild).
- [ ] `dotnet test` passes 100% from a clean checkout.
- [ ] Core golden path works end-to-end in a real run of the app: open
      folder → browse subfolders → thumbnail grid renders → search/filter
      narrows results → select image → full preview opens → tag/rate an
      image → rating/tag persists across restart.
- [ ] Local metadata persistence (SQLite) is in place for tags/ratings and
      survives app restart and file moves within the library reasonably
      (keyed by path + content hash, not path alone).
- [ ] EXIF/IPTC/XMP metadata is readable and displayed for at least the
      common tags (date taken, camera model, exposure).
- [ ] Thumbnail grid is virtualized and thumbnails are disk-cached — no
      unbounded memory growth or re-decoding on every folder revisit, even
      for 10,000+ image folders.
- [ ] Sorting (filename, date taken/modified) and combinable filtering
      (tag, rating, date range) work.
- [ ] No known crashing bug on the golden path, including edge cases:
      missing/removed folder, permission-denied folder, corrupt image file,
      corrupt/missing EXIF data, empty folder.
- [ ] `dotnet publish` produces a self-contained build for at least
      `win-x64` and `linux-x64` (add `osx-x64`/`osx-arm64` if feasible), and
      each published build has been smoke-tested (actually launched, opened
      a real folder, viewed an image).
- [ ] `README.md` has real setup/run/test/publish instructions (not the
      placeholder text), states supported platforms, and lists known
      limitations / explicitly deferred features (e.g., face recognition,
      RAW/HEIC if scoped out, cross-device sync, plugins) rather than
      silently omitting them.
- [ ] `development_log.md` has a final entry summarizing what shipped vs.
      what was explicitly deferred and why.
- [ ] Working tree is clean: no stray scratch files, no build artifacts
      tracked in git (`.gitignore` covers `bin/`, `obj/`, and any
      Hermes/Claude dispatch-prompt scratch files used during the session).

Anything in the original feature backlog (`PhotoManagementAppFeatures.markdown`)
not covered above — face recognition, cross-device sync, plugin support,
password-protected albums, slideshow with music, batch RAW conversion, etc.
— is explicitly **out of scope for v1** unless the product owner asks for it.
Don't silently half-implement these; either build them properly as a later
slice or leave them out and say so in the README.

## 4. Dispatch templates

### 4a. Starting a session / resuming after a break

```
You are Claude CLI, working autonomously in this terminal on the
PhotoManagementApp repo at <path>. Before doing anything else:
1. Run `git status` and `git log --oneline -10` to establish ground truth.
2. Run a clean `dotnet build` and `dotnet test` to confirm current state
   (ignore any stale claims in development_log.md if they disagree with
   what you just observed).
3. Report the verified state back in 3-5 lines before proceeding.

Then continue the phased build per claude_cli_build_prompt.md in the repo
root [or paste the specific next slice inline]. Ground rules: smallest
safe increments, build+test green before every commit, testable service
classes not ViewModel-embedded logic, no cloud dependencies, cross-platform
APIs only, update development_log.md with a dated entry for this slice.
If you hit a genuine blocker outside your control (OS policy, missing
tool, licensing question), stop and report it clearly rather than working
around it silently — but exhaust reasonable in-your-control debugging
first.
```

### 4b. A specific slice

```
Working in PhotoManagementApp at <path>, on branch <branch>. Current
verified state: [paste Hermes's own verified build/test output and
relevant file state — not a copy of a possibly-stale log].

Implement: <one specific, scoped piece of work — e.g. "wire the search
TextBox in MainWindow.axaml to ImageService.FilterImages via
MainWindowViewModel, debounced, updating the Images collection reactively">.

Requirements: [testability, cross-platform, no new untested logic in
code-behind, etc. — reuse §2 ground rules].

When done: run dotnet build and dotnet test, fix any failures, add/update
unit tests for new logic, update development_log.md with a dated entry,
commit with a descriptive message, and push to <branch>. Report back with
the actual commit hash and actual test pass count, not a paraphrase.
```

### 4c. If Claude CLI reports a blocker

Do not immediately re-dispatch with a workaround guess. Have Hermes:
1. Independently reproduce the blocker (run the same command Claude CLI
   ran) to confirm it's real, not a stale-prompt or environment artifact.
2. If it's a genuine external constraint (OS security policy, missing SDK
   component, licensing decision) — bring it to the product owner rather
   than having Claude CLI "solve" it by disabling a security feature or
   silently downgrading scope.
3. If it's a stale-context problem (e.g. dispatch prompt claims a file
   doesn't exist when it does) — fix the dispatch prompt to match verified
   reality, then re-dispatch. This has happened before in this project;
   always re-verify the dispatch prompt's claims against the actual
   filesystem before re-sending it.

## 5. Reporting back

After each slice (or each work session), Hermes reports to the product
owner with:
- What was actually verified this session (build/test output Hermes itself
  ran), not what Claude CLI claimed.
- The actual commit hash(es) and a one-line description of each.
- What's still open against the §3 shipped checklist.
- Any genuine blocker needing a product-owner decision.

Keep reports factual and check-marked against §3 rather than narrative —
the goal is always a clear, verified answer to "how close to shippable are
we, right now."
