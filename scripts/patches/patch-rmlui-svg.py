#!/usr/bin/env python3
"""Apply narrow host-boundary hooks to the pinned official RmlUi SVG cache.
Never edits the dependency checkout. The input digest intentionally fails closed
when the upstream pin changes: review the upstream implementation before rebasing.
"""
import hashlib
from pathlib import Path
import sys
source, destination = map(Path, sys.argv[1:])
raw = source.read_bytes()
if hashlib.sha256(raw).hexdigest() != '9141d75743798e499fad9c27c51a52274dfc9aec2151250c76a3ff885e547149':
    raise SystemExit('Unsupported upstream SVGCache.cpp; review and rebase host patch')
s = raw.decode()
s = s.replace('#include "../../Include/', '#include "')
s = s.replace('#include "../Core/ControlledLifetimeResource.h"', '#include "ControlledLifetimeResource.h"\n#include "ui_svg.h"')
s = s.replace('\tstruct SVGTexture {', '\tstruct SVGTexture {\n\t\tstd::shared_ptr<void> host_reservation;')
needle = '\t\tSVGKey key{std::move(move_from_id), dimensions, crop_to_content, colour};'
s = s.replace(needle, '''\t\t// GAL: only copied manifest sources, never inline data or arbitrary files.
\t\tif (source_type != SVGCache::SourceType::File) {
\t\t\tLog::Message(Log::LT_ERROR, "UI SVG: inline SVG is unsupported");
\t\t\treturn {};
\t\t}
\t\tmove_from_id = gal_svg::CacheKey(render_manager, source);
\t\tif (move_from_id.empty()) return {};
''' + needle)
s = s.replace('!GetFileInterface()->LoadFile(source, svg_data)', '!gal_svg::LoadData(render_manager, source, svg_data)')
s = s.replace('\t\t\tSVGTexture svg_texture;', '''\t\t\tSVGTexture svg_texture;
\t\t\t// GAL: reserve dimensions/variant/RGBA budget BEFORE LunaSVG allocates.
\t\t\tsvg_texture.host_reservation = gal_svg::Reserve(render_manager, source, dimensions, crop_to_content);
\t\t\tif (!svg_texture.host_reservation) { if (doc.textures.empty()) documents.erase(it_svg_document); return {}; }''')
needle = '\tvoid SVGCache::Initialize()'
s = s.replace(needle, '''\t// GAL: parse/raster/upload every manifest entry, even hidden/hover-only sources.
\tbool GalPreload(RenderManager& manager, const String& source)
\t{
\t\tauto handle = GetHandle(manager, source, source, SVGCache::File, {1, 1}, false, ColourbPremultiplied(255));
\t\treturn handle && handle->texture.GetDimensions() == Vector2i(1, 1);
\t}

''' + needle)
destination.parent.mkdir(parents=True, exist_ok=True)
if not destination.exists() or destination.read_text() != s:
    destination.write_text(s)
