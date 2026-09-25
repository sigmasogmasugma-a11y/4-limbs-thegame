"""4 Limbs -- turn the generated scans into two usable characters.

Run from Blender's Scripting tab, or headless. It builds into the open scene.

The generator was fed the four-view reference SHEET, so for each character it
reconstructed every panel as its own standalone body and returned all four
standing in a row. Each one is whole, so there is nothing to merge: keep the
front-facing one and drop the rest.

Order matters, and the order below is the one that works:

  import -> keep one -> stand and scale -> denoise -> egg the head ->
  subdivide (disguise only) -> paint -> eyeballs

THERE IS NO MEMBRANE BETWEEN HAND AND HIP. Measured: a ray cast inward from
the surface finds it 110 mm thick at the 5th percentile and nothing anywhere on
either body is under 14 mm. The hand and the hip are separate, with a real gap.
The mess in the crevice is reconstruction NOISE -- jagged triangles in a narrow
slot the generator had little information about. Do not cut it. Cutting it
destroyed the hands: the test used was "opposing surface within 30 mm", which is
true of a hand hanging beside a hip, so it took the facing side off both and the
fill afterwards turned what was left into a blob. The scan's hand is a decent
rounded mitten with a thumb bump, and it only needs leaving alone.

Other things that did not work, so they do not get tried again:

  * Voxel remesh does not clean the crevice, and costs the red child 20,000
    faces to not do it.
  * A despike pass finds nothing there; the noise is not spikes.
  * Filling a cut with sides=0 spans the gap, because the cut leaves one
    boundary loop running around it.
  * A blanket relax rounds the mitten and the thumb away along with the noise,
    so denoising is weighted by how much each vertex's normal disagrees with
    its neighbours'.
  * Relaxing the face around the EYEBALLS does not remove the scan's own eye
    lumps; the balls sit above where the lumps are. Project the whole head onto
    a fitted ellipsoid instead and every lump and crease goes at once.
  * A vertex colour cannot hold an edge sharper than the gap between vertices,
    so eyes painted on came out as soft smudges with no pupils. Eyes are
    geometry.
  * Per-FACE materials cannot paint a smooth seam either -- a material boundary
    runs along triangle edges, so it staircases. Limb colours are vertex
    colours; the material slots stay so Unity can still split by limb.
"""

import bpy
import math
import os
from mathutils import Vector


SCANS = [
    dict(name="Disguise",
         src=r"C:\Users\sigma\Downloads\white_mesh.glb",
         height=1.92,          # the coat rig in CoatRagdollBuilder
         subdiv=True,
         solid=None),
    dict(name="RedChild",
         src=r"C:\Users\sigma\Downloads\white_mesh (1).glb",
         height=1.21,          # the crew rig
         subdiv=False,         # one solid colour, so no seams to smooth
         solid=(0.90, 0.33, 0.31)),
]

LIMB = [
    ("FL_body", (0.78, 0.73, 0.65)),
    ("FL_armR", (0.90, 0.29, 0.25)),
    ("FL_armL", (0.20, 0.45, 0.88)),
    ("FL_legR", (0.97, 0.79, 0.15)),
    ("FL_legL", (0.28, 0.72, 0.34)),
]
EYE_RGB = (0.97, 0.97, 0.96)
PUPIL_RGB = (0.03, 0.03, 0.04)


# ---------------------------------------------------------------- materials

def base(name, rgb):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    for n in [n for n in nt.nodes if n.type == 'VERTEX_COLOR']:
        nt.nodes.remove(n)
    b = nt.nodes.get("Principled BSDF")
    if b:
        b.inputs["Base Color"].default_value = (rgb[0], rgb[1], rgb[2], 1.0)
        b.inputs["Roughness"].default_value = 0.62
    m.diffuse_color = (rgb[0], rgb[1], rgb[2], 1.0)
    return m


def vertex_lit(name, rgb):
    m = base(name, rgb)
    nt = m.node_tree
    b = nt.nodes.get("Principled BSDF")
    v = nt.nodes.new("ShaderNodeVertexColor")
    v.layer_name = "Col"
    nt.links.new(v.outputs["Color"], b.inputs["Base Color"])
    return m


# -------------------------------------------------------------------- utils

def world_bounds(o):
    bb = [o.matrix_world @ Vector(c) for c in o.bound_box]
    return (Vector((min(p.x for p in bb), min(p.y for p in bb), min(p.z for p in bb))),
            Vector((max(p.x for p in bb), max(p.y for p in bb), max(p.z for p in bb))))


