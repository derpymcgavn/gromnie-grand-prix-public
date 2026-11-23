shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, fog_disabled;

uniform float strength : hint_range(0.0, 1.0) = 0.0;
uniform float origin_radius = 4.8;
varying float life;
varying float seed;
varying float backlight;

float smoke_hash(vec2 cell) {
    vec3 p = fract(vec3(cell.xyx) * .1031);
    p += dot(p, p.yzx + 33.33);
    return fract((p.x + p.y) * p.z);
}

float smoke_noise(vec2 point) {
    vec2 cell = floor(point);
    vec2 f = fract(point);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(smoke_hash(cell), smoke_hash(cell + vec2(1.0, 0.0)), f.x),
        mix(smoke_hash(cell + vec2(0.0, 1.0)), smoke_hash(cell + vec2(1.0, 1.0)), f.x), f.y);
}

void vertex() {
    seed = UV2.y;
    life = fract(TIME * mix(.24, .34, seed) + UV2.x);
    float turn = seed * TAU * 1.7 + life * 1.8;
    vec2 outward = vec2(cos(turn), sin(turn));
    float radius = origin_radius + life * mix(5.7, 10.0, seed);
    float height = mix(-9.8, 6.0, fract(seed * 3.4)) + life * 19.0;
    vec3 local_center = vec3(outward.x * radius, height, outward.y * radius);
    vec3 center = (MODELVIEW_MATRIX * vec4(local_center, 1.0)).xyz;
    vec3 origin = MODELVIEW_MATRIX[3].xyz;
    float scale = length(MODEL_MATRIX[0].xyz);
    backlight = smoothstep(1.1, 6.2, abs(center.x - origin.x) / max(scale, .01));
    vec2 size = vec2(mix(3.2, 8.0, life), mix(6.1, 13.4, life)) * scale;
    vec2 offset = vec2(UV.x - .5, .5 - UV.y) * size;
    offset.x += (offset.y / size.y) * mix(-2.7, 2.7, seed) * scale;
    POSITION = PROJECTION_MATRIX * vec4(center + vec3(offset, 0.0), 1.0);
}

void fragment() {
    float height = 1.0 - UV.y;
    float side = UV.x - .5;
    vec2 drift = vec2(seed * 12.7, -TIME * .63);
    float eddy = smoke_noise(vec2(UV.x * 3.4, height * 4.4) + drift);
    float fine = smoke_noise(vec2(UV.x * 8.3, height * 8.0) + drift * 1.43);
    side += (eddy - .5) * .21 + sin(height * 8.5 + seed * 14.0 - TIME * 1.5) * .10;
    float width = mix(.22, .074, height) * (.75 + .36 * eddy);
    float body = 1.0 - smoothstep(width * .28, width * 1.40, abs(side));
    float tips = smoothstep(.02, .19, height) * (1.0 - smoothstep(.58, .98, height));
    float fade = smoothstep(.0, .16, life) * (1.0 - smoothstep(.54, 1.0, life));
    float heat = (1.0 - life) * (1.0 - height * .53) * (.48 + .52 * fine);
    ALBEDO = mix(vec3(.18, .006, .020), vec3(1.0, .085, .048), heat);
    ALPHA = strength * fade * body * tips * mix(.28, .91, eddy * fine)
        * mix(.24, .72, backlight);
}
