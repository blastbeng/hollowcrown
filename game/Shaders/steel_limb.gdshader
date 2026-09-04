// Steel limb shader (Vision 6.8/6.11): the store suit texture is warm tan —
// a plain AlbedoColor multiply cannot desaturate it. We take the LUMINANCE of
// the source texture (keeps the sculpted shading) and tint it absolutely.
shader_type spatial;
render_mode cull_back;

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap;
uniform vec4 tint : source_color = vec4(0.62, 0.66, 0.74, 1.0);
uniform float roughness_v : hint_range(0.0, 1.0) = 0.66;
uniform float metallic_v : hint_range(0.0, 1.0) = 0.4;
uniform float alpha_v : hint_range(0.0, 1.0) = 1.0;

void fragment() {
    vec3 tex = texture(albedo_tex, UV).rgb;
    float lum = dot(tex, vec3(0.299, 0.587, 0.114));
    ALBEDO = tint.rgb * lum * 2.0;   // luminance mean ~0.5 -> restore scale
    ROUGHNESS = roughness_v;
    METALLIC = metallic_v;
    ALPHA = alpha_v * tint.a;
}
