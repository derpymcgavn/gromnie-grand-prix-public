shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, shadows_disabled;

#include "res://Shaders/RegattaSurface.gdshaderinc"

uniform sampler2D native_running_water : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
varying vec2 water_xz;

void vertex() {
    water_xz = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xz;
    VERTEX.y += regatta_wave_height(water_xz) * regatta_geometry_strength(water_xz, CAMERA_POSITION_WORLD.xz);
}

void fragment() {
    vec2 flow = vec2(0.24, -0.13) * TIME;
    vec2 water_uv = water_xz * vec2(0.56, 0.43);
    vec3 flow_a = texture(native_running_water, water_uv + flow).rgb;
    vec3 flow_b = texture(native_running_water, (water_uv.yx - flow * 1.48) * 0.57).rgb;
    float pattern = dot(flow_a * 0.63 + flow_b * 0.37, vec3(0.19, 0.56, 0.25));

    float age = clamp(UV.y, 0.0, 1.0);
    float x = UV.x * 2.0 - 1.0;
    float cut = COLOR.g * 2.0 - 1.0;
    float strip_width = mix(0.18, 0.105, age);
    float rail_x = abs(x + (pattern - 0.4) * 0.25) - (0.55 + age * 0.16);
    float rail_falloff = rail_x / strip_width;
    float shoulders = exp(-rail_falloff * rail_falloff);
    float pump_width = mix(0.46, 0.26, age) + COLOR.b * 0.16;
    float pump_falloff = x / pump_width;
    float pump = exp(-pump_falloff * pump_falloff) * (1.0 - smoothstep(0.15, 0.68, age));
    float broken_foam = smoothstep(0.12, 0.42, pattern + 0.05 * sin(dot(water_xz, vec2(4.1, 3.7))));
    float edge = 1.0 - smoothstep(0.76, 1.0, abs(x));
    float persistence = (1.0 - smoothstep(0.38, 1.0, age)) * edge;
    float spray_side = 1.0 - 0.32 * cut * sign(x);
    float foam = shoulders * spray_side * 0.78 + pump * (0.40 + 0.29 * COLOR.b);

    ALBEDO = mix(vec3(0.46, 0.68, 0.69), vec3(0.90, 0.97, 0.98), broken_foam);
    ALPHA = clamp(foam * (0.18 + 0.82 * broken_foam) * persistence * (0.45 + 0.50 * COLOR.r)
        * smoothstep(0.02, 0.68, COLOR.a), 0.0, 0.90);
}
