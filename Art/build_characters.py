"""4 Limbs -- builds the disguise (and the crew, when asked for).

Run this from Blender's Scripting tab: Text Editor > Open > this file > Run
Script. It builds into whatever scene is open, so an empty startup file is
exactly right.

The body is ONE continuous mesh, grown from a stick figure by the Skin
modifier. That is the whole trick: limbs flow out of the torso instead of being
separate lumps pushed together, and the colour break lands cleanly at the
shoulder and the hip. The head is a separate ovoid joined on, because the Skin
modifier lerps radius between rings and so always tapers a stacked head to a
cone at the crown.

The disguise is laid out on a SIX HEAD canon at 1.92 m, so one head is 0.32:

    6h  1.92  top of skull
    5h  1.60  chin
              shoulders 1.45
    3h  0.96  crotch 0.93
              knee 0.448
    0h  0.00  floor

Unity is Y-up and Blender is Z-up, so Unity Y is Blender Z and Unity Z is
Blender -Y. It faces -Y, Blender's front, so Numpad 1 looks it in the eye.

Because it faces -Y, the character's own RIGHT is -X: side -1.0 below is its
right arm and right leg, and that is the side the reference paints red and
yellow.
"""

import bpy
import math
from mathutils import Vector


BUILD_CREW = False   # the little one; off while the man is being worked on


# ---------------------------------------------------------------- materials

def mat(name, rgb, rough=0.62):
    m = bpy.data.materials.get(name)
    if m is None:
        m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (rgb[0], rgb[1], rgb[2], 1.0)
        bsdf.inputs["Roughness"].default_value = rough
        if "Specular IOR Level" in bsdf.inputs:
            bsdf.inputs["Specular IOR Level"].default_value = 0.2
    m.diffuse_color = (rgb[0], rgb[1], rgb[2], 1.0)
    return m


# Each player owns a whole limb, so the colour has to read from across the
# room. Right arm red, left arm blue, right leg yellow, left leg green.
PALETTE = {
    "body":  (0.78, 0.73, 0.65),
    "armR":  (0.90, 0.29, 0.25),
    "armL":  (0.20, 0.45, 0.88),
    "legR":  (0.97, 0.79, 0.15),
    "legL":  (0.28, 0.72, 0.34),
    "crew":  (0.90, 0.33, 0.31),
    "eye":   (0.97, 0.97, 0.96),
    "pupil": (0.04, 0.04, 0.05),
}


# ------------------------------------------------------------ the armature

class Figure:
    """A stick figure with a thickness at every joint. The Skin modifier turns
    it into a body. Radii are (across, deep) -- a torso is wider than it is
    thick, and one number for both gives a length of pipe."""

    def __init__(self):
        self.pos = []
        self.rad = []
        self.region = []
        self.edges = []
        self.segs = []

    def node(self, p, r, region):
        self.pos.append(Vector(p))
        self.rad.append(r if isinstance(r, (tuple, list)) else (r, r))
        self.region.append(region)
        return len(self.pos) - 1

    def link(self, i, j):
        self.edges.append((i, j))
        # A segment takes its CHILD's region, which is what puts the seam at the
        # shoulder rather than halfway down the upper arm.
        self.segs.append((self.pos[i], self.pos[j], self.region[j]))

    def chain(self, start, spec):
        """spec is [(pos, radius, region), ...] run end to end from start."""
        prev = start
        for p, r, region in spec:
            n = self.node(p, r, region)
            self.link(prev, n)
            prev = n
        return prev


def nearest_region(p, segs):
    best, best_d = segs[0][2], 1e9
    for a, b, region in segs:
        ab = b - a
        t = (p - a).dot(ab) / max(ab.length_squared, 1e-9)
        t = max(0.0, min(1.0, t))
        d = (p - (a + ab * t)).length
        if d < best_d:
            best_d, best = d, region
    return best


