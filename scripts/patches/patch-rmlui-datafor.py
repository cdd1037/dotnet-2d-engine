#!/usr/bin/env python3
"""Order structural data-for before peers and guard generic draft attribute writes.
Pinned RmlUi 6.3 compatibility fix. Never edits the dependency checkout/archive.
Fail closed on upstream changes so the bounded host changes are reviewed again.
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
text = text.replace(old, new)
# Generic copied models are one-way: unchanged evaluated attributes preserve a
# local draft even when another field dirties the root. Legacy profiles return
# true from the hook and retain upstream behavior. Cache lifetime follows views.
text = text.replace('#include "DataViewDefault.h"', '#include "DataViewDefault.h"\n#include "ui_model.h"')
text = text.replace("""void DataViewCommon::Release()
{
	delete this;
}""", """void DataViewCommon::Release()
{
	gal_ui_model_release_view(this);
	delete this;
}""")
needle = '\t\tconst String value = variant.Get<String>();\n\t\tconst Variant* attribute = element->GetAttribute(attribute_name);'
assert text.count(needle) == 1
text = text.replace(needle, '\t\tconst String value = variant.Get<String>();\n\t\tif (!gal_ui_model_accept_attribute(this, element, attribute_name, value)) return false;\n\t\tconst Variant* attribute = element->GetAttribute(attribute_name);')
needle = '\t\tconst bool value = variant.Get<bool>();\n\t\tconst bool is_set = static_cast<bool>(element->GetAttribute(attribute_name));'
assert text.count(needle) == 1
text = text.replace(needle, '\t\tconst bool value = variant.Get<bool>();\n\t\tif (!gal_ui_model_accept_attribute(this, element, attribute_name, value ? "true" : "false")) return false;\n\t\tconst bool is_set = static_cast<bool>(element->GetAttribute(attribute_name));')
text = text.replace('#include "../../Include/', '#include "')
destination.parent.mkdir(parents=True, exist_ok=True)
if not destination.exists() or destination.read_text() != text:
    destination.write_text(text)
