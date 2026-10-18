#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Tiny file-protocol client for the UEBridge Lua mod (protocol 3).

No MCP, no sockets: writes <bridge>\\request.json, waits for <bridge>\\response.json.

    python uebridge_client.py --ping
    python uebridge_client.py --hello
    python uebridge_client.py --eval "return 1 + 1"
    python uebridge_client.py --file probe.lua

Set UEBRIDGE_DIR to override the default bridge folder.
"""
import argparse
import json
import os
import pathlib
import sys
import time
import uuid

DEFAULT_DIR = r"D:\SteamLibrary\steamapps\common\Palworld\Pal\Binaries\Win64\ue4ss\bridge"
BRIDGE = pathlib.Path(os.environ.get("UEBRIDGE_DIR", DEFAULT_DIR))
REQUEST = BRIDGE / "request.json"
RESPONSE = BRIDGE / "response.json"


def call(op="eval", code=None, calls=None, timeout=30.0):
    if not BRIDGE.is_dir():
        raise SystemExit(f"bridge folder missing: {BRIDGE}\n"
                         "UEBridge is only polled while the game is running; "
                         "restart the game after installing the mod.")
    req_id = uuid.uuid4().hex
    payload = {"id": req_id, "op": op}
    if code is not None:
        payload["code"] = code
    if calls is not None:
        payload["calls"] = calls
    tmp = BRIDGE / "request.tmp"
    tmp.write_text(json.dumps(payload, ensure_ascii=False), encoding="utf-8")
    os.replace(tmp, REQUEST)                     # atomic publish
    deadline = time.time() + timeout
    while time.time() < deadline:
        time.sleep(0.05)
        if not RESPONSE.exists():
            continue
        try:
            resp = json.loads(RESPONSE.read_text(encoding="utf-8"))
        except Exception:
            continue                              # partially written, retry
        if resp.get("id") != req_id:
            continue                              # stale response from an earlier call
        return resp
    raise SystemExit(f"timeout after {timeout:.0f}s waiting for response.json "
                     f"(is the game running with UEBridge loaded? see UE4SS.log)")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--ping", action="store_true")
    ap.add_argument("--hello", action="store_true")
    ap.add_argument("--eval", dest="code")
    ap.add_argument("--file")
    ap.add_argument("--timeout", type=float, default=30.0)
    a = ap.parse_args()
    if a.ping:
        op, code = "ping", None
    elif a.hello:
        op, code = "hello", None
    elif a.file:
        op, code = "eval", pathlib.Path(a.file).read_text(encoding="utf-8")
    elif a.code is not None:
        op, code = "eval", a.code
    else:
        ap.print_help()
        return 2
    resp = call(op, code, timeout=a.timeout)
    print(json.dumps(resp, ensure_ascii=False, indent=1)[:8000])
    return 0 if resp.get("ok") else 1


if __name__ == "__main__":
    sys.exit(main())
