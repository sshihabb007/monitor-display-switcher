#!/usr/bin/env python3
"""
Generates a crisp multi-resolution Windows .ico (16, 32, 48, 64, 256 px)
for DisplayInputSwitcher using pure Python standard library (zlib + struct).
"""
import math
import os
import struct
import zlib


def clamp(v, lo=0.0, hi=1.0):
    return lo if v < lo else (hi if v > hi else v)


def smoothstep(edge0, edge1, x):
    t = clamp((x - edge0) / (edge1 - edge0))
    return t * t * (3.0 - 2.0 * t)


def sd_rounded_box(px, py, cx, cy, hw, hh, r):
    qx = abs(px - cx) - hw + r
    qy = abs(py - cy) - hh + r
    out_d = math.hypot(max(qx, 0.0), max(qy, 0.0))
    in_d = min(max(qx, qy), 0.0)
    return out_d + in_d - r


def sd_segment(px, py, ax, ay, bx, by):
    pax, pay = px - ax, py - ay
    bax, bay = bx - ax, by - ay
    denom = bax * bax + bay * bay
    h = clamp((pax * bax + pay * bay) / denom) if denom > 1e-9 else 0.0
    return math.hypot(pax - bax * h, pay - bay * h)


def blend(dst, src_r, src_g, src_b, src_a):
    if src_a <= 0.0:
        return dst
    dr, dg, db, da = dst
    out_a = src_a + da * (1.0 - src_a)
    if out_a <= 1e-6:
        return (0.0, 0.0, 0.0, 0.0)
    out_r = (src_r * src_a + dr * da * (1.0 - src_a)) / out_a
    out_g = (src_g * src_a + dg * da * (1.0 - src_a)) / out_a
    out_b = (src_b * src_a + db * da * (1.0 - src_a)) / out_a
    return (out_r, out_g, out_b, out_a)


