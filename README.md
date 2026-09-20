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

Courses, teaching sessions and mastery records are written to `.local-storage/` as JSON —
one file each, written aside and moved into place so a crash cannot leave a half-record. Files
rather than a database because nothing yet asks a question a dictionary cannot answer; when
something does, the store interfaces are what change and their callers are not.

Without `ANTHROPIC_API_KEY` set, the server swaps the lesson author, the tutor and the answer
judge for a deterministic trio. That is a degraded mode so the upload path stays runnable
offline, not a second implementation — it does not teach, it recites.

## Accounts

Email and password, with the session in an HttpOnly cookie.

- **PBKDF2-HMAC-SHA256 at 210,000 iterations**, salted per password, compared in constant time.
  The cost is recorded inside the hash, so raising it later re-hashes people as they sign in
  rather than locking them out.
- **The session token is stored as its SHA-256**, never as itself. Whoever reads that store
  must not come away able to sign in as anybody.
- **Sessions are server-side and revocable.** Signing out has to mean now, and a self-contained
  token answers "has this been revoked" with "not until it expires".
- **A failed sign-in says one thing** whichever half was wrong, and takes the same time either
  way. Distinguishing them hands out a list of real accounts.
- **Sign-in and sign-up are rate limited** by address. A slow hash protects a stolen database
  and does nothing about ten thousand guesses at one live account.

Ownership is checked on every read. A course, a teaching session and a payment are found
through their owner or not at all, and somebody else's is `404` rather than `403` — "forbidden"
confirms the id is real, which is the one thing a stranger guessing ids wants to learn.

`ISignedIn` lives in `Lumen.Api` rather than `Lumen.Infrastructure`, because it is the one
piece of identity that knows what a cookie is. Infrastructure stores things; the web layer
turns an HTTP request into a person.

`Auth:CrossSiteCookies` is for a split-origin development setup only, and is never inferred
from a hostname: guessing it wrong in production silently drops the CSRF protection
`SameSite=Lax` gives for free.

## What can be uploaded

`.txt`, `.md`, `.docx`, `.pptx`, `.pdf`.

Everything but PDF is read with the base class library alone — a .docx and a .pptx are zips of
XML, and an extractor with no dependency cannot fail to restore. PDF is the exception, and
deliberately: a PDF is a graphics format that happens to contain glyphs, where text is
positioned rather than flowed and the order in the file is often not the order on the page.
Half a PDF parser does not fail loudly, it produces plausible scrambled text — which becomes a
plausible scrambled lesson. So that one borrows [PdfPig](https://github.com/UglyToad/PdfPig).

A PDF with no text layer is refused by name rather than taught from. Photocopied and
photographed textbooks are everywhere, and building a course out of one produces an empty
course and no explanation; the message says it looks like a scan and needs OCR first, which is
a different problem from a document that is simply too thin.

## Payments

Monnify, behind an `IPaymentProvider` seam. A server with credentials charges; one without says
so plainly from `/health` and leaves teaching open, because a paywall nobody can pay through is
just a closed door.

```jsonc
{
  "Billing": {
    "Enforce": true,            // omit to follow whether Monnify is configured
    "Monnify": {
      "BaseUrl": "https://sandbox.monnify.com",
      "ContractCode": "…",
      "RedirectUrl": "https://lumen.example/paid",
      "AllowUnsignedWebhooks": false
    }
  }
}
```

Keys come from `MONNIFY_API_KEY`, `MONNIFY_SECRET_KEY` and `MONNIFY_CONTRACT_CODE`, and never
from a settings file that could be committed.

One rule runs through the whole flow: **nothing arriving from outside decides anything.**

- The **price** is read from the plan in code, never from the request. A price a browser can
  name is a price a browser will name, and it will be zero.
- The **webhook** is a hint that something happened, not evidence of what. Its signature is
  checked as an HMAC-SHA512 over the *raw* body — re-serialising changes whitespace and key
  order, and therefore the hash, every time — and then the only field read from it is the
  reference, which says which payment to go and ask Monnify about.
- **Settlement** happens in one place that both the webhook and the return-from-checkout page
  run through, so the same payment reported four times grants one subscription, and the flow
  still completes when the webhook never arrives.
- **Underpayment** is its own terminal state, not a failure. Somebody is owed either the rest
  of the service or their money back, and a state saying "failed" hides that.
- **Renewing early** extends the time left rather than replacing it.

Sandbox sends no signature header at all. `AllowUnsignedWebhooks` exists for that and is never
inferred from the base URL, because inferring it means one settings change silently disables
the only thing protecting the endpoint.

## Which model does what

Three roles, chosen independently, because they are not the same purchase:

```jsonc
// appsettings.Development.json, or user secrets
{
  "Ai": {
    "Routing": { "Author": "Google", "Tutor": "Anthropic", "Judge": "Anthropic" },
    "Google": { "AuthorModel": "gemini-3.1-flash-lite" }
  }
}
```

**Authoring** — reading a document and deciding what it teaches — is one large call per upload,
paid once, over a whole chapter at a time. It is also the only role whose output is checked by
code before anyone sees it: the plan goes through `LessonPlanReader` and the validator, so a
cheaper model's mistakes surface as a rejected plan rather than as a bad lesson. Cheap and
checked is the right trade here, and Gemini Flash is the obvious candidate.

**Teaching** is the recurring cost and the product at the same time. Every turn is a call, and
what comes back is spoken to the student with nothing downstream to catch a dull explanation.
This is the role to spend on, and the one the prompt caching exists for.

**Marking** is the smallest call in the system and a well-bounded judgement. A good place for
a smaller model, once there is evidence it agrees with the larger one — which means running
both over the same answers, not guessing.

A role routed to a provider with no key, or to one with no implementation for that role, is a
startup failure with a message saying so. It never falls through to the deterministic trio:
choosing a provider for cost and silently getting the fallback would look like a cheaper bill
and a tutor that had stopped teaching, and nothing would say which had happened.

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
