"""
The Armory rack's armoire model (issue #77), built in Blender from a script so it can be
regenerated and tuned by numbers.  Blender is only the modeler: the game never sees it.

    & "C:\\Program Files\\Blender Foundation\\Blender 5.3\\blender.exe" -b --python Armory\\Model\\armoire.py -- Armory\\Model\\armoire.obj

A second argument after the output picks a variant: "shields" puts a round shield over crossed
axes on each door (the loader lifts those onto any other model's doors, so no other variant
needs them); "curved" swaps the stepped crown for a bonnet top, a hood whose front rises in an
ogee from the shoulders to a rounded crest (issue #83).  (Real item prefabs, hung at runtime by
ItemEmblems.cs, replaced a modeled helm-and-sword variant that looked like what it was, a few
boxes and spheres.)

The OBJ is embedded in the DLL and read by RackVisual's ObjModel at runtime, which lays the
game's own wood textures onto it face by face; the UVs written here are ignored.  Conventions
the loader relies on:

  * meters, origin at the floor center, front of the cabinet toward -Y (Blender), which the
    export's -Z forward / Y up turns into Unity's +Z;
  * three materials, Body, Panel and Iron, which select the texture regions;
  * every door part is an object whose name starts with "Door"; the loader sorts them by side
    and hangs each side on a hinge at the door's outer front edge.
"""
import math
import os
import sys

import bmesh
import bpy

W, D, H = 2.0, 1.0, 3.0          # the footprint RackVisual and the collider assume
HW, HD = W / 2, D / 2
T = 0.08                         # board thickness
BOARD = 0.32                     # widest board before a surface is split into several

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = ARGS[0] if ARGS else "armoire.obj"
VARIANT = ARGS[1] if len(ARGS) > 1 else "plain"

bpy.ops.wm.read_factory_settings(use_empty=True)
MATS = {name: bpy.data.materials.new(name) for name in ("Body", "Panel", "Iron")}


def finish(obj, name, mat, bevel=0.0, segments=1):
    obj.name = name
    obj.data.name = name
    obj.data.materials.append(MATS[mat])
    if bevel > 0:
        mod = obj.modifiers.new("Bevel", "BEVEL")
        mod.width = bevel
        mod.segments = segments
        mod.limit_method = "ANGLE"
        mod.angle_limit = math.radians(60)
    return obj


def box(name, mat, x0, x1, y0, y1, z0, z1, bevel=0.006, segments=1):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2))
    obj = bpy.context.active_object
    obj.scale = (x1 - x0, y1 - y0, z1 - z0)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish(obj, name, mat, bevel, segments)


def box_at(name, mat, size, center, rot=(0, 0, 0), bevel=0.006, segments=1):
    """A box of `size` turned by `rot` (Euler, radians) about its own center, then placed."""
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, 0))
    obj = bpy.context.active_object
    obj.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.rotation_euler = rot
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    obj.location = center
    return finish(obj, name, mat, bevel, segments)


def sphere(name, mat, radius, center, scale=(1, 1, 1), keep=None):
    """A sphere, optionally squashed, optionally cut: keep(x, y, z) says which vertices stay."""
    bpy.ops.mesh.primitive_uv_sphere_add(segments=20, ring_count=12, radius=radius, location=(0, 0, 0))
    obj = bpy.context.active_object
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if keep is not None:
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not keep(*v.co)], context="VERTS")
        bm.to_mesh(obj.data)
        bm.free()
    obj.location = center
    return finish(obj, name, mat)


def boards(name, mat, x0, x1, y0, y1, z0, z1, axis, bevel=0.004):
    """A slab split into boards of at most BOARD along one axis ("x", "y" or "z")."""
    lo, hi = {"x": (x0, x1), "y": (y0, y1), "z": (z0, z1)}[axis]
    n = max(1, math.ceil((hi - lo) / BOARD - 0.001))
    w = (hi - lo) / n
    for i in range(n):
        a, b = lo + i * w, lo + (i + 1) * w
        bounds = {"x": (x0, x1), "y": (y0, y1), "z": (z0, z1)}
        bounds[axis] = (a, b)
        box(f"{name}_{i}", mat, *bounds["x"], *bounds["y"], *bounds["z"], bevel=bevel)


