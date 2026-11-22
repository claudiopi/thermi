# Architecture decisions

One file per decision that is expensive to undo: architecture, drivers, admin rights, a dependency we build on. Small choices go in the PR description.

Name: `NNNN-short-title.md`. Keep it to one screen, with these sections:

- **Status**: Proposed, Accepted, or Superseded by NNNN.
- **Context**: the problem, and what forces the decision.
- **Decision**: what we do.
- **Rejected**: the best alternative we did not take, and why.
- **Consequences**: what gets easier, what gets harder, what to watch.
- **What it replaces in earlier ADRs**, when it changes part of one: the lines it replaces and with what.

An accepted ADR is not rewritten. If the decision changes, a new ADR supersedes it.
