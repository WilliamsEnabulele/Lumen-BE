#!/usr/bin/env python3
"""
Drives a whole lesson against a running Lumen API and says what happened.

This exists because every other check in this repository tests a piece in isolation, and
the bugs that have actually shipped here were all bugs of connection: a reteach path that
nothing could ever reach, an assessment layer the tutor could step around by declaring
itself finished. Unit tests passed through both. The only thing that catches them is
running a real lesson end to end and looking at what came back.

It asserts on shape, never on prose. What the tutor says is generated and will differ
every run; that it drew something, that it asked a question, that the answer was marked
and left evidence behind, will not.

    python3 tools/smoke.py                       # against http://localhost:5299
    python3 tools/smoke.py --base http://host    # somewhere else
    python3 tools/smoke.py --turns 30 --quiet

Exit code is 0 only if every assertion held.
"""

import argparse
import json
import mimetypes
import os
import sys
import time
import urllib.error
import urllib.request
import uuid
from pathlib import Path

FIXTURE = Path(__file__).parent / "fixtures" / "loops.md"

# What a student who is paying attention would say, in the order the material comes up.
# Deliberately spoken rather than written — half-sentences, thinking out loud, one
# genuine misconception (the off-by-one) so the marking path is exercised for real
# rather than only ever being told the right answer.
ANSWERS = [
    "so it runs five times? zero one two three four",
    "because five is where it stops, it never actually goes in as five",
    "erm... hang on, let me think",
    "a while loop, you don't know how many times up front",
    "it'd just run forever if nothing changes the condition",
    "six times I think, zero through five",
    "no wait, five — the count is five so the last index is four",
    "you break out, no point carrying on once you found it",
    "yeah that makes sense",
    "the highest index is one less than how many there are",
]

# Silence, as a student who is following along and not saying anything. The tutor has to
# keep teaching through these, and eventually has to stop waiting.
LISTENING = None


class Failed(Exception):
    pass


def call(base, method, path, body=None, form=None):
    url = base.rstrip("/") + path
    headers = {}

    if form is not None:
        boundary = uuid.uuid4().hex
        name, filename, content = form
        payload = b"".join([
            f"--{boundary}\r\n".encode(),
            f'Content-Disposition: form-data; name="{name}"; filename="{filename}"\r\n'.encode(),
            f"Content-Type: {mimetypes.guess_type(filename)[0] or 'text/plain'}\r\n\r\n".encode(),
            content,
            f"\r\n--{boundary}--\r\n".encode(),
        ])
        headers["Content-Type"] = f"multipart/form-data; boundary={boundary}"
    elif body is not None:
        payload = json.dumps(body).encode()
        headers["Content-Type"] = "application/json"
    else:
        payload = None

    request = urllib.request.Request(url, data=payload, headers=headers, method=method)
    try:
        with urllib.request.urlopen(request, timeout=180) as response:
            raw = response.read()
            return json.loads(raw) if raw else {}
    except urllib.error.HTTPError as error:
        detail = error.read().decode(errors="replace")[:400]
        raise Failed(f"{method} {path} returned {error.code}: {detail}") from None
    except urllib.error.URLError as error:
        raise Failed(f"{method} {path} could not reach the API ({error.reason}). Is it running?") from None