def cylinder(name, mat, radius, length, center, axis, segments=16):
    rot = {"x": (0, math.pi / 2, 0), "y": (math.pi / 2, 0, 0), "z": (0, 0, 0)}[axis]
    bpy.ops.mesh.primitive_cylinder_add(vertices=segments, radius=radius, depth=length, location=center, rotation=rot)
    return finish(bpy.context.active_object, name, mat)


def torus(name, mat, major, minor, center, axis):
    rot = {"x": (0, math.pi / 2, 0), "y": (math.pi / 2, 0, 0), "z": (0, 0, 0)}[axis]
    bpy.ops.mesh.primitive_torus_add(major_radius=major, minor_radius=minor, major_segments=20, minor_segments=8,
                                     location=center, rotation=rot)
    return finish(bpy.context.active_object, name, mat)


def arch_panel(name, mat, x0, x1, y0, y1, z0, z1, bevel=0.02):
    """A panel with a round-arched top, extruded from y0 to y1."""
    bm = bmesh.new()
    cx, hw = (x0 + x1) / 2, (x1 - x0) / 2
    spring = z1 - hw                      # the arch is a half circle of radius hw
    profile = [(x0, z0), (x1, z0), (x1, spring)]
    steps = 14
    for i in range(1, steps):
        a = math.pi * i / steps
        profile.append((cx + hw * math.cos(a), spring + hw * math.sin(a)))
    profile.append((x0, spring))
    verts = [bm.verts.new((x, y0, z)) for x, z in profile]
    face = bm.faces.new(verts)
    ext = bmesh.ops.extrude_face_region(bm, geom=[face])
    moved = [g for g in ext["geom"] if isinstance(g, bmesh.types.BMVert)]
    bmesh.ops.translate(bm, vec=(0, y1 - y0, 0), verts=moved)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    return finish(obj, name, mat, bevel, 2)


# ── Base ──────────────────────────────────────────────────────────────────────────
FOOT = 0.14
for i, (sx, sy) in enumerate(((-1, -1), (1, -1), (-1, 1), (1, 1))):
    cx, cy = sx * (HW - 0.12), sy * (HD - 0.12)
    box(f"Foot_{i}", "Body", cx - 0.11, cx + 0.11, cy - 0.11, cy + 0.11, 0.0, FOOT, bevel=0.02, segments=2)
PLINTH_TOP = FOOT + 0.12
box("Plinth", "Body", -HW - 0.02, HW + 0.02, -HD - 0.01, HD + 0.01, FOOT, PLINTH_TOP, bevel=0.01)

# ── Carcass ───────────────────────────────────────────────────────────────────────
Z0, Z1 = PLINTH_TOP, H - (0.36 if VARIANT == "curved" else 0.22)   # between plinth and crown
FRAME = 0.05                                   # the face frame's thickness, at the front
boards("SideL", "Body", -HW, -HW + T, -HD + FRAME, HD, Z0, Z1, "y")
boards("SideR", "Body", HW - T, HW, -HD + FRAME, HD, Z0, Z1, "y")
boards("Back", "Body", -HW + T, HW - T, HD - 0.05, HD, Z0, Z1, "x")
boards("Floor", "Body", -HW + T, HW - T, -HD + FRAME, HD - 0.05, Z0, Z0 + 0.06, "x")
boards("Ceiling", "Body", -HW + T, HW - T, -HD + FRAME, HD - 0.05, Z1 - 0.06, Z1, "x")
SHELF = Z0 + (Z1 - Z0) * 0.46
boards("Shelf", "Body", -HW + T, HW - T, -HD + 0.15, HD - 0.05, SHELF - 0.03, SHELF + 0.03, "x")
cylinder("Rail", "Iron", 0.015, W - 2 * T, (0.0, 0.05, Z1 - 0.30), "x")

