shader_type canvas_item;
render_mode unshaded, blend_disabled;
#include "res://Shaders/SceneGrade.gdshaderinc"

uniform sampler2D scene_color : hint_screen_texture, filter_linear, repeat_disable;
uniform float strength : hint_range(0.0, 1.0) = 0.0;
uniform float wind : hint_range(0.0, 1.0) = 0.0;
uniform float travel = 0.0;
uniform vec2 vanishing_point = vec2(.5, .48);

float seed(float value) {
    return fract(sin(value * 127.1 + 33.2) * 43758.5453);
}

float wind_streak(vec2 direction, float radius) {
    vec2 ray = radius > .0001 ? direction : vec2(1.0, 0.0);
    float angle = atan(ray.y, ray.x) * (72.0 / TAU);
    float slot = floor(angle);
    float variation = seed(slot);
    float phase = fract(travel * (.37 + variation * .29) + seed(slot + 17.1));
    float head = mix(.31, .85, phase * (.58 + phase * .42));
    float tail = head - mix(.035, .135, seed(slot + 5.3)) * (.62 + wind * .38);
    float angular_gap = abs(fract(angle) - mix(.21, .79, seed(slot + 62.2)));
    float antialias = clamp(fwidth(angle), .004, .055);
    float line = 1.0 - smoothstep(.015, .032 + antialias, angular_gap);
    float ends = smoothstep(tail, mix(tail, head, .62), radius) * (1.0 - smoothstep(head - .014, head, radius));
    float lifetime = smoothstep(.06, .24, phase) * (1.0 - smoothstep(.71, 1.0, phase));
    return line * ends * lifetime * smoothstep(.63, .95, variation);
}

void fragment() {
    vec2 ray = SCREEN_UV - vanishing_point;
    vec2 direction = ray * vec2(SCREEN_PIXEL_SIZE.y / SCREEN_PIXEL_SIZE.x / 1.6, 1.0);
    float radius = length(direction);
    float periphery = smoothstep(.28, .69, radius);
    vec2 step_uv = ray * (.058 * strength * periphery);
    vec3 original = texture(scene_color, SCREEN_UV).rgb;
    vec3 result = original;
    if (periphery > .003) {
        vec3 trail = original * .33
            + texture(scene_color, SCREEN_UV - step_uv * .34).rgb * .30
            + texture(scene_color, SCREEN_UV - step_uv * .72).rgb * .24
            + texture(scene_color, SCREEN_UV - step_uv * 1.26).rgb * .13;
        result = mix(original, trail, strength * periphery * .87);
    }
    if (wind > .025) {
        float edge = smoothstep(.37, .59, radius) * (1.0 - smoothstep(.76, .92, radius));
        float streak = wind_streak(direction, radius) * edge * wind;
        result += streak * vec3(.27, .34, .40) * (1.0 - max(result.r, max(result.g, result.b)) * .55);
    }
    COLOR = vec4(grade_scene(result, SCREEN_UV), 1.0);
}
