"""Repo-owned calibration gates and minimal hook envelopes. Standard library only."""
import datetime
import hashlib
import json
import os
from pathlib import Path
import re
import shlex
import sys
import time
import uuid


def emit(root, event, **fields):
    directory = Path(root) / "evidence"
    directory.mkdir(mode=0o700, parents=True, exist_ok=True)
    target = directory / (str(time.time_ns()) + "-" + uuid.uuid4().hex + ".json")
    payload = dict(event=event, at=datetime.datetime.now(datetime.timezone.utc).isoformat(), **fields)
    temporary = target.with_suffix(".tmp")
    temporary.write_text(json.dumps(payload), encoding="utf-8")
    temporary.replace(target)


def hook(root, event, scenario, agent):
    data = json.load(sys.stdin)
    tool = data.get("tool_name", data.get("toolName", ""))
    inputs = data.get("tool_input", data.get("toolArgs", {}))
    if isinstance(inputs, str):
        try:
            inputs = json.loads(inputs)
        except ValueError:
            inputs = {}
    if not isinstance(inputs, dict):
        inputs = {}
    command = str(inputs.get("command", inputs.get("cmd", "")))
    try:
        words = shlex.split(command)
    except ValueError:
        words = []
    is_gate = (len(words) in (4, 5) and Path(words[0]).name == "python3"
               and Path(words[1]).name == "calibration-helper.py" and words[2] == "gate"
               and re.fullmatch(r"[a-z][a-z0-9-]*", words[3]) is not None
               and (len(words) == 4 or re.fullmatch(r"[0-9]+(?:\.[0-9]+)?", words[4]) is not None))
    background = (inputs.get("run_in_background") is True or inputs.get("mode") in ("async", "background")
                  or tool in ("Monitor", "collaborationspawn_agent"))
    # Only allowlisted structural facts leave the hook. Never save tool inputs,
    # messages, transcript paths, authentication output, or environment variables.
    child = data.get("agent_id", data.get("agentId", ""))
    call = data.get("tool_use_id", data.get("toolCallId", ""))
    questions = inputs.get("questions", [])
    if not isinstance(questions, list):
        questions = []
    if not questions and isinstance(inputs.get("question"), str):
        questions = [inputs]
    schema = inputs.get("requestedSchema", inputs.get("schema", {}))
    if not questions and isinstance(schema, dict) and isinstance(schema.get("properties"), dict):
        questions = [dict(options=q.get("enum", q.get("oneOf", q.get("anyOf", []))))
                     for q in schema["properties"].values() if isinstance(q, dict)]
    option_counts = [len(q.get("options", q.get("choices", [])))
                     if isinstance(q, dict) and isinstance(q.get("options", q.get("choices", [])), list) else 0
                     for q in questions]
    emit(root, event, tool=tool, gate=is_gate, background=background,
         child=hashlib.sha256(str(child).encode()).hexdigest()[:16] if child else "",
         call=hashlib.sha256(str(call).encode()).hexdigest()[:16] if call else "",
         model=data.get("model", ""), notification=data.get("notification_type", ""),
         questionCount=len(questions), optionCounts=option_counts)
    # A native approval is forced only for the synthetic scenario operation.
    approval = event == "PreToolUse" and (
        (scenario == "approval-command" and is_gate) or
        (scenario == "approval-edit" and tool.lower() in ("edit", "write", "apply_patch", "create", "str_replace_editor")))
    if approval and agent != "codex":
        if agent == "copilot":
            print(json.dumps({"permissionDecision": "ask", "permissionDecisionReason": "Calibration: approve the synthetic operation"}))
        else:
            print(json.dumps({"hookSpecificOutput": {"hookEventName": "PreToolUse", "permissionDecision": "ask",
                "permissionDecisionReason": "Calibration: approve the synthetic operation"}}))
    else:
        print("{}")


def gate(root, name, seconds):
    if not re.fullmatch(r"[a-z][a-z0-9-]*", name):
        raise ValueError("Invalid gate name")
    seconds = max(1, min(float(seconds), 600))
    emit(root, "GateStart", name=name)
    end = time.monotonic() + seconds
    release = Path(root) / ("release-" + name)
    while time.monotonic() < end and not release.exists() and not (Path(root) / "release-all").exists():
        time.sleep(0.1)
    emit(root, "GateEnd", name=name)
    print("Calibration gate completed.")


if __name__ == "__main__":
    # The helper is copied into the disposable project. Evidence stays beside it.
    root = Path(__file__).resolve().parent
    if sys.argv[1] == "hook":
        hook(root, *sys.argv[2:5])
    elif sys.argv[1] == "gate":
        gate(root, sys.argv[2], sys.argv[3] if len(sys.argv) > 3 else 120)
    else:
        raise ValueError("Expected hook or gate")