# Face frame around the door opening.
STILE, TOP_RAIL, BOT_RAIL = 0.10, 0.12, 0.08
box("FrameL", "Body", -HW, -HW + STILE, -HD, -HD + FRAME, Z0, Z1)
box("FrameR", "Body", HW - STILE, HW, -HD, -HD + FRAME, Z0, Z1)
box("FrameTop", "Body", -HW + STILE, HW - STILE, -HD, -HD + FRAME, Z1 - TOP_RAIL, Z1)
box("FrameBottom", "Body", -HW + STILE, HW - STILE, -HD, -HD + FRAME, Z0, Z0 + BOT_RAIL)

def bezier(p0, p1, p2, n):
    """Points along a quadratic curve from p0 to p2 pulled toward p1, p0 excluded."""
    out = []
    for i in range(1, n + 1):
        t = i / n
        out.append(tuple((1 - t) ** 2 * a + 2 * (1 - t) * t * b + t ** 2 * c for a, b, c in zip(p0, p1, p2)))
    return out


def profile_solid(name, mat, profile, y0, y1, bevel=0.004, segments=1):
    """A flat shape (x, z) extruded from y0 to y1, at the origin, un-rotated."""
    bm = bmesh.new()
    verts = [bm.verts.new((x, y0, z)) for x, z in profile]
    face = bm.faces.new(verts)
    ext = bmesh.ops.extrude_face_region(bm, geom=[face])
    moved = [g for g in ext["geom"] if isinstance(g, bmesh.types.BMVert)]
    bmesh.ops.translate(bm, vec=(0, y1 - y0, 0), verts=moved)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    finish(obj, name, mat, bevel, segments)
    # The loader fans each polygon from its first vertex, which only works on convex ones;
    # a concave outline is cut into triangles here, after the bevel.
    obj.modifiers.new("Triangulate", "TRIANGULATE")
    return obj



# ── Crown ─────────────────────────────────────────────────────────────────────────


def bonnet(x0, x1, zb, zs, zt, n=28):
    """The outline (x, z) of a bonnet: flat from zb up to the shoulders at zs, then an ogee
    rising to a rounded crest at zt over the center and falling to the other shoulder."""
    cx, hw = (x0 + x1) / 2, (x1 - x0) / 2
    pts = [(x0, zb), (x1, zb), (x1, zs)]
    for i in range(1, n):
        x = x1 - (x1 - x0) * i / n
        s = 1 - abs(x - cx) / hw
        pts.append((x, zs + (zt - zs) * (3 * s * s - 2 * s ** 3)))
    pts.append((x0, zs))
    return pts


if VARIANT == "curved":
    # One straight molding, a solid hood the full depth with the bonnet outline, and a
    # pediment board in front of it a little larger, so the curve has an edge to read by.
    box("Crown_0", "Body", -HW - 0.03, HW + 0.03, -HD - 0.015, HD + 0.015, Z1, Z1 + 0.07, bevel=0.01)
    profile_solid("Crown_Hood", "Body", bonnet(-HW - 0.05, HW + 0.05, Z1 + 0.07, Z1 + 0.13, H - 0.02),
                  -HD - 0.025, HD + 0.025, bevel=0.012, segments=2)
    profile_solid("Crown_Pediment", "Body", bonnet(-HW - 0.08, HW + 0.08, Z1 + 0.07, Z1 + 0.15, H),
                  -HD - 0.06, -HD - 0.02, bevel=0.008, segments=2)
else:
    # Three steps.
    for i, (grow, gz) in enumerate(((0.03, 0.0), (0.06, 0.08), (0.09, 0.16))):
        box(f"Crown_{i}", "Body", -HW - grow, HW + grow, -HD - grow / 2, HD + grow / 2, Z1 + gz, Z1 + gz + 0.08 if i < 2 else H, bevel=0.01)

