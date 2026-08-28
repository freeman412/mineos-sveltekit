#!/usr/bin/env python3
"""
Minimal Minecraft Java protocol probe for the local lab.

Answers two questions that no unit test can, without needing a game client:

1. **Where does a hostname go?** The handshake packet carries the address the
   player typed, which is exactly what Velocity's forced-hosts routes on. With
   `ping-passthrough = "ALL"` the status reply comes from the chosen backend, so
   its MOTD tells you which one you reached.

2. **Is a secured backend really closed?** Status pings are never blocked, so
   this needs a login attempt. A backend running Velocity modern forwarding
   challenges every connection on the `velocity:player_info` channel; anything
   that is not the proxy cannot answer, and gets refused.

Usage:
    python3 mcprobe.py status <host> <port> <hostname-to-claim> [protocol]
    python3 mcprobe.py login  <host> <port> <hostname-to-claim> [protocol] [username]

Read the protocol number from a status ping (`version.protocol`) and pass it to
`login` — a mismatch is rejected as "Outdated client!" before the server ever
reaches the forwarding check, which looks like a security result and is not one.
"""
import json
import socket
import struct
import sys
import uuid

DEFAULT_PROTOCOL = 767  # 1.21.1; override per server.


# ---- encoding ------------------------------------------------------------

def write_varint(value: int) -> bytes:
    out = b""
    while True:
        byte = value & 0x7F
        value >>= 7
        out += struct.pack("B", byte | (0x80 if value else 0))
        if not value:
            return out


def write_string(text: str) -> bytes:
    raw = text.encode("utf-8")
    return write_varint(len(raw)) + raw


def packet(packet_id: int, payload: bytes) -> bytes:
    body = write_varint(packet_id) + payload
    return write_varint(len(body)) + body


def handshake(server_address: str, port: int, protocol: int, next_state: int) -> bytes:
    """next_state: 1 = status, 2 = login."""
    return packet(
        0x00,
        write_varint(protocol)
        + write_string(server_address)   # the routing key — what the player "typed"
        + struct.pack(">H", port)
        + write_varint(next_state),
    )


# ---- decoding ------------------------------------------------------------

class Reader:
    def __init__(self, sock: socket.socket):
        self.sock = sock

    def byte(self) -> int:
        chunk = self.sock.recv(1)
        if not chunk:
            raise EOFError("connection closed by peer")
        return chunk[0]

    def varint(self) -> int:
        result = 0
        for shift in range(0, 35, 7):
            current = self.byte()
            result |= (current & 0x7F) << shift
            if not current & 0x80:
                return result
        raise ValueError("varint too long")

    def exact(self, count: int) -> bytes:
        buf = b""
        while len(buf) < count:
            chunk = self.sock.recv(count - len(buf))
            if not chunk:
                raise EOFError("connection closed mid-packet")
            buf += chunk
        return buf


def read_varint_from(buf: bytes, offset: int):
    result = 0
    for shift in range(0, 35, 7):
        current = buf[offset]
        offset += 1
        result |= (current & 0x7F) << shift
        if not current & 0x80:
            return result, offset
    raise ValueError("varint too long")


def read_packet(reader: Reader):
    """Reads one whole packet as (id, payload). Buffering the entire body is what
    keeps the stream in sync across a multi-packet login exchange."""
    length = reader.varint()
    body = reader.exact(length)
    packet_id, offset = read_varint_from(body, 0)
    return packet_id, body[offset:]


# ---- probes --------------------------------------------------------------

def status(host: str, port: int, claimed_hostname: str,
           protocol: int = DEFAULT_PROTOCOL, timeout: float = 10.0) -> dict:
    """Server list ping. Returns the parsed status JSON."""
    with socket.create_connection((host, port), timeout=timeout) as sock:
        sock.settimeout(timeout)
        sock.sendall(handshake(claimed_hostname, port, protocol, 1))
        sock.sendall(packet(0x00, b""))

        reader = Reader(sock)
        packet_id, payload = read_packet(reader)
        if packet_id != 0x00:
            raise ValueError(f"unexpected status packet id 0x{packet_id:02x}")
        length, offset = read_varint_from(payload, 0)
        return json.loads(payload[offset:offset + length].decode("utf-8"))


