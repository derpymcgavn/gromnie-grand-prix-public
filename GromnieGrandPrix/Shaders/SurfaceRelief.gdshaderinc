// Perturb the view-space normal from a filtered surface height, without
// needing a common tangent basis across differently tiled terrain layers.
vec3 surface_relief(vec3 position, vec3 normal, float height, float limit) {
    vec3 dx = dFdx(position);
    vec3 dy = dFdy(position);
    vec3 along_x = cross(dy, normal);
    vec3 along_y = cross(normal, dx);
    float determinant = dot(dx, along_x);
    vec3 slope = (along_x * dFdx(height) + along_y * dFdy(height)) * sign(determinant) / max(abs(determinant), 0.000001);
    return normalize(normal - slope * min(1.0, limit / max(length(slope), 0.0001)));
}
