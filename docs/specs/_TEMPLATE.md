# SPEC-NNN — <feature name>

<!-- Sync contract: `## Current behavior & invariants` is the section a behavior-changing commit must update in the same commit. -->

- Status: Current
- Feature area: <duplicate scanning | folder tools | shell/UI | persistence>
- Ships in: <projects/modules that implement it, e.g. WindowsFileManager.Application, WindowsFileManager (UI)>

## What

<The behavior in plain terms — what a user can do and what the app does in response. One short paragraph, then a bullet list of the capabilities this spec owns. No implementation detail here.>

## Why

<The problem this solves and the product intent behind it. Why the feature exists at all, and what would be worse without it.>

## Scope

### In

- <capability this spec is the contract for>

### Out

- <adjacent capability that belongs to another spec — link it: [SPEC-NNN](SPEC-NNN-<slug>.md)>
- <deliberate non-goal — say plainly that it is not built>

## Current behavior & invariants

<The maintained current truth. Everything here must be grounded in code that exists today; a designed-but-not-built behavior is written as an explicit stub, never as working behavior.>

**Entry points**

| Trigger | Handler | Notes |
|---------|---------|-------|
| <command / control / call site> | <type.member> | <what it does> |

**Rules**

1. <ordered, testable statement of behavior — inputs, decisions, outputs>

**Invariants**

- <a property that must hold for every run, with the test that pins it if one exists>

**Edge cases**

| Case | Behavior |
|------|----------|
| <empty input / cancellation / permission denied / collision> | <what actually happens> |

**Not implemented**

- <behavior that is designed or persisted but not wired to anything — state it plainly as a stub, with where the gap is>

## Links

- Decisions: [ADR-NNN](../adr/ADR-NNN-<slug>.md)
- Module docs: [<module>](../modules/<module>.md)
- Related specs: [SPEC-NNN](SPEC-NNN-<slug>.md)
- Tests: `tests/WindowsFileManager.Tests/<path>`

<Link, do not duplicate — a spec points at ADRs, module docs, and tests rather than restating them.>
