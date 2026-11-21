shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, fog_disabled;

uniform sampler2D backdrop : source_color, repeat_disable, filter_linear_mipmap;

void vertex() {
    mat4 billboard = mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
    MODELVIEW_MATRIX = VIEW_MATRIX * billboard;
}

void fragment() {
    vec4 texel = texture(backdrop, UV);
    ALBEDO = texel.rgb;
    ALPHA = texel.a;
}
