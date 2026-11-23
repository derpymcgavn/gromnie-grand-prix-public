shader_type spatial;
render_mode cull_disabled, specular_schlick_ggx, diffuse_burley;

void fragment() {
    vec3 smooth_normal = normalize(NORMAL);
    vec3 facet_normal = cross(dFdx(VERTEX), dFdy(VERTEX));
    facet_normal *= inversesqrt(max(dot(facet_normal, facet_normal), 1e-12));
    facet_normal *= dot(smooth_normal, facet_normal) < 0.0 ? -1.0 : 1.0;
    vec3 normal = normalize(smooth_normal + facet_normal * 1.7);
    NORMAL = normal;

    vec3 eye = normalize(VIEW);
    float facing = clamp(dot(normal, eye), 0.0, 1.0);
    float edge = pow(1.0 - facing, 3.0);
    vec3 facet_key = mat3(VIEW_MATRIX) * normalize(vec3(-.48, .81, .34));
    float cut_light = smoothstep(-.66, .85, dot(normal, facet_key));

    // Slightly spread the diamond's visible wavelengths so passing racers can see the fire.
    vec3 prism_axis = mat3(VIEW_MATRIX) * normalize(vec3(.54, -.66, -.52));
    vec3 dispersed = vec3(
        dot(refract(-eye, normal, 1.0 / 2.397), prism_axis),
        dot(refract(-eye, normal, 1.0 / 2.426), prism_axis),
        dot(refract(-eye, normal, 1.0 / 2.459), prism_axis)
    );
    vec3 fire = 1.0 - smoothstep(vec3(.022), vec3(.15), abs(dispersed - vec3(.43, .56, .69)));

    ALBEDO = mix(vec3(.095, .12, .16), vec3(.36, .40, .44), cut_light) + fire * .035;
    METALLIC = 0.0;
    SPECULAR = 1.0;
    ROUGHNESS = mix(.107, .037, cut_light);
    EMISSION = vec3(.026, .035, .051) + vec3(.073, .091, .11) * (cut_light * .57 + edge * .43)
        + fire * (.065 + edge * .16);
}
