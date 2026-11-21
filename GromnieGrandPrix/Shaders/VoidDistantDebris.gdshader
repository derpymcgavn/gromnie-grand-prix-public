shader_type spatial;
render_mode unshaded, fog_disabled;

void fragment() {
    float grazing = pow(1.0 - max(0.0, dot(normalize(NORMAL), normalize(VIEW))), 2.0);
    float face = max(0.0, dot(normalize(NORMAL), normalize(vec3(.27, .95, .17))));
    ALBEDO = vec3(.009, .011, .022) + COLOR.rgb * (grazing * .17 + face * .21);
}
