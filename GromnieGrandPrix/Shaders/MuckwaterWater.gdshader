shader_type spatial;
render_mode cull_disabled;
#include "res://Shaders/SurfaceRelief.gdshaderinc"

uniform sampler2D water_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D blend_map : filter_linear_mipmap, repeat_disable;
uniform vec2 map_origin;
uniform vec2 map_extent;

varying vec2 world_xz;

void vertex() {
    world_xz = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xz;
}

void fragment() {
    vec2 p = world_xz * vec2(1.0, -1.0);
    vec3 water = texture(water_tex, p / 10.0 + TIME * vec2(0.008, -0.012)).rgb;
    vec3 waves = texture(water_tex, p / 7.1 + TIME * vec2(0.016, 0.007)).rgb;
    float depth = texture(blend_map, (world_xz - map_origin) / map_extent).a;
    vec3 tint = mix(vec3(0.25, 0.30, 0.17), vec3(0.10, 0.28, 0.26), smoothstep(0.16, 0.74, depth));
    ALBEDO = tint * 0.72 + (water * 0.66 + waves * 0.34) * vec3(0.25, 0.36, 0.31);
    NORMAL = surface_relief(VERTEX, NORMAL, water.r * .21 + waves.r * .13, .15);
    ROUGHNESS = 0.74;
    SPECULAR = 0.15;
}
