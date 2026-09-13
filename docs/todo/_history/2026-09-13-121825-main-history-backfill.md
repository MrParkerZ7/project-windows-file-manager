# 2026-09-13 12:18 - main - history backfill (board mutations that were never logged)

This board requires one `_history/` file per mutation. The mutations below were committed during the
2026-09-05 to 2026-09-07 `todo-next-all` and `todo-suggest` runs WITHOUT that entry - the omission is mine.
They are recorded here late, from git, rather than written as backdated files that would look contemporaneous.

| Ticket | Final status | Commits that changed `status:` | When |
|---|---|---|---|
| T-013 | done | `f377924` | 2026-09-05 10:28 |
| T-010a | done | `74c9874` | 2026-09-05 10:38 |
| T-015 | done (created and closed in one commit) | `d21f8e1` | 2026-09-05 10:40 |
| T-011a | done | `6363be4` | 2026-09-05 11:04 |
| T-001 | done | `e5fc275`, `62d1a42`, `4998dde` | 2026-09-05 12:19 to 2026-09-06 11:57 |
| T-002 | review | `cf2c753`, `b371d15` | 2026-09-06 21:21 to 2026-09-07 00:27 |

Intermediate statuses between those commits are not reconstructed here - only the commits that touched the
field and where each ticket stood afterwards. The T-002 close-out that follows this entry is logged separately.
