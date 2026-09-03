#!/usr/bin/env python3
"""Download Genshin Impact talent icons from the Genshin Impact Wiki.

The script deliberately treats the ``character`` field in each Wiki file page
as the source of truth.  Character IDs are always read from
``Charts/Characters.xlsx``; they are not hard-coded here.

Default output layout::

    art_assets/Skill_Icon/
      1009_T01.png
      1009_T02.png
      Normal/Bow Anemo.png
      ...
      icon_manifest.json
      icon_manifest.csv
      _unmatched/<original Wiki filename>

English Wiki character names are matched directly against the workbook's
``NameID`` column.  Spaces in NameID are represented by underscores.

Character icon numbering is semantic rather than alphabetical: elemental
skill is always ``T01``, elemental burst is always ``T02``, and passive
talents are continuous from ``T03``. Extra stance/change icons follow the
passives. Generic weapon Normal Attack icons keep their original Wiki names.

Only Python's standard library is required.
"""

from __future__ import annotations

import argparse
import csv
import gzip
import html
import json
import os
import posixpath
import re
import sys
import time
import unicodedata
import urllib.error
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET
import zipfile
from collections import defaultdict
from pathlib import Path
from typing import Any, Iterable


WIKI_API = "https://genshin-impact.fandom.com/api.php"
GENSHIN_DB_ROOT = (
    "https://raw.githubusercontent.com/theBowja/genshin-db-dist/"
    "main/data/gzips"
)
DEFAULT_CATEGORIES = ("Category:Talent Icons", "Category:Old Talent Icons")
USER_AGENT = (
    "GenshinTurnbasedTalentIconDownloader/1.0 "
    "(personal fan-project asset organizer)"
)

WEAPON_NORMAL_RE = re.compile(
    r"^(Sword|Claymore|Polearm|Bow|Catalyst) "
    r"(Pyro|Hydro|Electro|Cryo|Anemo|Dendro|Geo|Unaligned)\.png$",
    re.IGNORECASE,
)
FIELD_RE = re.compile(
    r"^\s*\|\s*(character|characters|type|talent\s*type)\s*=\s*(.*?)\s*$",
    re.IGNORECASE | re.MULTILINE,
)
INVALID_WINDOWS_CHARS_RE = re.compile(r'[<>:"/\\|?*\x00-\x1f]')


class DownloadError(RuntimeError):
    """An expected remote-data or validation failure."""


def normalize_name(value: str) -> str:
    value = unicodedata.normalize("NFKC", value or "").casefold()
    return "".join(ch for ch in value if ch.isalnum())


def safe_filename(value: str) -> str:
    value = INVALID_WINDOWS_CHARS_RE.sub("_", value).strip(" .")
    return value or "unnamed"


def column_index(reference: str) -> int:
    letters = "".join(ch for ch in reference if ch.isalpha()).upper()
    result = 0
    for letter in letters:
        result = result * 26 + ord(letter) - ord("A") + 1
    return result - 1


