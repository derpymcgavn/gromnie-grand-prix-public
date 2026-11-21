shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, fog_disabled, blend_add;

uniform vec3 tentacle_tip;
uniform vec3 tip_bend;
uniform vec3 orb_bend;
uniform vec3 orb_socket;
uniform float phase = 0.0;
uniform float charge : hint_range(0.0, 1.0) = .2;
uniform float pulse : hint_range(0.0, 1.0) = 0.0;
uniform float age = 0.0;

varying float rim;

void vertex() {
    float t = UV.x;
    float other = 1.0 - t;
    vec3 axis = normalize(3.0 * other * other * (tip_bend - tentacle_tip)
        + 6.0 * other * t * (orb_bend - tip_bend)
        + 3.0 * t * t * (orb_socket - orb_bend));
    vec3 cross_axis = normalize(cross(axis, vec3(0.0, 1.0, .001)));
    vec3 up = normalize(cross(cross_axis, axis));
    float roll = UV.y * TAU + t * 1.8;
    vec3 normal = cos(roll) * cross_axis + sin(roll) * up;
    vec3 middle = tentacle_tip * other * other * other
        + tip_bend * 3.0 * t * other * other + orb_bend * 3.0 * t * t * other + orb_socket * t * t * t;
    float coil = age * 2.1 - t * 8.1 + phase * 5.5;
    middle += (cross_axis * cos(coil) + up * sin(coil)) * sin(t * PI) * (1.4 + .85 * charge);
    float size = mix(1.28, .49, pow(t, .69)) * (1.0 + .15 * sin(t * 52.0 - age * 5.3 + phase));
    VERTEX = middle + normal * size;
    vec3 gaze = normalize((inverse(MODEL_MATRIX) * vec4(CAMERA_POSITION_WORLD, 1.0)).xyz - VERTEX);
    rim = pow(clamp(1.0 - abs(dot(gaze, normal)), 0.0, 1.0), 2.1);
}

void fragment() {
    float thread = pow(.5 + .5 * cos(TAU * UV.y - UV.x * 14.0 + age * 2.0 + phase), 8.0);
    float sparks = pow(.5 + .5 * cos(UV.x * 32.0 - age * 9.5 + phase * 7.0), 4.0);
    float fade = smoothstep(0.0, .065, UV.x) * (1.0 - smoothstep(.85, 1.0, UV.x));
    vec3 basin = vec3(.61, .012, .087);
    vec3 stream = vec3(1.0, .16, .17);
    ALBEDO = mix(basin, stream, .34 * rim + .51 * thread + .15 * sparks);
    ALPHA = fade * (.21 + .36 * rim + .28 * thread + .22 * sparks) * (.38 + .53 * charge + pulse * .09);
}
