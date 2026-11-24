shader_type spatial;
render_mode cull_disabled, specular_schlick_ggx;

uniform sampler2D lens_view : hint_default_black, filter_linear, repeat_disable;
uniform bool live_view = false;
uniform bool portal = false;
uniform float warp = .01;

void fragment() {
    vec2 uv = SCREEN_UV;
    if (!portal) uv.x = 1. - uv.x;
    vec2 wave = vec2(sin(UV.y * 13. + UV.x * 4. + TIME * 1.12),
        sin(UV.x * 12. - UV.y * 6. - TIME * .81));
    vec2 warped = clamp(uv + warp * wave, vec2(.003), vec2(.997));
    vec3 reflection = texture(lens_view, warped).rgb;
    float fresnel = pow(1. - abs(dot(normalize(VIEW), normalize(NORMAL))), 3.);
    float ripple = .5 + .5 * sin(UV.y * 28. - UV.x * 6. + TIME * 1.8);
    vec3 tint = portal ? vec3(.19, .071, .26) : vec3(.06, .11, .13);
    vec3 patina = tint * (.26 + .20 * ripple + fresnel * .46);
    ALBEDO = live_view ? tint * .25 : tint;
    METALLIC = live_view ? .56 : 1.;
    ROUGHNESS = .055 + warp * .85;
    EMISSION = live_view ? reflection * (portal ? .83 : .74) + patina : patina;
}
