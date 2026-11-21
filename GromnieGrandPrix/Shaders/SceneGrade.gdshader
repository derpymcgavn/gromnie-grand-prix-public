shader_type canvas_item;
render_mode unshaded, blend_disabled;
#include "res://Shaders/SceneGrade.gdshaderinc"

uniform sampler2D scene_color : hint_screen_texture, filter_nearest, repeat_disable;

void fragment() {
    COLOR = vec4(grade_scene(texture(scene_color, SCREEN_UV).rgb, SCREEN_UV), 1.0);
}