def grow(fig, name, regions, subdiv=1, smoothing=0.10, sole=0.018, fix=None):
    me = bpy.data.meshes.new(name + "Mesh")
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)

    me.from_pydata([tuple(p) for p in fig.pos], fig.edges, [])
    me.update()

    bpy.ops.object.select_all(action='DESELECT')
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)

    skin = ob.modifiers.new("Skin", 'SKIN')
    skin.use_smooth_shade = False
    # Low. High branch smoothing melts the arms into the ribs and loses the
    # sliver of daylight at the armpit, which is most of what reads as a body
    # rather than a slab.
    skin.branch_smoothing = smoothing

    # skin_vertices only exists once the modifier does.
    data = me.skin_vertices[0].data
    for i, r in enumerate(fig.rad):
        data[i].radius = r
    data[0].use_root = True

    sub = ob.modifiers.new("Subdivision", 'SUBSURF')
    sub.levels = subdiv
    sub.render_levels = subdiv

    bpy.ops.object.modifier_apply(modifier="Skin")
    bpy.ops.object.modifier_apply(modifier="Subdivision")

    # Flat soles, landing exactly on z=0 so it stands on the floor instead of
    # hovering. The skin modifier rounds a foot off into a sausage, and a figure
    # balanced on two curved sausages reads as tiptoeing.
    if sole:
        for v in ob.data.vertices:
            if v.co.z < sole:
                v.co.z = 0.0

    for region in regions:
        ob.data.materials.append(mat("FL_" + region, PALETTE[region]))
    index = {r: i for i, r in enumerate(regions)}

    if len(regions) > 1:
        for f in ob.data.polygons:
            # Nearest segment, then an explicit veto. Nearest-segment alone
            # lets a limb claim the trunk -- a leg segment rooted at the pelvis
            # runs up inside the hips, so the leg colour climbs the sides of the
            # torso. Earlier passes fought that by inserting body-coloured stub
            # nodes, which fixed the colour and wrecked the silhouette: the
            # stubs bulge. Say where the seam goes instead.
            r = nearest_region(f.center, fig.segs)
            if fix is not None:
                r = fix(f.center, r)
            f.material_index = index[r]

    return ob


def ball(ob, centre, radii, material, seg=20, ring=14):
    """A proper ovoid, joined onto the body.

    The Skin modifier cannot make one. It lerps the radius between consecutive
    rings, so a head built as a vertical chain of nodes always tapers to a cone
    at the crown -- no arrangement of the numbers avoids it, because a linear
    taper to a small top ring IS a cone. A sphere has to be a sphere.
    """
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=ring,
                                         radius=1.0, location=centre)
    h = bpy.context.object
    h.scale = radii
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    h.data.materials.append(material)
    for f in h.data.polygons:
        f.material_index = 0

    bpy.ops.object.select_all(action='DESELECT')
    h.select_set(True)
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.join()
    return ob


# ----------------------------------------------------------------- the eyes

def add_eyes(ob, centre, spread, forward, size, bulge):
    """Two balls sitting proud of the face. Sinking them into the head loses
    the whole expression -- googly eyes stand OFF the surface."""
    white = mat("FL_eye", PALETTE["eye"], 0.28)
    dark = mat("FL_pupil", PALETTE["pupil"], 0.22)

    made = []
    for side in (-1.0, 1.0):
        base = Vector((centre[0] + side * spread, centre[1] - forward, centre[2]))
        for radius, m, push in ((size, white, 0.0),
                                (size * 0.44, dark, bulge)):
            bpy.ops.mesh.primitive_uv_sphere_add(
                segments=20, ring_count=14, radius=radius,
                location=base + Vector((0.0, -push, 0.0)))
            e = bpy.context.object
            # Give each ball its OWN material and let join remap the slots. An
            # earlier version appended both materials to the body and stamped
            # face ranges by index after joining -- but join does not append
            # objects in the order they were selected, so the ranges landed on
            # the wrong faces and every pupil came out white.
            e.data.materials.append(m)
            for f in e.data.polygons:
                f.material_index = 0
            made.append(e)

    bpy.ops.object.select_all(action='DESELECT')
    for e in made:
        e.select_set(True)
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.join()


def facet(ob):
    """Flat shading for the body, smooth for the eyes.

    The reference is a faceted matte surface -- every quad reads as its own
    plane. Smooth shading hides exactly that, and earlier passes came back
    looking like inflated vinyl. The eyes are the one thing that should stay
    round, so they keep their smoothing.
    """
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob

    glossy = set(i for i, m in enumerate(ob.data.materials)
                 if m.name in ("FL_eye", "FL_pupil"))
    for poly in ob.data.polygons:
        poly.use_smooth = poly.material_index in glossy


