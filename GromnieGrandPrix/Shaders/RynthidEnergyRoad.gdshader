shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, fog_disabled;

uniform sampler2D cloak_energy : source_color, repeat_enable, filter_linear_mipmap_anisotropic;
uniform float track_tiles = 160.0;

vec3 spectral_light(float phase) {
    return .57 + .43 * cos(TAU * (phase + vec3(0.0, -.32, .31)));
}

float band(float position, float middle, float width) {
    float distance_from_middle = abs(position - middle);
    return 1.0 - smoothstep(width * .44, width, distance_from_middle);
}

void fragment() {
    float chapter = UV.y / track_tiles;
    float across = clamp(UV.x, 0.0, 1.0);
    float shoulder = min(across, 1.0 - across);
    float distance_fade = 1.0 - smoothstep(.08, .45, fwidth(UV.y));
    float sweep = sin(TAU * (UV.y * .5 - TIME * .17)) * .5 + .5;
    float eddy = sin(TAU * (UV.y * .5 + across * .70 - TIME * .10));
    vec2 field = vec2(across * 1.73 + eddy * .045, UV.y);
    vec2 drift = vec2(.022, -.20) * TIME;
    float native_energy = texture(cloak_energy, field + drift).r;
    float crossflow = texture(cloak_energy, field * vec2(1.8, 2.0) - drift * .56 + vec2(.2, .6)).r;
    float threads = pow(clamp(native_energy * 1.92, 0.0, 1.0), .90);
    float filaments = smoothstep(.10, .45, max(native_energy, crossflow * .76));

    // Slow color changes give the course landmarks. The cloak remains the moving surface.
    float phase = chapter * 2.0 + .095 * cos(TAU * chapter) + .10;
    vec3 spectrum = spectral_light(phase + (across - .5) * .25);
    vec3 rail = spectral_light(phase + .12 + across * .14);
    vec3 body = vec3(.068, .021, .093) + spectrum * (.12 + .45 * threads + .20 * filaments);
    body *= .83 + .17 * sweep;
    body *= .79 + .21 * exp(-shoulder * 4.4);
    body += vec3(.15, .067, .21) * threads * filaments * distance_fade;

    float shoulder_flow = band(shoulder, .076 + eddy * .012, .065);
    float glass_line = band(shoulder, .036, .014);
    float edge = 1.0 - smoothstep(.004, .021, shoulder);
    float boundary = mix(.63, 1.0, distance_fade);
    body += rail * (.18 * shoulder_flow + .18 * glass_line) * (.72 + .28 * sweep);
    body = mix(body, vec3(.68, .73, .90) + rail * .21, edge * boundary);

    // Look through the racing surface across its width. At a shallow angle the
    // stronger tint and bright shoulders still show where the road is going.
    float facing = clamp(abs(dot(normalize(NORMAL), VIEW)), 0.0, 1.0);
    float grazing = 1.0 - smoothstep(.12, .72, facing);
    float center_alpha = .19 + .12 * grazing + .13 * threads + .08 * filaments;
    float rim = 1.0 - smoothstep(.025, .165, shoulder);
    ALBEDO = clamp(body * (1.0 + .15 * (1.0 - rim)), 0.0, 1.0);
    ALPHA = mix(center_alpha, .96, rim);
}