def adjacency(me):
    nbr = [[] for _ in me.vertices]
    for e in me.edges:
        a, b = e.vertices
        nbr[a].append(b)
        nbr[b].append(a)
    return nbr


def centroid(pts):
    n = len(pts)
    return Vector((sum(p.x for p in pts) / n, sum(p.y for p in pts) / n,
                   sum(p.z for p in pts) / n))


def activate(ob):
    bpy.ops.object.select_all(action='DESELECT')
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)


# ------------------------------------------------------------------- stages

def import_one(spec):
    """Import the four-up scan and keep the front-facing figure.

    Only the objects this import creates are touched. Wiping the whole scene
    first would be simpler and would delete the character built on the previous
    pass, which is the sort of thing that is very confusing to debug.
    """
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=spec["src"])
    fresh = [o for o in bpy.data.objects if o not in before]

    src = next(o for o in fresh if o.type == 'MESH')
    activate(src)
    src.parent = None
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.separate(type='LOOSE')
    bpy.ops.object.mode_set(mode='OBJECT')

    parts = [o for o in bpy.context.selected_objects if o.type == 'MESH']
    # Leftmost is the front panel's reconstruction: widest in X, shallowest in
    # Y, and already facing -Y, which is Blender's front.
    keep = min(parts, key=lambda o: world_bounds(o)[0].x)
    for o in list(bpy.data.objects):
        if o is not keep and (o in fresh or o in parts):
            bpy.data.objects.remove(o, do_unlink=True)

    keep.name = spec["name"]
    keep.data.name = spec["name"] + "Mesh"

    lo, hi = world_bounds(keep)
    keep.location -= Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z))
    activate(keep)
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)
    lo, hi = world_bounds(keep)
    s = spec["height"] / (hi.z - lo.z)
    keep.scale = (s, s, s)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    print("  kept front figure: %d faces" % len(keep.data.polygons))
    return keep


def denoise(ob, passes=10, threshold=0.20, weight=0.50):
    """Smooth the ragged patches, leave everything else alone.

    NOTHING IS CUT HERE, and that is the point. The mess in the crevice between
    each hand and the hip is not a membrane -- the hand and the hip are separate
    and a ray cast inward finds the surface 110 mm thick at the 5th percentile,
    with nothing under 14 mm anywhere on the body. There is no sheet. It is
    reconstruction NOISE in a narrow slot where the generator had little to go
    on: jagged triangles facing each other across a real gap.

    Every attempt to cut it did damage instead. The test used was "opposing
    surface within 30 mm", which is true of a hand hanging beside a hip, so it
    flagged the hand and the hip, took the facing side off both, and the fill
    and rim-relax that followed turned the hand into a blob.

    Smoothing has to be targeted for the same reason. A blanket relax rounds the
    mitten and its thumb away along with the noise. So: measure how much each
    vertex's normal disagrees with its neighbours', and only move the ones that
    disagree a lot. Flat and gently curved surfaces score near zero and do not
    move; a field of jagged triangles scores high and settles down.
    """
    me = ob.data
    nbr = adjacency(me)
    nrm = [v.normal.copy() for v in me.vertices]

    rough = []
    for i in range(len(me.vertices)):
        n = nbr[i]
        if not n:
            rough.append(0.0)
            continue
        agree = sum(nrm[i].dot(nrm[j]) for j in n) / len(n)
        rough.append(max(0.0, 1.0 - agree))

    # Blur the roughness itself, so a patch is treated as a patch rather than
    # as scattered single vertices.
    for _ in range(3):
        rough = [rough[i] if not nbr[i] else
                 0.5 * rough[i] + 0.5 * sum(rough[j] for j in nbr[i]) / len(nbr[i])
                 for i in range(len(rough))]

    hot = [min(1.0, max(0.0, (r - threshold * 0.4) / max(1e-6, threshold * 0.6)))
           for r in rough]
    moved = sum(1 for w in hot if w > 0.05)

    co = [v.co.copy() for v in me.vertices]
    for _ in range(passes):
        nxt = list(co)
        for i in range(len(co)):
            if nbr[i] and hot[i] > 0.05:
                nxt[i] = co[i].lerp(centroid([co[j] for j in nbr[i]]),
                                    weight * hot[i])
        co = nxt
    for i, v in enumerate(me.vertices):
        v.co = co[i]
    me.update()
    print("  denoised %d of %d verts" % (moved, len(me.vertices)))