def read_xlsx_rows(path: Path, sheet_name: str) -> list[list[Any]]:
    """Read one XLSX worksheet using only zipfile and ElementTree."""
    if not path.is_file():
        raise DownloadError(f"角色表不存在：{path}")

    ns_main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main"
    ns_rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships"
    ns_pkg_rel = "http://schemas.openxmlformats.org/package/2006/relationships"

    with zipfile.ZipFile(path) as archive:
        workbook = ET.fromstring(archive.read("xl/workbook.xml"))
        relationship_id = None
        for node in workbook.findall(f".//{{{ns_main}}}sheet"):
            if node.attrib.get("name") == sheet_name:
                relationship_id = node.attrib.get(f"{{{ns_rel}}}id")
                break
        if not relationship_id:
            raise DownloadError(f"角色表缺少工作表：{sheet_name}")

        rels = ET.fromstring(archive.read("xl/_rels/workbook.xml.rels"))
        target = None
        for node in rels.findall(f"{{{ns_pkg_rel}}}Relationship"):
            if node.attrib.get("Id") == relationship_id:
                target = node.attrib.get("Target")
                break
        if not target:
            raise DownloadError(f"无法定位工作表：{sheet_name}")

        if target.startswith("/"):
            target_path = target.lstrip("/")
        else:
            target_path = posixpath.normpath(posixpath.join("xl", target))
        shared_strings: list[str] = []
        if "xl/sharedStrings.xml" in archive.namelist():
            shared_root = ET.fromstring(archive.read("xl/sharedStrings.xml"))
            for item in shared_root.findall(f"{{{ns_main}}}si"):
                shared_strings.append(
                    "".join(node.text or "" for node in item.iter(f"{{{ns_main}}}t"))
                )

        sheet = ET.fromstring(archive.read(target_path))
        rows: list[list[Any]] = []
        for row_node in sheet.findall(f".//{{{ns_main}}}row"):
            values: dict[int, Any] = {}
            for cell in row_node.findall(f"{{{ns_main}}}c"):
                index = column_index(cell.attrib.get("r", "A1"))
                cell_type = cell.attrib.get("t")
                value_node = cell.find(f"{{{ns_main}}}v")
                if cell_type == "inlineStr":
                    value = "".join(
                        node.text or "" for node in cell.iter(f"{{{ns_main}}}t")
                    )
                elif value_node is None:
                    value = ""
                elif cell_type == "s":
                    value = shared_strings[int(value_node.text or "0")]
                elif cell_type == "b":
                    value = value_node.text == "1"
                else:
                    raw = value_node.text or ""
                    try:
                        numeric = float(raw)
                        value = int(numeric) if numeric.is_integer() else numeric
                    except ValueError:
                        value = raw
                values[index] = value
            width = max(values, default=-1) + 1
            rows.append([values.get(index, "") for index in range(width)])
        return rows


def load_character_table(
    path: Path,
) -> tuple[dict[str, int], dict[str, int], dict[int, str]]:
    rows = read_xlsx_rows(path, "Sheet1")
    if not rows:
        raise DownloadError("Characters.xlsx / Sheet1 为空")
    headers = {str(value).strip(): index for index, value in enumerate(rows[0])}
    missing = {"CharacterID", "NameID", "Name"} - headers.keys()
    if missing:
        raise DownloadError(f"Characters.xlsx 缺少列：{', '.join(sorted(missing))}")

    by_name: dict[str, int] = {}
    by_name_id: dict[str, int] = {}
    by_id: dict[int, str] = {}
    for row_number, row in enumerate(rows[1:], start=2):
        id_index = headers["CharacterID"]
        name_id_index = headers["NameID"]
        name_index = headers["Name"]
        if id_index >= len(row) or name_id_index >= len(row) or name_index >= len(row):
            continue
        if (
            row[id_index] in (None, "")
            or row[name_id_index] in (None, "")
            or row[name_index] in (None, "")
        ):
            continue
        try:
            character_id = int(row[id_index])
        except (TypeError, ValueError) as exc:
            raise DownloadError(f"角色表第 {row_number} 行 CharacterID 无效") from exc
        name = str(row[name_index]).strip()
        name_id = str(row[name_id_index]).strip()
        key = normalize_name(name)
        name_id_key = normalize_name(name_id)
        if key in by_name and by_name[key] != character_id:
            raise DownloadError(f"角色表存在重名角色：{name}")
        if character_id in by_id and by_id[character_id] != name:
            raise DownloadError(f"角色表存在重复 ID：{character_id}")
        if name_id_key in by_name_id and by_name_id[name_id_key] != character_id:
            raise DownloadError(f"角色表存在重复 NameID：{name_id}")
        by_name[key] = character_id
        by_name_id[name_id_key] = character_id
        by_id[character_id] = name
    return by_name, by_name_id, by_id


def http_bytes(
    url: str,
    *,
    params: dict[str, Any] | None = None,
    timeout: float = 45,
    retries: int = 4,
) -> bytes:
    if params:
        url += ("&" if "?" in url else "?") + urllib.parse.urlencode(params)
    request = urllib.request.Request(
        url,
        headers={"User-Agent": USER_AGENT, "Accept-Encoding": "gzip"},
    )
    last_error: Exception | None = None
    for attempt in range(retries):
        try:
            with urllib.request.urlopen(request, timeout=timeout) as response:
                body = response.read()
                if response.headers.get("Content-Encoding", "").lower() == "gzip":
                    body = gzip.decompress(body)
                return body
        except (OSError, urllib.error.URLError, urllib.error.HTTPError) as exc:
            last_error = exc
            if isinstance(exc, urllib.error.HTTPError) and exc.code in (400, 401, 403, 404):
                break
            if attempt + 1 < retries:
                time.sleep(min(2 ** attempt, 8))
    raise DownloadError(f"下载失败：{url}\n{last_error}")


