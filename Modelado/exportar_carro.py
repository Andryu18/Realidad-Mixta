# prepara el chasis para unity y exporta fbx
import bpy, bmesh, sys, os
from mathutils import Vector

out = sys.argv[sys.argv.index("--") + 1]

def center(o):
    pts = [o.matrix_world @ Vector(c) for c in o.bound_box]
    return sum(pts, Vector()) / 8

# sin sensor ni drivers que no se usan
for o in list(bpy.data.objects):
    if o.type == 'CAMERA' or any(k in o.name for k in ("HCSR04", "TB6612", "L293D")):
        bpy.data.objects.remove(o, do_unlink=True)


def mat(name, rgb, rough=0.6, metal=0.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (*rgb, 1)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    return m

def box(name, size, loc, m):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    o = bpy.context.active_object
    o.name = name
    o.scale = size
    bpy.ops.object.transform_apply(scale=True)
    o.data.materials.append(m)
    return o

# l298n en la bahia trasera
bat = bpy.data.objects["REF_portapilas_4AA"]
c = center(bat)
red = mat("mat_l298n", (0.75, 0.05, 0.05), 0.5)
sink = mat("mat_disipador", (0.05, 0.05, 0.06), 0.35, 0.8)
y = c.y - 53
box("L298N_pcb", (43, 43, 1.6), (c.x, y, c.z + 12), red)
box("L298N_disipador", (23, 16, 25), (c.x, y + 6, c.z + 25), sink)

# ruedas con pivote propio
for o in bpy.data.objects:
    if o.name.startswith("REF_rueda") or o.name.startswith("REF_eje"):
        bpy.context.view_layer.objects.active = o
        for s in bpy.context.selected_objects: s.select_set(False)
        o.select_set(True)
        bpy.ops.object.origin_set(type='ORIGIN_GEOMETRY', center='BOUNDS')

# biselar ruedas para que no se vean planas
for o in bpy.data.objects:
    if o.name.startswith("REF_rueda"):
        mod = o.modifiers.new("bisel", 'BEVEL')
        mod.width = 2.5
        mod.segments = 3
        sub = o.modifiers.new("suave", 'WEIGHTED_NORMAL')

for o in bpy.data.objects:
    print("OUT", o.name, tuple(round(v,1) for v in center(o)))

bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=out, use_selection=False, apply_unit_scale=True,
    apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
    use_mesh_modifiers=True, mesh_smooth_type='FACE', bake_space_transform=True)
print("EXPORTED", out)