def login(host: str, port: int, claimed_hostname: str, username: str = "ProbeBot",
          protocol: int = DEFAULT_PROTOCOL, timeout: float = 10.0) -> dict:
    """
    Attempt a login, answering any Login Plugin Request the way a non-proxy must.

    Outcomes:
      REJECTED              - refused; `challenged_on` names the channel it demanded
      ACCEPTED              - let in (with challenged_on null, nothing was verified)
      encryption-requested  - the server authenticates players itself (online-mode=true)

    Caveat: packet 0x03 is Set Compression. After it the stream is compressed and
    this probe cannot read further, so an ACCEPTED here may be premature — set
    `network-compression-threshold=-1` on the server and check its log.
    """
    with socket.create_connection((host, port), timeout=timeout) as sock:
        sock.settimeout(timeout)
        sock.sendall(handshake(claimed_hostname, port, protocol, 2))
        sock.sendall(packet(0x00, write_string(username) + uuid.uuid4().bytes))

        reader = Reader(sock)
        challenged = None

        for _ in range(6):
            try:
                packet_id, payload = read_packet(reader)
            except (EOFError, IndexError):
                return {"outcome": "closed", "challenged_on": challenged,
                        "message": "server closed the connection"}

            if packet_id == 0x04:                        # Login Plugin Request
                message_id, offset = read_varint_from(payload, 0)
                channel_len, offset = read_varint_from(payload, offset)
                challenged = payload[offset:offset + channel_len].decode("utf-8", errors="replace")
                # "cannot answer" — the only honest reply from a non-proxy.
                sock.sendall(packet(0x02, write_varint(message_id) + b"\x00"))
                continue
            if packet_id == 0x00:                        # Disconnect
                length, offset = read_varint_from(payload, 0)
                return {"outcome": "REJECTED", "challenged_on": challenged,
                        "message": payload[offset:offset + length].decode("utf-8", errors="replace")}
            if packet_id == 0x01:                        # Encryption Request
                return {"outcome": "encryption-requested", "challenged_on": challenged,
                        "message": "server authenticates players itself (online-mode=true)"}
            if packet_id in (0x02, 0x03):
                return {"outcome": "ACCEPTED", "challenged_on": challenged,
                        "message": f"server let us in (packet 0x{packet_id:02x})"}

        return {"outcome": "gave-up", "challenged_on": challenged, "message": "too many packets"}


def _motd(status_json: dict) -> str:
    description = status_json.get("description", "")
    if isinstance(description, dict):
        text = description.get("text", "")
        for extra in description.get("extra", []) or []:
            if isinstance(extra, dict):
                text += extra.get("text", "")
        return text or json.dumps(description)
    return str(description)


if __name__ == "__main__":
    if len(sys.argv) < 5:
        raise SystemExit(__doc__)

    mode, host, port, claimed = sys.argv[1], sys.argv[2], int(sys.argv[3]), sys.argv[4]
    protocol = int(sys.argv[5]) if len(sys.argv) > 5 else DEFAULT_PROTOCOL

    if mode == "status":
        result = status(host, port, claimed, protocol)
        print(json.dumps({
            "motd": _motd(result),
            "version": result.get("version", {}).get("name"),
            "protocol": result.get("version", {}).get("protocol"),
            "players": result.get("players", {}).get("online"),
        }, indent=2))
    elif mode == "login":
        username = sys.argv[6] if len(sys.argv) > 6 else "ProbeBot"
        print(json.dumps(login(host, port, claimed, username, protocol), indent=2))
    else:
        raise SystemExit(__doc__)
