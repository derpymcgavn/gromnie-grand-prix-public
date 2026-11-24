shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, shadows_disabled;

uniform sampler2D native_sprite : source_color, hint_default_white, filter_linear_mipmap, repeat_disable;

void vertex() {
    vec3 axis_x = INV_VIEW_MATRIX[0].xyz * length(MODEL_MATRIX[0].xyz);
    vec3 axis_y = INV_VIEW_MATRIX[1].xyz * length(MODEL_MATRIX[1].xyz);
    MODELVIEW_MATRIX = VIEW_MATRIX * mat4(vec4(axis_x, 0.0), vec4(axis_y, 0.0),
        vec4(INV_VIEW_MATRIX[2].xyz, 0.0), MODEL_MATRIX[3]);
}

void fragment() {
    vec4 mask = texture(native_sprite, UV);
    float edge = 1.0 - smoothstep(0.34, 1.03, length((UV - 0.5) * 2.0));
    float detail = 0.67 + clamp(dot(mask.rgb, vec3(0.24, 0.56, 0.20)), 0.0, 1.0) * 0.33;
    ALBEDO = mix(vec3(0.72, 0.89, 0.94), vec3(0.96, 0.99, 1.0), detail) * COLOR.rgb;
    ALPHA = mask.a * edge * COLOR.a * 0.61;
}
