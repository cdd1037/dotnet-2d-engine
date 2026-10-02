#pragma once
#include <algorithm>
#include <array>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <limits>
#include <map>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

// A deliberately small, static authored-SVG profile. Validate the exact immutable
// bytes before handing them to LunaSVG. These source/expansion budgets defend the
// authoring boundary; they are not a CPU or allocator sandbox for a renderer.
constexpr size_t svg_max_encoded_bytes = 256u * 1024u;
constexpr uint32_t svg_max_dimension = 4096;
constexpr size_t svg_max_depth = 32;
constexpr size_t svg_max_nodes = 2048;
constexpr size_t svg_max_numeric_tokens = 16384;
constexpr size_t svg_max_path_bytes = 64u * 1024u;
constexpr size_t svg_max_expanded_nodes = 16384;
constexpr size_t svg_max_expanded_numeric_tokens = 65536;
constexpr size_t svg_max_expanded_path_bytes = 256u * 1024u;
constexpr size_t svg_max_reference_depth = 16;
struct SvgMetadata { double width = 0, height = 0; };

namespace svg_validation_detail {
inline bool space(char c) { return c == ' ' || c == '\t' || c == '\r' || c == '\n'; }
inline bool letter(char c) { return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'); }
inline bool digit(char c) { return c >= '0' && c <= '9'; }
inline std::string_view trim(std::string_view s) {
    while (!s.empty() && space(s.front())) s.remove_prefix(1);
    while (!s.empty() && space(s.back())) s.remove_suffix(1);
    return s;
}
inline bool one_of(std::string_view value, std::string_view words) {
    while (!words.empty()) {
        const auto end = words.find(' ');
        if (value == words.substr(0, end)) return true;
        if (end == std::string_view::npos) break;
        words.remove_prefix(end + 1);
    }
    return false;
}
inline bool identifier(std::string_view s) {
    if (s.empty() || s.size() > 64 || !(letter(s[0]) || s[0] == '_')) return false;
    for (char c : s) if (!(letter(c) || digit(c) || c == '_' || c == '-' || c == '.')) return false;
    return true;
}
inline bool utf8(std::string_view s) {
    for (size_t i = 0; i < s.size();) {
        const auto c = static_cast<unsigned char>(s[i++]);
        if (c < 0x80) { if (c < 0x20 && c != 9 && c != 10 && c != 13) return false; continue; }
        uint32_t cp = 0; unsigned trailing = 0;
        if (c >= 0xc2 && c <= 0xdf) { cp = c & 31; trailing = 1; }
        else if (c >= 0xe0 && c <= 0xef) { cp = c & 15; trailing = 2; }
        else if (c >= 0xf0 && c <= 0xf4) { cp = c & 7; trailing = 3; }
        else return false;
        if (trailing > s.size() - i) return false;
        for (unsigned j = 0; j < trailing; ++j) {
            const auto d = static_cast<unsigned char>(s[i++]);
            if ((d & 0xc0) != 0x80) return false;
            cp = (cp << 6) | (d & 63);
        }
        if ((trailing == 1 && cp < 0x80) || (trailing == 2 && cp < 0x800) ||
            (trailing == 3 && cp < 0x10000) || cp > 0x10ffff ||
            (cp >= 0xd800 && cp <= 0xdfff) || cp == 0xfffe || cp == 0xffff) return false;
    }
    return true;
}
inline bool utf8_encoding(std::string_view value) {
    if (value.size() != 5) return false;
    const char* expected = "utf-8";
    for (size_t i = 0; i < 5; ++i) {
        char c = value[i]; if (c >= 'A' && c <= 'Z') c = static_cast<char>(c - 'A' + 'a');
        if (c != expected[i]) return false;
    }
    return true;
}
struct Attribute { std::string first, second; };
struct Ref { std::string id, kind; size_t target = 0; };
struct Node {
    std::string tag;
    std::vector<size_t> children;
    std::vector<Ref> refs;
    size_t numbers = 0, path_bytes = 0;
    double linear_bound = 1, translation_bound = 0;
};
struct Cost { size_t nodes = 1, numbers = 0, path_bytes = 0, ref_depth = 0; };
class Validator {
    std::string_view source;
    std::string& error;
    size_t at = 0, numeric_tokens = 0, path_bytes = 0;
    std::vector<Node> nodes;
    std::vector<size_t> stack;
    std::map<std::string, size_t> ids;
    bool xlink = false, root_closed = false;
    SvgMetadata dimensions;
    using Attributes = std::vector<Attribute>;

    bool fail(const char* message) { error = message; return false; }
    void ws() { while (at < source.size() && space(source[at])) ++at; }
    bool starts(std::string_view value) const { return source.substr(at, value.size()) == value; }
    std::string name() {
        const size_t begin = at;
        if (at == source.size() || !(letter(source[at]) || source[at] == '_')) return {};
        ++at;
        while (at < source.size() && (letter(source[at]) || digit(source[at]) ||
               source[at] == '_' || source[at] == '-' || source[at] == '.' || source[at] == ':')) ++at;
        return std::string(source.substr(begin, at - begin));
    }
    const std::string* attr(const Attributes& a, std::string_view key) const {
        for (const auto& p : a) if (p.first == key) return &p.second;
        return nullptr;
    }
    bool read_attributes(Attributes& a, bool declaration, bool& closed) {
        closed = false;
        for (;;) {
            const size_t before = at; ws();
            if (declaration && starts("?>")) { at += 2; return true; }
            if (!declaration && starts("/>")) { at += 2; closed = true; return true; }
            if (!declaration && starts(">")) { ++at; return true; }
            if (at == before) return fail("SVG attributes require whitespace and quoted values");
            auto key = name();
            if (key.empty() || key.size() > 64) return fail("invalid SVG attribute name");
            for (const auto& p : a) if (p.first == key) return fail("duplicate SVG attribute");
            if (a.size() >= 64) return fail("too many SVG attributes");
            ws();
            if (at == source.size() || source[at++] != '=') return fail("SVG attributes require '='");
            ws();
            if (at == source.size() || (source[at] != '\'' && source[at] != '"')) return fail("SVG attributes require quoted values");
            const char quote = source[at++]; const size_t begin = at;
            while (at < source.size() && source[at] != quote) {
                if (source[at] == '<') return fail("invalid '<' in SVG attribute");
                ++at;
            }
            if (at == source.size()) return fail("unterminated SVG attribute");
            std::string value;
            value.reserve(at - begin);
            for (size_t p = begin; p < at; ++p) {
                char c = source[p];
                if (c == '\r') { if (p + 1 < at && source[p + 1] == '\n') ++p; c = ' '; }
                else if (c == '\n' || c == '\t') c = ' ';
                value.push_back(c);
            }
            if (value.size() > 65536) return fail("oversized normalized SVG attribute");
            a.push_back({std::move(key), std::move(value)});
            ++at;
        }
    }
    bool number(std::string_view s, size_t& p, double& out, Node& n) {
        const size_t begin = p;
        bool negative = false;
        if (p < s.size() && (s[p] == '+' || s[p] == '-')) negative = s[p++] == '-';
        long double mantissa = 0; size_t digits = 0, fraction = 0;
        while (p < s.size() && digit(s[p])) { mantissa = mantissa * 10 + (s[p++] - '0'); ++digits; }
        if (p < s.size() && s[p] == '.') {
            ++p;
            while (p < s.size() && digit(s[p])) { mantissa = mantissa * 10 + (s[p++] - '0'); ++digits; ++fraction; }
        }
        if (!digits || p - begin > 64) return fail("invalid or oversized SVG number");
        int exponent = 0; bool enegative = false;
        if (p < s.size() && (s[p] == 'e' || s[p] == 'E')) {
            ++p;
            if (p < s.size() && (s[p] == '+' || s[p] == '-')) enegative = s[p++] == '-';
            const size_t ebegin = p;
            while (p < s.size() && digit(s[p])) {
                if (exponent > 308) return fail("SVG numeric exponent is out of range");
                exponent = exponent * 10 + (s[p++] - '0');
            }
            if (p == ebegin || exponent > 308) return fail("invalid SVG numeric exponent");
        }
        if (p - begin > 64) return fail("oversized SVG number");
        const int power = (enegative ? -exponent : exponent) - static_cast<int>(fraction);
        const long double value = mantissa * std::pow(10.0L, power) * (negative ? -1 : 1);
        out = static_cast<double>(value);
        if (!std::isfinite(out) || std::fabs(out) > 1000000) return fail("SVG numbers must be finite and within +/-1000000");
        if (++numeric_tokens > svg_max_numeric_tokens) return fail("SVG numeric token budget exceeded");
        ++n.numbers;
        return true;
    }
    // SVG permits sign/dot-separated numbers, in addition to whitespace or one comma.
    bool separator(std::string_view s, size_t& p, bool first) {
        const size_t before = p;
        while (p < s.size() && space(s[p])) ++p;
        bool comma = false;
        if (p < s.size() && s[p] == ',') {
            if (first) return fail("unexpected SVG numeric comma");
            comma = true; ++p;
            while (p < s.size() && space(s[p])) ++p;
            if (p == s.size() || s[p] == ',') return fail("trailing or repeated SVG numeric comma");
        }
        if (!first && p < s.size() && !comma && before == p && s[p] != '+' && s[p] != '-' && s[p] != '.')
            return fail("missing SVG numeric separator");
        return true;
    }
    bool numbers(std::string_view s, std::vector<double>& values, Node& n, size_t maximum = svg_max_numeric_tokens) {
        s = trim(s); size_t p = 0;
        while (p < s.size()) {
            if (!separator(s, p, values.empty())) return false;
            if (p == s.size()) break;
            double value = 0;
            if (!number(s, p, value, n)) return false;
            values.push_back(value);
            if (values.size() > maximum) return fail("too many numbers in SVG attribute");
        }
        return true;
    }
    bool length(std::string_view s, Node& n, double& value, bool percentage = true, bool* is_percent = nullptr) {
        s = trim(s); size_t p = 0;
        if (!number(s, p, value, n)) return false;
        const auto suffix = s.substr(p);
        const bool percent = suffix == "%";
        if (is_percent) *is_percent = percent;
        return suffix.empty() || suffix == "px" || (percentage && percent) || fail("SVG lengths must be unitless, px or permitted percentages");
    }
    bool scalar(std::string_view s, Node& n, double low, double high) {
        s = trim(s); size_t p = 0; double value = 0;
        return number(s, p, value, n) && ((p == s.size() && value >= low && value <= high) || fail("SVG numeric attribute is out of range"));
    }
    bool reference(std::string_view s, const char* kind, Node& n, bool url) {
        s = trim(s);
        if (url) {
            if (s.size() < 7 || s.substr(0, 4) != "url(" || s.back() != ')') return fail("SVG paint/clip/mask references must be url(#id)");
            s = trim(s.substr(4, s.size() - 5));
        }
        if (s.empty() || s.front() != '#' || !identifier(s.substr(1))) return fail("SVG references must be local #id fragments");
        n.refs.push_back({std::string(s.substr(1)), kind, 0});
        return true;
    }
    bool color(std::string_view s, Node& n) {
        s = trim(s);
        if (s == "currentColor" || s == "inherit") return true;
        if (!s.empty() && s[0] == '#') {
            if (s.size() != 4 && s.size() != 5 && s.size() != 7 && s.size() != 9) return fail("invalid SVG hexadecimal color");
            for (char c : s.substr(1)) if (!(digit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return fail("invalid SVG hexadecimal color");
            return true;
        }
        if (one_of(s, "aliceblue antiquewhite aqua aquamarine azure beige bisque black blanchedalmond blue blueviolet brown burlywood cadetblue chartreuse chocolate coral cornflowerblue cornsilk crimson cyan darkblue darkcyan darkgoldenrod darkgray darkgreen darkgrey darkkhaki darkmagenta darkolivegreen darkorange darkorchid darkred darksalmon darkseagreen darkslateblue darkslategray darkslategrey darkturquoise darkviolet deeppink deepskyblue dimgray dimgrey dodgerblue firebrick floralwhite forestgreen fuchsia gainsboro ghostwhite gold goldenrod gray green greenyellow grey honeydew hotpink indianred indigo ivory khaki lavender lavenderblush lawngreen lemonchiffon lightblue lightcoral lightcyan lightgoldenrodyellow lightgray lightgreen lightgrey lightpink lightsalmon lightseagreen lightskyblue lightslategray lightslategrey lightsteelblue lightyellow lime limegreen linen magenta maroon mediumaquamarine mediumblue mediumorchid mediumpurple mediumseagreen mediumslateblue mediumspringgreen mediumturquoise mediumvioletred midnightblue mintcream mistyrose moccasin navajowhite navy oldlace olive olivedrab orange orangered orchid palegoldenrod palegreen paleturquoise palevioletred papayawhip peachpuff peru pink plum powderblue purple rebeccapurple red rosybrown royalblue saddlebrown salmon sandybrown seagreen seashell sienna silver skyblue slateblue slategray slategrey snow springgreen steelblue tan teal thistle tomato transparent turquoise violet wheat white whitesmoke yellow yellowgreen")) return true;
        const bool rgba = s.substr(0, 5) == "rgba(";
        if ((rgba || s.substr(0, 4) == "rgb(") && s.back() == ')') {
            const size_t begin = rgba ? 5 : 4; auto inner = s.substr(begin, s.size() - begin - 1); size_t p = 0; bool percent_rgb = false;
            for (unsigned i = 0; i < (rgba ? 4u : 3u); ++i) {
                while (p < inner.size() && space(inner[p])) ++p;
                double v = 0; if (!number(inner, p, v, n)) return false;
                const bool percent = p < inner.size() && inner[p] == '%'; if (percent) ++p;
                if (!i) percent_rgb = percent;
                if (i < 3 && percent != percent_rgb) return fail("SVG RGB channels must use matching units");
                if (v < 0 || v > (percent ? 100 : (i == 3 ? 1 : 255))) return fail("SVG color component is out of range");
                while (p < inner.size() && space(inner[p])) ++p;
                if (i + 1 < (rgba ? 4u : 3u)) { if (p == inner.size() || inner[p++] != ',') return fail("SVG RGB channels require commas"); }
                else if (p != inner.size()) return fail("invalid SVG RGB color");
            }
            return true;
        }
        return fail("unsupported SVG color");
    }
    bool transform(std::string_view s, Node& n) {
        s = trim(s); size_t p = 0; size_t count = 0;
        std::array<double, 6> matrix{1, 0, 0, 1, 0, 0};
        while (p < s.size()) {
            while (p < s.size() && space(s[p])) ++p;
            const size_t begin = p; while (p < s.size() && letter(s[p])) ++p;
            const auto function = s.substr(begin, p - begin);
            while (p < s.size() && space(s[p])) ++p;
            if (p == s.size() || s[p++] != '(') return fail("invalid SVG transform function");
            const size_t args = p; while (p < s.size() && s[p] != ')') ++p;
            if (p == s.size()) return fail("unterminated SVG transform");
            std::vector<double> v;
            if (!numbers(s.substr(args, p - args), v, n, 6)) return false;
            ++p;
            const bool valid = (function == "matrix" && v.size() == 6) ||
                ((function == "translate" || function == "scale") && (v.size() == 1 || v.size() == 2)) ||
                (function == "rotate" && (v.size() == 1 || v.size() == 3)) ||
                ((function == "skewX" || function == "skewY") && v.size() == 1);
            if (!valid || ++count > 128) return fail("unsupported or oversized SVG transform");
            std::array<double, 6> operation{1, 0, 0, 1, 0, 0};
            constexpr double radians = 3.14159265358979323846 / 180.0;
            if (function == "matrix") std::copy(v.begin(), v.end(), operation.begin());
            else if (function == "translate") { operation[4] = v[0]; operation[5] = v.size() == 2 ? v[1] : 0; }
            else if (function == "scale") { operation[0] = v[0]; operation[3] = v.size() == 2 ? v[1] : v[0]; }
            else if (function == "rotate") {
                const double c = std::cos(v[0] * radians), q = std::sin(v[0] * radians);
                operation[0] = c; operation[1] = q; operation[2] = -q; operation[3] = c;
                if (v.size() == 3) { operation[4] = v[1] - c * v[1] + q * v[2]; operation[5] = v[2] - q * v[1] - c * v[2]; }
            } else {
                if (std::fabs(std::cos(v[0] * radians)) <= .000001) return fail("SVG skew is too close to a singular angle");
                operation[function == "skewX" ? 2 : 1] = std::tan(v[0] * radians);
            }
            const auto a = matrix; const auto& b = operation;
            matrix = {a[0]*b[0]+a[2]*b[1], a[1]*b[0]+a[3]*b[1], a[0]*b[2]+a[2]*b[3],
                      a[1]*b[2]+a[3]*b[3], a[0]*b[4]+a[2]*b[5]+a[4], a[1]*b[4]+a[3]*b[5]+a[5]};
            for (double component : matrix) if (!std::isfinite(component) || std::fabs(component) > 1000000) return fail("SVG composed transform is out of range");
            while (p < s.size() && space(s[p])) ++p;
            if (p < s.size() && s[p] == ',') { ++p; while (p < s.size() && space(s[p])) ++p; if (p == s.size()) return fail("trailing SVG transform comma"); }
        }
        if (!count) return fail("SVG transform cannot be empty");
        const double linear = std::max(std::fabs(matrix[0]) + std::fabs(matrix[2]), std::fabs(matrix[1]) + std::fabs(matrix[3]));
        const double translation = std::max(std::fabs(matrix[4]), std::fabs(matrix[5]));
        n.translation_bound += n.linear_bound * translation;
        n.linear_bound *= linear;
        return (std::isfinite(n.linear_bound) && std::isfinite(n.translation_bound) && n.linear_bound <= 1000000 && n.translation_bound <= 1000000) || fail("SVG local affine bound is out of range");
    }
    bool path(std::string_view s, Node& n) {
        n.path_bytes += s.size(); path_bytes += s.size();
        if (path_bytes > svg_max_path_bytes) return fail("SVG path source budget exceeded");
        s = trim(s); size_t p = 0; bool first = true;
        while (p < s.size()) {
            while (p < s.size() && space(s[p])) ++p;
            if (p == s.size()) break;
            const char command = s[p++];
            if (first && command != 'M' && command != 'm') return fail("SVG paths must begin with moveto");
            first = false;
            unsigned arity = 0;
            switch (command) {
                case 'M': case 'm': case 'L': case 'l': case 'T': case 't': arity = 2; break;
                case 'H': case 'h': case 'V': case 'v': arity = 1; break;
                case 'C': case 'c': arity = 6; break;
                case 'S': case 's': case 'Q': case 'q': arity = 4; break;
                case 'A': case 'a': arity = 7; break;
                case 'Z': case 'z': continue;
                default: return fail("unsupported SVG path command");
            }
            size_t values = 0; std::array<double, 7> group{};
            for (;;) {
                const size_t before = p;
                while (p < s.size() && space(s[p])) ++p;
                if (p == s.size() || letter(s[p])) break;
                p = before;
                if (!separator(s, p, values == 0)) return false;
                if (p == s.size()) break;
                const size_t token_begin = p;
                if (!number(s, p, group[values % arity], n)) return false;
                if ((command == 'A' || command == 'a') && (values % arity == 3 || values % arity == 4) &&
                    (p != token_begin + 1 || (s[token_begin] != '0' && s[token_begin] != '1')))
                    return fail("SVG arc flags must be literal zero or one");
                ++values;
                if ((command == 'A' || command == 'a') && values % arity == 0 &&
                    (group[0] < 0 || group[1] < 0 || (group[3] != 0 && group[3] != 1) || (group[4] != 0 && group[4] != 1)))
                    return fail("invalid SVG arc radii or flags");
            }
            if (!values || values % arity != 0) return fail("incomplete SVG path command arguments");
        }
        return true;
    }
    bool preserve_aspect(std::string_view value) {
        value = trim(value); const auto split = value.find_first_of(" \t\r\n");
        const auto align = value.substr(0, split);
        const auto tail = split == std::string_view::npos ? std::string_view{} : trim(value.substr(split));
        return (one_of(align, "none xMinYMin xMidYMin xMaxYMin xMinYMid xMidYMid xMaxYMid xMinYMax xMidYMax xMaxYMax") &&
            (tail.empty() || (align != "none" && (tail == "meet" || tail == "slice")))) || fail("invalid SVG preserveAspectRatio");
    }
    bool allowed_attr(std::string_view tag, std::string_view key) const {
        if (key == "id") return true;
        if (tag == "title" || tag == "desc") return false;
        if (one_of(key, "transform fill fill-rule fill-opacity stroke stroke-width stroke-opacity stroke-linecap stroke-linejoin stroke-miterlimit stroke-dasharray stroke-dashoffset opacity clip-path clip-rule mask color display visibility")) return true;
        if (tag == "svg") return one_of(key, "x y width height viewBox preserveAspectRatio version");
        if (tag == "symbol") return one_of(key, "x y width height viewBox preserveAspectRatio");
        if (tag == "use") return one_of(key, "x y width height href xlink:href");
        if (tag == "clipPath") return key == "clipPathUnits";
        if (tag == "mask") return one_of(key, "x y width height maskUnits maskContentUnits mask-type");
        if (tag == "linearGradient") return one_of(key, "x1 y1 x2 y2 gradientUnits gradientTransform spreadMethod href xlink:href");
        if (tag == "radialGradient") return one_of(key, "cx cy r fx fy gradientUnits gradientTransform spreadMethod href xlink:href");
        if (tag == "stop") return one_of(key, "offset stop-color stop-opacity");
        if (tag == "rect") return one_of(key, "x y width height rx ry");
        if (tag == "circle") return one_of(key, "cx cy r");
        if (tag == "ellipse") return one_of(key, "cx cy rx ry");
        if (tag == "line") return one_of(key, "x1 y1 x2 y2");
        if (tag == "polyline" || tag == "polygon") return key == "points";
        return tag == "path" && key == "d";
    }
    bool attributes(const Attributes& a, size_t index) {
        Node& n = nodes[index]; const bool root = index == 0;
        if (n.tag == "use" && !attr(a, "href") && !attr(a, "xlink:href")) return fail("SVG use requires an internal href");
        if (attr(a, "href") && attr(a, "xlink:href")) return fail("SVG cannot combine href and xlink:href");
        if (root) {
            if (const auto* v = attr(a, "xmlns")) if (!v->empty() && *v != "http://www.w3.org/2000/svg") return fail("unsupported SVG namespace");
            if (const auto* v = attr(a, "xmlns:xlink")) { if (*v != "http://www.w3.org/1999/xlink") return fail("unsupported SVG xlink namespace"); xlink = true; }
        }
        std::vector<double> box;
        for (const auto& pair : a) {
            const auto& key = pair.first; const auto value = trim(pair.second);
            if (root && (key == "xmlns" || key == "xmlns:xlink")) continue;
            if (!allowed_attr(n.tag, key)) return fail("unsupported SVG attribute");
            if (key == "id") {
                if (!identifier(pair.second) || !ids.emplace(pair.second, index).second) return fail("invalid or duplicate SVG id");
            } else if (key == "href" || key == "xlink:href") {
                if (key == "xlink:href" && !xlink) return fail("undeclared SVG xlink namespace");
                if (!reference(value, n.tag == "use" ? "use" : "gradient", n, false)) return false;
            } else if (key == "fill" || key == "stroke") {
                if (value == "none") continue;
                if (value.substr(0, 4) == "url(") { if (!reference(value, "paint", n, true)) return false; }
                else if (!color(value, n)) return false;
            } else if (key == "color" || key == "stop-color") {
                if (!color(value, n)) return false;
            } else if (key == "clip-path" || key == "mask") {
                if (value != "none" && !reference(value, key == "mask" ? "mask" : "clip", n, true)) return false;
            } else if (key == "transform" || key == "gradientTransform") {
                if (!transform(value, n)) return false;
            } else if (key == "d") {
                if (!path(pair.second, n)) return false;
            } else if (key == "points") {
                std::vector<double> points; if (!numbers(value, points, n)) return false;
                if (points.size() < 4 || points.size() % 2 != 0) return fail("SVG points require coordinate pairs");
            } else if (key == "viewBox") {
                if (!numbers(value, box, n, 4) || box.size() != 4) return fail("SVG viewBox requires four numbers");
                if (box[2] <= 0 || box[3] <= 0 || box[2] > svg_max_dimension || box[3] > svg_max_dimension) return fail("SVG viewBox dimensions must be positive and <=4096");
            } else if (key == "preserveAspectRatio") {
                if (!preserve_aspect(value)) return false;
            } else if (key == "version") {
                if (value != "1.1") return fail("unsupported SVG version");
            } else if (key == "fill-rule" || key == "clip-rule") {
                if (!one_of(value, "nonzero evenodd inherit")) return fail("unsupported SVG fill/clip rule");
            } else if (key == "stroke-linecap") {
                if (!one_of(value, "butt round square inherit")) return fail("unsupported SVG line cap");
            } else if (key == "stroke-linejoin") {
                if (!one_of(value, "miter round bevel inherit")) return fail("unsupported SVG line join");
            } else if (key == "display") {
                if (!one_of(value, "inline none inherit")) return fail("unsupported SVG display");
            } else if (key == "visibility") {
                if (!one_of(value, "visible hidden collapse inherit")) return fail("unsupported SVG visibility");
            } else if (key == "gradientUnits" || key == "clipPathUnits" || key == "maskUnits" || key == "maskContentUnits") {
                if (!one_of(value, "userSpaceOnUse objectBoundingBox")) return fail("unsupported SVG coordinate units");
            } else if (key == "spreadMethod") {
                if (!one_of(value, "pad reflect repeat")) return fail("unsupported SVG gradient spread");
            } else if (key == "mask-type") {
                if (!one_of(value, "luminance alpha")) return fail("unsupported SVG mask type");
            } else if (key == "offset") {
                double v = 0; bool percent = false;
                if (!length(value, n, v, true, &percent) || v < 0 || v > (percent ? 100 : 1) || value.substr(value.size() >= 2 ? value.size() - 2 : 0) == "px") return fail("SVG stop offset must be 0..1 or 0..100%");
            } else if (one_of(key, "opacity fill-opacity stroke-opacity stop-opacity")) {
                if (value == "inherit") continue;
                double v = 0; bool percent = false;
                if (!length(value, n, v, true, &percent) || v < 0 || v > (percent ? 100 : 1) || value.substr(value.size() >= 2 ? value.size() - 2 : 0) == "px") return fail("SVG opacity must be 0..1 or 0..100%");
            } else if (key == "stroke-miterlimit") {
                if (value != "inherit" && !scalar(value, n, 1, 1000000)) return false;
            } else if (key == "stroke-dasharray") {
                if (value == "none" || value == "inherit") continue;
                size_t p = 0, count = 0; bool positive = false;
                while (p < value.size()) {
                    if (!separator(value, p, count == 0)) return false;
                    if (p == value.size()) break;
                    double v = 0; if (!number(value, p, v, n) || v < 0) return fail("invalid SVG stroke dash");
                    positive = positive || v > 0;
                    if (value.substr(p, 2) == "px") p += 2; else if (p < value.size() && value[p] == '%') ++p;
                    if (++count > 64) return fail("SVG stroke dash count exceeds 64");
                }
                if (!count || !positive) return fail("SVG stroke dash array requires a positive entry");
            } else {
                double v = 0;
                if (value == "inherit" && one_of(key, "stroke-width stroke-dashoffset")) continue;
                if (!length(value, n, v, !(root && (key == "width" || key == "height")))) return false;
                if (one_of(key, "width height r rx ry stroke-width") && v < 0) return fail("SVG lengths cannot be negative here");
                if (root && (key == "width" || key == "height")) {
                    if (v <= 0 || v > svg_max_dimension) return fail("SVG intrinsic dimensions must be positive and <=4096");
                    (key == "width" ? dimensions.width : dimensions.height) = v;
                }
            }
        }
        if (root) {
            if (!dimensions.width && box.size() == 4) dimensions.width = box[2];
            if (!dimensions.height && box.size() == 4) dimensions.height = box[3];
            if (dimensions.width <= 0 || dimensions.height <= 0) return fail("SVG requires positive intrinsic width/height or viewBox");
            if (box.size() == 4 && (dimensions.width / box[2] > 1000000 || dimensions.height / box[3] > 1000000)) return fail("SVG intrinsic viewBox scale is out of range");
        }
        return true;
    }
    bool affine_bounds(size_t index, double parent_linear, double parent_translation) {
        const auto& n = nodes[index];
        const double linear = parent_linear * n.linear_bound;
        const double translation = parent_linear * n.translation_bound + parent_translation;
        if (!std::isfinite(linear) || !std::isfinite(translation) || linear > 1000000 || translation > 1000000) return fail("SVG inherited affine bound is out of range");
        for (auto child : n.children) if (!affine_bounds(child, linear, translation)) return false;
        for (const auto& ref : n.refs) if (!affine_bounds(ref.target, linear, translation)) return false;
        return true;
    }
    bool cost(size_t index, std::vector<unsigned char>& state, std::vector<Cost>& costs) {
        if (state[index] == 1) return fail("cyclic SVG internal reference");
        if (state[index] == 2) return true;
        state[index] = 1; const auto& n = nodes[index]; Cost result; result.numbers = n.numbers; result.path_bytes = n.path_bytes;
        auto add = [&](size_t target, bool ref) {
            if (!cost(target, state, costs)) return false;
            const auto& c = costs[target];
            result.nodes += c.nodes; result.numbers += c.numbers; result.path_bytes += c.path_bytes;
            result.ref_depth = std::max(result.ref_depth, c.ref_depth + (ref ? 1u : 0u));
            return (result.nodes <= svg_max_expanded_nodes && result.numbers <= svg_max_expanded_numeric_tokens &&
                    result.path_bytes <= svg_max_expanded_path_bytes && result.ref_depth <= svg_max_reference_depth) || fail("SVG internal expansion budget exceeded");
        };
        for (auto child : n.children) if (!add(child, false)) return false;
        for (const auto& ref : n.refs) if (!add(ref.target, true)) return false;
        costs[index] = result; state[index] = 2; return true;
    }
    bool references() {
        for (auto& n : nodes) for (auto& ref : n.refs) {
            const auto found = ids.find(ref.id);
            if (found == ids.end()) return fail("unresolved SVG internal reference");
            ref.target = found->second; const auto& tag = nodes[ref.target].tag;
            if ((ref.kind == "paint" || ref.kind == "gradient") && tag != "linearGradient" && tag != "radialGradient") return fail("SVG paint references require a gradient");
            if (ref.kind == "clip" && tag != "clipPath") return fail("SVG clipping references require clipPath");
            if (ref.kind == "mask" && tag != "mask") return fail("SVG mask references require mask");
            if (ref.kind == "use" && !one_of(tag, "g symbol use rect circle ellipse line polyline polygon path")) return fail("unsupported SVG use target");
        }
        std::vector<unsigned char> state(nodes.size()); std::vector<Cost> costs(nodes.size());
        return cost(0, state, costs) && affine_bounds(0, 1, 0);
    }
public:
    Validator(std::string_view bytes, std::string& message) : source(bytes), error(message) {}
    bool run(SvgMetadata& metadata) {
        if (source.empty() || source.size() > svg_max_encoded_bytes) return fail("SVG encoded size must be 1 byte..256 KiB");
        if (!utf8(source)) return fail("SVG must contain valid UTF-8 XML characters");
        if (source.find('&') != std::string_view::npos) return fail("SVG entities are not supported");
        if (source.substr(0, 3) == "\xef\xbb\xbf") at = 3;
        if (starts("<?xml")) {
            at += 5; Attributes declaration; bool closed = false;
            if (!read_attributes(declaration, true, closed)) return false;
            if (declaration.empty() || (declaration[0].first != "version" || declaration[0].second != "1.0")) return fail("SVG XML declaration requires version 1.0");
            bool encoding = false, standalone = false;
            for (size_t i = 1; i < declaration.size(); ++i) {
                const auto& a = declaration[i];
                if (a.first == "encoding" && !encoding && !standalone && utf8_encoding(a.second)) encoding = true;
                else if (a.first == "standalone" && !standalone && (a.second == "yes" || a.second == "no")) standalone = true;
                else return fail("unsupported SVG XML declaration");
            }
        }
        while (at < source.size()) {
            if (source[at] != '<') {
                const size_t begin = at; while (at < source.size() && source[at] != '<') ++at;
                const auto text = source.substr(begin, at - begin);
                if (text.find("]]>") != std::string_view::npos) return fail("invalid SVG XML text");
                if ((stack.empty() || (nodes[stack.back()].tag != "title" && nodes[stack.back()].tag != "desc")) && !trim(text).empty()) return fail("SVG text content is allowed only in title/desc");
                continue;
            }
            if (starts("<!--")) {
                const auto end = source.find("-->", at + 4);
                if (end == std::string_view::npos || source.substr(at + 4, end - at - 4).find("--") != std::string_view::npos || (end > at + 4 && source[end - 1] == '-')) return fail("invalid SVG XML comment");
                at = end + 3; continue;
            }
            if (starts("</")) {
                at += 2; const auto tag = name(); ws();
                if (stack.empty() || nodes[stack.back()].tag != tag || at == source.size() || source[at++] != '>') return fail("mismatched SVG closing element");
                stack.pop_back(); if (stack.empty()) root_closed = true;
                continue;
            }
            if (starts("<!") || starts("<?")) return fail("SVG DTD, CDATA and processing instructions are not supported");
            ++at; const auto tag = name();
            if (!one_of(tag, "svg g defs symbol use clipPath mask linearGradient radialGradient stop rect circle ellipse line polyline polygon path title desc")) return fail("unsupported SVG element");
            if ((nodes.empty() && tag != "svg") || root_closed) return fail("SVG requires exactly one svg root");
            if (!stack.empty() && (nodes[stack.back()].tag == "title" || nodes[stack.back()].tag == "desc")) return fail("SVG title/desc cannot contain elements");
            if (nodes.size() >= svg_max_nodes || stack.size() + 1 > svg_max_depth) return fail("SVG element count or depth budget exceeded");
            Attributes a; bool closed = false;
            if (!read_attributes(a, false, closed)) return false;
            const size_t index = nodes.size(); nodes.push_back(Node{}); nodes.back().tag = tag;
            if (!stack.empty()) nodes[stack.back()].children.push_back(index);
            if (!attributes(a, index)) return false;
            if (!closed) stack.push_back(index); else if (index == 0) root_closed = true;
        }
        if (nodes.empty() || !stack.empty() || !root_closed) return fail("incomplete SVG document");
        if (!references()) return false;
        metadata = dimensions; error.clear(); return true;
    }
};
} // namespace svg_validation_detail

inline bool svg_validate(const std::vector<unsigned char>& bytes, SvgMetadata& metadata, std::string& error) {
    metadata = {};
    const std::string_view source(bytes.empty() ? "" : reinterpret_cast<const char*>(bytes.data()), bytes.size());
    return svg_validation_detail::Validator(source, error).run(metadata);
}