def describe(command):
    """One line per drawing, so a lesson reads as a transcript rather than a wall of JSON."""
    tool = command.get("tool")
    if tool == "show_statement":
        return f'statement: "{command.get("text", "")}"'
    if tool == "show_steps":
        return f'steps: {command.get("title", "")} ({len(command.get("items") or [])} items)'
    if tool == "show_code":
        lines = (command.get("source") or "").count("\n") + 1
        return f'code: {command.get("language", "")} ({lines} lines, highlight {command.get("highlightLine")})'
    if tool == "highlight_code":
        return f'highlight: line {command.get("line")}'
    if tool == "show_diagram":
        return (f'diagram: {command.get("title", "")} '
                f'({len(command.get("nodes") or [])} nodes, {len(command.get("edges") or [])} edges)')
    if tool == "show_chart":
        return f'chart: {command.get("kind", "")} {command.get("title", "")} ({len(command.get("points") or [])} points)'
    if tool == "show_math":
        return f'math: {command.get("latex", "")}'
    return str(tool)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--base", default=os.environ.get("LUMEN_API", "http://localhost:5299"))
    parser.add_argument("--turns", type=int, default=24)
    parser.add_argument("--document", default=str(FIXTURE))
    parser.add_argument("--quiet", action="store_true", help="assertions only, no transcript")
    options = parser.parse_args()

    say = (lambda *_: None) if options.quiet else print

    health = call(options.base, "GET", "/health")
    if health.get("status") != "ok":
        raise Failed(f"/health said {health}")

    document = Path(options.document)
    if not document.exists():
        raise Failed(f"No fixture at {document}")

    say(f"→ uploading {document.name} ({document.stat().st_size} bytes)")
    accepted = call(options.base, "POST", "/api/courses",
                    form=("file", document.name, document.read_bytes()))

    document_id = accepted["documentId"]
    stage, waited = None, 0.0
    while waited < 300:
        status = call(options.base, "GET", f"/api/documents/{document_id}/status")
        if status.get("stage") != stage:
            stage = status.get("stage")
            say(f"  {stage}: {status.get('message')}")
        if status.get("failed"):
            raise Failed(f"Ingestion failed: {status.get('message')}")
        if status.get("ready"):
            break
        time.sleep(1.5)
        waited += 1.5
    else:
        raise Failed(f"Ingestion never finished; last stage was {stage}")

    course = call(options.base, "GET", f"/api/courses/{status['courseId']}")
    concepts = [(lesson["title"], concept["title"])
                for lesson in course["lessons"] for concept in lesson["concepts"]]

    say(f"\n{course['title']} — by {course['authoredBy']}")
    say(f"{len(course['lessons'])} lessons, {len(concepts)} concepts")
    for lesson in course["lessons"]:
        say(f"  {lesson['title']}: " + ", ".join(concept["title"] for concept in lesson["concepts"]))

    session = call(options.base, "POST", "/api/sessions", {"courseId": course["id"]})
    say(f"\n── teaching ──────────────────────────────────────")

    drawn, asked, marked, verdicts, tutor = [], 0, 0, [], None
    answers, seen_concepts, moved = list(ANSWERS), set(), []
    awaiting, complete = False, False

    for turn in range(options.turns):
        said = answers.pop(0) if awaiting and answers else LISTENING
        if said:
            say(f"\n  student: {said}")

        response = call(options.base, "POST", f"/api/sessions/{session['sessionId']}/turn", {"said": said})
        tutor = response.get("tutor") or tutor

        if response.get("marked"):
            marked += 1
            verdicts.append(response["marked"]["verdict"])
            say(f"  ✓ marked {response['marked']['verdict']} — {response['marked']['reason']}")

        for title in response.get("skipped") or []:
            moved.append(f"skipped {title} (already demonstrated)")
            say(f"  ↷ skipped {title}")
        if response.get("abandoned"):
            moved.append(f"abandoned {response['abandoned']} (stalled)")
            say(f"  ⏭ gave up on {response['abandoned']}")

        if response.get("complete"):
            complete = True
            say("\n  course finished.")
            break

        seen_concepts.add(response.get("conceptTitle"))
        awaiting = bool(response.get("awaitingAnswer"))
        if awaiting:
            asked += 1

        say(f"\n  [{response.get('lessonTitle')} / {response.get('conceptTitle')}]"
            + ("  ← question" if awaiting else ""))
        say(f"  tutor: {response.get('said')}")
        for command in response.get("drew") or []:
            drawn.append(command)
            say(f"         ▸ {describe(command)}")

    progress = call(options.base, "GET", f"/api/sessions/{session['sessionId']}/progress")

    say("\n── what the server believes ──────────────────────")
    for record in progress:
        flag = " [moved on unmastered]" if record.get("movedOnUnmastered") else ""
        say(f"  {record['concept']}: {record['belief']:.2f}"
            f"{' mastered' if record['mastered'] else ''}{flag}"
            f" — {len(record['evidence'])} answers, {record['reteaches']} reteaches")

    say("\n── assertions ────────────────────────────────────")
    checks = [
        ("the document produced more than one concept", len(concepts) > 1),
        ("the tutor drew on the canvas", len(drawn) > 0),
        ("more than one kind of display tool was used",
         len({command.get("tool") for command in drawn}) > 1),
        ("a comprehension check was asked", asked > 0),
        ("an answer was marked", marked > 0),
        ("marking left an evidence trail",
         any(record["evidence"] for record in progress)),
        ("the lesson moved past its first concept", len(seen_concepts) > 1 or complete),
        ("nothing was drawn that the canvas cannot render",
         all(command.get("tool") for command in drawn)),
    ]

    # Only meaningful with a key configured; the deterministic pair is a degraded mode, and
    # a green smoke run against it would say nothing about the product.
    degraded = tutor in (None, "scripted")
    if degraded:
        say(f"  ! tutor is '{tutor}' — no ANTHROPIC_API_KEY, so this ran in degraded mode")

    failures = [name for name, held in checks if not held]
    for name, held in checks:
        say(f"  {'PASS' if held else 'FAIL'}  {name}")

    say(f"\n  {len(drawn)} drawings, {asked} checks asked, {marked} marked "
        f"({', '.join(verdicts) or 'none'}), {len(moved)} jumps")

    if failures:
        print(f"\nFAILED: {len(failures)} assertion(s) did not hold: {'; '.join(failures)}", file=sys.stderr)
        return 1

    print(f"\nOK — a whole lesson ran against '{tutor}'"
          + (" (degraded mode)" if degraded else "") + ".")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Failed as failure:
        print(f"\nFAILED: {failure}", file=sys.stderr)
        sys.exit(1)
    except KeyboardInterrupt:
        sys.exit(130)
