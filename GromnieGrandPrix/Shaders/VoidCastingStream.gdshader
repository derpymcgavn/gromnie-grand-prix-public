shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, fog_disabled;

uniform vec3 palm;
uniform vec3 aperture;
uniform float surge : hint_range(0.0, 1.0) = .4;

varying float braid;

void vertex() {
    float t = UV.x;
    braid = COLOR.r;
    float angle = t * 9.0 + TIME * 1.45 + braid * TAU;
    float envelope = sin(t * PI) * (2.1 + braid * 2.4);
    vec3 here = mix(palm, aperture, t) + vec3(sin(angle) * envelope, sin(t * PI) * 2.2, cos(angle) * envelope);
    vec3 gaze = CAMERA_POSITION_WORLD - (MODEL_MATRIX * vec4(here, 1.0)).xyz;
    vec3 across = normalize(cross(normalize(aperture - palm), normalize(gaze)));
    VERTEX = here + across * (UV.y - .5) * mix(1.8, .82, t);
}

void fragment() {
    float edge = 1.0 - smoothstep(.09, .5, abs(UV.y - .5));
    float ends = smoothstep(0.0, .055, UV.x) * (1.0 - smoothstep(.79, 1.0, UV.x));
    float flow = .20 + .80 * pow(.5 + .5 * sin(UV.x * 22.0 - TIME * 8.2 + braid * 19.0), 2.0);
    vec3 cold = vec3(.23, .35, 1.0);
    vec3 hot = vec3(.84, .43, 1.0);
    ALBEDO = mix(cold, hot, .20 + .63 * flow) + vec3(.10, .10, .18) * edge * flow;
    ALPHA = edge * ends * (.46 + .54 * flow) * (.38 + .38 * surge);
}