def egg_head(ob):
    """Project the head onto a fitted ellipsoid, fading out at the neck.

    This is what removes the scan's own eye lumps, its creases and its
    nose-ish ridge -- all at once, and without having to find any of them.
    The reference head is a bald egg with no features but the eyes.
    """
    verts = ob.data.vertices
    zs = [v.co.z for v in verts]
    top, bottom = max(zs), min(zs)
    H = top - bottom

    neck, thinnest = bottom + 0.70 * H, 1e9
    for k in range(40):
        z = bottom + H * (0.58 + 0.30 * k / 39.0)
        sl = [abs(v.co.x) for v in verts if abs(v.co.z - z) < H * 0.012]
        if len(sl) > 12:
            wide = sorted(sl)[int(len(sl) * 0.95)]
            if wide < thinnest:
                thinnest, neck = wide, z

    head = [v for v in verts if v.co.z > neck]
    if len(head) < 50:
        return None
    C = centroid([v.co for v in head])

    # 92nd percentile, not the max: the max IS a lump, and using it would
    # inflate the ellipsoid around the very bumps this is meant to erase.
    def rad(axis):
        d = sorted(abs(getattr(v.co - C, axis)) for v in head)
        return max(1e-4, d[int(len(d) * 0.92)])

    a, b, c = rad("x"), rad("y"), rad("z")
    for v in head:
        d = v.co - C
        q = math.sqrt((d.x / a) ** 2 + (d.y / b) ** 2 + (d.z / c) ** 2)
        if q < 1e-6:
            continue
        t = min(1.0, max(0.0, (v.co.z - neck) / (0.30 * (top - neck))))
        v.co = v.co.lerp(C + d * (1.0 / q), 0.93 * (t * t * (3 - 2 * t)))
    ob.data.update()
    print("  head: neck %.3f  radii %.3f %.3f %.3f" % (neck, a, b, c))
    return C, a, b, c


def subdivide(ob):
    activate(ob)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.subdivide(number_cuts=1, smoothness=0.0)
    bpy.ops.object.mode_set(mode='OBJECT')


def paint_limbs(ob):
    """Nearest limb axis, label-diffused, written as vertex colours."""
    me = ob.data
    verts = me.vertices
    h = max(v.co.z for v in verts)
    nbr = adjacency(me)

    def sample(z0, z1, sign, pick):
        c = [v.co for v in verts if z0 * h <= v.co.z < z1 * h and v.co.x * sign > 0]
        if not c:
            return None
        return max(c, key=lambda p: abs(p.x)) if pick == "out" else centroid(c)

    crotch, z, step = 0.48 * h, 0.62 * h, h / 120.0
    while z > 0.22 * h:
        if not any(abs(v.co.z - z) < step and abs(v.co.x) < 0.035 * h for v in verts):
            crotch = z
            break
        z -= step

    def chain(p):
        return [(p[i], p[i + 1]) for i in range(len(p) - 1)]

    axes = {0: chain([Vector((0, 0, crotch * 0.96)), Vector((0, 0, 0.80 * h)),
                      Vector((0, 0, h))])}
    for sign, arm, leg in ((-1, 1, 3), (1, 2, 4)):
        p = [sample(*w, sign, "out")
             for w in ((0.75, 0.79), (0.60, 0.64), (0.43, 0.52))]
        if all(q is not None for q in p):
            # Pulled toward the midline: an axis sitting on the skin wins too
            # much of the shoulder and the seam becomes a yoke.
            axes[arm] = chain([Vector((q.x * 0.82, q.y, q.z)) for q in p])
        p = [q for q in (sample(*w, sign, "mid")
                         for w in ((0.40, 0.46), (0.22, 0.28), (0.02, 0.07)))
             if q is not None]
        if len(p) >= 2:
            axes[leg] = chain([Vector((q.x, 0, q.z)) for q in p]
                              + [Vector((p[-1].x, 0, 0.0))])

    def near(p, segs):
        best = 1e9
        for a, b in segs:
            ab = b - a
            t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-9)))
            best = min(best, (p - (a + ab * t)).length)
        return best

    slots = sorted(axes)
    label = [min((near(v.co, axes[s]), s) for s in slots)[1] for v in verts]

    # Diffuse the LABELS, not the distances. Smoothing both distance fields by
    # the same amount leaves the contour where they cross exactly where it was.
    one = [[1.0 if s == label[i] else 0.0 for s in slots] for i in range(len(verts))]
    for c in range(len(slots)):
        col = [row[c] for row in one]
        for _ in range(16):
            col = [col[i] if not nbr[i] else
                   0.45 * col[i] + 0.55 * sum(col[j] for j in nbr[i]) / len(nbr[i])
                   for i in range(len(verts))]
        for i in range(len(verts)):
            one[i][c] = col[i]
    label = [slots[max(range(len(slots)), key=lambda c: one[i][c])]
             for i in range(len(verts))]

    for a in list(me.color_attributes):
        me.color_attributes.remove(a)
    ca = me.color_attributes.new(name="Col", type='FLOAT_COLOR', domain='POINT')
    for i in range(len(verts)):
        r, g, b = LIMB[label[i]][1]
        ca.data[i].color = (r, g, b, 1.0)

    me.materials.clear()
    for name, rgb in LIMB:
        me.materials.append(vertex_lit(name, rgb))
    me.materials.append(base("FL_eye", EYE_RGB))
    me.materials.append(base("FL_pupil", PUPIL_RGB))
    for f in me.polygons:
        votes = [label[i] for i in f.vertices]
        f.material_index = max(set(votes), key=votes.count)
        f.use_smooth = False
    print("  painted, crotch at %.3f" % crotch)