def foot(side, ankle_z, heel_y, toe_y, lift, r, region):
    """Ankle down into a rounded foot: heel behind, sole along, toe in front.
    Running a leg straight into the ground instead tapers it to a point and the
    figure ends up standing on two pencils."""
    return [
        ((side, 0.0, ankle_z), r, region),
        ((side, heel_y, lift), (r[0] * 0.98, r[1] * 1.02), region),
        ((side, toe_y * 0.5, lift * 0.95), (r[0] * 1.0, r[1] * 1.10), region),
        ((side, toe_y, lift * 0.88), (r[0] * 0.84, r[1] * 0.90), region),
    ]


# ------------------------------------------------------------- the disguise

def build_disguise():
    """The thing four people wear.

    Tall and skinny on a six-head canon, relaxed A-pose. The arms sit at 20
    degrees off vertical, which is not a look-and-guess number: from the
    shoulder at (0.185, 1.440), an 0.58 arm at 20 degrees puts the elbow at
    0.185 + 0.30*sin20 = 0.288 and the wrist at 0.185 + 0.58*sin20 = 0.383.
    Every arm x below comes off that line.
    """
    f = Figure()

    # Every radius below is inflated by about a third, because subdivision
    # SHRINKS a skinned mesh by roughly 26% while the head -- joined on after
    # the modifiers are applied -- keeps its full size. Authoring both at face
    # value gave a normal head on a body two sizes too small, which is what made
    # every earlier pass read as scrawny. A 0.232 half-width here arrives as
    # 0.172, against a head 0.234 across, and that ratio is the reference.
    #
    # Squarer than a bean: it holds its width through the whole chest and eases
    # off only at the hips and the collarbones. Tapering both ends gives a
    # spindle.
    pelvis = f.node((0, 0, 0.962), (0.210, 0.131), "body")
    f.chain(pelvis, [
        ((0, 0, 1.085), (0.222, 0.139), "body"),
        ((0, 0, 1.225), (0.232, 0.145), "body"),
        ((0, 0, 1.365), (0.230, 0.144), "body"),
        ((0, 0, 1.500), (0.214, 0.134), "body"),   # shoulders
        ((0, 0, 1.542), (0.132, 0.098), "body"),   # slope, not a step
        ((0, 0, 1.578), (0.068, 0.070), "body"),   # short thin neck
        # Runs UP INTO the head. The ovoid's underside is at 1.597, so a neck
        # stopping short of that leaves the head floating above a gap. Raising
        # the SHOULDERS to 1.48 is what keeps the neck short while doing it --
        # lengthening the neck upward instead gave it a giraffe's.
        ((0, 0, 1.632), (0.074, 0.076), "body"),
    ])

    shoulder = 4   # the 1.500 node

    for side, region in ((-1.0, "armR"), (1.0, "armL")):
        f.chain(shoulder, [
            # Rooted INSIDE the torso and just below the shoulder line. Put it
            # outside and above and the skin modifier caps the arm off with its
            # own rounded top, so the limb reads as a sausage parked against the
            # body instead of something growing out of the shoulder.
            ((side * 0.102, 0, 1.482), (0.084, 0.084), "body"),
            ((side * 0.192, 0, 1.472), (0.078, 0.078), region),   # shoulder
            ((side * 0.295, 0, 1.190), (0.070, 0.070), region),   # elbow
            ((side * 0.390, 0, 0.927), (0.060, 0.060), region),   # wrist
        ])
        wrist = len(f.pos) - 1

        # The mitten, then a thumb branching off it toward the body and
        # forward. Without the thumb the hand is a paddle.
        f.chain(wrist, [
            ((side * 0.416, -0.016, 0.854), (0.112, 0.084), region),
            ((side * 0.432, -0.040, 0.788), (0.062, 0.046), region),
        ])
        f.chain(wrist, [
            ((side * 0.356, -0.056, 0.868), (0.052, 0.044), region),
            ((side * 0.338, -0.084, 0.838), (0.037, 0.031), region),
        ])

    # Long and straight, barely splayed.
    for side, region in ((-1.0, "legR"), (1.0, "legL")):
        f.chain(pelvis, [
            ((side * 0.102, 0, 0.892), (0.119, 0.119), region),   # hip
            ((side * 0.106, 0, 0.455), (0.088, 0.088), region),   # knee
        ] + foot(side * 0.110, 0.092, 0.055, -0.210, 0.058,
                 (0.080, 0.084), region))

    def seams(p, r):
        """Where the colour actually breaks, said out loud."""
        if r[:3] == "leg" and p.z > 0.952:
            return "body"          # legs stop at the crotch
        if r[:3] == "arm" and abs(p.x) < 0.148:
            return "body"          # arms stop at the shoulder, not the sternum
        return r

    ob = grow(f, "Disguise", ["body", "armR", "armL", "legR", "legL"],
              fix=seams)

    # One head unit is 0.32 on a 1.92 figure, and the egg is taller than wide.
    ball(ob, (0, 0, 1.760), (0.117, 0.127, 0.163),
         mat("FL_body", PALETTE["body"]))
    # Halfway down the face, which on a head spanning 1.597 to 1.923 is its
    # centre. Big, and standing proud of the surface.
    add_eyes(ob, (0, 0, 1.760), spread=0.062, forward=0.096,
             size=0.046, bulge=0.030)

    facet(ob)
    return ob


