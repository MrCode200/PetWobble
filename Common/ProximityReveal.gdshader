shader_type canvas_item;

uniform vec2 target_position;
uniform float reveal_distance = 350.0;
uniform float fade_width = 150.0;

varying vec2 world_position;

void vertex() {
    world_position = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy;
}

void fragment() {
    vec4 tex_color = texture(TEXTURE, UV);

    float distance_to_target = distance(target_position, world_position);

    // 0.0 at reveal_distance
    // 1.0 at reveal_distance - fade_width
    float fade = clamp(
        (reveal_distance - distance_to_target) / fade_width,
        0.0,
        1.0
    );

    // Optional: smoother, less linear transition.
    float visibility = smoothstep(0.0, 1.0, fade);

    COLOR = vec4(tex_color.rgb, tex_color.a * visibility);
}
