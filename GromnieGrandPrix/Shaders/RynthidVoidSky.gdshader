shader_type sky;

float void_hash(vec2 point) {
    vec3 p = fract(vec3(point.xyx) * .1031);
    p += dot(p, p.yzx + 33.33);
    return fract((p.x + p.y) * p.z);
}

float void_noise(vec2 uv) {
    vec2 cell = floor(uv);
    vec2 f = fract(uv);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(void_hash(cell), void_hash(cell + vec2(1, 0)), f.x),
        mix(void_hash(cell + vec2(0, 1)), void_hash(cell + vec2(1)), f.x), f.y);
}

void sky() {
    vec3 ray = normalize(EYEDIR);
    float skyline = exp(-abs(ray.y) * 5.7);
    vec3 darkness = vec3(.051, .061, .098) + skyline * vec3(.032, .025, .052);
    if (!AT_CUBEMAP_PASS) {
        float skyward = smoothstep(-.06, .24, ray.y);
        float latitude = abs(dot(ray, normalize(vec3(-.34, .83, .45))));
        float band = 1.0 - smoothstep(.035, .31, latitude);
        vec2 fold = ray.xz * 7.1 + ray.yy * vec2(2.3, -1.8);
        float dust = void_noise(fold) * .70 + void_noise(fold * 2.37 + vec2(3.4, 7.7)) * .30;
        float haze = band * smoothstep(.35, .86, dust) * skyward;
        darkness += haze * vec3(.060, .033, .090);

        vec2 angles = vec2(atan(ray.z, ray.x) / TAU + .5, asin(clamp(ray.y, -1.0, 1.0)) / PI + .5);
        vec2 field = angles * vec2(510.0, 255.0);
        vec2 cell = floor(field);
        float seed = void_hash(cell + vec2(15, 21));
        vec2 star = vec2(void_hash(cell + vec2(2, 7)), void_hash(cell + vec2(3, 19))) * .64 + .18;
        float radius = mix(.028, .091, void_hash(cell + vec2(5, 14)));
        float core = 1.0 - smoothstep(radius * .22, radius, length(fract(field) - star));
        float presence = smoothstep(.980, .999, seed);
        vec3 starlight = mix(vec3(.23, .31, .55), vec3(.48, .43, .61), void_hash(cell + 29.0));
        darkness += starlight * core * presence * skyward;
    }
    COLOR = darkness;
}
