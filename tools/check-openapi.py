#!/usr/bin/env python3
"""Reads the document the API serves and checks what it claims about authentication.

The document is generated at runtime from the endpoints, so a green build says nothing about
whether it is right — it compiles just as happily with every operation documented as open. The
consequence of getting that wrong is not a broken server, it is somebody writing a client
against a description that is not true, which is a slower and more annoying failure than a
crash.

So this asks the running server for its own description and checks the parts that are a
decision rather than a derivation: that the bearer scheme is declared at all, that the
endpoints which read ISignedIn are documented as needing it, that the ones which cannot use it
are not, and that every operation landed in a group.
"""
import json
import sys

# The endpoints whose documentation is a claim about security rather than a restatement of the
# handler, listed by hand because the point is to disagree with the code when the code is wrong.
NEEDS_BEARER = [
    ("/api/courses", "post"),
    ("/api/courses", "get"),
    ("/api/sessions", "post"),
    ("/api/entitlement", "get"),
    # The one auth endpoint that does need it: "me" answers from the token, not the cookie.
    ("/api/auth/me", "get"),
]

OPEN_TO_ANYBODY = [
    # The cookie is the credential here, and a bearer is exactly what the caller does not have
    # yet. Documenting these as needing one sends people round a loop with no entrance.
    ("/api/auth/login", "post"),
    ("/api/auth/register", "post"),
    ("/api/auth/refresh", "post"),
    ("/api/auth/logout", "post"),
    # Monnify has no session here; its signature is the authentication.
    ("/api/payments/monnify/webhook", "post"),
    # Asked before anybody has signed in, and neither is a secret.
    ("/api/formats", "get"),
    ("/api/plans", "get"),
    ("/health", "get"),
]

SCHEME = "bearer"


def main() -> int:
    document = json.load(open(sys.argv[1], encoding="utf-8"))
    paths = document.get("paths", {})
    wrong = []

    if not paths:
        print("! the document describes no endpoints at all", file=sys.stderr)
        return 1

    declared = document.get("components", {}).get("securitySchemes", {})
    if SCHEME not in declared:
        wrong.append(f"no {SCHEME!r} security scheme is declared, so the UI has nothing to authorise with")
    elif declared[SCHEME].get("scheme") != "bearer":
        wrong.append(f"the {SCHEME!r} scheme is not an HTTP bearer scheme: {declared[SCHEME]}")

    def operation(path, method):
        found = paths.get(path, {}).get(method)
        if found is None:
            wrong.append(f"{method.upper()} {path} is missing from the document entirely")
        return found

    def secured(op):
        return any(SCHEME in requirement for requirement in op.get("security", []))

    for path, method in NEEDS_BEARER:
        op = operation(path, method)
        if op is not None and not secured(op):
            wrong.append(f"{method.upper()} {path} refuses anonymous callers but is documented as open")

    for path, method in OPEN_TO_ANYBODY:
        op = operation(path, method)
        if op is not None and secured(op):
            wrong.append(f"{method.upper()} {path} is documented as needing a token it cannot be given")

    # Every operation belongs to a group. Without this an endpoint added later lands in an
    # unnamed pile at the bottom of the page, which is where endpoints go to be forgotten.
    for path, methods in sorted(paths.items()):
        for method, op in sorted(methods.items()):
            if not op.get("tags"):
                wrong.append(f"{method.upper()} {path} has no tag, so it has no group in the explorer")

    counted = sum(len(methods) for methods in paths.values())

    if wrong:
        print(f"The document describes {counted} operations, and is wrong about these:", file=sys.stderr)
        for line in wrong:
            print(f"  - {line}", file=sys.stderr)
        return 1

    print(f"The document describes {counted} operations, and says the right thing about each.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