# ------------------------------------------------------------------- a crew

def build_crew():
    """One of the four. Head 33% of its height, body 46%, legs 21% -- the legs
    really are only a fifth of it, and giving them 29% made the little one read
    as a normal person who happened to be short, which is not the joke."""
    f = Figure()

    pelvis = f.node((0, 0, 0.33), (0.175, 0.155), "crew")
    f.chain(pelvis, [
        ((0, 0, 0.44), (0.215, 0.192), "crew"),
        ((0, 0, 0.55), (0.228, 0.203), "crew"),   # belly, the widest point
        ((0, 0, 0.67), (0.212, 0.188), "crew"),
        ((0, 0, 0.77), (0.172, 0.152), "crew"),   # shoulders
        ((0, 0, 0.835), (0.105, 0.105), "crew"),  # the stub the head sits on
    ])

    shoulder = 4
    for side in (-1.0, 1.0):
        f.chain(shoulder, [
            ((side * 0.175, 0, 0.742), (0.050, 0.050), "crew"),
            ((side * 0.208, 0, 0.640), (0.045, 0.045), "crew"),   # elbow
            ((side * 0.228, 0, 0.530), (0.040, 0.040), "crew"),   # wrist
            ((side * 0.238, -0.010, 0.478), (0.050, 0.043), "crew"),
            ((side * 0.242, -0.022, 0.432), (0.025, 0.021), "crew"),
        ])

    for side in (-1.0, 1.0):
        f.chain(pelvis, [
            ((side * 0.088, 0, 0.300), (0.090, 0.090), "crew"),
            ((side * 0.093, 0, 0.175), (0.075, 0.075), "crew"),   # knee
        ] + foot(side * 0.096, 0.062, 0.028, -0.115, 0.042,
                 (0.060, 0.064), "crew"))

    ob = grow(f, "Crew", ["crew"])
    ball(ob, (0, 0, 1.012), (0.175, 0.180, 0.200),
         mat("FL_crew", PALETTE["crew"]))
    add_eyes(ob, (0, 0, 0.995), spread=0.080, forward=0.155,
             size=0.056, bulge=0.036)
    facet(ob)
    return ob


# --------------------------------------------------------------------- main

def report(ob):
    xs = [(ob.matrix_world @ v.co).x for v in ob.data.vertices]
    zs = [(ob.matrix_world @ v.co).z for v in ob.data.vertices]
    h = max(zs) - min(zs)
    print("%-9s height %.2f (%.1f heads)  width %.2f  faces %5d  slots %d"
          % (ob.name, h, h / 0.326, max(xs) - min(xs),
             len(ob.data.polygons), len(ob.data.materials)))


def main():
    if bpy.context.object and bpy.context.object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')

    for name in ("Disguise", "Crew"):
        old = bpy.data.objects.get(name)
        if old:
            bpy.data.objects.remove(old, do_unlink=True)

    report(build_disguise())

    if BUILD_CREW:
        crew = build_crew()
        crew.location = (0.86, 0, 0)
        report(crew)

    bpy.ops.object.select_all(action='DESELECT')


main()
