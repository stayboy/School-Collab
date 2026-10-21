# GLOSSARY

Project vocabulary. Terms only — no decisions, no model ids, no mechanics.

## Tier

The cost/rigour level of one piece of agent work. There are four: **Tier 0 (Solo)**, **Tier 1**,
**Tier 2**, **Tier 3**. The tier fixes how many agent *seats* the work needs and who accepts it.
"Mode" was an older name for the same idea and is retired.

## Seat

One agent role inside a tier — orchestrator, worker, reviewer, UI tester. Tier 0 has no seats: its
agent is the implementer, the reviewer of its own diff, and the acceptor.

## Round

One execution instance of **any** tier, Tier 0 included. Every round produces one round doc.

## Unattended

A modifier on a tier: the round runs without waiting for the owner. It never changes which tier the
work is, and it never grants the author of the plan the right to accept their own recommendations.
