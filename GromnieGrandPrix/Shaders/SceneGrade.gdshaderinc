global uniform vec4 dereth_scene_grade;

vec3 grade_scene(vec3 scene, vec2 uv) {
    const vec3 luma_weights = vec3(.2126, .7152, .0722);
    float luma = max(dot(scene, luma_weights), 0.0);
    float level = clamp(luma, 0.0, 1.0);
    float mids = 4.0 * level * (1.0 - level);
    float tone = luma + dereth_scene_grade.x * (level - .46) * mids + dereth_scene_grade.w * mids;
    tone /= 1.0 + max(tone - .67, 0.0) * .115;
    vec3 balanced = scene * (tone / max(luma, .001));
    float saturation = mix(dereth_scene_grade.y, .94, smoothstep(.64, 1.0, level));
    vec3 graded = mix(vec3(tone), balanced, saturation);

    float shadows = smoothstep(.012, .13, level) * (1.0 - smoothstep(.17, .56, level));
    float highlights = smoothstep(.36, .83, level) * (1.0 - smoothstep(.84, 1.0, level));
    graded += vec3(-.012, .003, .023) * shadows;
    graded += vec3(.034, .013, -.032) * highlights * dereth_scene_grade.z;
    float corner = smoothstep(.29, .72, length((uv - .5) * vec2(1.0, .89)));
    return max(graded, vec3(0.0)) * (1.0 - corner * .080);
}
