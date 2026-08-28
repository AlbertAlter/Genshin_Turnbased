"""Export a Photoshop document into Unity-friendly PNG layers and layout.json.

Usage:
    python export_psd_layout.py "s19 手机壳.psd"
    python export_psd_layout.py "design.psd" --all-layers

The default output directory is ``PSD_Export/<psd-name>/`` beside this script.
Install the parser once with: ``python -m pip install -r requirements_psd.txt``.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path


SCRIPT_DIR = Path(__file__).resolve().parent
LOCAL_PACKAGES = SCRIPT_DIR / "_python_libs"
if LOCAL_PACKAGES.is_dir():
    sys.path.insert(0, str(LOCAL_PACKAGES))

try:
    from PIL import Image, ImageDraw, ImageFont
    from psd_tools import PSDImage
except ImportError as exc:
    raise SystemExit(
        "Missing psd-tools. Run: python -m pip install -r "
        f'"{SCRIPT_DIR / "requirements_psd.txt"}"'
    ) from exc


def enum_text(value):
    if value is None or isinstance(value, (str, int, float, bool)):
        return value
    return str(value)


def safe_filename(name: str, fallback: str) -> str:
    cleaned = re.sub(r"[<>:\"/\\|?*\x00-\x1f]", "_", name).strip(" .")
    cleaned = re.sub(r"\s+", "_", cleaned)
    return (cleaned[:70] or fallback) + ".png"


def layer_image(layer):
    """Return a bbox-sized raster, preferring rendered effects when available."""
    try:
        image = layer.composite()
        if image is not None:
            return image, "composite"
    except Exception:
        pass

    try:
        image = layer.topil()
        if image is not None:
            return image, "raw_pixels"
    except Exception:
        pass
    return None, None


def export_layer(layer, depth, state, export_hidden):
    state["index"] += 1
    index = state["index"]
    bbox = list(layer.bbox)
    node = {
        "name": layer.name,
        "depth": depth,
        "kind": layer.kind,
        "visible": layer.is_visible(),
        "opacity": layer.opacity,
        "blend_mode": enum_text(layer.blend_mode),
        "bbox": bbox,
        "clipping": bool(layer.clipping),
        "is_group": layer.is_group(),
        "has_mask": getattr(layer, "has_mask", lambda: False)(),
        "has_vector_mask": getattr(layer, "has_vector_mask", lambda: False)(),
        "has_effects": bool(getattr(layer, "effects", None)),
        "has_smart_object": getattr(layer, "smart_object", None) is not None,
    }

    if layer.kind == "type":
        node["text"] = getattr(layer, "text", None)

    if layer.is_group():
        node["children"] = [
            export_layer(child, depth + 1, state, export_hidden) for child in layer
        ]
        return node

    should_export = export_hidden or layer.is_visible()
    if should_export and layer.width > 0 and layer.height > 0:
        image, quality = layer_image(layer)
        if image is not None:
            filename = f"{index:04d}_" + safe_filename(layer.name, f"layer_{index:04d}")
            output_path = state["layers_dir"] / filename
            image.save(output_path)
            node["asset"] = f"layers/{filename}"
            node["raster_quality"] = quality
            state["exported"] += 1
        else:
            node["export_error"] = "No raster representation was available."
            state["failed"] += 1
    return node


def flatten(nodes):
    for node in nodes:
        yield node
        yield from flatten(node.get("children", []))


def make_contact_sheet(nodes, output_dir):
    rows = []
    for node in flatten(nodes):
        asset = node.get("asset")
        if not asset:
            continue
        image = Image.open(output_dir / asset).convert("RGBA")
        image.thumbnail((220, 150), Image.Resampling.LANCZOS)
        rows.append((node["name"], image.copy()))
        if len(rows) >= 40:
            break

    if not rows:
        return None

    width, row_height = 900, 175
    sheet = Image.new("RGB", (width, len(rows) * row_height), "#20242b")
    draw = ImageDraw.Draw(sheet)
    font = ImageFont.load_default()
    for i, (name, image) in enumerate(rows):
        y = i * row_height
        cell = Image.new("RGBA", (240, 155), (48, 52, 60, 255))
        cell.alpha_composite(
            image, ((240 - image.width) // 2, (155 - image.height) // 2)
        )
        sheet.paste(cell.convert("RGB"), (10, y + 10))
        draw.text((270, y + 25), name, fill="white", font=font)
    path = output_dir / "layers_contact_sheet.jpg"
    sheet.save(path, quality=90)
    return path.name


def export_psd(source: Path, output_dir: Path, export_hidden: bool):
    output_dir.mkdir(parents=True, exist_ok=True)
    layers_dir = output_dir / "layers"
    layers_dir.mkdir(parents=True, exist_ok=True)

    psd = PSDImage.open(source)
    state = {"index": 0, "exported": 0, "failed": 0, "layers_dir": layers_dir}
    tree = [export_layer(layer, 0, state, export_hidden) for layer in psd]
    nodes = list(flatten(tree))

    composite_path = output_dir / "composite.png"
    composite = psd.composite(force=False)
    if composite is not None:
        composite.save(composite_path)

    counts = {}
    for node in nodes:
        counts[node["kind"]] = counts.get(node["kind"], 0) + 1

    contact_sheet = make_contact_sheet(tree, output_dir)
    manifest = {
        "schema_version": 1,
        "source": str(source.resolve()),
        "document": {
            "width": psd.width,
            "height": psd.height,
            "color_mode": enum_text(psd.color_mode),
            "depth": psd.depth,
        },
        "top_level_layers": len(tree),
        "total_layers": len(nodes),
        "kind_counts": counts,
        "visible_layers": sum(1 for node in nodes if node["visible"]),
        "groups": sum(1 for node in nodes if node["is_group"]),
        "exported_layers": state["exported"],
        "failed_layer_exports": state["failed"],
        "text_layers": [
            {"name": n["name"], "text": n.get("text"), "bbox": n["bbox"]}
            for n in nodes
            if n["kind"] == "type"
        ],
        "smart_object_layers": [n["name"] for n in nodes if n["has_smart_object"]],
        "masked_layers": [
            n["name"] for n in nodes if n["has_mask"] or n["has_vector_mask"]
        ],
        "effect_layers": [n["name"] for n in nodes if n["has_effects"]],
        "outputs": {
            "composite": composite_path.name if composite_path.exists() else None,
            "contact_sheet": contact_sheet,
            "layer_folder": "layers",
        },
        "layers": tree,
    }
    manifest_path = output_dir / "layout.json"
    manifest_path.write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    return manifest_path, manifest


def parse_args():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("psd", type=Path, help="PSD file to parse")
    parser.add_argument("--output", type=Path, help="Output directory")
    parser.add_argument(
        "--all-layers",
        action="store_true",
        help="Rasterize hidden layers too (larger and slower).",
    )
    return parser.parse_args()


def main():
    args = parse_args()
    source = args.psd
    if not source.is_absolute():
        source = (Path.cwd() / source).resolve()
    if not source.is_file():
        raise SystemExit(f"PSD not found: {source}")

    output = args.output
    if output is None:
        output = SCRIPT_DIR / "PSD_Export" / source.stem
    manifest_path, manifest = export_psd(source, output.resolve(), args.all_layers)
    print(f"Layout: {manifest_path}")
    print(
        f"Layers: {manifest['total_layers']}, exported: {manifest['exported_layers']}, "
        f"failed: {manifest['failed_layer_exports']}"
    )


if __name__ == "__main__":
    main()
