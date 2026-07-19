#!/usr/bin/env python3
"""Assert that a rectangular PNG region contains a required RGB color."""

import binascii
import json
import pathlib
import struct
import sys
import zlib


def read_png(path: pathlib.Path):
    data = path.read_bytes()
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError(f"{path} is not a PNG")
    position = 8
    payload = bytearray()
    width = height = color_type = bit_depth = interlace = None
    while position < len(data):
        length = struct.unpack(">I", data[position:position + 4])[0]
        kind = data[position + 4:position + 8]
        body = data[position + 8:position + 8 + length]
        checksum = struct.unpack(">I", data[position + 8 + length:position + 12 + length])[0]
        if binascii.crc32(kind + body) & 0xFFFFFFFF != checksum:
            raise ValueError(f"{path} contains a corrupt {kind!r} chunk")
        position += 12 + length
        if kind == b"IHDR":
            width, height, bit_depth, color_type, _, _, interlace = struct.unpack(">IIBBBBB", body)
        elif kind == b"IDAT":
            payload.extend(body)
        elif kind == b"IEND":
            break
    if bit_depth != 8 or color_type not in (2, 6) or interlace != 0:
        raise ValueError(
            f"{path} uses unsupported PNG format: depth={bit_depth}, color={color_type}, interlace={interlace}")
    channels = 3 if color_type == 2 else 4
    stride = width * channels
    packed = zlib.decompress(payload)
    rows = []
    previous = bytearray(stride)
    offset = 0
    for _ in range(height):
        filter_type = packed[offset]
        encoded = packed[offset + 1:offset + 1 + stride]
        offset += 1 + stride
        row = bytearray(stride)
        for index, value in enumerate(encoded):
            left = row[index - channels] if index >= channels else 0
            above = previous[index]
            upper_left = previous[index - channels] if index >= channels else 0
            if filter_type == 0:
                predictor = 0
            elif filter_type == 1:
                predictor = left
            elif filter_type == 2:
                predictor = above
            elif filter_type == 3:
                predictor = (left + above) // 2
            elif filter_type == 4:
                estimate = left + above - upper_left
                distances = (abs(estimate - left), abs(estimate - above), abs(estimate - upper_left))
                predictor = (left, above, upper_left)[distances.index(min(distances))]
            else:
                raise ValueError(f"{path} uses unknown PNG filter {filter_type}")
            row[index] = (value + predictor) & 0xFF
        rows.append(row)
        previous = row
    return width, height, channels, rows


def main():
    if len(sys.argv) != 3:
        raise SystemExit(f"usage: {sys.argv[0]} SCREENSHOT.png LAYOUT.json")
    png_path = pathlib.Path(sys.argv[1])
    layout_path = pathlib.Path(sys.argv[2])
    layout = json.loads(layout_path.read_text(encoding="utf-8"))
    density = float(layout["density"])
    bounds = layout["renderProbe"]
    expected = bytes.fromhex(layout["renderProbeRgb"])
    width, height, channels, rows = read_png(png_path)
    x0 = max(0, round((float(bounds["x"]) + 4) * density))
    y0 = max(0, round((float(bounds["y"]) + 4) * density))
    x1 = min(width, round((float(bounds["x"]) + float(bounds["width"]) - 4) * density))
    y1 = min(height, round((float(bounds["y"]) + float(bounds["height"]) - 4) * density))
    if x1 <= x0 or y1 <= y0:
        raise SystemExit(f"Invalid render probe bounds in {layout_path}: {bounds}")
    total = (x1 - x0) * (y1 - y0)
    matched = 0
    tolerance = 12
    for y in range(y0, y1):
        row = rows[y]
        for x in range(x0, x1):
            pixel = row[x * channels:x * channels + 3]
            if all(abs(pixel[index] - expected[index]) <= tolerance for index in range(3)):
                matched += 1
    ratio = matched / total
    if ratio < 0.80:
        raise SystemExit(
            f"UI render probe is absent in {png_path}: {matched}/{total} matching pixels ({ratio:.1%})")
    print(f"UI render probe visible in {png_path}: {ratio:.1%}")


if __name__ == "__main__":
    main()
