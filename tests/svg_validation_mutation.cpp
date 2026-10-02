#include "svg_validation.h"
#include <cstdio>
#include <random>

// Optional deterministic robustness smoke, run with ASan/UBSan (see UI_SVG.md).
// This checks parser safety only; it is not an exhaustive fuzzer or SVG renderer.
int main() {
    std::mt19937 random(1729);
    const std::vector<std::string> seeds = {
        "<svg width='10' height='10'><path d='M0 0L10 10Z'/></svg>",
        "<?xml version='1.0'?><svg viewBox='0 0 10 10'><defs><linearGradient id='a'>"
        "<stop offset='0' stop-color='red'/></linearGradient><g id='g'>"
        "<rect width='10' height='10' fill='url(#a)'/></g></defs><use href='#g'/></svg>",
        "<svg width='10' height='10'><title>hello</title><g transform='scale(2) translate(1 2)'>"
        "<path d='M0 0A1 1 20 0 1 5 6'/></g></svg>"
    };
    size_t accepted = 0;
    for (unsigned i = 0; i < 60000; ++i) {
        auto source = seeds[random() % seeds.size()];
        for (unsigned j = 0, count = 1 + random() % 8; j < count; ++j) {
            const size_t at = random() % (source.size() + 1);
            switch (random() % 4) {
                case 0: source.insert(at, 1, char(random() % 256)); break;
                case 1:
                    if (at < source.size()) source.erase(at, 1 + random() % std::min<size_t>(16, source.size() - at));
                    break;
                case 2:
                    if (at < source.size()) source[at] = char(random() % 256);
                    break;
                case 3: source.insert(at, seeds[random() % seeds.size()].substr(0, random() % 24)); break;
            }
        }
        SvgMetadata metadata;
        std::string error;
        if (svg_validate(std::vector<unsigned char>(source.begin(), source.end()), metadata, error)) ++accepted;
    }
    std::printf("PASS SVG mutation smoke: 60000 cases, %zu accepted\n", accepted);
}
