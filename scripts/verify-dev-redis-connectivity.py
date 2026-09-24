"""Check development Redis through its host-published ports without exposing credentials."""
import argparse
from pathlib import Path
import re
import socket

parser = argparse.ArgumentParser()
parser.add_argument("--directory", type=Path, default=Path(__file__).resolve().parents[1] / ".redis-dev")
args = parser.parse_args()
roles = ("api-persistent", "api-volatile", "worker-persistent")
connections = {}
for role in roles:
    value = (args.directory / role / "connection").read_text(encoding="utf-8").strip()
    match = re.fullmatch(r"(127\.0\.0\.1):([0-9]+),user=" + role + r",password=([A-Fa-f0-9]{64})", value)
    if not match or not 0 < int(match[2]) <= 65535:
        raise SystemExit("REFUSED: unexpected development connection format")
    connections[role] = (match[1], int(match[2]), match[3])
if connections["api-persistent"][:2] != connections["worker-persistent"][:2] or connections["api-persistent"][:2] == connections["api-volatile"][:2]:
    raise SystemExit("REFUSED: development stores are not distinct")


def command(stream, *parts):
    fields = [str(part).encode("ascii") for part in parts]
    request = b"*" + str(len(fields)).encode("ascii") + b"\r\n"
    for field in fields:
        request += b"$" + str(len(field)).encode("ascii") + b"\r\n" + field + b"\r\n"
    stream.write(request)
    stream.flush()
    return stream.readline(4096)


def verify(role, wrong_store=None, wrong_password=False):
    host, port, password = connections[role]
    user = wrong_store or role
    if wrong_store:
        password = connections[wrong_store][2]
    if wrong_password:
        password = ("1" if password[0] == "0" else "0") + password[1:]
    with socket.create_connection((host, port), timeout=3) as client, client.makefile("rwb") as stream:
        if not command(stream, "PING").startswith(b"-NOAUTH "):
            raise RuntimeError("anonymous client was not refused")
        auth = command(stream, "AUTH", user, password)
        if wrong_store or wrong_password:
            if not auth.startswith(b"-WRONGPASS "):
                raise RuntimeError("invalid identity was not refused")
        elif auth != b"+OK\r\n" or command(stream, "PING") != b"+PONG\r\n":
            raise RuntimeError("application identity could not ping")
        elif not command(stream, "ACL", "LIST").startswith(b"-NOPERM "):
            raise RuntimeError("administrative command was not refused")


try:
    for role in roles:
        verify(role)
        verify(role, wrong_password=True)
    verify("api-persistent", wrong_store="api-volatile")
    verify("api-volatile", wrong_store="api-persistent")
except (OSError, RuntimeError):
    raise SystemExit("REFUSED: development Redis host connectivity/authentication contract failed") from None
print("PASS host-published Redis: three application identities; anonymous, wrong-password, wrong-store and administrative-command refusal")
