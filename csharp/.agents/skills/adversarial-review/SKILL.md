---
name: adversarial-review
description: Multi-persona adversarial review (Auditor, Adversary, Pragmatist) for code changes, enforcing behavioral parity, stress-testing edge cases, and preventing over-engineering.
---

# Multi-Persona Adversarial Code Review

This skill defines an adversarial review workflow powered by three orthogonal reviewer personas: **The Auditor**, **The Adversary**, and **The Pragmatist**. Any agent (Claude, Antigravity, or subagent) reviewing code or verifying a migration ticket must evaluate the changes through these three distinct lenses before approving.

---

## 1. The Three Personas

```
                       ┌─────────────────────────┐
                       │     Code Under Review   │
                       └────────────┬────────────┘
         ┌──────────────────────────┼──────────────────────────┐
         ▼                          ▼                          ▼
┌──────────────────┐       ┌──────────────────┐       ┌──────────────────┐
│   THE AUDITOR    │       │  THE ADVERSARY   │       │  THE PRAGMATIST  │
│                  │       │                  │       │                  │
│ • Specification  │       │ • Break the code │       │ • Maintainability│
│   correctness    │       │ • Extreme edges  │       │ • Anti-bloat /   │
│ • Parity & rules │       │ • State corrupt  │       │   YAGNI          │
│ • Invariants     │       │ • Unhandled NaN  │       │ • Real-world fit │
└────────┬─────────┘       └────────┬─────────┘       └────────┬─────────┘
         └──────────────────────────┼──────────────────────────┘
                                    ▼
                       ┌─────────────────────────┐
                       │   Synthesized Verdict   │
                       │     (Pass / Reject)     │
                       └─────────────────────────┘
```

### Lens 1: The Auditor (Correctness & Parity)
* **Mission:** Ensure the implementation strictly matches specifications, upstream code, and architectural invariants.
* **Review Checklist:**
  - **Behavioral Parity:** Does the code mirror original behavior line-by-line? (Preserve quirks, integer wrapping semantics, half-up vs half-even rounding, nullability rules).
  - **Determinism:** Absolutely no unseeded randomness (`System.Random` is banned), no wall-clock time in engine logic, no culture-sensitive formatting (`CultureInfo.InvariantCulture` required), and no iteration over unordered hash maps feeding state.
  - **Analyzer & Policy Enforcement:** Passes `dotnet build -warnaserror && dotnet format --verify-no-changes && dotnet test`. No unapproved warnings or violations of `BannedSymbols.txt`.
* **Out of Scope:** Subjective style preferences, speculative design changes.

### Lens 2: The Adversary (Hostile Edge Cases & Failure Modes)
* **Mission:** Actively attempt to break the implementation with malicious, extreme, or unexpected inputs and state transitions.
* **Review Checklist:**
  - **Boundary & Extreme Inputs:** What happens with `NaN`, `-0.0f`, `int.MinValue`, empty collections, nulls, negative coordinates, or overflow?
  - **State Invalidation:** Can calling methods out of expected sequence corrupt state or cause silent failures?
  - **Stress & Test Coverage:** Write or demand concrete test cases that probe unexercised branches, off-by-one boundary conditions, and failure modes.
* **Out of Scope:** Code cleanliness, documentation grammar, formatting.

### Lens 3: The Pragmatist (Simplicity & Maintainability)
* **Mission:** Defend the codebase against premature abstraction, over-engineering, and unnecessary maintenance burdens.
* **Review Checklist:**
  - **YAGNI (You Aren't Gonna Need It):** Did the author invent complex generic machinery, factory patterns, or unused helpers where simple methods suffice?
  - **Test Maintainability:** Are tests testing real behavioral contracts rather than brittle implementation details or excessive mocks?
  - **Seam Alignment:** Does the change integrate cleanly with surrounding code without introducing leaky abstractions or unnecessary coupling?
* **Out of Scope:** Line-by-line math verification (Auditor's job) and hostile edge-case fuzzing (Adversary's job).

---

## 2. Review Execution Protocol for Agents

Agents can run this review either by spawning an isolated subagent or performing a structured 3-phase audit:

### Step 1: Run Gates & Tooling
Before evaluating code logic, execute the automated validation suite:
```bash
dotnet build -warnaserror && dotnet format --verify-no-changes && dotnet test
```

### Step 2: Evaluate Each Persona
Produce findings categorized under each persona:

```markdown
### 1. Auditor Findings
- [PARITY] ... (or "No parity deviations found.")
- [DETERMINISM] ...
- [POLICY] ...

### 2. Adversary Findings
- [EDGE CASE] ... (or "Verified robust against edge inputs.")
- [FAILURE MODE] ...

### 3. Pragmatist Findings
- [COMPLEXITY] ... (or "Design is simple, focused, and maintainable.")
- [TEST QUALITY] ...
```

### Step 3: Synthesis & Verdict
- **Critical (Blocking):** Any parity deviation, determinism breach, policy failure (Auditor), or unhandled crash/data corruption on edge inputs (Adversary). **Must be fixed before approval.**
- **Non-blocking (Suggestions):** Simplifications or documentation enhancements (Pragmatist).
- **Verdict:**
  - `APPROVED` — All 3 personas sign off. Ready to commit `migration: review T### pass` and push to remote.
  - `NEEDS_REVISION` — Concrete failure examples and required fixes provided.
