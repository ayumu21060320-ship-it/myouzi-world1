"""モデルの部品の絵を描く（Unity の外で 1 回だけ動かす道具。ゲームの中では使わない）

横向き（右を向いた）のファッションモデル: まとめ髪の横顔、赤いミニドレス、黒いピンヒール。
部品ごとに PNG を書き出す。どの絵も、絵のまん中が RunwayModel.cs の物理の部品のまん中に重なるように描く。
1 メートル = PPU ピクセル。

作るもの（My project/Assets/Resources/Model/）:
  head.png, torso.png, thigh.png, shin.png, foot.png, upperarm.png, forearm.png

使い方:
  python3 tools/draw_model.py            … 部品を書き出す
  python3 tools/draw_model.py preview.png … あわせて、立ち姿に組み立てた見本も書き出す
"""
import os
import sys

from PIL import Image, ImageDraw

PPU = 500        # 1 メートルのピクセル数（RunwayModel.cs の ModelPixelsPerUnit と同じにする）
SS = 4           # なめらかにするため、4 倍の大きさで描いてから縮める
OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "My project", "Assets", "Resources", "Model")

SKIN = (245, 208, 182, 255)
SKIN_SHADE = (226, 182, 156, 255)
LINE = (92, 52, 40, 255)
HAIR = (48, 28, 22, 255)
HAIR_SHINE = (92, 60, 46, 255)
DRESS = (204, 24, 52, 255)
DRESS_SHADE = (150, 12, 38, 255)
DRESS_SHINE = (240, 80, 100, 255)
GOLD = (236, 190, 70, 255)
SHOE = (22, 20, 24, 255)
SOLE = (210, 20, 40, 255)
LIPS = (190, 40, 70, 255)
EYE = (40, 26, 24, 255)
LW = 0.006       # 線の太さ（メートル）


class Canvas:
    """まん中が (0, 0)、上が +y のメートルの座標で描く"""

    def __init__(self, w, h):
        self.w, self.h = w, h
        self.img = Image.new("RGBA", (round(w * PPU * SS), round(h * PPU * SS)), (0, 0, 0, 0))
        self.d = ImageDraw.Draw(self.img)

    def p(self, x, y):
        return ((x + self.w / 2) * PPU * SS, (self.h / 2 - y) * PPU * SS)

    def poly(self, pts, fill, line=True):
        q = [self.p(x, y) for x, y in pts]
        self.d.polygon(q, fill=fill)
        if line:
            self.d.line(q + [q[0]], fill=LINE, width=round(LW * PPU * SS), joint="curve")

    def ellipse(self, cx, cy, rx, ry, fill, line=True):
        a, b = self.p(cx - rx, cy + ry), self.p(cx + rx, cy - ry)
        self.d.ellipse([a, b], fill=fill, outline=LINE if line else None, width=round(LW * PPU * SS) if line else 0)

    def pieslice(self, cx, cy, rx, ry, start, end, fill):
        a, b = self.p(cx - rx, cy + ry), self.p(cx + rx, cy - ry)
        self.d.pieslice([a, b], start, end, fill=fill)

    def line(self, pts, fill=LINE, width=LW):
        self.d.line([self.p(x, y) for x, y in pts], fill=fill, width=max(1, round(width * PPU * SS)), joint="curve")

    def capsule(self, top, bottom, w_top, w_bottom, fill, front_bulge=0.0, back_bulge=0.0):
        """上下に長い、角の丸い手足（上の端 top〜下の端 bottom、幅は上から下へ変わる）"""
        n = 12
        right, left = [], []
        for i in range(n + 1):
            t = i / n
            y = top + (bottom - top) * t
            w = w_top + (w_bottom - w_top) * t
            bump = 4 * t * (1 - t)  # まん中でふくらむ
            right.append((w / 2 + front_bulge * bump, y))
            left.append((-w / 2 - back_bulge * bump, y))
        self.poly(right + left[::-1], fill, line=False)
        self.ellipse(0, top, w_top / 2, w_top / 2, fill, line=False)
        self.ellipse(0, bottom, w_bottom / 2, w_bottom / 2, fill, line=False)
        # りんかく線
        self.line(right)
        self.line(left)
        self.d.arc([self.p(-w_top / 2, top + w_top / 2), self.p(w_top / 2, top - w_top / 2)], 180, 360,
                   fill=LINE, width=round(LW * PPU * SS))
        self.d.arc([self.p(-w_bottom / 2, bottom + w_bottom / 2), self.p(w_bottom / 2, bottom - w_bottom / 2)], 0, 180,
                   fill=LINE, width=round(LW * PPU * SS))

    def save(self, name):
        out = self.img.resize((round(self.w * PPU), round(self.h * PPU)), Image.LANCZOS)
        out.save(os.path.join(OUT_DIR, name + ".png"))
        return out


