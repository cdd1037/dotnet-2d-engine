#!/usr/bin/env bash
# Recreate omitted generated room art; never download tools or fonts.
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ -f assets/room-a.bmp && -f assets/room-b.bmp ]]; then exit 0; fi
if ! command -v python3 >/dev/null; then echo 'Room assets require Python 3 with Pillow and DejaVu Sans fonts; see docs/GENERATED_ASSETS.md' >&2; exit 2; fi
python3 - <<'PY'
from pathlib import Path
try:
    from PIL import Image
except ImportError:
    raise SystemExit('Install Pillow for Python 3 before generating room assets (no automatic install).')
for name in ('DejaVuSans.ttf', 'DejaVuSans-Bold.ttf'):
    path = Path('/usr/share/fonts/truetype/dejavu') / name
    if not path.is_file():
        raise SystemExit(f'Required font is missing: {path}; see docs/GENERATED_ASSETS.md')
PY
python3 scripts/generate-room-assets.py
