shader_type sky;
render_mode disable_fog;

uniform bool compatibility_srgb = false;

float void_hash(vec2 point) {
    vec3 p = fract(vec3(point.xyx) * .1031);
    p += dot(p, p.yzx + 33.33);
    return fract((p.x + p.y) * p.z);
}

vec3 void_display_srgb(vec3 linear_color) {
    vec3 low = linear_color * 12.92;
    vec3 high = 1.055 * pow(linear_color, vec3(1.0 / 2.4)) - .055;
    return mix(low, high, step(vec3(.0031308), linear_color));
}

void sky() {
    vec3 ray = normalize(EYEDIR);
    float horizon = exp(-abs(ray.y) * 7.0);
    vec3 darkness = vec3(.0019, .00265, .0056) + horizon * vec3(.0027, .0032, .0062);
    if (!AT_CUBEMAP_PASS) {
        vec2 angles = vec2(atan(ray.z, ray.x) / TAU + .5, asin(clamp(ray.y, -1.0, 1.0)) / PI + .5);
        vec2 field = angles * vec2(360.0, 180.0);
        vec2 cell = floor(field);
        float seed = void_hash(cell + vec2(15, 21));
        vec2 star = vec2(void_hash(cell + vec2(2, 7)), void_hash(cell + vec2(3, 19))) * .64 + .18;
        float radius = mix(.024, .066, void_hash(cell + vec2(5, 14)));
        float pixel = clamp(length(fwidth(field)) * .23, .005, .044);
        float core = 1.0 - smoothstep(max(.0, radius - pixel), radius + pixel, length(fract(field) - star));
        float presence = smoothstep(.988, .9985, seed);
        vec3 starlight = mix(vec3(.22, .32, .54), vec3(.48, .39, .60), void_hash(cell + 29.0));
        darkness += starlight * core * presence * (.48 + .14 * smoothstep(-.55, .30, ray.y));
    }
    // Compatibility decodes sky shader output to linear before tonemapping.
    COLOR = compatibility_srgb ? void_display_srgb(darkness) : darkness;
}
