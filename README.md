# Lumen — backend

.NET 10 API, domain and infrastructure for [Lumen](https://github.com/WilliamsEnabulele/Lumen),
an AI tutor you can interrupt.

The product documents — the specification, the dimensioning, and the decisions taken against
it — live in the frontend repository, because they describe the product rather than either
half of it: **[WilliamsEnabulele/Lumen → `docs/`](https://github.com/WilliamsEnabulele/Lumen/tree/main/docs)**.

> **Status: domain first.** The pieces that carry the teaching rules are here and tested.
> Persistence, the API surface and the ingestion pipeline are not built yet.

## What is in the domain, and why it is here rather than in a service

Each of these encodes a decision that a later caller must not be able to route around.

| | What it protects |
|---|---|
| `Sessions/ResumePointer` | Where teaching picks back up: script node, offset **inside** the utterance, and canvas state. Three-dimensional because restoring the speech but not the diagram is still a wrong resume |
| `Sessions/TutorSessionStateMachine` | Teaching is never re-entered without a pointer. Every path back into Teaching is a detour returning, and each one is a chance to come back in the wrong place |
| `Sessions/UtteranceBoundary` | Where to *start speaking* again, which is a different question from where the tutor stopped. Resuming on the exact character is precise and sounds like a stutter |
| `Courses/ConceptKey` | Concept identity derived from the course and title, not from position or a fresh id — so rewriting an explanation does not orphan every mastery record pointing at it |
| `Courses/ContentFingerprint` | Whether the content changed, which is the *other* half of reprocessing: unchanged concepts keep their script and their pre-synthesised audio |
| `Courses/PrerequisiteGraph` | A cycle is named and refused, never broken silently. A course published in an impossible order mis-teaches everyone who takes it |
| `Scripts/LessonScript` | Teaching order is fixed once, on construction, not by however the rows came back |
| `Teaching/RegisterPolicy` | The interjection carries the affect; the explanation carries the content. Fails closed, because a machine over-performing a register is a caricature |
| `Teaching/RegisterLadder` | The student's register pulls the tutor's, never the reverse |

## Layout

```
Lumen.Domain/
  Common/      entities, tenancy
  Courses/     course graph, concept identity, prerequisite ordering
  Scripts/     script nodes and the lesson script
  Sessions/    resume pointer, utterance boundary, session state machine
  Teaching/    register level, interjections, the policy that places them
Lumen.Tests/   domain-rule tests
```

## Running

```bash
dotnet restore
dotnet build
dotnet test
```

Requires the .NET 10 SDK.

```bash
dotnet run --project Lumen.Api      # http://localhost:5299
```

Without `ANTHROPIC_API_KEY` set, the server swaps the lesson author, the tutor and the answer
judge for a deterministic trio. That is a degraded mode so the upload path stays runnable
offline, not a second implementation — it does not teach, it recites.

## The smoke harness

```bash
export ANTHROPIC_API_KEY=...
tools/smoke.sh                      # starts the API, teaches a whole lesson, stops it
```

It uploads a fixture, waits for ingestion, then drives a real session for two dozen turns —
answering the tutor's questions, one of them wrongly — and prints what the tutor said, what it
drew, how each answer was marked and what the server ended up believing about the student.

It exists because every bug that has actually shipped here was a bug of connection rather than
of logic: a reteach path nothing could reach, an assessment layer the tutor could step around
by declaring itself finished. Unit tests passed through both, because each piece was correct
on its own. Nothing catches that but running the whole thing and looking at the output.

It asserts on shape, never on prose — that something was drawn, that a check was asked, that an
answer was marked and left evidence. What the tutor actually says differs every run, and a test
that asserts on a generation is a test that fails for no reason.

## Conventions that are not negotiable

**Tenancy.** `TenantId` is on `Entity`, so it is on every row from the first migration.
Single-tenant institutional deployment is a configuration, not a fork. Retrofitting this is a
migration through every table and index at exactly the moment a customer is watching.

**The resume pointer is persisted, never inferred.** Not from conversation history, not from
the transcript, not from "the last thing we were talking about". It is a stored object, and
teaching cannot resume without one — enforced in the state machine, not in a service.

**Concept identity is content-derived and stable.** Never a fresh id per ingestion run, never
positional. Reprocessing an edited document is a diff, not a regeneration.

**The register policy fails closed.** Every uncertain case refuses the interjection. There is
no "probably fine" branch, and an interjection with no recorded take is never spoken by a
general-purpose voice.

**Traceability.** Generated teaching content carries a `SourceRef` back to the material it
came from. A claim nobody can locate is a claim nobody can withdraw.
