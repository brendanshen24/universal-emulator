#!/usr/bin/env python3
"""Rebuild an installable, unsigned .appx from an installed UWP package folder (sign it afterwards).

usage: pack_appx.py <installed package folder> <out.appx>

The payload is exactly the files the folder's own AppxBlockMap.xml lists, byte for byte, stored uncompressed.
Store/install leftovers (AppxSignature.p7x, AppxMetadata/CodeIntegrity.cat, microsoft.system.package.metadata/)
are left out. Self-check: every file's block hashes and zip local-header size must equal the original block map's,
which proves both the payload and the OPC part-name encoding are right.
"""
import base64
import hashlib
import struct
import sys
import urllib.parse
import xml.etree.ElementTree as ET
import zlib
from pathlib import Path
from xml.sax.saxutils import quoteattr

BLOCK = 64 * 1024
BLOCKMAP_NS = "http://schemas.microsoft.com/appx/2010/blockmap"
DOS_TIME, DOS_DATE = 0, (2017 - 1980) << 9 | 2 << 5 | 19  # fixed date keeps builds reproducible

CONTENT_TYPES = {
    "png": "image/png", "dll": "application/x-msdownload", "exe": "application/x-msdownload",
    "winmd": "application/vnd.ms-windows.winmd", "xml": "application/xml", "html": "text/html",
    "js": "application/javascript", "json": "application/json", "xaml": "application/xaml+xml",
}


def part_name(name):
    """Block-map name (backslashes) -> zip entry name. makeappx percent-encodes everything but RFC 3986 unreserved
    characters (the original package has "Diver, The..." stored as "Diver%2C%20The...")."""
    return "/".join(urllib.parse.quote(seg, safe="-._~") for seg in name.split("\\"))


def block_hashes(data):
    return [base64.b64encode(hashlib.sha256(data[i:i + BLOCK]).digest()).decode() for i in range(0, len(data), BLOCK)]


def main(src, out):
    src = Path(src)
    original = ET.parse(src / "AppxBlockMap.xml").getroot()
    files = []  # (blockmap name, zip name, data)
    for f in original.findall(f"{{{BLOCKMAP_NS}}}File"):
        name = f.get("Name")
        data = (src / name.replace("\\", "/")).read_bytes()
        zname = part_name(name)
        want = [b.get("Hash") for b in f.findall(f"{{{BLOCKMAP_NS}}}Block")]
        assert len(data) == int(f.get("Size")), f"size changed: {name}"
        assert block_hashes(data) == want, f"content differs from the original package: {name}"
        assert 30 + len(zname.encode()) == int(f.get("LfhSize")), f"part name encoding differs: {name} -> {zname}"
        files.append((name, zname, data))

    blockmap = ['<?xml version="1.0" encoding="UTF-8" standalone="no"?>',
                f'<BlockMap xmlns="{BLOCKMAP_NS}" HashMethod="http://www.w3.org/2001/04/xmlenc#sha256">']
    for name, zname, data in files:
        blocks = "".join(f'<Block Hash="{h}"/>' for h in block_hashes(data))
        lfh = 30 + len(zname.encode())
        blockmap.append(f'<File Name={quoteattr(name)} Size="{len(data)}" LfhSize="{lfh}"' + (f">{blocks}</File>" if blocks else "/>"))
    blockmap.append("</BlockMap>")

    exts = sorted({z.rsplit(".", 1)[1].lower() for _, z, _ in files if "." in z.rsplit("/", 1)[-1]})
    types = ['<?xml version="1.0" encoding="UTF-8"?>', '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">']
    types += [f'<Default Extension="{e}" ContentType="{CONTENT_TYPES.get(e, "application/octet-stream")}"/>' for e in exts]
    types += [f'<Override PartName="/{z}" ContentType="application/octet-stream"/>' for _, z, _ in files if "." not in z.rsplit("/", 1)[-1]]
    types += ['<Override PartName="/AppxManifest.xml" ContentType="application/vnd.ms-appx.manifest+xml"/>',
              '<Override PartName="/AppxBlockMap.xml" ContentType="application/vnd.ms-appx.blockmap+xml"/>', "</Types>"]

    entries = [(z, d, False) for _, z, d in files]
    # The two footprint parts are deflated, as makeappx does; osslsigncode also re-deflates [Content_Types].xml when
    # it adds the signature but keeps the old method field, so a stored one would come out corrupt.
    entries.append(("AppxBlockMap.xml", "".join(blockmap).encode(), True))
    entries.append(("[Content_Types].xml", "".join(types).encode(), True))
    write_zip(out, entries)
    print(f"{out}: {len(files)} files + block map + content types, all {sum(len(d) for _, _, d in files)} bytes match the original")


def write_zip(path, entries):
    """ZIP like makeappx's (no local extra fields) plus a ZIP64 end record, as makeappx writes.
    entries: (name, data, deflate?)."""
    body, central = bytearray(), bytearray()
    for name, data, deflate in entries:
        n, crc, offset = name.encode(), zlib.crc32(data), len(body)
        method, stored = (8, zlib.compress(data, 9)[2:-4]) if deflate else (0, data)  # raw deflate stream
        body += struct.pack("<IHHHHHIIIHH", 0x04034B50, 20, 0, method, DOS_TIME, DOS_DATE, crc, len(stored), len(data), len(n), 0) + n + stored
        central += struct.pack("<IHHHHHHIIIHHHHHII", 0x02014B50, 45, 20, 0, method, DOS_TIME, DOS_DATE, crc, len(stored), len(data),
                               len(n), 0, 0, 0, 0, 0, offset) + n
    cd_offset, count = len(body), len(entries)
    eocd64_offset = cd_offset + len(central)
    eocd64 = struct.pack("<IQHHIIQQQQ", 0x06064B50, 44, 45, 45, 0, 0, count, count, len(central), cd_offset)
    locator = struct.pack("<IIQI", 0x07064B50, 0, eocd64_offset, 1)
    eocd = struct.pack("<IHHHHIIH", 0x06054B50, 0, 0, count, count, len(central), cd_offset, 0)
    Path(path).write_bytes(bytes(body + central + eocd64 + locator + eocd))


if __name__ == "__main__":
    main(*sys.argv[1:3])
