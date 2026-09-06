"""
生成 DesktopBeautify Launcher 的程序图标与中心按钮默认图标。

设计：4 个淡蓝色圆角方块组成 2x2，整体向下倾斜约 18 度，背景透明。
- src/Launcher.UI/Assets/logo.png  -> 中心按钮默认图标（WPF 资源）
- src/Launcher.App/Assets/app.ico  -> 程序图标（exe / 托盘 / 任务栏），含 16/32/48/256 多尺寸

依赖：Pillow（`python -m pip install Pillow`）
用法：python tools/generate-icon.py
"""

from PIL import Image, ImageDraw

LIGHT_BLUE = (90, 169, 230, 255)  # 淡蓝色 #5AA9E6
CANVAS = 512
SQUARE = 170          # 单个方块边长
GAP = 34              # 方块间空隙
RADIUS = 30           # 圆角半径
TILT_DEG = 18         # 整体下倾角度（顺时针）


def draw_grid(size: int) -> Image.Image:
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    grid = 2 * SQUARE + GAP
    ox = (size - grid) // 2
    oy = (size - grid) // 2
    positions = [
        (ox, oy),                              # 左上
        (ox + SQUARE + GAP, oy),               # 右上
        (ox, oy + SQUARE + GAP),               # 左下
        (ox + SQUARE + GAP, oy + SQUARE + GAP),# 右下
    ]
    for (x, y) in positions:
        d.rounded_rectangle(
            [x, y, x + SQUARE, y + SQUARE],
            radius=RADIUS,
            fill=LIGHT_BLUE,
        )
    return img


def main() -> None:
    master = draw_grid(CANVAS)
    # 整体顺时针旋转（PIL 正角度为逆时针，故取负）
    tilted = master.rotate(-TILT_DEG, expand=True, resample=Image.BICUBIC)
    # 取中心正方形区域并缩放到目标尺寸，避免旋转后留白边
    w, h = tilted.size
    side = min(w, h)
    left = (w - side) // 2
    top = (h - side) // 2
    cropped = tilted.crop((left, top, left + side, top + side))

    out_png = "src/Launcher.UI/Assets/logo.png"
    logo = cropped.resize((256, 256), Image.LANCZOS)
    logo.save(out_png)
    print("wrote", out_png)

    out_ico = "src/Launcher.App/Assets/app.ico"
    ico = cropped.resize((256, 256), Image.LANCZOS)
    ico.save(out_ico, sizes=[(16, 16), (32, 32), (48, 48), (256, 256)])
    print("wrote", out_ico)


if __name__ == "__main__":
    main()
