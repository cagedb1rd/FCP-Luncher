"""从 Fluent UI System Icons（MIT）官方仓库下载图标，生成 src/FCP.UI/Themes/Icons.xaml。

用法：python -I tools/gen-icons.py
新增图标：在 ICONS 中添加一行（资源名, 官方目录名, 文件名, 样式），然后重新运行。
"""
import re
import sys
import urllib.parse
import urllib.request
from pathlib import Path

ICONS = [
    # 资源名         官方目录名            文件名               样式
    ("AddCircle",      "Add Circle",        "add_circle",        "regular"),
    ("ArrowLeft",      "Arrow Left",        "arrow_left",        "regular"),
    ("ArrowSwap",      "Arrow Swap",        "arrow_swap",        "regular"),
    ("ArrowSync",      "Arrow Sync",        "arrow_sync",        "regular"),
    ("Box",            "Box",               "box",               "regular"),
    ("Checkmark",      "Checkmark Circle",  "checkmark_circle",  "regular"),
    ("ChevronRight",   "Chevron Right",     "chevron_right",     "regular"),
    ("Clock",          "Clock",             "clock",             "regular"),
    ("Dismiss",        "Dismiss",           "dismiss",           "regular"),
    ("Download",       "Arrow Download",    "arrow_download",    "regular"),
    ("Folder",         "Folder",            "folder",            "regular"),
    ("Games",          "Games",             "games",             "regular"),
    ("Globe",          "Globe",             "globe",             "regular"),
    ("Grid",           "Grid",              "grid",              "regular"),
    ("Image",          "Image",             "image",             "regular"),
    ("Info",           "Info",              "info",              "regular"),
    ("Lightbulb",      "Lightbulb",         "lightbulb",         "regular"),
    ("Memory",         "Developer Board",   "developer_board",   "regular"),
    ("PaintBrush",     "Paint Brush",       "paint_brush",       "regular"),
    ("People",         "People Community",  "people_community",  "regular"),
    ("Person",         "Person",            "person",            "regular"),
    ("Play",           "Play",              "play",              "regular"),
    ("Puzzle",         "Puzzle Piece",      "puzzle_piece",      "regular"),
    ("Search",         "Search",            "search",            "regular"),
    ("Settings",       "Settings",          "settings",          "regular"),
    ("Shield",         "Shield Checkmark",  "shield_checkmark",  "regular"),
    ("Sparkle",        "Sparkle",           "sparkle",           "regular"),
    ("Star",           "Star",              "star",              "regular"),
    ("StarFilled",     "Star",              "star",              "filled"),
    ("Subtract",       "Subtract",          "subtract",          "regular"),
    ("Window",         "Window",            "window",            "regular"),
    ("Wrench",         "Wrench",            "wrench",            "regular"),
]

BASE = "https://raw.githubusercontent.com/microsoft/fluentui-system-icons/main/assets"


def fetch(folder: str, name: str, style: str) -> str:
    url = f"{BASE}/{urllib.parse.quote(folder)}/SVG/ic_fluent_{name}_24_{style}.svg"
    with urllib.request.urlopen(url, timeout=30) as response:
        svg = response.read().decode("utf-8")
    paths = re.findall(r'<path[^>]*\sd="([^"]+)"', svg)
    if not paths:
        raise ValueError(f"no path data in {url}")
    return " ".join(paths)


def main() -> int:
    root = Path(__file__).resolve().parent.parent
    target = root / "src" / "FCP.UI" / "Themes" / "Icons.xaml"
    lines = [
        '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"',
        '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">',
        "  <!-- 图标：Fluent UI System Icons（MIT，Copyright Microsoft Corporation），24px。见 NOTICE.md -->",
        "  <!-- 由 tools/gen-icons.py 生成，请勿手动修改 -->",
    ]
    for key, folder, name, style in ICONS:
        lines.append(f'  <StreamGeometry x:Key="Icon.{key}">F1 {fetch(folder, name, style)}</StreamGeometry>')
        print(f"  {key}")
    lines.append("</ResourceDictionary>")
    target.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"wrote {len(ICONS)} icons to {target}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
