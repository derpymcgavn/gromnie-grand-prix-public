shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, fog_disabled;

uniform vec3 source;
uniform vec3 target;
uniform vec3 bow;
uniform vec4 tint : source_color = vec4(1.0, .85, .56, 1.0);
uniform float phase = 0.0;
uniform float age = 0.0;
uniform float strength : hint_range(0.0, 1.5) = .2;

void vertex() {
    float t = UV.x;
    float sine = sin(t * PI);
    vec3 center = mix(source, target, t) + bow * sine;
    vec3 beam = target - source + bow * (PI * cos(t * PI));
    float weave = t * 14.0 - age * 4.3 + phase;
    center += vec3(sin(weave), cos(weave), sin(weave * .91)) * sine * .59;
    vec3 local_camera = (inverse(MODEL_MATRIX) * vec4(CAMERA_POSITION_WORLD, 1.0)).xyz;
    vec3 across = cross(beam, local_camera - center);
    across /= max(length(across), .0001);
    float width = mix(1.6, .62, t) * (.83 + .37 * strength);
    VERTEX = center + across * (UV.y - .5) * width;
}

void fragment() {
    float across = abs(UV.y - .5);
    float ribbon = 1.0 - smoothstep(.06, .5, across);
    float filament = 1.0 - smoothstep(.0, .13, across);
    float ends = smoothstep(0.0, .08, UV.x) * (1.0 - smoothstep(.90, 1.0, UV.x));
    float travel = .32 + .68 * pow(.5 + .5 * sin(UV.x * 27.0 - age * 10.0 + phase), 4.0);
    ALBEDO = mix(tint.rgb, vec3(1.0, .98, .90), filament * travel * .78);
    ALPHA = clamp(ends * strength * (ribbon * .57 + filament * travel * .85), 0.0, 1.0);
}
