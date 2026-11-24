shader_type sky;
render_mode disable_fog;

uniform float descent = 0.;
uniform bool compatibility_srgb = false;

float dust_hash(vec2 p) {
    vec3 h = fract(p.xyx * .1031);
    h += dot(h, h.yzx + 33.33);
    return fract((h.x + h.y) * h.z);
}

void sky() {
    vec3 ray = normalize(EYEDIR);
    float horizon = exp(-abs(ray.y) * 5.5);
    vec3 color = vec3(.0025, .0037, .0061) + horizon * vec3(.0019, .0036, .0050);
    if (!AT_CUBEMAP_PASS) {
        vec2 polar = vec2(atan(ray.z, ray.x), asin(clamp(ray.y, -1., 1.)));
        float helix = sin(3. * polar.x + polar.y * (7. + 6. * descent) + 1.3 * sin(polar.y * 6.));
        float ribbon = exp(-abs(helix + .32 * sin(polar.x * 7. - polar.y * 3.)) * 7.5);
        float haze = .45 + .55 * sin(polar.x * 2. - polar.y * 5.);
        vec3 velvet = mix(vec3(.008, .027, .033), vec3(.039, .007, .032), smoothstep(.16, .88, descent));
        color += velvet * (.17 * horizon + descent * .80 * ribbon * haze) * (.35 + horizon * .65);
        vec2 field = vec2(polar.x / TAU, polar.y / PI) * vec2(410., 190.);
        vec2 cell = floor(field);
        vec2 grain = vec2(dust_hash(cell + 4.), dust_hash(cell + 13.)) * .65 + .175;
        float star = 1. - smoothstep(.025, .071, length(fract(field) - grain));
        color += vec3(.24, .32, .43) * star * smoothstep(.989, .999, dust_hash(cell)) * .43;
    }
    vec3 srgb = mix(color * 12.92, 1.055 * pow(color, vec3(1. / 2.4)) - .055, step(vec3(.0031308), color));
    COLOR = compatibility_srgb ? srgb : color;
}