def paint_solid(ob, rgb):
    me = ob.data
    me.materials.clear()
    me.materials.append(base("FL_crew", rgb))
    me.materials.append(base("FL_eye", EYE_RGB))
    me.materials.append(base("FL_pupil", PUPIL_RGB))
    for f in me.polygons:
        f.material_index = 0
        f.use_smooth = False


def add_eyes(ob, fit):
    """Googly eyes as geometry, placed by proportion on the cleaned head."""
    C, a, b, c = fit
    # C is in the object's LOCAL space; the spheres are made in WORLD space.
    C = C + ob.matrix_world.translation

    white = base("FL_eye", EYE_RGB)
    pupil = base("FL_pupil", PUPIL_RGB)
    R = a * 0.33
    dx, dz = a * 0.44, c * 0.17
    k = max(0.05, 1.0 - (dx / a) ** 2 - (dz / c) ** 2)
    dy = -b * math.sqrt(k)

    made = []
    for sign in (-1, 1):
        surf = Vector((C.x + sign * dx, C.y + dy, C.z + dz))
        centre = Vector((surf.x, surf.y + R * 0.52, surf.z))   # two thirds proud
        for radius, mat_, push in ((R, white, 0.0),
                                   (R * 0.42, pupil, R * 0.66)):
            bpy.ops.mesh.primitive_uv_sphere_add(
                segments=22, ring_count=14, radius=radius,
                location=(centre.x, centre.y - push, centre.z))
            e = bpy.context.object
            e.data.materials.append(mat_)
            for f in e.data.polygons:
                f.material_index = 0
                f.use_smooth = True
            made.append(e)

    bpy.ops.object.select_all(action='DESELECT')
    for e in made:
        e.select_set(True)
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.join()
    print("  eyes r %.3f at x %+.3f / %+.3f  z %.3f"
          % (R, C.x - dx, C.x + dx, C.z + dz))


# --------------------------------------------------------------------- main

def main():
    if bpy.context.object and bpy.context.object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')

    built = []
    for spec in SCANS:
        if not os.path.exists(spec["src"]):
            print("%s: MISSING %s" % (spec["name"], spec["src"]))
            continue
        print(spec["name"] + ":")
        ob = import_one(spec)
        denoise(ob)
        fit = egg_head(ob)
        if spec["subdiv"]:
            subdivide(ob)
        if spec["solid"] is None:
            paint_limbs(ob)
        else:
            paint_solid(ob, spec["solid"])
        if fit:
            add_eyes(ob, fit)
        ob.name = spec["name"]
        built.append(ob)

    # Stand them side by side with a hand's width between.
    for i in range(1, len(built)):
        prev, cur = built[i - 1], built[i]
        cur.location.x += (world_bounds(prev)[1].x + 0.12) - world_bounds(cur)[0].x
        bpy.context.view_layer.update()

    bpy.ops.object.select_all(action='DESELECT')
    for ob in built:
        lo, hi = world_bounds(ob)
        print("%-10s h %.2f  faces %6d  slots %d"
              % (ob.name, hi.z - lo.z, len(ob.data.polygons),
                 len(ob.data.materials)))
    return built


BUILT = main()
