"""世界地図のデータを作る（Unity の外で 1 回だけ動かす道具。ゲームの中では使わない）

元データ: Natural Earth の国境データ ne_50m_admin_0_countries.geojson（パブリックドメイン）
  https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_50m_admin_0_countries.geojson

作るもの（My project/Assets/Resources/Map/ に置く）:
  CountryMap.bytes  … 地図の 1 ピクセルごとに「どの国か」の番号（1 バイト）。0 = 海、255 = 人の住まない土地
                      上の行から順に並ぶ（Unity では上下を反対にして使う）
  Countries.json    … 国の一覧（番号・日本語名・人口・大陸・名前を出す位置）

使い方:
  python3 tools/make_world_map.py <geojson のパス>
"""
import json
import math
import os
import sys

from PIL import Image, ImageDraw

WIDTH = 2048
# 日本が真ん中に来る地図（太平洋が中心）。左端が西経 30 度、右端は東経 330 度（= 西経 30 度）
LON_MIN, LON_MAX = -30.0, 330.0
LAT_MAX, LAT_MIN = 84.0, -57.0  # 南極は入れない
HEIGHT = round(WIDTH * (LAT_MAX - LAT_MIN) / (LON_MAX - LON_MIN))
OCEAN, NO_PEOPLE = 0, 255

OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "My project", "Assets", "Resources", "Map")

CONTINENT_JA = {
    "Asia": "アジア", "Europe": "ヨーロッパ", "Africa": "アフリカ", "North America": "北アメリカ",
    "South America": "南アメリカ", "Oceania": "オセアニア", "Seven seas (open ocean)": "海の島々",
    "Antarctica": "南極",
}


def wrap_lon(lon):
    while lon < LON_MIN:
        lon += 360
    while lon >= LON_MAX:
        lon -= 360
    return lon


def to_px(lon, lat):
    x = (lon - LON_MIN) / (LON_MAX - LON_MIN) * WIDTH
    y = (LAT_MAX - lat) / (LAT_MAX - LAT_MIN) * HEIGHT
    return (x, y)


def polygons(geom):
    if geom["type"] == "Polygon":
        return [geom["coordinates"]]
    if geom["type"] == "MultiPolygon":
        return geom["coordinates"]
    return []


def ring_area(ring):
    a = 0.0
    for (x1, y1), (x2, y2) in zip(ring, ring[1:] + ring[:1]):
        a += x1 * y2 - x2 * y1
    return abs(a) / 2


def main(path):
    with open(path, encoding="utf-8") as f:
        features = json.load(f)["features"]

    countries = []
    for ft in features:
        p = ft["properties"]
        if p["ADMIN"] == "Antarctica":
            continue
        rings = [poly[0] for poly in polygons(ft["geometry"])]  # 外側の輪郭だけ（内側の穴は別の国が上から塗る）
        if not rings:
            continue
        countries.append({
            "props": p,
            "rings": rings,
            "area": sum(ring_area(r) for r in rings),
            "pop": max(0, int(p["POP_EST"] or 0)),
        })

    inhabited = [c for c in countries if c["pop"] > 0]
    inhabited.sort(key=lambda c: -c["pop"])  # 人口の多い順に番号を付ける
    if len(inhabited) > 254:
        sys.exit("国が多すぎて 1 バイトに入らない")
    for i, c in enumerate(inhabited):
        c["id"] = i + 1
    for c in countries:
        c.setdefault("id", NO_PEOPLE)

    img = Image.new("L", (WIDTH, HEIGHT), OCEAN)
    draw = ImageDraw.Draw(img)
    # 大きい国から塗る（国の中にある小さい国が上から塗られて残るように）
    for c in sorted(countries, key=lambda c: -c["area"]):
        for ring in c["rings"]:
            if len(ring) < 3:
                continue
            # 地図の端をまたぐ国（グリーンランドなど）も描けるよう、1 周ずらした位置にも描く（はみ出た分は切れる）
            for shift in (-360, 0, 360):
                draw.polygon([to_px(lon + shift, lat) for lon, lat in ring], fill=c["id"])

    px = img.load()
    counts = img.histogram()
    out = []
    for c in inhabited:
        p = c["props"]
        lx, ly = to_px(wrap_lon(p["LABEL_X"]), p["LABEL_Y"])
        lx = min(WIDTH - 1, max(0, int(lx)))
        ly = min(HEIGHT - 1, max(0, int(ly)))
        # 小さすぎて 1 ピクセルも無い国は、名前の位置に 1 ピクセルだけ置く（クリックできるように）
        if counts[c["id"]] == 0:
            px[lx, ly] = c["id"]
        out.append({
            "id": c["id"],
            "name": p.get("NAME_JA") or p["NAME"],
            "nameEn": p["NAME"],
            "population": c["pop"],
            "continent": CONTINENT_JA.get(p["CONTINENT"], p["CONTINENT"]),
            "labelX": lx,
            "labelY": ly,
        })

    os.makedirs(OUT_DIR, exist_ok=True)
    with open(os.path.join(OUT_DIR, "CountryMap.bytes"), "wb") as f:
        f.write(img.tobytes())
    with open(os.path.join(OUT_DIR, "Countries.json"), "w", encoding="utf-8") as f:
        json.dump({"width": WIDTH, "height": HEIGHT, "countries": out}, f, ensure_ascii=False, indent=1)
    print(f"{WIDTH}x{HEIGHT}, 国 {len(out)}, 世界の人口 {sum(c['population'] for c in out):,}")


if __name__ == "__main__":
    main(sys.argv[1])