# ── Doors ─────────────────────────────────────────────────────────────────────────
OPEN_X0, OPEN_X1 = -HW + STILE, HW - STILE     # the opening in the face frame
OPEN_Z0, OPEN_Z1 = Z0 + BOT_RAIL, Z1 - TOP_RAIL
GAP, DOOR_T = 0.006, 0.07
DY0, DY1 = -HD, -HD + DOOR_T                   # a door lies flush with the frame front
D_STILE, D_RAIL_B, D_RAIL_M, D_RAIL_T = 0.13, 0.15, 0.12, 0.13
MID = OPEN_Z0 + (OPEN_Z1 - OPEN_Z0) * 0.42      # the middle rail, a little below center

for side, tag in ((-1, "DoorL"), (1, "DoorR")):
    xi = side * GAP                             # inner (meeting) edge
    xo = side * (HW - STILE - GAP)              # outer (hinge) edge
    z0, z1 = OPEN_Z0 + GAP, OPEN_Z1 - GAP
    lo, hi = min(xi, xo), max(xi, xo)
    # Frame
    box(f"{tag}_StileIn", "Body", min(xi, xi + side * D_STILE), max(xi, xi + side * D_STILE), DY0, DY1, z0, z1)
    box(f"{tag}_StileOut", "Body", min(xo, xo - side * D_STILE), max(xo, xo - side * D_STILE), DY0, DY1, z0, z1)
    px0, px1 = lo + D_STILE, hi - D_STILE       # between the stiles
    box(f"{tag}_RailBottom", "Body", px0, px1, DY0, DY1, z0, z0 + D_RAIL_B)
    box(f"{tag}_RailMid", "Body", px0, px1, DY0, DY1, MID - D_RAIL_M / 2, MID + D_RAIL_M / 2)
    box(f"{tag}_RailTop", "Body", px0, px1, DY0, DY1, z1 - D_RAIL_T, z1)
    # Panels: a raised rectangle below, a round-arched one above, both a touch behind the frame face.
    inset = 0.012
    box(f"{tag}_PanelLower", "Panel", px0 + inset, px1 - inset, DY0 + 0.005, DY1 - 0.01,
        z0 + D_RAIL_B + inset, MID - D_RAIL_M / 2 - inset, bevel=0.025, segments=2)
    arch_panel(f"{tag}_PanelUpper", "Panel", px0 + inset, px1 - inset, DY0 + 0.005, DY1 - 0.01,
               MID + D_RAIL_M / 2 + inset, z1 - D_RAIL_T - inset)
    # Iron: strap hinges on the front, barrels on the outer edge, a ring pull by the meeting edge.
    for j, hz in enumerate((z0 + 0.40, z1 - 0.40)):
        length = 0.46
        sx0, sx1 = (xo - side * length, xo) if side > 0 else (xo, xo - side * length)
        box(f"{tag}_Strap{j}", "Iron", min(sx0, sx1), max(sx0, sx1), DY0 - 0.012, DY0, hz - 0.028, hz + 0.028, bevel=0.0)
        cylinder(f"{tag}_StrapEnd{j}", "Iron", 0.045, 0.012, (xo - side * length, DY0 - 0.006, hz), "y", 12)
        cylinder(f"{tag}_Barrel{j}", "Iron", 0.02, 0.16, (xo, DY0 + 0.01, hz), "z", 10)
    hx = xi + side * 0.085
    cylinder(f"{tag}_PullPlate", "Iron", 0.035, 0.01, (hx, DY0 - 0.005, MID + 0.02), "y", 14)
    torus(f"{tag}_Pull", "Iron", 0.042, 0.007, (hx, DY0 - 0.018, MID + 0.02 - 0.045), "y")

# ── Emblems on the upper door panels ──────────────────────────────────────────────
# The loader mirrors X, so Blender's DoorR is the door on the player's left.
PANEL_Z0 = MID + D_RAIL_M / 2 + 0.012
PANEL_Z1 = OPEN_Z1 - GAP - D_RAIL_T - 0.012
PANEL_W = (HW - STILE - 2 * GAP) - 2 * D_STILE - 0.024
EMBLEM_Z = (PANEL_Z0 + PANEL_Z1 - PANEL_W / 2) / 2      # centered below the arch's spring line
FACE = DY0                                                # the door's front plane; emblems stand off it toward -Y


