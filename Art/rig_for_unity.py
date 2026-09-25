"""Rig the scans to the Unity ragdolls and export FBX.

The scans are static meshes; the ragdoll is a pile of separate rigid bodies.
To make one wear the other, the mesh gets an armature whose bone NAMES match
the rigidbody GameObject names in CoatRagdollBuilder, and Unity's CoatSkin
component copies each body's transform onto the matching bone every frame.

The bones are placed along the MESH's own limbs, not along the rig's rest
positions. The scan's arms splay out; the rig's hang straight down at x +-0.20.
Putting bones where the rig has them would run the arm bones through the chest
and automatic weights would be nonsense. CoatSkin bakes the offset between bone
and body at bind time, so the two rest poses do not have to agree.

Bone names for ClassicRig (15 bodies):

    Pelvis  Torso  Head
    ThighL  ShinL  FootL        ThighR  ShinR  FootR
    UpperArmL  ForearmL  HandL  UpperArmR  ForearmR  HandR
"""

import bpy
import os
from mathutils import Vector

OUT_DIR = r"C:\Users\sigma\MultiPlayer\Assets\CoatGame\Art"


# ------------------------------------------------------------ measuring it

def sample(verts, h, z0, z1, sign, pick):
    c = [v.co for v in verts if z0 * h <= v.co.z < z1 * h and v.co.x * sign > 0]
    if not c:
        return None
    if pick == "out":
        return max(c, key=lambda p: abs(p.x))
    n = len(c)
    return Vector((sum(p.x for p in c) / n, sum(p.y for p in c) / n,
                   sum(p.z for p in c) / n))


def crotch_of(verts, h):
    z, step = 0.62 * h, h / 120.0
    while z > 0.22 * h:
        if not any(abs(v.co.z - z) < step and abs(v.co.x) < 0.035 * h for v in verts):
            return z
        z -= step
    return 0.48 * h


def skeleton(ob):
    """Where the limbs actually run in this mesh."""
    verts = ob.data.vertices
    h = max(v.co.z for v in verts)
    crotch = crotch_of(verts, h)
    top = h

    # Neck: narrowest slice in the upper half, same test the head fit uses.
    neck, thinnest = 0.80 * h, 1e9
    for k in range(40):
        z = h * (0.58 + 0.30 * k / 39.0)
        sl = [abs(v.co.x) for v in verts if abs(v.co.z - z) < h * 0.012]
        if len(sl) > 12:
            wide = sorted(sl)[int(len(sl) * 0.95)]
            if wide < thinnest:
                thinnest, neck = wide, z

    chains = {}
    chains["spine"] = [
        ("Pelvis", Vector((0, 0, crotch)), Vector((0, 0, (crotch + neck) * 0.5))),
        ("Torso", Vector((0, 0, (crotch + neck) * 0.5)), Vector((0, 0, neck))),
        ("Head", Vector((0, 0, neck)), Vector((0, 0, top))),
    ]

    for sign, tag in ((-1, "R"), (1, "L")):
        # Unity's left arm is built at x = -0.20 and the mesh faces -Y in
        # Blender, so the mesh's -X side is the character's RIGHT. Whether the
        # FBX round trip preserves that is checked in Unity, not assumed here.
        a = [sample(verts, h, *w, sign, "out")
             for w in ((0.75, 0.79), (0.60, 0.64), (0.43, 0.52))]
        if all(p is not None for p in a):
            # Pulled toward the midline so the bone sits inside the arm rather
            # than on its skin.
            sh, el, wr = [Vector((p.x * 0.82, p.y, p.z)) for p in a]
            hand = wr + (wr - el).normalized() * ((wr - el).length * 0.34)
            chains["arm" + tag] = [
                ("UpperArm" + tag, sh, el),
                ("Forearm" + tag, el, wr),
                ("Hand" + tag, wr, hand),
            ]

        p = [q for q in (sample(verts, h, *w, sign, "mid")
                         for w in ((0.40, 0.46), (0.22, 0.28), (0.02, 0.07)))
             if q is not None]
        if len(p) >= 3:
            hip = Vector((p[0].x, 0, crotch))
            knee = Vector((p[1].x, 0, p[1].z))
            ankle = Vector((p[2].x, 0, p[2].z))
            toe = Vector((p[2].x, ankle.y - 0.16 * h, ankle.z * 0.55))
            chains["leg" + tag] = [
                ("Thigh" + tag, hip, knee),
                ("Shin" + tag, knee, ankle),
                ("Foot" + tag, ankle, toe),
            ]

    print("  crotch %.3f  neck %.3f  top %.3f" % (crotch, neck, top))
    return chains


# ------------------------------------------------------------- building it

def build_armature(ob, chains, name):
    bpy.ops.object.select_all(action='DESELECT')
    arm_data = bpy.data.armatures.new(name + "Arm")
    rig = bpy.data.objects.new(name + "Rig", arm_data)
    bpy.context.collection.objects.link(rig)
    rig.location = ob.location
    bpy.context.view_layer.objects.active = rig
    rig.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')

    made = {}
    for chain in chains.values():
        parent = None
        for bname, head, tail in chain:
            b = arm_data.edit_bones.new(bname)
            b.head, b.tail = head, tail
            b.use_connect = False
            if parent is not None:
                b.parent = parent
            made[bname] = b
            parent = b

    # Limbs hang off the trunk, as they do in the ragdoll.
    for tag in ("L", "R"):
        if "UpperArm" + tag in made and "Torso" in made:
            made["UpperArm" + tag].parent = made["Torso"]
        if "Thigh" + tag in made and "Pelvis" in made:
            made["Thigh" + tag].parent = made["Pelvis"]
    if "Torso" in made and "Pelvis" in made:
        made["Torso"].parent = made["Pelvis"]
    if "Head" in made and "Torso" in made:
        made["Head"].parent = made["Torso"]

    bpy.ops.object.mode_set(mode='OBJECT')
    print("  armature: %d bones -- %s"
          % (len(made), " ".join(sorted(made))))
    return rig


def skin(ob, rig):
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.parent_set(type='ARMATURE_AUTO')
    groups = len(ob.vertex_groups)
    print("  skinned: %d vertex groups" % groups)
    return groups


def export(ob, rig, path):
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        apply_scale_options='FBX_SCALE_ALL',
        object_types={'ARMATURE', 'MESH'},
        add_leaf_bones=False,
        bake_anim=False,
        mesh_smooth_type='FACE',
        colors_type='SRGB',
        axis_forward='-Z',
        axis_up='Y',
    )
    print("  exported " + path)


def main():
    if not os.path.isdir(OUT_DIR):
        os.makedirs(OUT_DIR)
    if bpy.context.object and bpy.context.object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')

    # The crew uses the SAME bone names. Its rig has fewer bodies -- one leg,
    # and hands with no upper arm or forearm behind them -- and CoatSkin handles
    # that: ThighL and ThighR both fall back to the single "Thigh", and a bone
    # with no body at all just rides its parent.
    for name in ("Disguise", "RedChild"):
        ob = bpy.data.objects.get(name)
        if ob is None:
            print("%s: not in this file" % name)
            continue
        print(name + ":")
        # Park it at the origin; the rig it will wear lives there.
        ob.location = (0, 0, 0)
        chains = skeleton(ob)
        rig = build_armature(ob, chains, name)
        skin(ob, rig)
        export(ob, rig, os.path.join(OUT_DIR, name + ".fbx"))


main()