def head():
    c = Canvas(0.42, 0.42)
    # 首（胴体の上に重なるように下へのばす）
    c.poly([(-0.035, -0.06), (0.03, -0.06), (0.032, -0.19), (-0.04, -0.19)], SKIN)
    # まとめ髪（後ろのおだんご）
    c.ellipse(-0.125, 0.04, 0.055, 0.05, HAIR)
    # 頭
    c.ellipse(0, 0, 0.105, 0.12, SKIN)
    # あご・顔の輪郭（前に少し出す）
    c.poly([(0.06, -0.06), (0.1, -0.05), (0.095, -0.085), (0.05, -0.115), (0.0, -0.11)], SKIN, line=False)
    c.line([(0.1, -0.05), (0.095, -0.085), (0.05, -0.115), (0.0, -0.115)])
    # 鼻
    c.poly([(0.098, 0.015), (0.135, -0.03), (0.1, -0.035)], SKIN, line=False)
    c.line([(0.098, 0.015), (0.135, -0.03), (0.1, -0.035)])
    # くちびる
    c.ellipse(0.1, -0.06, 0.014, 0.008, LIPS, line=False)
    # 目（まつげ付き）と、まゆ
    c.ellipse(0.06, 0.01, 0.016, 0.008, EYE, line=False)
    c.line([(0.045, 0.02), (0.085, 0.022), (0.092, 0.03)], EYE, 0.005)
    c.line([(0.04, 0.05), (0.07, 0.058), (0.092, 0.05)], HAIR, 0.006)
    # 髪（頭の上と後ろ）
    c.pieslice(-0.01, 0.01, 0.118, 0.13, 150, 322, HAIR)
    c.line([(-0.06, 0.1), (-0.02, 0.12), (0.03, 0.115)], HAIR_SHINE, 0.008)
    # 耳とイヤリング
    c.ellipse(-0.005, -0.02, 0.018, 0.026, SKIN_SHADE)
    c.ellipse(-0.005, -0.06, 0.01, 0.01, GOLD, line=False)
    return c.save("head")


def torso():
    c = Canvas(0.5, 1.0)
    # 肩と胸元の肌（ドレスの上に見える部分）
    c.poly([(-0.095, 0.2), (0.08, 0.2), (0.07, 0.25), (0.035, 0.28), (0.03, 0.32), (-0.04, 0.32), (-0.05, 0.28),
            (-0.085, 0.25)], SKIN)
    # ドレス（胴とミニスカート）
    body = [(-0.1, 0.21), (0.085, 0.21), (0.13, 0.13), (0.12, 0.06), (0.08, -0.04), (0.1, -0.2),
            (0.15, -0.46), (-0.17, -0.46), (-0.13, -0.2), (-0.1, -0.04), (-0.11, 0.1)]
    c.poly(body, DRESS)
    # かげ（背中側）と、つや（前側）
    c.poly([(-0.1, 0.2), (-0.06, 0.2), (-0.06, -0.04), (-0.09, -0.2), (-0.13, -0.45), (-0.165, -0.45), (-0.13, -0.2),
            (-0.1, -0.04), (-0.11, 0.1)], DRESS_SHADE, line=False)
    c.line([(0.1, 0.15), (0.11, 0.08)], DRESS_SHINE, 0.012)
    c.line([(0.09, -0.22), (0.12, -0.42)], DRESS_SHINE, 0.01)
    # スカートのすそのひだ
    for x in (-0.09, -0.02, 0.05, 0.11):
        c.line([(x * 0.6, -0.24), (x, -0.45)], DRESS_SHADE, 0.006)
    # 金のベルト
    c.poly([(-0.105, -0.025), (0.085, -0.025), (0.085, -0.06), (-0.105, -0.06)], GOLD)
    return c.save("torso")


def thigh():
    c = Canvas(0.2, 0.6)
    c.capsule(0.235, -0.235, 0.13, 0.085, SKIN, front_bulge=0.01, back_bulge=0.006)
    return c.save("thigh")


