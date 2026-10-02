#!/usr/bin/env python3
"""Make structural data-for updates precede same-depth cloned-root data views.
Pinned RmlUi 6.3 compatibility fix. Never edits the dependency checkout/archive.
Fail closed on upstream changes so the one-line behavior change is reviewed again.
"""
import hashlib
from pathlib import Path
import sys

source, destination = map(Path, sys.argv[1:])
raw = source.read_bytes()
if hashlib.sha256(raw).hexdigest() != "b0e05af16620a8a346852d0817339f82be5bf30148dd271ecf1452f0ad93d47d":
    raise SystemExit("Unsupported upstream DataViewDefault.cpp; review and rebase data-for ordering patch")
text = raw.decode()
old = "DataViewFor::DataViewFor(Element* element) : DataView(element, 0) {}"
new = "DataViewFor::DataViewFor(Element* element) : DataView(element, -1000) {}"
assert text.count(old) == 1
text = text.replace(old, new).replace('#include "../../Include/', '#include "')
destination.parent.mkdir(parents=True, exist_ok=True)
if not destination.exists() or destination.read_text() != text:
    destination.write_text(text)