def http_json(url: str, params: dict[str, Any] | None = None) -> dict[str, Any]:
    try:
        return json.loads(http_bytes(url, params=params).decode("utf-8-sig"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise DownloadError(f"远端返回的不是有效 JSON：{url}") from exc


def recursive_talent_records(value: Any) -> Iterable[dict[str, Any]]:
    if isinstance(value, dict):
        if "id" in value and "name" in value and "combat1" in value:
            yield value
        for child in value.values():
            yield from recursive_talent_records(child)
    elif isinstance(value, list):
        for child in value:
            yield from recursive_talent_records(child)


def download_genshin_db_dataset(language: str) -> Any:
    filename = f"{language}-talents.min.json.gzip"
    payload = http_bytes(f"{GENSHIN_DB_ROOT}/{filename}")
    try:
        decoded = gzip.decompress(payload)
    except gzip.BadGzipFile:
        decoded = payload
    try:
        return json.loads(decoded.decode("utf-8-sig"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise DownloadError(f"无法解析 genshin-db 数据：{filename}") from exc


TALENT_SLOTS = {
    "combat1": (10, "Normal Attack", "普通攻击"),
    "combatsp": (15, "Alternate Sprint", "替代冲刺"),
    "combatju": (16, "Special Movement", "特殊移动"),
    "combat2": (20, "Elemental Skill", "元素战技"),
    "combat3": (30, "Elemental Burst", "元素爆发"),
    "passive1": (40, "1st Ascension Passive", "突破天赋1"),
    "passive2": (50, "4th Ascension Passive", "突破天赋2"),
    "passive3": (60, "Utility Passive", "探索天赋"),
    "passive4": (70, "Additional Passive", "附加天赋"),
}


def clean_game_text(value: str) -> str:
    value = re.sub(r"<[^>]+>", "", value or "")
    value = re.sub(r"\{LINK#[^}]+}", "", value)
    value = value.replace("{/LINK}", "")
    return html.unescape(value).strip()


def linked_talent_names(value: str) -> list[str]:
    result: list[str] = []
    for match in re.finditer(r"\{LINK#[^}]+}(.*?)\{/LINK}", value or "", re.DOTALL):
        name = clean_game_text(match.group(1))
        if name:
            result.append(name)
    return list(dict.fromkeys(result))


def load_talent_metadata(
    local_by_name_id: dict[str, int],
) -> dict[tuple[int, str], dict[str, Any]]:
    english = {
        int(item["id"]): item
        for item in recursive_talent_records(download_genshin_db_dataset("english"))
    }
    chinese = {
        int(item["id"]): item
        for item in recursive_talent_records(
            download_genshin_db_dataset("chinesesimplified")
        )
    }
    metadata: dict[tuple[int, str], dict[str, Any]] = {}
    for talent_id, english_character in english.items():
        character_id = local_by_name_id.get(normalize_name(str(english_character["name"])))
        if character_id is None:
            continue
        chinese_character = chinese.get(talent_id, {})
        for slot, (order, type_en, type_cn) in TALENT_SLOTS.items():
            talent_en = english_character.get(slot)
            if not isinstance(talent_en, dict) or not talent_en.get("name"):
                continue
            talent_cn = chinese_character.get(slot, {})
            if not isinstance(talent_cn, dict):
                talent_cn = {}
            primary_name = str(talent_en["name"]).strip()
            base = {
                "talent_slot": slot,
                "talent_order": order,
                "talent_type": type_en,
                "talent_type_cn": type_cn,
                "talent_name_en": primary_name,
                "talent_name_cn": str(talent_cn.get("name", "")).strip(),
                "parent_talent_name_en": "",
                "parent_talent_name_cn": "",
                "description_en": clean_game_text(str(talent_en.get("description", ""))),
                "description_cn": clean_game_text(str(talent_cn.get("description", ""))),
            }
            metadata[(character_id, normalize_name(primary_name))] = base

            # Stance changes and enhanced buttons often have their own icon and
            # are named through LINK tags inside the parent talent description.
            linked_en = linked_talent_names(str(talent_en.get("descriptionRaw", "")))
            linked_cn = linked_talent_names(str(talent_cn.get("descriptionRaw", "")))
            for index, linked_name in enumerate(linked_en):
                linked = dict(base)
                linked["talent_name_en"] = linked_name
                linked["talent_name_cn"] = linked_cn[index] if index < len(linked_cn) else ""
                linked["parent_talent_name_en"] = primary_name
                linked["parent_talent_name_cn"] = str(talent_cn.get("name", "")).strip()
                linked["talent_order"] = order + 1
                metadata[(character_id, normalize_name(linked_name))] = linked
    return metadata


def talent_name_from_filename(filename: str) -> str:
    name = Path(filename).stem
    return name[7:].strip() if name.casefold().startswith("talent ") else name.strip()


def load_aliases(path: Path | None) -> dict[str, str | int]:
    if path is None:
        return {}
    try:
        value = json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, json.JSONDecodeError) as exc:
        raise DownloadError(f"无法读取别名文件：{path}") from exc
    if not isinstance(value, dict):
        raise DownloadError("别名文件必须是 JSON 对象")
    return {normalize_name(str(key)): item for key, item in value.items()}


def extract_revision_text(page: dict[str, Any]) -> str:
    revisions = page.get("revisions") or []
    if not revisions:
        return ""
    revision = revisions[0]
    slots = revision.get("slots") or {}
    main = slots.get("main") or {}
    return str(main.get("content") or revision.get("content") or "")


def wiki_file_records(categories: Iterable[str]) -> list[dict[str, Any]]:
    records: dict[str, dict[str, Any]] = {}
    for category in categories:
        continuation: dict[str, Any] = {}
        while True:
            params: dict[str, Any] = {
                "action": "query",
                "generator": "categorymembers",
                "gcmtitle": category,
                "gcmtype": "file",
                "gcmlimit": "50",
                "prop": "imageinfo|revisions|categories",
                "iiprop": "url|mime|size|sha1",
                "rvprop": "content",
                "rvslots": "main",
                "cllimit": "max",
                "format": "json",
                "formatversion": "2",
                "origin": "*",
            }
            params.update(continuation)
            response = http_json(WIKI_API, params)
            if "error" in response:
                raise DownloadError(
                    f"Wiki API 错误：{response['error'].get('info', response['error'])}"
                )
            for page in response.get("query", {}).get("pages", []):
                title = str(page.get("title", ""))
                if not title.startswith("File:"):
                    continue
                page["_source_category"] = category
                records[title] = page
            continuation = response.get("continue") or {}
            if not continuation:
                break
    return list(records.values())


def clean_wikitext_value(value: str) -> str:
    value = html.unescape(value)
    value = re.sub(r"<!--.*?-->", "", value, flags=re.DOTALL)
    value = re.sub(r"\[\[([^]|]+)\|([^]]+)]]", r"\2", value)
    value = re.sub(r"\[\[([^]]+)]]", r"\1", value)
    value = re.sub(r"<[^>]+>", " ", value)
    value = re.sub(r"\{\{[^{}]*\|\s*([^|{}]+?)\s*}}", r"\1", value)
    return value.strip()


def parse_wiki_fields(wikitext: str) -> tuple[list[str], str]:
    characters: list[str] = []
    talent_type = ""
    for match in FIELD_RE.finditer(wikitext):
        key = re.sub(r"\s+", "", match.group(1).casefold())
        value = clean_wikitext_value(match.group(2))
        if key in ("character", "characters"):
            for item in re.split(r"\s*(?:,|;|/|<br\s*/?>|\n)\s*", value):
                item = item.strip()
                if item and item.casefold() not in ("none", "n/a"):
                    characters.append(item)
        elif key in ("type", "talenttype") and not talent_type:
            talent_type = value
    return list(dict.fromkeys(characters)), talent_type


def talent_sort_key(record: dict[str, Any]) -> tuple[int, str, str]:
    return (
        int(record.get("talent_order", 999)),
        str(record.get("source_filename", "")).casefold(),
        str(record.get("sha1", "")),
    )


def talent_role(record: dict[str, Any]) -> str:
    """Return the fixed project naming role for one character icon."""
    slot = str(record.get("talent_slot", "")).casefold()
    is_linked_variant = bool(record.get("parent_talent_name_en"))
    if not is_linked_variant:
        if slot == "combat2":
            return "skill"
        if slot == "combat3":
            return "burst"
        if slot.startswith("passive"):
            return "passive"

    # Wiki fields/categories are the fallback when a newly released icon has
    # not reached genshin-db yet. In that case the first deterministic match
    # becomes the main button and further matches are treated as variants.
    value = normalize_name(str(record.get("talent_type", "")))
    categories = normalize_name(" ".join(record.get("categories", [])))
    combined = value + categories
    if "elementalskill" in combined:
        return "skill"
    if "elementalburst" in combined:
        return "burst"
    if "passive" in combined:
        return "passive"
    return "extra"


def enrich_talent_record(
    item: dict[str, Any],
    character_id: int,
    talent_metadata: dict[tuple[int, str], dict[str, Any]],
) -> None:
    talent_name = talent_name_from_filename(str(item["source_filename"]))
    metadata = talent_metadata.get((character_id, normalize_name(talent_name)))
    if metadata:
        item.update(metadata)
    item.setdefault("talent_slot", "")
    item.setdefault("talent_order", 999)
    item.setdefault("talent_type_cn", "")
    item.setdefault("talent_name_en", talent_name)
    item.setdefault("talent_name_cn", "")
    item.setdefault("parent_talent_name_en", "")
    item.setdefault("parent_talent_name_cn", "")
    item.setdefault("description_en", "")
    item.setdefault("description_cn", "")
    item["talent_page"] = (
        "https://genshin-impact.fandom.com/wiki/"
        + urllib.parse.quote(talent_name.replace(" ", "_"))
    )


def resolve_character_id(
    source_name: str,
    local_by_name: dict[str, int],
    local_by_name_id: dict[str, int],
    local_by_id: dict[int, str],
    aliases: dict[str, str | int],
) -> tuple[int | None, str | None]:
    key = normalize_name(source_name)
    override = aliases.get(key)
    if isinstance(override, int):
        return (override, local_by_id.get(override)) if override in local_by_id else (None, None)
    if isinstance(override, str):
        local_name = override.strip()
    else:
        direct_id = local_by_name_id.get(key)
        if direct_id is not None:
            return direct_id, local_by_id.get(direct_id)
        local_name = ""
    character_id = local_by_name.get(normalize_name(local_name))
    return character_id, local_by_id.get(character_id) if character_id else None


def page_to_record(page: dict[str, Any]) -> dict[str, Any] | None:
    image_info = page.get("imageinfo") or []
    if not image_info:
        return None
    info = image_info[0]
    title = str(page.get("title", ""))
    filename = title.removeprefix("File:")
    characters, talent_type = parse_wiki_fields(extract_revision_text(page))
    category_names = [
        str(item.get("title", "")).removeprefix("Category:")
        for item in page.get("categories") or []
    ]
    # Most file pages express ownership through a category such as
    # "Amber Talent Icons", rather than repeating |character= in wikitext.
    # Keep the explicit field first, then add category-derived candidates.
    reserved = {
        "talent",
        "old",
        "unknown",
        "normalattack",
        "elementalskill",
        "elementalburst",
        "passive",
        "utilitypassive",
        "talenticonsbycharacter",
    }
    for category_name in category_names:
        suffix = " Talent Icons"
        if not category_name.endswith(suffix):
            continue
        candidate = category_name[: -len(suffix)].strip()
        if candidate and normalize_name(candidate) not in reserved:
            characters.append(candidate)
    characters = list(dict.fromkeys(characters))
    return {
        "source_page": f"https://genshin-impact.fandom.com/wiki/{urllib.parse.quote(title.replace(' ', '_'))}",
        "source_title": title,
        "source_filename": filename,
        "source_url": info.get("url", ""),
        "mime": info.get("mime", ""),
        "width": info.get("width"),
        "height": info.get("height"),
        "sha1": info.get("sha1", ""),
        "characters": characters,
        "talent_type": talent_type,
        "categories": category_names,
    }


def png_has_transparency(payload: bytes) -> bool | None:
    if not payload.startswith(b"\x89PNG\r\n\x1a\n"):
        return None
    offset = 8
    color_type: int | None = None
    has_trns = False
    while offset + 12 <= len(payload):
        length = int.from_bytes(payload[offset : offset + 4], "big")
        chunk_type = payload[offset + 4 : offset + 8]
        data = payload[offset + 8 : offset + 8 + length]
        if chunk_type == b"IHDR" and len(data) >= 10:
            color_type = data[9]
        elif chunk_type == b"tRNS":
            has_trns = True
        elif chunk_type == b"IEND":
            break
        offset += 12 + length
    return color_type in (4, 6) or has_trns


def extension_for(record: dict[str, Any]) -> str:
    suffix = Path(str(record["source_filename"])).suffix.lower()
    if suffix:
        return suffix
    return {"image/png": ".png", "image/webp": ".webp"}.get(record.get("mime"), ".img")


def write_payload(path: Path, payload: bytes, overwrite: bool) -> str:
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists() and not overwrite:
        return "skipped_existing"
    temporary = path.with_name(path.name + ".part")
    temporary.write_bytes(payload)
    os.replace(temporary, path)
    return "downloaded"


def save_manifests(output: Path, records: list[dict[str, Any]]) -> None:
    output.mkdir(parents=True, exist_ok=True)
    json_path = output / "icon_manifest.json"
    json_path.write_text(
        json.dumps(records, ensure_ascii=False, indent=2) + "\n", encoding="utf-8-sig"
    )
    csv_path = output / "icon_manifest.csv"
    columns = (
        "output_file",
        "status",
        "transparent",
        "character_id",
        "character_cn",
        "character_source",
        "naming_role",
        "talent_slot",
        "talent_type",
        "talent_type_cn",
        "talent_name_en",
        "talent_name_cn",
        "parent_talent_name_en",
        "parent_talent_name_cn",
        "description_cn",
        "description_en",
        "source_filename",
        "talent_page",
        "source_page",
        "source_url",
        "sha1",
    )
    with csv_path.open("w", newline="", encoding="utf-8-sig") as stream:
        writer = csv.DictWriter(stream, fieldnames=columns, extrasaction="ignore")
        writer.writeheader()
        writer.writerows(records)


def load_previous_manifest(output: Path) -> dict[str, dict[str, Any]]:
    path = output / "icon_manifest.json"
    if not path.is_file():
        return {}
    try:
        records = json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, json.JSONDecodeError):
        return {}
    if not isinstance(records, list):
        return {}
    return {
        str(item["output_file"]): item
        for item in records
        if isinstance(item, dict) and item.get("output_file")
    }


def make_assignments(
    source_records: list[dict[str, Any]],
    local_by_name: dict[str, int],
    local_by_name_id: dict[str, int],
    local_by_id: dict[int, str],
    aliases: dict[str, str | int],
    talent_metadata: dict[tuple[int, str], dict[str, Any]],
) -> list[dict[str, Any]]:
    by_character: dict[int, list[dict[str, Any]]] = defaultdict(list)
    generic: list[dict[str, Any]] = []
    unresolved: list[dict[str, Any]] = []

    for source in source_records:
        # Generic weapon/element Normal Attack icons can appear inside a
        # character-specific category (for example Bow Pyro under Amber).
        # They must keep the Wiki filename and must never consume a _Txx slot.
        if WEAPON_NORMAL_RE.match(source["source_filename"]):
            item = dict(source)
            item["character_id"] = ""
            item["character_cn"] = ""
            item["character_source"] = ""
            generic.append(item)
            continue
        matched = False
        for source_character in source["characters"]:
            character_id, character_cn = resolve_character_id(
                source_character,
                local_by_name,
                local_by_name_id,
                local_by_id,
                aliases,
            )
            if character_id is None:
                continue
            item = dict(source)
            item["character_id"] = character_id
            item["character_cn"] = character_cn
            item["character_source"] = source_character
            enrich_talent_record(item, character_id, talent_metadata)
            by_character[character_id].append(item)
            matched = True
        if matched:
            continue
        item = dict(source)
        item["character_id"] = ""
        item["character_cn"] = ""
        item["character_source"] = " | ".join(source["characters"])
        if WEAPON_NORMAL_RE.match(source["source_filename"]):
            generic.append(item)
        else:
            unresolved.append(item)

    assignments: list[dict[str, Any]] = []
    for character_id in sorted(by_character):
        unique: dict[str, dict[str, Any]] = {}
        for item in by_character[character_id]:
            unique[item["source_title"]] = item
        records = sorted(unique.values(), key=talent_sort_key)
        skills = [item for item in records if talent_role(item) == "skill"]
        bursts = [item for item in records if talent_role(item) == "burst"]
        passives = [item for item in records if talent_role(item) == "passive"]

        fixed: list[tuple[int, dict[str, Any]]] = []
        if skills:
            fixed.append((1, skills.pop(0)))
        if bursts:
            fixed.append((2, bursts.pop(0)))
        for index, item in enumerate(passives, start=3):
            fixed.append((index, item))

        used_titles = {item["source_title"] for _, item in fixed}
        extras = [item for item in records if item["source_title"] not in used_titles]
        next_index = 3 + len(passives)
        for item in extras:
            fixed.append((next_index, item))
            next_index += 1

        for index, item in sorted(fixed, key=lambda pair: pair[0]):
            item["naming_role"] = talent_role(item)
            item["output_file"] = f"{character_id}_T{index:02d}{extension_for(item)}"
            assignments.append(item)

    for item in sorted(generic, key=lambda value: value["source_filename"].casefold()):
        item["output_file"] = str(
            Path("Normal") / safe_filename(item["source_filename"])
        )
        assignments.append(item)
    for item in sorted(unresolved, key=lambda value: value["source_filename"].casefold()):
        item["output_file"] = str(Path("_unmatched") / safe_filename(item["source_filename"]))
        assignments.append(item)
    return assignments


def run(args: argparse.Namespace) -> int:
    characters_path = args.characters_xlsx.resolve()
    output = args.output.resolve()
    previous_manifest = load_previous_manifest(output)
    local_by_name, local_by_name_id, local_by_id = load_character_table(characters_path)
    print(f"角色表：{len(local_by_id)} 名角色 ({characters_path})")

    if args.self_test:
        assert parse_wiki_fields("| character = Amber\n| type = Elemental Skill") == (
            ["Amber"],
            "Elemental Skill",
        )
        assert normalize_name("Hu Tao") == "hutao"
        sample = [
            {
                "source_title": "File:Talent Passive.png",
                "source_filename": "Talent Passive.png",
                "characters": ["Amber"],
                "categories": [],
                "talent_type": "",
            },
            {
                "source_title": "File:Talent Burst.png",
                "source_filename": "Talent Burst.png",
                "characters": ["Amber"],
                "categories": [],
                "talent_type": "",
            },
            {
                "source_title": "File:Talent Skill.png",
                "source_filename": "Talent Skill.png",
                "characters": ["Amber"],
                "categories": [],
                "talent_type": "",
            },
        ]
        amber_id = local_by_name_id[normalize_name("Amber")]
        metadata = {
            (amber_id, normalize_name("Skill")): {"talent_slot": "combat2", "talent_order": 20},
            (amber_id, normalize_name("Burst")): {"talent_slot": "combat3", "talent_order": 30},
            (amber_id, normalize_name("Passive")): {"talent_slot": "passive1", "talent_order": 40},
        }
        assigned = make_assignments(
            sample, local_by_name, local_by_name_id, local_by_id, {}, metadata
        )
        assert [item["output_file"] for item in assigned] == [
            f"{amber_id}_T01.png",
            f"{amber_id}_T02.png",
            f"{amber_id}_T03.png",
        ]
        print("离线自检通过")
        return 0

    aliases = load_aliases(args.aliases)
    print("读取最新 genshin-db 中英文技能名称与说明…")
    try:
        talent_metadata = load_talent_metadata(local_by_name_id)
        print(f"已建立 {len(talent_metadata)} 条技能/天赋名称映射")
    except DownloadError as exc:
        talent_metadata = {}
        print(f"警告：genshin-db 暂时不可用，将用 Wiki 类型信息排序：{exc}")
    categories = [DEFAULT_CATEGORIES[0]]
    if args.include_old:
        categories.append(DEFAULT_CATEGORIES[1])
    print("读取 Wiki 图标页面元数据：" + "、".join(categories))
    pages = wiki_file_records(categories)
    source_records = [record for page in pages if (record := page_to_record(page))]
    assignments = make_assignments(
        source_records,
        local_by_name,
        local_by_name_id,
        local_by_id,
        aliases,
        talent_metadata,
    )
    if args.max_files is not None:
        assignments = assignments[: args.max_files]

    print(f"找到 {len(source_records)} 个源图标，生成 {len(assignments)} 个输出任务")
    manifest: list[dict[str, Any]] = []
    alpha_warnings = 0
    failures = 0
    for number, item in enumerate(assignments, start=1):
        result = dict(item)
        target = output / item["output_file"]
        if args.dry_run:
            result["status"] = "dry_run"
            result["transparent"] = ""
        else:
            try:
                previous = previous_manifest.get(str(item["output_file"]), {})
                same_source = bool(
                    previous
                    and previous.get("sha1")
                    and previous.get("sha1") == item.get("sha1")
                )
                if target.exists() and not args.overwrite and same_source:
                    payload = target.read_bytes()
                    result["status"] = "skipped_existing"
                else:
                    payload = http_bytes(str(item["source_url"]))
                    # A path may already contain a different icon created by
                    # an older alphabetical numbering rule. Replacing that
                    # stale file is required even without --overwrite.
                    result["status"] = write_payload(target, payload, True)
                transparent = png_has_transparency(payload)
                result["transparent"] = transparent if transparent is not None else "unknown"
                if transparent is False:
                    alpha_warnings += 1
                    result["status"] += "_opaque_warning"
            except (OSError, DownloadError) as exc:
                failures += 1
                result["status"] = f"failed: {exc}"
                result["transparent"] = ""
        manifest.append(result)
        print(f"[{number}/{len(assignments)}] {item['output_file']} - {result['status']}")

    save_manifests(output, manifest)
    unresolved_count = sum(
        1 for item in assignments if str(item["output_file"]).startswith("_unmatched")
    )
    print(f"清单：{output / 'icon_manifest.json'}")
    print(
        f"完成：失败 {failures}，透明通道警告 {alpha_warnings}，"
        f"未匹配角色/通用图标 {unresolved_count}"
    )
    return 1 if failures else 0


def build_parser() -> argparse.ArgumentParser:
    script_path = Path(__file__).resolve()
    repository = next(
        (
            parent
            for parent in script_path.parents
            if (parent / "Charts" / "Characters.xlsx").is_file()
        ),
        script_path.parent,
    )
    parser = argparse.ArgumentParser(
        description="下载并按项目 CharacterID 重命名原神 Wiki 天赋图标"
    )
    parser.add_argument(
        "--characters-xlsx",
        type=Path,
        default=repository / "Charts" / "Characters.xlsx",
        help="项目角色总表（默认：Charts/Characters.xlsx）",
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=repository / "art_assets" / "Skill_Icon",
        help="图标输出目录（默认：art_assets/Skill_Icon）",
    )
    parser.add_argument(
        "--aliases",
        type=Path,
        help='可选 JSON 别名表，例如 {"Traveler": 1008, "Source Name": "项目中文名"}',
    )
    parser.add_argument(
        "--include-old",
        action="store_true",
        help="额外下载 Old Talent Icons 中的历史旧版图标",
    )
    parser.add_argument("--overwrite", action="store_true", help="覆盖已经存在的图标")
    parser.add_argument("--dry-run", action="store_true", help="生成清单但不下载图片")
    parser.add_argument("--max-files", type=int, help="只处理前 N 个输出任务（试跑用）")
    parser.add_argument("--self-test", action="store_true", help="只运行离线自检")
    return parser


def main() -> int:
    # Keep Chinese progress/error text readable in Windows PowerShell and in
    # redirected logs, regardless of the active legacy code page.
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    if hasattr(sys.stderr, "reconfigure"):
        sys.stderr.reconfigure(encoding="utf-8", errors="replace")
    try:
        return run(build_parser().parse_args())
    except (DownloadError, OSError, zipfile.BadZipFile) as exc:
        print(f"错误：{exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