def render_icon_rgba(size):
    buf = bytearray(size * size * 4)
    aa = 1.35 / size

    for y in range(size):
        ny = (y + 0.5) / size
        for x in range(size):
            nx = (x + 0.5) / size
            col = (0.0, 0.0, 0.0, 0.0)

            # 1. Outer dark slate squircle card
            d_bg = sd_rounded_box(nx, ny, 0.5, 0.5, 0.45, 0.45, 0.21)
            bg_alpha = 1.0 - smoothstep(-aa, aa, d_bg)
            if bg_alpha > 0.0:
                t = clamp((nx * 0.55 + ny * 0.85))
                bg_r = (13.0 * (1.0 - t) + 22.0 * t) / 255.0
                bg_g = (18.0 * (1.0 - t) + 31.0 * t) / 255.0
                bg_b = (30.0 * (1.0 - t) + 52.0 * t) / 255.0

                # Subtle cyan/indigo radial ambient glow
                glow1 = math.exp(-((nx - 0.28) ** 2 + (ny - 0.28) ** 2) / 0.14)
                glow2 = math.exp(-((nx - 0.74) ** 2 + (ny - 0.72) ** 2) / 0.14)
                bg_r = clamp(bg_r + glow1 * 0.04 + glow2 * 0.16)
                bg_g = clamp(bg_g + glow1 * 0.22 + glow2 * 0.08)
                bg_b = clamp(bg_b + glow1 * 0.30 + glow2 * 0.32)
                col = blend(col, bg_r, bg_g, bg_b, bg_alpha)

                # Crisp outer border highlight
                border_w = max(0.018, 1.1 / size)
                border_a = (1.0 - smoothstep(0.0, aa, abs(d_bg + border_w * 0.5) - border_w * 0.5)) * bg_alpha
                if border_a > 0.0:
                    br = (56.0 * (1.0 - nx) + 139.0 * nx) / 255.0
                    bgc = (189.0 * (1.0 - nx) + 92.0 * nx) / 255.0
                    bb = (248.0 * (1.0 - nx) + 246.0 * nx) / 255.0
                    col = blend(col, br, bgc, bb, border_a * 0.78)

            # 2. Sleek monitor bezel outline
            d_mon_outer = sd_rounded_box(nx, ny, 0.5, 0.44, 0.30, 0.21, 0.055)
            stroke = max(0.030, 1.35 / size)
            mon_ring = 1.0 - smoothstep(-aa, aa, abs(d_mon_outer) - stroke * 0.5)
            if mon_ring > 0.0:
                gt = clamp((nx - 0.2) / 0.6)
                mr = (34.0 * (1.0 - gt) + 168.0 * gt) / 255.0
                mg = (211.0 * (1.0 - gt) + 85.0 * gt) / 255.0
                mb = (238.0 * (1.0 - gt) + 247.0 * gt) / 255.0
                col = blend(col, mr, mg, mb, mon_ring)

            # Monitor stand neck + base
            d_neck = sd_segment(nx, ny, 0.5, 0.66, 0.5, 0.76) - max(0.024, 1.1 / size)
            d_base = sd_segment(nx, ny, 0.36, 0.77, 0.64, 0.77) - max(0.025, 1.15 / size)
            d_stand = min(d_neck, d_base)
            stand_a = 1.0 - smoothstep(-aa, aa, d_stand)
            if stand_a > 0.0:
                col = blend(col, 148.0 / 255.0, 163.0 / 255.0, 184.0 / 255.0, stand_a * 0.95)

            # 3. Dual directional switch arrows inside monitor (Cyan -> right, Violet <- left)
            thick = max(0.026, 1.2 / size)
            # Top arrow (Cyan: HDMI)
            d_top = min(
                sd_segment(nx, ny, 0.34, 0.38, 0.64, 0.38),
                sd_segment(nx, ny, 0.56, 0.31, 0.65, 0.38),
                sd_segment(nx, ny, 0.56, 0.45, 0.65, 0.38),
            ) - thick
            top_a = 1.0 - smoothstep(-aa, aa, d_top)
            if top_a > 0.0:
                col = blend(col, 34.0 / 255.0, 211.0 / 255.0, 238.0 / 255.0, top_a)

            # Bottom arrow (Emerald/Violet: DP)
            d_bot = min(
                sd_segment(nx, ny, 0.36, 0.51, 0.66, 0.51),
                sd_segment(nx, ny, 0.44, 0.44, 0.35, 0.51),
                sd_segment(nx, ny, 0.44, 0.58, 0.35, 0.51),
            ) - thick
            bot_a = 1.0 - smoothstep(-aa, aa, d_bot)
            if bot_a > 0.0:
                col = blend(col, 167.0 / 255.0, 139.0 / 255.0, 250.0 / 255.0, bot_a)

            idx = (y * size + x) * 4
            buf[idx + 0] = int(clamp(col[0]) * 255.0 + 0.5)
            buf[idx + 1] = int(clamp(col[1]) * 255.0 + 0.5)
            buf[idx + 2] = int(clamp(col[2]) * 255.0 + 0.5)
            buf[idx + 3] = int(clamp(col[3]) * 255.0 + 0.5)

    return bytes(buf)


def encode_png(size, rgba_bytes):
    raw_rows = bytearray()
    stride = size * 4
    for y in range(size):
        raw_rows.append(0)  # Filter type 0 (None)
        raw_rows.extend(rgba_bytes[y * stride : (y + 1) * stride])

    def chunk(tag, data):
        crc = zlib.crc32(tag + data) & 0xFFFFFFFF
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", crc)

    ihdr = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    idat = zlib.compress(bytes(raw_rows), 9)
    return (
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", ihdr)
        + chunk(b"IDAT", idat)
        + chunk(b"IEND", b"")
    )


def build_ico(out_path):
    sizes = [16, 32, 48, 64, 256]
    png_blobs = []
    for s in sizes:
        rgba = render_icon_rgba(s)
        png_blobs.append((s, encode_png(s, rgba)))

    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    header = struct.pack("<HHH", 0, 1, len(png_blobs))
    offset = 6 + 16 * len(png_blobs)
    entries = bytearray()
    payload = bytearray()
    for s, blob in png_blobs:
        dim = 0 if s >= 256 else s
        entries.extend(struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(blob), offset))
        payload.extend(blob)
        offset += len(blob)

    with open(out_path, "wb") as f:
        f.write(header + entries + payload)


if __name__ == "__main__":
    target = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "assets", "app.ico")
    build_ico(target)
    print("Generated icon:", target, os.path.getsize(target), "bytes")

