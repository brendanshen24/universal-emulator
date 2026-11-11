#!/usr/bin/env python3
"""Repackage and re-sign the dumped Universal Emulator UWP directory as a
sideloadable .appx package with matching self-signed Code Signing .cer.
"""

import argparse
import base64
import hashlib
import os
import shutil
import struct
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zlib
from datetime import datetime, timezone
from xml.sax.saxutils import escape

from cryptography import x509
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import rsa
from cryptography.x509.oid import ExtendedKeyUsageOID, NameOID

DEFAULT_DUMP_DIR = (
    "/Users/brendan/Downloads/50446Nesbox.16735B73F29AD_8y3dwps6jawp4/TempState/DUMP"
)
DEFAULT_OUT_DIR = "/Users/brendan/pokopia-switch-one/universal-emulator/original-uwp"
APPX_BASENAME = "UniversalEmulator_1.1.3.0_x64"

EXT_MIME_TYPES = {
    "dll": "application/x-msdownload",
    "exe": "application/x-msdownload",
    "winmd": "application/x-msdownload",
    "png": "image/png",
    "xml": "application/xml",
    "xaml": "application/xaml+xml",
    "xbf": "application/octet-stream",
    "pri": "application/octet-stream",
    "html": "text/html",
    "js": "application/javascript",
    "json": "application/json",
    "nes": "application/octet-stream",
    "smc": "application/octet-stream",
}


def read_publisher_cn(manifest_path: str) -> str:
    """Extract the Publisher DN string from AppxManifest.xml."""
    tree = ET.parse(manifest_path)
    ns = {"m": "http://schemas.microsoft.com/appx/manifest/foundation/windows10"}
    ident = tree.getroot().find("m:Identity", ns)
    if ident is None or "Publisher" not in ident.attrib:
        raise ValueError(f"Missing Identity/@Publisher in {manifest_path}")
    return ident.attrib["Publisher"]


def generate_code_signing_cert(publisher_dn: str, key_out: str, pem_out: str, cer_out: str) -> None:
    """Create a self-signed Code Signing certificate matching Publisher DN."""
    if not publisher_dn.startswith("CN=") or "," in publisher_dn:
        raise ValueError(f"Expected single CN= attribute in Publisher, got: {publisher_dn}")
    cn = publisher_dn[3:]

    key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    subject = issuer = x509.Name([x509.NameAttribute(NameOID.COMMON_NAME, cn)])
    cert = (
        x509.CertificateBuilder()
        .subject_name(subject)
        .issuer_name(issuer)
        .public_key(key.public_key())
        .serial_number(x509.random_serial_number())
        .not_valid_before(datetime(2016, 1, 1, 0, 0, 0, tzinfo=timezone.utc))
        .not_valid_after(datetime(2046, 1, 1, 0, 0, 0, tzinfo=timezone.utc))
        .add_extension(x509.BasicConstraints(ca=False, path_length=None), critical=True)
        .add_extension(
            x509.KeyUsage(
                digital_signature=True,
                content_commitment=False,
                key_encipherment=False,
                data_encipherment=False,
                key_agreement=False,
                key_cert_sign=False,
                crl_sign=False,
                encipher_only=False,
                decipher_only=False,
            ),
            critical=True,
        )
        .add_extension(x509.ExtendedKeyUsage([ExtendedKeyUsageOID.CODE_SIGNING]), critical=False)
        .add_extension(x509.SubjectKeyIdentifier.from_public_key(key.public_key()), critical=False)
        .sign(key, hashes.SHA256())
    )

    with open(key_out, "wb") as f:
        f.write(
            key.private_bytes(
                serialization.Encoding.PEM,
                serialization.PrivateFormat.PKCS8,
                serialization.NoEncryption(),
            )
        )
    with open(pem_out, "wb") as f:
        f.write(cert.public_bytes(serialization.Encoding.PEM))
    with open(cer_out, "wb") as f:
        f.write(cert.public_bytes(serialization.Encoding.DER))


def deflate_blocks_64k(data: bytes):
    """Deflate payload into 64 KiB Z_FULL_FLUSH blocks so each block hashes & inflates independently."""
    comp = zlib.compressobj(level=6, method=zlib.DEFLATED, wbits=-zlib.MAX_WBITS)
    compressed_chunks = []
    blocks = []
    pos = 0
    while pos < len(data):
        chunk = data[pos : pos + 65536]
        pos += len(chunk)
        is_last = pos >= len(data)
        c = comp.compress(chunk)
        c += comp.flush(zlib.Z_FINISH if is_last else zlib.Z_FULL_FLUSH)
        digest = base64.b64encode(hashlib.sha256(chunk).digest()).decode("ascii")
        compressed_chunks.append(c)
        blocks.append((digest, len(c)))
    return b"".join(compressed_chunks), blocks


