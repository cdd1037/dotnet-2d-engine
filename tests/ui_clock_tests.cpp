#include "gal.h"
#include "gal_ui.h"
#include <SDL3/SDL.h>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <initializer_list>

// Optional SDL/RmlUi integration test; see docs/TESTING.md for prerequisites.
static gal_context* context = nullptr;

static void require(bool ok, const char* message) {
    if (!ok) {
        std::fprintf(stderr, "FAIL UI clock: %s\n", message);
        if (context) gal_destroy(context);
        std::exit(1);
    }
}

static void check(int status, const char* operation) {
    if (status != 0) std::fprintf(stderr, "%s: %s\n", operation, gal_last_error());
    require(status == 0, operation);
}

static void render() {
    gal_camera camera{0, 0, 1};
    check(gal_begin(context, &camera), "begin frame");
    check(gal_end(context), "end frame");
}

static void poll() {
    gal_input_v2 input{};
    input.size = sizeof(input);
    input.version = 2;
    check(gal_poll_v2(context, &input), "poll input");
}

static gal_ui_text_state text_state() {
    gal_ui_text_state state{};
    state.size = sizeof(state);
    state.version = 1;
    check(gal_ui_get_text_state(context, &state), "read text state");
    return state;
}

static uint32_t frame_count() {
    gal_stats stats{};
    stats.size = sizeof(stats);
    check(gal_get_stats(context, &stats), "read frame count");
    return stats.frames;
}

static void push(SDL_Event& event) {
    if (!SDL_PushEvent(&event)) std::fprintf(stderr, "SDL_PushEvent: %s\n", SDL_GetError());
    else return;
    require(false, "queue mouse event");
}

static void click(SDL_WindowID window, float x, float y) {
    SDL_Event motion{};
    motion.type = SDL_EVENT_MOUSE_MOTION;
    motion.motion.windowID = window;
    motion.motion.x = x;
    motion.motion.y = y;
    push(motion);
    for (bool down : {true, false}) {
        SDL_Event button{};
        button.type = down ? SDL_EVENT_MOUSE_BUTTON_DOWN : SDL_EVENT_MOUSE_BUTTON_UP;
        button.button.windowID = window;
        button.button.button = SDL_BUTTON_LEFT;
        button.button.down = down;
        button.button.x = x;
        button.button.y = y;
        push(button);
    }
    poll();
}

int main(int argc, char** argv) {
    require(argc == 2, "usage: gal_ui_clock_tests /path/to/assets/ui/settings.rml");
    const char* font = std::getenv("GAL_UI_FONT");
    if (!font) font = "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc";
    gal_config config{sizeof(config), 1, 960, 540, 16, 0};
    check(gal_create(&config, &context), "create engine");
    check(gal_ui_open(context, argv[1], font), "open settings UI");
    render();
    poll();

    gal_ui_state ui{};
    ui.size = sizeof(ui);
    check(gal_ui_get_state(context, &ui), "read UI generation");
    gal_ui_model model{};
    model.size = sizeof(model);
    model.generation = ui.generation;
    model.volume = 65;
    std::strcpy(model.name, "alpha beta");
    check(gal_ui_set_model(context, &model), "set text fixture");
    check(gal_ui_test_command(context, ui.generation, GAL_UI_TEST_FOCUS), "focus text field");
    render();
    check(gal_ui_test_command(context, ui.generation, GAL_UI_TEST_SELECT_END), "move caret to end");
    render();

    int count = 0;
    SDL_Window** windows = SDL_GetWindows(&count);
    require(windows && count == 1, "expected one engine window");
    const SDL_WindowID window = SDL_GetWindowID(windows[0]);
    const float density = SDL_GetWindowPixelDensity(windows[0]);
    SDL_free(windows);
    require(density > 0, "valid pixel density");
    const auto initial = text_state();
    require(initial.selection_start == 10 && initial.selection_end == 10, "fixture caret at end");
    const float x = (initial.caret_x - 20) / density;
    const float y = (initial.caret_y + 5) / density;
    click(window, x, y);
    const auto first = text_state();
    require(first.selection_start == first.selection_end && first.selection_start >= 6 && first.selection_start < 10,
            "first click places a caret inside beta");

    // RmlUi's double-click threshold is 0.5 s. A paused renderer must not freeze it.
    const uint32_t frames = frame_count();
    const Uint64 paused_at = SDL_GetTicksNS();
    SDL_Delay(1100);
    click(window, x, y);
    const auto second = text_state();
    std::printf("After %.3f s without rendering: selection=%d..%d\n",
                double(SDL_GetTicksNS() - paused_at) / 1e9, second.selection_start, second.selection_end);
    require(frame_count() == frames, "no render occurs between clicks");
    require(second.selection_start == first.selection_start && second.selection_end == first.selection_end,
            "clicks separated by 1.1 s must remain single clicks");

    // The next immediate click must still select the word: do not disable double clicks.
    click(window, x, y);
    const auto third = text_state();
    require(third.selection_start == 6 && third.selection_end == 10, "prompt second click selects beta");
    require(std::strcmp(third.value, model.name) == 0, "mouse selection preserves the text");
    check(gal_destroy(context), "destroy engine");
    context = nullptr;
    std::puts("PASS UI clock: paused-render single click and prompt double click through SDL/RmlUi");
}