def door_center_x(side):
    return side * (GAP + (HW - STILE - 2 * GAP) / 2)


def axe(tag, mat_handle, cx, cz, tilt, length=0.76, y=FACE - 0.012):
    """An axe lying on the door, handle along `tilt` radians from vertical, head at the top end.
    A bearded head: a long curved edge, a beard hooking back under it, a socket on the haft,
    the bit on the side the handle leans toward so a crossed pair faces outward."""
    rot = (0, tilt, 0)
    box_at(f"{tag}_AxeHandle", mat_handle, (0.03, 0.02, length), (cx, y, cz), rot, bevel=0.004)
    hx, hz = cx + math.sin(tilt) * (length / 2 - 0.06), cz + math.cos(tilt) * (length / 2 - 0.06)
    f = 1 if tilt >= 0 else -1
    # Local (x, z): x toward the bit, z up the haft; the socket wraps the haft at x = 0.
    top, horn, beard, chin = (0.035, 0.04), (0.185, 0.095), (0.15, -0.165), (0.035, -0.075)
    outline = [(-0.035, -0.075), (-0.035, 0.04), top]
    outline += bezier(top, (0.12, 0.06), horn, 4)           # the back of the bit, rising to the top horn
    outline += bezier(horn, (0.235, -0.04), beard, 10)      # the cutting edge, bellied outward
    outline += bezier(beard, (0.115, -0.085), chin, 6)      # the hollow under the beard, back to the haft
    outline = [(f * x, z) for x, z in outline]
    head = profile_solid(f"{tag}_AxeHead", "Iron", outline, -0.011, 0.011, bevel=0.006, segments=2)
    head.rotation_euler = rot
    head.location = (hx, y, hz)
    # A thicker socket collar around the haft, and a wedge peened into its top.
    box_at(f"{tag}_AxeSocket", "Iron", (0.075, 0.03, 0.12), (hx, y, hz - 0.015), rot, bevel=0.006, segments=2)
    wx, wz = hx + math.sin(tilt) * 0.05, hz + math.cos(tilt) * 0.05
    box_at(f"{tag}_AxeWedge", "Iron", (0.02, 0.024, 0.016), (wx, y, wz), rot, bevel=0.003)


def shield(tag, cx, cz):
    cylinder(f"{tag}_Shield", "Body", 0.22, 0.03, (cx, FACE - 0.03, cz), "y", 28)
    torus(f"{tag}_ShieldRim", "Iron", 0.22, 0.012, (cx, FACE - 0.032, cz), "y")
    sphere(f"{tag}_ShieldBoss", "Iron", 0.05, (cx, FACE - 0.045, cz), scale=(1, 0.5, 1))


if VARIANT == "shields":
    for side, tag in ((-1, "DoorL"), (1, "DoorR")):
        cx = door_center_x(side)
        axe(tag + "_A", "Body", cx, EMBLEM_Z, math.radians(35))
        axe(tag + "_B", "Body", cx, EMBLEM_Z, math.radians(-35))
        shield(tag, cx, EMBLEM_Z)

# ── Export ────────────────────────────────────────────────────────────────────────
out = OUT
bpy.ops.wm.obj_export(filepath=out, forward_axis="NEGATIVE_Z", up_axis="Y",
                      apply_modifiers=True, export_materials=True, export_uv=False, export_normals=False,
                      export_triangulated_mesh=False, export_selected_objects=False)
mtl = os.path.splitext(out)[0] + ".mtl"       # the loader wants only the material names, which are in the .obj
if os.path.exists(mtl):
    os.remove(mtl)
polys = sum(len(o.data.polygons) for o in bpy.data.objects if o.type == "MESH")
print(f"armoire ({VARIANT}): {len(bpy.data.objects)} objects, {polys} faces before bevels -> {out}")