def store_blocks_64k(data: bytes):
    """Chunk uncompressed data into 64 KiB SHA-256 blocks."""
    blocks = []
    pos = 0
    while pos < len(data):
        chunk = data[pos : pos + 65536]
        pos += len(chunk)
        digest = base64.b64encode(hashlib.sha256(chunk).digest()).decode("ascii")
        blocks.append((digest, None))
    return data, blocks


def dos_time_date(ts: float | None):
    dt = datetime(2022, 3, 26, 12, 0, 0) if ts is None else datetime.fromtimestamp(ts)
    dtime = ((dt.hour & 0x1F) << 11) | ((dt.minute & 0x3F) << 5) | ((dt.second // 2) & 0x1F)
    ddate = (((max(1980, dt.year) - 1980) & 0x7F) << 9) | ((dt.month & 0x0F) << 5) | (dt.day & 0x1F)
    return dtime, ddate


class Zip64AppxWriter:
    """Minimal OPC-compliant ZIP64 writer matching MakeAppx local/central headers and ZIP64 EOCD."""

    def __init__(self, path: str):
        self.f = open(path, "wb")
        self.cd_entries = []

    def write_entry(self, zip_name: str, raw_data: bytes, compress: bool = True, mtime: float | None = None):
        name_bytes = zip_name.encode("utf-8")
        crc = zlib.crc32(raw_data) & 0xFFFFFFFF
        uncomp_size = len(raw_data)
        if compress and uncomp_size > 0:
            method = 8
            comp_data, blocks = deflate_blocks_64k(raw_data)
        else:
            method = 0
            comp_data, blocks = store_blocks_64k(raw_data)
        comp_size = len(comp_data)
        offset = self.f.tell()
        dtime, ddate = dos_time_date(mtime)
        ver_needed = 45

        lfh = (
            struct.pack(
                "<IHHHHHIIIHH",
                0x04034B50,
                ver_needed,
                0,
                method,
                dtime,
                ddate,
                crc,
                comp_size,
                uncomp_size,
                len(name_bytes),
                0,
            )
            + name_bytes
        )
        self.f.write(lfh)
        self.f.write(comp_data)
        self.cd_entries.append(
            {
                "ver_made": 45,
                "ver_needed": ver_needed,
                "flags": 0,
                "method": method,
                "dtime": dtime,
                "ddate": ddate,
                "crc": crc,
                "comp_size": comp_size,
                "uncomp_size": uncomp_size,
                "name_bytes": name_bytes,
                "offset": offset,
            }
        )
        return len(lfh), blocks

    def close(self):
        cd_offset = self.f.tell()
        for e in self.cd_entries:
            cdh = (
                struct.pack(
                    "<IHHHHHHIIIHHHHHII",
                    0x02014B50,
                    e["ver_made"],
                    e["ver_needed"],
                    e["flags"],
                    e["method"],
                    e["dtime"],
                    e["ddate"],
                    e["crc"],
                    e["comp_size"],
                    e["uncomp_size"],
                    len(e["name_bytes"]),
                    0,
                    0,
                    0,
                    0,
                    0,
                    e["offset"],
                )
                + e["name_bytes"]
            )
            self.f.write(cdh)
        cd_size = self.f.tell() - cd_offset
        n = len(self.cd_entries)
        zip64_eocd_offset = self.f.tell()
        self.f.write(struct.pack("<IQHHIIQQQQ", 0x06064B50, 44, 45, 45, 0, 0, n, n, cd_size, cd_offset))
        self.f.write(struct.pack("<IIQI", 0x07064B50, 0, zip64_eocd_offset, 1))
        # Sentinel 0xFFFF/0xFFFFFFFF ensures osslsigncode and AppX tools preserve ZIP64 EOCD
        self.f.write(struct.pack("<IHHHHIIH", 0x06054B50, 0, 0, 0xFFFF, 0xFFFF, 0xFFFFFFFF, 0xFFFFFFFF, 0))
        self.f.close()


def build_unsigned_appx(dump_dir: str, unsigned_path: str) -> None:
    """Pack the 98 payload files listed in original AppxBlockMap.xml into a fresh unsigned ZIP64 .appx."""
    bm_tree = ET.parse(os.path.join(dump_dir, "AppxBlockMap.xml"))
    ns = {"b": "http://schemas.microsoft.com/appx/2010/blockmap"}
    payload_files = [
        (el.attrib["Name"], el.attrib["Name"].replace("\\", "/"))
        for el in bm_tree.getroot().findall("b:File", ns)
    ]

    w = Zip64AppxWriter(unsigned_path)
    bm_lines = [
        '<?xml version="1.0" encoding="UTF-8" standalone="no"?>',
        '<BlockMap xmlns="http://schemas.microsoft.com/appx/2010/blockmap" '
        'HashMethod="http://www.w3.org/2001/04/xmlenc#sha256">',
    ]
    exts_used = set()
    overrides = []

    for win_name, posix_rel in payload_files:
        src = os.path.join(dump_dir, posix_rel)
        with open(src, "rb") as f:
            raw = f.read()
        zip_name = posix_rel.replace(" ", "%20")
        ext = os.path.splitext(posix_rel)[1].lower().lstrip(".")
        if ext and ext not in EXT_MIME_TYPES:
            raise ValueError(f"Unmapped extension {ext!r} for {posix_rel}")
        compress = ext != "png" and len(raw) > 0
        lfh_size, blocks = w.write_entry(zip_name, raw, compress=compress, mtime=os.path.getmtime(src))

        if ext:
            exts_used.add(ext)
        if posix_rel == "AppxManifest.xml":
            overrides.append(("/AppxManifest.xml", "application/vnd.ms-appx.manifest+xml"))
        elif not ext:
            overrides.append(("/" + zip_name, "application/octet-stream"))

        if not blocks:
            bm_lines.append(f'<File Name="{escape(win_name)}" Size="{len(raw)}" LfhSize="{lfh_size}"/>')
        else:
            bxml = "".join(
                f'<Block Hash="{h}"' + (f' Size="{cs}"' if cs is not None else "") + "/>"
                for h, cs in blocks
            )
            bm_lines.append(
                f'<File Name="{escape(win_name)}" Size="{len(raw)}" LfhSize="{lfh_size}">{bxml}</File>'
            )

    bm_lines.append("</BlockMap>")
    w.write_entry("AppxBlockMap.xml", "".join(bm_lines).encode("utf-8"), compress=True)
    overrides.append(("/AppxBlockMap.xml", "application/vnd.ms-appx.blockmap+xml"))

    ct_lines = [
        '<?xml version="1.0" encoding="UTF-8"?>',
        '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">',
    ]
    for ext in sorted(exts_used):
        ct_lines.append(f'<Default Extension="{ext}" ContentType="{EXT_MIME_TYPES[ext]}"/>')
    for part, ctype in overrides:
        ct_lines.append(f'<Override PartName="{part}" ContentType="{ctype}"/>')
    ct_lines.append("</Types>")
    w.write_entry("[Content_Types].xml", "".join(ct_lines).encode("utf-8"), compress=True)
    w.close()


def main():
    parser = argparse.ArgumentParser(description="Repackage and re-sign Universal Emulator UWP .appx")
    parser.add_argument("--dump-dir", default=DEFAULT_DUMP_DIR)
    parser.add_argument("--out-dir", default=DEFAULT_OUT_DIR)
    args = parser.parse_args()

    os.makedirs(args.out_dir, exist_ok=True)
    publisher_dn = read_publisher_cn(os.path.join(args.dump_dir, "AppxManifest.xml"))
    signed_appx = os.path.join(args.out_dir, f"{APPX_BASENAME}.appx")
    out_cer = os.path.join(args.out_dir, f"{APPX_BASENAME}.cer")

    with tempfile.TemporaryDirectory() as tmp:
        key_path = os.path.join(tmp, "sign.key")
        pem_path = os.path.join(tmp, "sign.pem")
        unsigned_appx = os.path.join(tmp, "unsigned.appx")

        generate_code_signing_cert(publisher_dn, key_path, pem_path, out_cer)
        build_unsigned_appx(args.dump_dir, unsigned_appx)

        if os.path.exists(signed_appx):
            os.remove(signed_appx)
        # Signing time 2024-01-01 falls well inside 2016..2046 cert validity on any modern PC clock
        subprocess.run(
            [
                "osslsigncode",
                "sign",
                "-certs",
                pem_path,
                "-key",
                key_path,
                "-n",
                "Universal Emulator",
                "-time",
                "1704067200",
                "-in",
                unsigned_appx,
                "-out",
                signed_appx,
            ],
            check=True,
        )
        subprocess.run(
            ["osslsigncode", "verify", "-CAfile", pem_path, "-in", signed_appx],
            check=True,
        )
    print(f"Created signed package: {signed_appx}")
    print(f"Created public cert:    {out_cer}")


if __name__ == "__main__":
    main()
