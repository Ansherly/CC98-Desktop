#!/usr/bin/env python3
"""把 Assets/Fonts 下的 HarmonyOS Sans SC 子集化，减小安装包体积。

用法：
    python tools/subset-fonts.py --level gb2312 --out build/fonts   # 默认，推荐
    python tools/subset-fonts.py --level gbk    --out build/fonts   # 保守（保留全部 GBK 汉字）
    python tools/subset-fonts.py --level source --out build/fonts   # 极限，只留源码用字（不推荐上线）

字集 = 档位汉字 ∪ 源码里出现的字符 ∪ 常用符号，所以 UI 文案永远不会缺字。
输出文件名与原文件一致（HarmonyOS_Sans_SC_*.ttf），直接覆盖 Assets/Fonts 即可。

依赖：pip install fonttools
"""

from __future__ import annotations

import argparse
import os
import sys
import zlib

from fontTools import subset
from fontTools.ttLib import TTFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FONT_DIR = os.path.join(ROOT, "Assets", "Fonts")
SKIP_DIRS = {"obj", "bin", ".git", ".vs", ".dsh_better_edit", ".workbuddy", "tools", "native"}
SOURCE_EXT = (".cs", ".xaml")


def source_chars() -> set[str]:
    """扫全仓 .cs/.xaml 用字（排除生成目录）。"""
    chars: set[str] = set()
    for dirpath, dirnames, filenames in os.walk(ROOT):
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
        for name in filenames:
            if name.lower().endswith(SOURCE_EXT):
                try:
                    with open(os.path.join(dirpath, name), encoding="utf-8", errors="ignore") as fh:
                        chars |= set(fh.read())
                except OSError:
                    pass
    return chars


def gb_chars(codec: str, first: int, last: int) -> set[str]:
    """枚举某编码的双字节空间，得到该字符集覆盖的全部字符。"""
    out: set[str] = set()
    for hi in range(first, last + 1):
        for lo in range(0x40, 0x100):
            try:
                out.add(bytes([hi, lo]).decode(codec))
            except UnicodeDecodeError:
                continue
    return out


def symbols() -> set[str]:
    s = {chr(c) for c in range(0x20, 0x7F)}        # 可打印 ASCII
    s |= {chr(c) for c in range(0x3000, 0x3040)}   # CJK 标点
    s |= {chr(c) for c in range(0xFF01, 0xFF61)}   # 全角
    s |= set("“”‘’—–…·•°±×÷§¶©®™€£¥←→↑↓↔⇒≠≤≥∞※★☆▲△▼▽●○■□◆◇«»▪◦")
    return s


def charset(level: str) -> set[str]:
    base: set[str] = set()
    if level in ("gb2312", "gbk"):
        base |= gb_chars("gb2312", 0xA1, 0xF8)
    if level == "gbk":
        base |= {c for c in gb_chars("gbk", 0x81, 0xFE) if 0x4E00 <= ord(c) <= 0x9FFF}
    if level == "source":
        base = set()
    return base | source_chars() | symbols()


def build(font_path: str, chars: set[str], out_path: str) -> tuple[int, int]:
    opts = subset.Options()
    opts.name_IDs = ["*"]          # 保留 family/full name，XAML 里的 #HarmonyOS Sans SC 才认得到
    opts.name_languages = ["*"]
    opts.layout_features = ["*"]   # 保留 kerning 等排版特性
    opts.notdef_outline = True
    opts.recalc_bounds = True

    font = TTFont(font_path, recalcTimestamp=False)  # 不动 head.modified，产物可字节级复现
    sub = subset.Subsetter(options=opts)
    sub.populate(unicodes=sorted(ord(c) for c in chars))  # 排序让产物可复现
    sub.subset(font)
    font.save(out_path)
    font.close()
    with open(out_path, "rb") as fh:
        raw = fh.read()
    return len(raw), len(zlib.compress(raw, 9))


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--level", choices=["gb2312", "gbk", "source"], default="gb2312")
    ap.add_argument("--out", required=True, help="输出目录（别直接写 Assets/Fonts，先量完再覆盖）")
    ap.add_argument("--font-dir", default=FONT_DIR)
    args = ap.parse_args()

    chars = charset(args.level)
    fonts = sorted(f for f in os.listdir(args.font_dir) if f.lower().endswith(".ttf"))
    if not fonts:
        print("没有找到 .ttf", file=sys.stderr)
        return 1
    os.makedirs(args.out, exist_ok=True)
    print("档位 %s，字符集 %d 个，字体 %d 个" % (args.level, len(chars), len(fonts)))
    for name in fonts:
        raw, comp = build(os.path.join(args.font_dir, name), chars, os.path.join(args.out, name))
        print("  %-34s %6.2f MB  压缩后 %5.2f MB" % (name, raw / 1048576, comp / 1048576))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
