shader_type spatial;
render_mode cull_disabled, specular_disabled, diffuse_lambert;
#include "res://Shaders/SurfaceRelief.gdshaderinc"
#include "res://Shaders/TrackWater.gdshaderinc"

uniform sampler2D grass_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D marsh_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D mud_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D packed_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D road_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D detail_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D blend_map : filter_linear_mipmap, repeat_disable;
uniform vec2 map_origin;
uniform vec2 map_extent;
uniform bool stable_ground = false;

varying vec2 world_xz;

void vertex() {
    world_xz = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xz;
}

void fragment() {
    vec2 p = world_xz * vec2(1.0, -1.0);
    vec4 blend = texture(blend_map, (world_xz - map_origin) / map_extent);
    if (stable_ground) {
        blend = vec4(0.0, 0.0, 0.12, 0.0);
    }
    vec3 grass = texture(grass_tex, p / 9.0).rgb * vec3(0.83, 0.95, 0.81);
    vec3 marsh = texture(marsh_tex, p / 10.5).rgb * vec3(0.82, 0.93, 0.77);
    vec3 mud = texture(mud_tex, p / 8.0).rgb * vec3(0.68, 0.64, 0.52);
    vec3 cobbles = texture(road_tex, p / 8.5).rgb * vec3(0.93, 0.86, 0.75);
    vec3 packed = texture(packed_tex, p / 8.0).rgb * vec3(1.13, 0.99, 0.76);
    float detail = dot(texture(detail_tex, p / 3.1).rgb, vec3(0.299, 0.587, 0.114));
    float apron = 1.0 - smoothstep(10.0, 23.0, distance(world_xz, vec2(0.0, 9.0)));
    vec3 road = mix(packed, cobbles, max(apron, 0.12 + detail * 0.22));
    vec3 ground = mix(grass, marsh, blend.b);
    ground = mix(ground, mud, blend.g * (0.76 + 0.18 * detail));
    vec3 surface = mix(ground, road, blend.r);
    float grain = texture(detail_tex, p / .94).r;
    float height = dot(surface, vec3(.2126, .7152, .0722)) * .21 + (grain + detail * .55) * .055;
    float close_detail = 1.0 - smoothstep(34.0, 138.0, length(VERTEX));
    vec3 ground_up = normalize(mat3(INV_VIEW_MATRIX) * NORMAL);
    NORMAL = normalize(mix(NORMAL, surface_relief(VERTEX, NORMAL, height, .43), close_detail));
    ALBEDO = surface * (0.87 + detail * 0.25 + (grain - .5) * .13 * close_detail);
    if (!stable_ground) {
        vec3 reflection;
        vec3 base = ALBEDO;
        track_water(world_xz, blend.r, .5 + blend.a * .5, ground_up, normalize(mat3(INV_VIEW_MATRIX)*VIEW), TIME, base, reflection);
        ALBEDO = base;
        EMISSION = reflection;
    }
    ROUGHNESS = .99;
    SPECULAR = 0.0;
}

