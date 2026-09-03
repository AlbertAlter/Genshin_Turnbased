#!/usr/bin/env python3
"""Download generic weapon/element Normal Attack icons from Genshin Wiki.

The files keep their original Wiki names, for example ``Sword Pyro.png`` and
``Catalyst Unaligned.png``. By default they are written to the sibling
``Normal`` directory. Only Python's standard library is required.
"""

from __future__ import annotations

import argparse
import csv
import gzip
import json
import os
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path
from typing import Any


WIKI_API = "https://genshin-impact.fandom.com/api.php"
CATEGORY = "Category:Normal Attack Talent Icons"
USER_AGENT = (
    "GenshinTurnbasedNormalAttackIconDownloader/1.0 "
    "(personal fan-project asset organizer)"
)
ICON_RE = re.compile(
    r"^(Sword|Claymore|Polearm|Bow|Catalyst) "
    r"(Pyro|Hydro|Electro|Cryo|Anemo|Dendro|Geo|Unaligned)\.png$",
    re.IGNORECASE,
)


class DownloadError(RuntimeError):
    """An expected download or validation error."""


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
                payload = response.read()
                if response.headers.get("Content-Encoding", "").lower() == "gzip":
                    payload = gzip.decompress(payload)
                return payload
        except (OSError, urllib.error.URLError, urllib.error.HTTPError) as exc:
            last_error = exc
            if isinstance(exc, urllib.error.HTTPError) and exc.code in (400, 401, 403, 404):
                break
            if attempt + 1 < retries:
                time.sleep(min(2**attempt, 8))
    raise DownloadError(f"下载失败：{url}\n{last_error}")


def http_json(url: str, params: dict[str, Any]) -> dict[str, Any]:
    try:
        return json.loads(http_bytes(url, params=params).decode("utf-8-sig"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise DownloadError(f"远端返回的不是有效 JSON：{url}") from exc


def list_normal_attack_icons() -> list[dict[str, Any]]:
    records: dict[str, dict[str, Any]] = {}
    continuation: dict[str, Any] = {}
    while True:
        params: dict[str, Any] = {
            "action": "query",
            "generator": "categorymembers",
            "gcmtitle": CATEGORY,
            "gcmtype": "file",
            "gcmlimit": "max",
            "prop": "imageinfo",
            "iiprop": "url|mime|size|sha1",
            "format": "json",
            "formatversion": "2",
            "origin": "*",
        }
        params.update(continuation)
        response = http_json(WIKI_API, params)
        if "error" in response:
            error = response["error"]
            raise DownloadError(f"Wiki API 错误：{error.get('info', error)}")
        for page in response.get("query", {}).get("pages", []):
            title = str(page.get("title", ""))
            image_info = page.get("imageinfo") or []
            if not title.startswith("File:") or not image_info:
                continue
            filename = title.removeprefix("File:")
            if not ICON_RE.fullmatch(filename):
                continue
            info = image_info[0]
            records[filename.casefold()] = {
                "filename": filename,
                "source_url": str(info.get("url", "")),
                "source_page": (
                    "https://genshin-impact.fandom.com/wiki/"
                    + urllib.parse.quote(title.replace(" ", "_"))
                ),
                "mime": str(info.get("mime", "")),
                "width": info.get("width"),
                "height": info.get("height"),
                "sha1": str(info.get("sha1", "")),
            }
        continuation = response.get("continue") or {}
        if not continuation:
            break
    return sorted(records.values(), key=lambda item: item["filename"].casefold())


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


def write_file(path: Path, payload: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".part")
    temporary.write_bytes(payload)
    os.replace(temporary, path)


def save_manifest(output: Path, records: list[dict[str, Any]]) -> None:
    output.mkdir(parents=True, exist_ok=True)
    (output / "normal_attack_manifest.json").write_text(
        json.dumps(records, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8-sig",
    )
    columns = (
        "filename",
        "status",
        "transparent",
        "width",
        "height",
        "sha1",
        "source_page",
        "source_url",
    )
    with (output / "normal_attack_manifest.csv").open(
        "w", newline="", encoding="utf-8-sig"
    ) as stream:
        writer = csv.DictWriter(stream, fieldnames=columns, extrasaction="ignore")
        writer.writeheader()
        writer.writerows(records)


def run(args: argparse.Namespace) -> int:
    output = args.output.resolve()
    if args.self_test:
        assert ICON_RE.fullmatch("Sword Pyro.png")
        assert ICON_RE.fullmatch("Catalyst Unaligned.png")
        assert not ICON_RE.fullmatch("Talent Fiery Rain.png")
        print("离线自检通过")
        return 0

    print(f"读取 Wiki 分类：{CATEGORY}")
    records = list_normal_attack_icons()
    if args.max_files is not None:
        records = records[: args.max_files]
    if not records:
        raise DownloadError("分类中没有找到符合武器＋元素命名规则的 PNG 图标")

    manifest: list[dict[str, Any]] = []
    failures = 0
    for index, record in enumerate(records, start=1):
        result = dict(record)
        target = output / record["filename"]
        try:
            if args.dry_run:
                result["status"] = "dry_run"
                result["transparent"] = ""
            elif target.exists() and not args.overwrite:
                payload = target.read_bytes()
                result["status"] = "skipped_existing"
                transparent = png_has_transparency(payload)
                result["transparent"] = (
                    transparent if transparent is not None else "unknown"
                )
            else:
                payload = http_bytes(record["source_url"])
                write_file(target, payload)
                result["status"] = "downloaded"
                transparent = png_has_transparency(payload)
                result["transparent"] = (
                    transparent if transparent is not None else "unknown"
                )
                if transparent is False:
                    result["status"] += "_opaque_warning"
        except (OSError, DownloadError) as exc:
            failures += 1
            result["status"] = f"failed: {exc}"
            result["transparent"] = ""
        manifest.append(result)
        print(f"[{index}/{len(records)}] {record['filename']} - {result['status']}")

    save_manifest(output, manifest)
    print(f"完成：{len(records) - failures} 个成功，{failures} 个失败；目录：{output}")
    return 1 if failures else 0


def build_parser() -> argparse.ArgumentParser:
    default_output = Path(__file__).resolve().parent / "Normal"
    parser = argparse.ArgumentParser(
        description="单独下载原神 Wiki 的武器＋元素通用普攻图标"
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=default_output,
        help=f"输出目录（默认：{default_output}）",
    )
    parser.add_argument("--overwrite", action="store_true", help="覆盖已有同名图标")
    parser.add_argument("--dry-run", action="store_true", help="只生成清单，不下载图片")
    parser.add_argument("--max-files", type=int, help="只处理前 N 个图标（试跑用）")
    parser.add_argument("--self-test", action="store_true", help="只运行离线自检")
    return parser


def main() -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    if hasattr(sys.stderr, "reconfigure"):
        sys.stderr.reconfigure(encoding="utf-8", errors="replace")
    try:
        return run(build_parser().parse_args())
    except (DownloadError, OSError) as exc:
        print(f"错误：{exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
