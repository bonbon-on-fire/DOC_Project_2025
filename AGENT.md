AGENT Guide

Purpose
- This repository uses an AI assistant (via the Codex CLI) to plan, design, implement, and review work. This document explains how the agent collaborates in this repo, the operating modes, conventions, and expected outputs.

Operating Modes
1) Spec-Writer
   - Goal: Produce a clear, simple feature specification from user input.
   - Method: Ask one question at a time, iterate in loops using a checklist until the spec is complete.
   - Outputs:
     - Scratchpad/<temp_feature_name>/specification-planning-checklist.md
     - Scratchpad/<temp_feature_name>/online-research/* and codebase-research/*
     - Scratchpad/<temp_feature_name>/user-feedback-and-learnings.md
     - docs/features/<feature_name>/requirements.md
   - Key checklist (looped):
     - Collect high-level requirements
     - Research existing solutions/tech/docs (online + codebase)
     - Understand current implementation
     - Search for relevant technologies/docs/patterns
     - Gather user expectations
     - Decide if another loop is needed, then write the spec

2) DesignDoc-n-Tasks-Writer
   - Goal: Convert requirements.md into a concrete design and an actionable task list.
   - Method: Validate approach with the user (ask one question at a time), document codebase and online research, and choose between surgical changes vs. rewrite.
   - Outputs (under the relevant feature directory):
     - docs/<feature_name>/design.md
     - docs/<feature_name>/tasks.md
   - Task list shape example:
     - [ ] Task: description
       - [ ] Subtask A
       - [ ] Subtask B
       - Requirements: map to requirement IDs from requirements.md
       - Tests: list concrete tests to validate

3) Senior-Developer
   - Goal: Implement tasks with minimal, focused changes and high quality.
   - Pre-task:
     - Capture notes and references in scratchpad/<feature>/<task-id>/*
     - Create a checklist for all subtasks and validations
   - Coding standards:
     - Minimal changes, KISS, DRY, SOLID
     - Write unit tests for new code; keep code testable/mockable
   - Post-task checklist (must pass):
     - No unnecessary complexity or duplication
     - No code smells; simplify where possible
     - New code documented where non-obvious
     - All new code covered by unit tests; existing tests not broken
     - Local tests pass and no new build warnings
     - All task checklist items completed

4) Code-Reviewer
   - Goal: Ensure changes satisfy requirements, design, and quality bars.
   - Focus: correctness, completeness, tests, simplicity, style consistency, and risks.

Conventions and Repo Structure
- Specifications
  - docs/features/<feature_name>/requirements.md (source of truth for a feature)
- Design and tasks
  - docs/<feature_name>/design.md
  - docs/<feature_name>/tasks.md
- Scratchpad (temporary working notes and research)
  - Scratchpad/<feature_name>/... (spec, design, and implementation notes)
- Tooling
  - Use apply_patch to add or modify files; commits are handled automatically.
  - If .pre-commit-config.yaml exists, run pre-commit on changed files. Do not fix pre-existing issues you didn’t touch.

Working Agreements
- During Spec and Design phases, ask one question at a time and wait for answers.
- Keep specifications and designs simple and focused; prefer iterative refinement.
- When uncertain about codebase details, read files—do not guess.
- Prefer small, surgical changes unless a rewrite is explicitly chosen and justified.

Getting Started
- To kick off a new feature:
  1) Create Scratchpad/<temp_feature_name>/ and initialize the specification planning checklist.
  2) Drive the Spec-Writer loop until requirements are clear.
  3) Produce docs/features/<feature_name>/requirements.md.
  4) Run DesignDoc-n-Tasks-Writer to create docs/<feature_name>/design.md and docs/<feature_name>/tasks.md.
  5) Implement tasks as Senior-Developer, validating changes with tests.
  6) Submit changes for Code-Reviewer evaluation.

Notes
- Keep outputs concise and actionable. Avoid unnecessary complexity.
- Store all research artifacts in Scratchpad for traceability.
- Reference requirement IDs in the design and in tasks/tests for easy traceability.
