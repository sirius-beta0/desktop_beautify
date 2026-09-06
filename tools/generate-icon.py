"""生成应用图标 Assets/app.ico。

用纯标准库绘制（圆角渐变底板 + 放大镜），渲染时 3 倍超采样再降采样，
得到带抗锯齿边缘的 RGBA 图标，最后以 PNG 载荷封装进 ICO。

用法：python tools/generate-icon.py
"""

import math
import struct
import zlib
from pathlib import Path

SIZE = 256
SS = 3                     # 超采样倍数
N = SIZE * SS
CORNER = 56 * SS           # 圆角半径
MARGIN = 6 * SS

GRAD_TOP = (0x3C, 0x8A, 0xE8)
GRAD_BOTTOM = (0x12, 0x35, 0x6B)
INK = (0xFF, 0xFF, 0xFF)

# 放大镜（相对 256 基准坐标）
LENS_CX, LENS_CY = 106, 102
LENS_R = 43
LENS_W = 13
HANDLE_START = (LENS_CX + 30, LENS_CY + 30)
HANDLE_END = (LENS_CX + 74, LENS_CY + 74)
HANDLE_W = 19


def segment_distance(px: float, py: float,
                     ax: float, ay: float,
                     bx: float, by: float) -> float:
    vx, vy = bx - ax, by - ay
    wx, wy = px - ax, py - ay
    dot = vx * wx + vy * wy
    if dot <= 0:
        return math.hypot(px - ax, py - ay)
    length_sq = vx * vx + vy * vy
    if dot >= length_sq:
        return math.hypot(px - bx, py - by)
    t = dot / length_sq
    return math.hypot(px - (ax + vx * t), py - (ay + vy * t))


def render_master() -> bytearray:
    """在 N x N 的超采样画布上绘制，返回 RGBA 缓冲。"""
    buf = bytearray(N * N * 4)
    half = N / 2.0
    box_half = half - MARGIN

    lens_cx, lens_cy = LENS_CX * SS, LENS_CY * SS
    lens_r, lens_w = LENS_R * SS, LENS_W * SS
    hx0, hy0 = HANDLE_START[0] * SS, HANDLE_START[1] * SS
    hx1, hy1 = HANDLE_END[0] * SS, HANDLE_END[1] * SS
    handle_half = HANDLE_W * SS / 2.0

    for y in range(N):
        # 圆角矩形的 y 分量只跟行有关，先算出来
        dy = abs(y + 0.5 - half) - (box_half - CORNER)
        dy_pos = dy if dy > 0 else 0.0
        row_base = y * N * 4
        for x in range(N):
            dx = abs(x + 0.5 - half) - (box_half - CORNER)
            dx_pos = dx if dx > 0 else 0.0
            dist = math.hypot(dx_pos, dy_pos) + min(max(dx, dy), 0.0) - CORNER

            i = row_base + x * 4
            if dist > 0:
                continue  # 圆角外，保持透明

            t = (x + y) / (2.0 * N)
            r = GRAD_TOP[0] + (GRAD_BOTTOM[0] - GRAD_TOP[0]) * t
            g = GRAD_TOP[1] + (GRAD_BOTTOM[1] - GRAD_TOP[1]) * t
            b = GRAD_TOP[2] + (GRAD_BOTTOM[2] - GRAD_TOP[2]) * t

            # 放大镜覆盖度：圆环 + 手柄
            d_lens = math.hypot(x + 0.5 - lens_cx, y + 0.5 - lens_cy)
            cover = 1.0 if abs(d_lens - lens_r) <= lens_w / 2.0 else 0.0
            if segment_distance(x + 0.5, y + 0.5, hx0, hy0, hx1, hy1) <= handle_half:
                cover = 1.0

            if cover > 0:
                r += (INK[0] - r) * cover
                g += (INK[1] - g) * cover
                b += (INK[2] - b) * cover

            buf[i] = int(r)
            buf[i + 1] = int(g)
            buf[i + 2] = int(b)
            buf[i + 3] = 255

    return buf


def downsample(buf: bytearray, factor: int) -> bytearray:
    out = bytearray(SIZE * SIZE * 4)
    samples = factor * factor
    for y in range(SIZE):
        for x in range(SIZE):
            r = g = b = a = 0
            for sy in range(factor):
                base = ((y * factor + sy) * N + x * factor) * 4
                for sx in range(factor):
                    i = base + sx * 4
                    r += buf[i]
                    g += buf[i + 1]
                    b += buf[i + 2]
                    a += buf[i + 3]
            o = (y * SIZE + x) * 4
            out[o] = r // samples
            out[o + 1] = g // samples
            out[o + 2] = b // samples
            out[o + 3] = a // samples
    return out


def area_resize(buf: bytearray, src: int, dst: int) -> bytearray:
    """按面积平均缩放 RGBA，用于生成小尺寸图标。"""
    if src == dst:
        return buf
    out = bytearray(dst * dst * 4)
    step = src / dst
    for y in range(dst):
        y0 = int(y * step)
        y1 = max(y0 + 1, int((y + 1) * step))
        for x in range(dst):
            x0 = int(x * step)
            x1 = max(x0 + 1, int((x + 1) * step))
            r = g = b = a = n = 0
            for sy in range(y0, y1):
                base = sy * src * 4
                for sx in range(x0, x1):
                    i = base + sx * 4
                    r += buf[i]
                    g += buf[i + 1]
                    b += buf[i + 2]
                    a += buf[i + 3]
                    n += 1
            o = (y * dst + x) * 4
            out[o] = r // n
            out[o + 1] = g // n
            out[o + 2] = b // n
            out[o + 3] = a // n
    return out


def encode_png(rgba: bytearray, size: int) -> bytes:
    raw = bytearray()
    stride = size * 4
    for y in range(size):
        raw.append(0)  # filter type: None
        raw += rgba[y * stride:(y + 1) * stride]

    def chunk(tag: bytes, data: bytes) -> bytes:
        return (struct.pack(">I", len(data)) + tag + data
                + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))

    ihdr = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", ihdr)
            + chunk(b"IDAT", zlib.compress(bytes(raw), 9))
            + chunk(b"IEND", b""))


def build_ico(images: list[tuple[int, bytes]]) -> bytes:
    header = struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)
    entries, blobs = b"", b""
    for size, png in images:
        code = 0 if size >= 256 else size
        entries += struct.pack("<BBBBHHII", code, code, 0, 0, 1, 32, len(png), offset)
        blobs += png
        offset += len(png)
    return header + entries + blobs


def main() -> None:
    print("渲染主图（3x 超采样）...")
    master = downsample(render_master(), SS)

    sizes = [256, 128, 64, 48, 32, 16]
    images = [(s, encode_png(area_resize(master, SIZE, s), s)) for s in sizes]

    target = Path(__file__).resolve().parent.parent / "src" / "Launcher.App" / "Assets" / "app.ico"
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(build_ico(images))

    print(f"已生成 {target}  ({target.stat().st_size} 字节, 尺寸: {', '.join(map(str, sizes))})")


if __name__ == "__main__":
    main()
