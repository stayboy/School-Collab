# Triage Labels

The engineering skills speak in terms of five canonical triage roles. This file maps those roles to
the label strings actually used in this repo's tracker (`stayboy/School-Collab`).

| Label in mattpocock/skills | Label in our tracker | Meaning                                  |
| -------------------------- | -------------------- | ---------------------------------------- |
| `needs-triage`             | `needs-triage`       | Maintainer needs to evaluate this issue  |
| `needs-info`               | `needs-info`         | Waiting on reporter for more information |
| `ready-for-agent`          | `ready-for-agent`    | Fully specified, ready for an agent      |
| `ready-for-human`          | `ready-for-human`    | Requires human implementation            |
| `wontfix`                  | `wontfix`            | Will not be actioned                     |

When a skill mentions a role (e.g. "apply the AFK-ready triage label"), use the corresponding label
string from this table.

## State of the tracker

- Already present: `ready-for-agent`, `wontfix` (plus GitHub's stock labels, which the skills do not
  use as triage roles).
- Created with this setup: `needs-triage`, `needs-info`, `ready-for-human`.
- To change the vocabulary, edit the right-hand column here and create the matching labels — the
  skills read this table, not the names.
