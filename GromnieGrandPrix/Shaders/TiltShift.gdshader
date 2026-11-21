shader_type canvas_item;
render_mode unshaded, blend_disabled;
#include "res://Shaders/SceneGrade.gdshaderinc"

uniform sampler2D scene : hint_screen_texture, filter_linear, repeat_disable;
uniform float focus_y = .55;

void fragment() {
    vec2 uv = SCREEN_UV;
    float blur = smoothstep(.09, .40, abs(uv.y - focus_y));
    vec2 d = SCREEN_PIXEL_SIZE * (blur * 6.0);
    vec3 base = texture(scene, uv).rgb;
    vec3 soft = base * .24;
    soft += (texture(scene, uv + vec2(d.x, 0)).rgb + texture(scene, uv - vec2(d.x, 0)).rgb) * .14;
    soft += (texture(scene, uv + vec2(0, d.y)).rgb + texture(scene, uv - vec2(0, d.y)).rgb) * .14;
    soft += (texture(scene, uv + d).rgb + texture(scene, uv - d).rgb
          + texture(scene, uv + vec2(d.x, -d.y)).rgb + texture(scene, uv + vec2(-d.x, d.y)).rgb) * .05;
    vec3 miniature = mix(base, soft, blur);
    float gray = dot(miniature, vec3(.2126, .7152, .0722));
    COLOR = vec4(grade_scene(mix(vec3(gray), miniature, 1.12) * (1.0 - blur * .08), uv), 1.0);
}