def shin():
    c = Canvas(0.16, 0.56)
    c.capsule(0.235, -0.225, 0.085, 0.05, SKIN, front_bulge=0.0, back_bulge=0.03)
    return c.save("shin")


def foot():
    # 物理の足: 板 x -0.11〜0.11（y = 0）、かかとの棒 x = -0.095（y 0〜-0.1）、つま先の台 x = 0.085（y = -0.1）。足首は (-0.04, 0.02)
    c = Canvas(0.32, 0.32)
    # 足の甲の肌（足首からつま先へ）
    c.poly([(-0.07, 0.05), (-0.01, 0.05), (0.06, -0.04), (0.1, -0.075), (0.07, -0.085), (-0.06, -0.0)], SKIN)
    # ピンヒール（細いかかと）と靴底（赤）
    c.poly([(-0.105, -0.0), (-0.085, -0.0), (-0.09, -0.1), (-0.098, -0.1)], SHOE)
    c.poly([(-0.1, -0.095), (-0.088, -0.095), (-0.088, -0.103), (-0.1, -0.103)], SOLE, line=False)
    # 靴（かかとからつま先へ下がる形）
    c.poly([(-0.115, 0.03), (-0.06, 0.025), (0.0, -0.03), (0.06, -0.06), (0.12, -0.085), (0.12, -0.1), (0.05, -0.1),
            (-0.03, -0.035), (-0.11, -0.01)], SHOE)
    c.line([(-0.03, -0.037), (0.05, -0.098), (0.118, -0.098)], SOLE, 0.006)
    # 足首のストラップ
    c.line([(-0.075, 0.035), (-0.005, 0.04)], SHOE, 0.012)
    return c.save("foot")


def upperarm():
    c = Canvas(0.14, 0.4)
    c.capsule(0.16, -0.155, 0.075, 0.058, SKIN, back_bulge=0.008)
    return c.save("upperarm")


def forearm():
    c = Canvas(0.14, 0.44)
    c.capsule(0.15, -0.13, 0.058, 0.045, SKIN)
    # 手（指先を前へ）
    c.poly([(-0.022, -0.13), (0.022, -0.13), (0.035, -0.17), (0.02, -0.2), (-0.01, -0.19), (-0.025, -0.16)], SKIN)
    # ブレスレット
    c.line([(-0.024, -0.115), (0.024, -0.115)], GOLD, 0.01)
    return c.save("forearm")


def preview(path, parts):
    """立ち姿に組み立てた見本（RunwayModel.cs と同じ位置）"""
    ankle_y = 0.14
    knee_y = ankle_y + 0.45
    hip_y = knee_y + 0.45
    neck_y = hip_y + 0.58
    w, h = 1.2, 2.2
    img = Image.new("RGBA", (round(w * PPU), round(h * PPU)), (30, 26, 40, 255))

    def put(name, x, y, tint=1.0):
        im = parts[name]
        if tint != 1.0:
            r, g, b, a = im.split()
            r = r.point(lambda v: v * tint)
            g = g.point(lambda v: v * tint)
            b = b.point(lambda v: v * tint)
            im = Image.merge("RGBA", (r, g, b, a))
        px = round((x + w / 2) * PPU - im.width / 2)
        py = round((h - 0.1 - y) * PPU - im.height / 2)
        img.alpha_composite(im, (px, py))

    shoulder_y = neck_y - 0.04
    put("upperarm", -0.03, shoulder_y - 0.15, 0.78)
    put("forearm", -0.03, shoulder_y - 0.30 - 0.14, 0.78)
    for dx, tint in ((-0.05, 0.78), (0.05, 1.0)):
        put("thigh", dx, knee_y + 0.225, tint)
        put("shin", dx, ankle_y + 0.225, tint)
        put("foot", dx + 0.04, 0.12, tint)
    put("torso", 0, hip_y + 0.29)  # スカートが太ももの付け根にかぶさるよう、足より手前
    put("head", 0, neck_y + 0.14)
    put("upperarm", 0.03, shoulder_y - 0.15)
    put("forearm", 0.03, shoulder_y - 0.30 - 0.14)
    img.save(path)


if __name__ == "__main__":
    os.makedirs(OUT_DIR, exist_ok=True)
    parts = {f.__name__: f() for f in (head, torso, thigh, shin, foot, upperarm, forearm)}
    if len(sys.argv) > 1:
        preview(sys.argv[1], parts)
    print("書き出した:", ", ".join(sorted(parts)))
