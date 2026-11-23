shader_type spatial;
render_mode cull_disabled, specular_schlick_ggx, diffuse_lambert;
#include "res://Shaders/SurfaceRelief.gdshaderinc"
#include "res://Shaders/TrackWater.gdshaderinc"

uniform sampler2D packed_dirt : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D road_stone : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D road_grit : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D course_mask : hint_default_black, filter_linear_mipmap, repeat_disable;
uniform vec2 course_origin;
uniform vec2 course_extent = vec2(1.0);
uniform sampler2D cloud_shadow_map : hint_default_black, filter_linear_mipmap, repeat_enable;
uniform vec2 cloud_shadow_offset;
uniform float cloud_shadow_scale = 256.0;
uniform float cloud_shadow_strength;
uniform bool road_detail = true;

varying vec3 world_point;
varying vec3 world_normal;
varying float stone_weight;
varying float verge_cover;

void vertex() {
    world_point = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    world_normal = normalize(MODEL_NORMAL_MATRIX * NORMAL);
    stone_weight = COLOR.r;
    verge_cover = COLOR.g;
}

vec3 rock_texture(sampler2D tex, vec3 p, vec3 w) {
    return texture(tex, p.zy).rgb * w.x + texture(tex, p.xz).rgb * w.y + texture(tex, p.xy).rgb * w.z;
}

float grit_hash(vec2 cell) {
    vec3 p = fract(vec3(cell.xyx) * .1031);
    p += dot(p, p.yzx + 33.33);
    return fract((p.x + p.y) * p.z);
}

float gravel_patch(vec2 at) {
    vec2 tile = floor(at);
    vec2 f = fract(at);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(grit_hash(tile), grit_hash(tile + vec2(1, 0)), f.x),
               mix(grit_hash(tile + vec2(0, 1)), grit_hash(tile + vec2(1)), f.x), f.y);
}

void fragment() {
    vec3 dirt;
    vec3 stone;
    float grain = .5;
    float patch = .5;
    if (road_detail) {
        vec3 axis = pow(abs(normalize(world_normal)), vec3(4.0));
        axis /= max(dot(axis, vec3(1.0)), .001);
        dirt = rock_texture(packed_dirt, world_point / 8.5, axis);
        stone = rock_texture(road_stone, world_point / 4.7, axis);
        grain = dot(rock_texture(road_grit, world_point / 1.45, axis), vec3(.21, .72, .07));
        patch = gravel_patch(world_point.xz * .23 + vec2(.21, .63));
    } else {
        dirt = texture(packed_dirt, world_point.xz / 8.5).rgb;
        stone = texture(road_stone, world_point.xz / 4.7).rgb;
    }
    float gravel = clamp(stone_weight + smoothstep(.30, .78, patch) * .43 + (grain - .48) * .16, .08, .96);
    float dust = dot(dirt, vec3(.21, .72, .07));
    vec3 earth = mix(dirt, dust * vec3(1.13, .91, .65), .58);
    vec3 surface = mix(earth * .92, stone * vec3(.81, .79, .74), gravel);
    float near_detail = 0.0;
    if (road_detail) {
        near_detail = 1.0 - smoothstep(34.0, 136.0, length(VERTEX));
        float height = dot(surface, vec3(.21, .72, .07)) * mix(.13, .25, gravel) + grain * .035;
        NORMAL = normalize(mix(NORMAL, surface_relief(VERTEX, NORMAL, height, .65), near_detail));
    }
    vec3 base = surface * (0.94 + patch * .11 + (grain - .5) * .16 * near_detail);
    if (cloud_shadow_strength > 0.0) {
        float cloud = texture(cloud_shadow_map, (world_point.xz + cloud_shadow_offset) / cloud_shadow_scale).r;
        base *= 1.0 - cloud_shadow_strength * cloud;
    }
    vec3 reflected;
    float film;
    float pool;
    vec3 pool_normal;
    vec4 course = texture(course_mask, (world_point.xz - course_origin) / course_extent);
    track_water_surface(world_point.xz, verge_cover, course.a, normalize(world_normal),
        normalize(mat3(INV_VIEW_MATRIX) * VIEW), TIME, base, reflected, film, pool, pool_normal);
    ALBEDO = base;
    EMISSION = reflected;
    ALPHA = smoothstep(.08, .95, verge_cover);
    ALPHA_SCISSOR_THRESHOLD = road_detail ? mix(.14, .86, gravel_patch(world_point.xz * 5.5)) : .45;
    ROUGHNESS = mix(mix(.98, .46, film), .065, pool);
    SPECULAR = .36 * pool;
    if (pool > .005) NORMAL = normalize(mix(NORMAL, mat3(VIEW_MATRIX) * pool_normal, pool));
}
