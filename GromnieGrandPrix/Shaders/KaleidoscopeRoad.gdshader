shader_type spatial;
render_mode cull_disabled, specular_schlick_ggx;

uniform sampler2D cloak_energy : source_color, repeat_enable, filter_linear_mipmap_anisotropic;
varying float course_fraction;

void vertex() {
    course_fraction = COLOR.r;
}

vec3 spectral_light(float phase) {
    vec3 wave = .5 + .5 * cos(TAU * (phase + vec3(0.0, -1.0 / 3.0, 1.0 / 3.0)));
    return .09 + .91 * wave * wave;
}

void fragment() {
    float across = clamp(UV.x, 0.0, 1.0);
    float shoulder = min(across, 1.0 - across);
    float eddy = sin(TAU * (UV.y * .50 + across * .80 + TIME * .18));
    // The native cloak filaments flow in two directions. Both scales wrap at the lap seam.
    vec2 flow_uv = vec2(across * 2.10 + .055 * eddy, UV.y * .50) + TIME * vec2(.045, -.34);
    vec2 weave_uv = vec2(across * 3.65 - .036 * eddy, UV.y * .32) + TIME * vec2(-.025, .245);
    float flow = texture(cloak_energy, flow_uv).r;
    float weave = texture(cloak_energy, weave_uv).r;
    float filaments = smoothstep(.055, .46, max(flow, weave * .72));
    float sparks = smoothstep(.18, .60, flow + weave * .28);

    float oscillation = .12 * sin(TAU * (UV.y * .02 - TIME * .05));
    oscillation += .035 * sin(TAU * (across * 1.5 + TIME * .075));
    float phase = UV.y * .04 + across * .42 - TIME * .11 + oscillation;
    vec3 rainbow = spectral_light(phase);
    float swell = .93 + .07 * cos(TAU * (course_fraction * 3.0 + TIME * .025));
    vec3 plasma = rainbow * ((.22 + .36 * filaments + .12 * sparks) * swell);
    plasma += vec3(.05, .06, .08) * filaments * sparks;

    float edge = 1.0 - smoothstep(.005, .016, shoulder);
    float edge_halo = 1.0 - smoothstep(.014, .058, shoulder);
    float center = 1.0 - smoothstep(.004, .012, abs(across - .5));
    center *= smoothstep(.29, .44, abs(fract(UV.y * .5) - .5));
    vec3 white = vec3(.64, .72, .81);

    // The silver field carries sharp reflections under the glowing cloak filaments.
    // The pale shoulder softens the reflection to keep the edge readable.
    vec3 silver = vec3(.82, .85, .88) + rainbow * .06;
    ALBEDO = mix(silver, white * .58, edge);
    ROUGHNESS = mix(.008, .22, edge);
    METALLIC = mix(.97, .32, edge);
    EMISSION = mix(plasma + white * (.095 * edge_halo + .14 * center), white, edge);
}
