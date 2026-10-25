# v7.7.0 Desierto Cálido: copia los modelos convertidos (desert_props.py) y las texturas CC0 de Poly Haven a
# Assets/Resources/Desert con tamaños contenidos (el build no debe crecer demasiado):
#  Props/<Nombre>.fbx + <Nombre>_m<i>_a.jpg (plantas: .png con alfa) / _n.jpg / _m.png (R metal, G AO, A suavidad)
#  Tex/<corto>_a.jpg / _n.jpg / _m.png
import os, sys, shutil
from PIL import Image
PROPS, TEX, ALPHA, DST = sys.argv[1:5]
os.makedirs(DST + "/Props", exist_ok=True); os.makedirs(DST + "/Tex", exist_ok=True)
BIG = ("DRock", "DCliff")
for f in sorted(os.listdir(PROPS)):
    p = os.path.join(PROPS, f); name = f.split("_m")[0] if "_m" in f and not f.endswith(".fbx") else f[:-4]
    if f.endswith(".fbx"): shutil.copy(p, DST + "/Props/" + f); continue
    size = 1024 if name.startswith(BIG) else 512
    base = f.rsplit(".", 1)[0]
    im = Image.open(p)
    if base.endswith("_a"):
        if name in ("DPlant1", "DPlant3"):
            src = {"DPlant1": "wild_rooibos_bush", "DPlant3": "shrub_sorrel_01"}[name]
            rgb = Image.open(os.path.join(ALPHA, src + "_Diffuse.png")).convert("RGB")
            a = Image.open(os.path.join(ALPHA, src + "_Alpha.png")).convert("L").resize(rgb.size)
            rgb.putalpha(a); rgb.resize((size, size), Image.LANCZOS).save(DST + "/Props/" + base + ".png")
        else: im.convert("RGB").resize((size, size), Image.LANCZOS).save(DST + "/Props/" + base + ".jpg", quality=90)
    elif base.endswith("_n"): im.convert("RGB").resize((size, size), Image.LANCZOS).save(DST + "/Props/" + base + ".jpg", quality=92)
    elif base.endswith("_m"): im.convert("RGBA").resize((min(size, 512),) * 2, Image.LANCZOS).save(DST + "/Props/" + base + ".png")
SURF = {   # corto: (carpeta Poly Haven, px)
    "plaster": ("rough_plaster_broken", 2048), "clay": ("clay_plaster", 1024), "stone": ("old_stone_wall", 2048),
    "stone2": ("stacked_stone_wall", 1024), "wood": ("weathered_planks", 1024), "floor": ("brown_mud_dry", 1024),
    "rock": ("rock_face", 1024), "sandstone": ("sandstone_cracks", 1024), "blocks": ("large_sandstone_blocks", 1024),
    "mud": ("mud_cracked_dry_03", 1024), "rubble": ("dry_ground_rocks", 1024), "worn": ("worn_cracked_plaster", 1024),
    "grain": ("sand_03", 1024), "grain2": ("dense_sand", 1024), "macro": ("aerial_sand", 512),
}
for k, (d, px) in SURF.items():
    sd = os.path.join(TEX, d)
    Image.open(f"{sd}/{d}_Diffuse_2k.png").convert("RGB").resize((px, px), Image.LANCZOS).save(f"{DST}/Tex/{k}_a.jpg", quality=90)
    if k != "macro":
        Image.open(f"{sd}/{d}_nor_gl_2k.png").convert("RGB").resize((px, px), Image.LANCZOS).save(f"{DST}/Tex/{k}_n.jpg", quality=92)
        ao = Image.open(f"{sd}/{d}_AO_2k.png").convert("L").resize((512, 512), Image.LANCZOS)
        ro = Image.open(f"{sd}/{d}_Rough_2k.png").convert("L").resize((512, 512), Image.LANCZOS)
        z = Image.new("L", (512, 512), 0)
        Image.merge("RGBA", (z, ao, z, ro.point(lambda v: 255 - v))).save(f"{DST}/Tex/{k}_m.png")
print("ok")
