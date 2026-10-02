#include "svg_validation.h"
#include <cstdio>
#include <string>
#include <vector>
#define CHECK(x) do { if (!(x)) { std::fprintf(stderr, "FAIL SVG validation line %d: %s (%s)\n", __LINE__, #x, error.c_str()); return 1; } } while (0)
static std::string svg(const std::string& body, const std::string& attrs = "width='32' height='24'") {
    return "<svg " + attrs + ">" + body + "</svg>";
}
int main() {
    SvgMetadata metadata; std::string error;
    auto valid = [&](const std::string& source) { return svg_validate(std::vector<unsigned char>(source.begin(), source.end()), metadata, error); };
    CHECK(valid(svg("<rect width='32' height='24' fill='currentColor'/>")) && metadata.width == 32 && metadata.height == 24 && error.empty());
    CHECK(valid("\xef\xbb\xbf<?xml version='1.0' encoding='UTF-8' standalone='yes'?><!-- ok -->" + svg("<title>Title \xe2\x9c\x93</title><desc>Static artwork</desc>", "viewBox='-1 -2 48 36'")) && metadata.width == 48 && metadata.height == 36);
    CHECK(valid(svg("", "width='16px' viewBox='0 0 48 36'")) && metadata.width == 16 && metadata.height == 36);
    CHECK(valid(svg("", "width='.5' height='1e1'")) && metadata.width == .5 && metadata.height == 10);
    CHECK(valid(svg("", "width='4096' height='4096'")));
    const std::string complex =
        "<defs><linearGradient id='base' x1='0%' y1='0%' x2='100%' y2='0%' gradientUnits='objectBoundingBox'>"
        "<stop offset='0' stop-color='red'/><stop offset='100%' stop-color='#0000ff' stop-opacity='.5'/></linearGradient>"
        "<radialGradient id='paint' href='#base' cx='.5' cy='.5' r='.5' gradientTransform='translate(0 0) scale(1)'/>"
        "<clipPath id='clip' clipPathUnits='userSpaceOnUse'><rect width='20' height='20'/></clipPath>"
        "<mask id='mask' maskUnits='userSpaceOnUse' maskContentUnits='userSpaceOnUse' mask-type='alpha'><rect width='24' height='24' fill='white'/></mask>"
        "<symbol id='shape' viewBox='0 0 24 24'><path d='M0 0 H24 V24 L0 24 Z' fill='currentColor'/></symbol></defs>"
        "<g clip-path='url(#clip)' mask='url(#mask)' opacity='.8'><use href='#shape' width='24' height='24' fill='url(#paint)'/></g>";
    CHECK(valid(svg(complex, "xmlns='http://www.w3.org/2000/svg' width='32' height='24' preserveAspectRatio='xMidYMid meet'")));
    CHECK(valid(svg("<defs><g id='a'><circle r='1'/></g></defs><use xlink:href='#a'/>", "xmlns:xlink='http://www.w3.org/1999/xlink' viewBox='0 0 8 8'")));
    CHECK(valid(svg("<path d='M.5.5 l1-1 2 2 H1 2 V1 2 C0 0 1 1 2 2 S1 1 2 2 Q1 1 2 2 T1 1 A2 3 45 0 1 8 9 z'/>")));
    CHECK(valid(svg("<polygon points='0,0 20,0 20,20'/><polyline points='0 0 1 1'/><ellipse cx='10' cy='10' rx='4' ry='2'/><line x1='0' y1='0' x2='8' y2='8' stroke='rgb(100%, 0%, 0%)' stroke-dasharray='1px,2 3%'/>")));
    for (const char* tag : {"image", "text", "style", "filter", "pattern", "script", "animate", "animateTransform", "animateMotion", "set", "foreignObject", "feGaussianBlur", "a"}) CHECK(!valid(svg(std::string("<") + tag + "/>")));
    for (const char* attr : {"style='fill:red'", "class='red'", "onclick='alert(1)'", "onload='a'", "filter='url(#x)'", "foo='1'", "xml:base='https://example.com'", "xml:space='preserve'", "xmlns='https://example.com'", "data-test='1'"}) CHECK(!valid(svg(std::string("<g ") + attr + "/>")));
    for (const char* ref : {"https://example.com/a.svg#x", "file:///a.svg#x", "data:image/svg+xml,a", "other.svg#x", "//host/x", "#", "#bad id", "#a%20b", "#a#b", "javascript:alert(1)"}) CHECK(!valid(svg(std::string("<use href='") + ref + "'/>")));
    for (const char* paint : {"url(https://example.com/x)", "url(data:x)", "url(#x) red", "url('#x')", "URL(#x)", "var(--paint)", "red;stroke:blue", "nonsense"}) CHECK(!valid(svg(std::string("<rect fill='") + paint + "'/>")));
    for (const char* bad : {"<svg/>", "<svg width='0' height='1'/>", "<svg width='-1' height='1'/>", "<svg width='4097' height='1'/>", "<svg width='1%' height='1'/>", "<svg width='1cm' height='1'/>", "<svg viewBox='0 0 0 1'/>", "<svg viewBox='0 0 4097 1'/>", "<svg viewBox='0 0 1'/>", "<svg width='NaN' height='1'/>", "<svg width='inf' height='1'/>", "<svg width='1e309' height='1'/>", "<svg width='1' width='1' height='1'/>", "<svg width=1 height='1'/>", "<svg width='1'height='1'/>", "<svg width='1' height='1'>", "<svg width='1' height='1'></g>", "<svg:svg width='1' height='1'/>", "<svg xmlns='urn:wrong' width='1' height='1'/>", "<svg width='1' height='1'/><svg width='1' height='1'/>", "<?xml-stylesheet href='x'?><svg width='1' height='1'/>", "<?xml version='1.1'?><svg width='1' height='1'/>", "<?xml version='1.0' encoding='UTF-16'?><svg width='1' height='1'/>", "<!DOCTYPE svg><svg width='1' height='1'/>", "<!DOCTYPE svg [<!ENTITY x 'x'>]><svg width='1' height='1'/>", "text<svg width='1' height='1'/>"}) CHECK(!valid(bad));
    for (const char* body : {"<![CDATA[x]]>", "<?foo x?>", "<title>&amp;</title>", "<title>&#65;</title>", "<title><g/></title>", "<g>text</g>", "<g id='same'/><g id='same'/>", "<g id='1bad'/>", "<g id='bad space'/>", "<use href='#missing'/>", "<use xlink:href='#a'/><g id='a'/>", "<g id='a'/><use href='#a' xlink:href='#a'/>", "<use id='a' href='#a'/>", "<g id='a'><use href='#a'/></g>", "<g id='a'><use href='#b'/></g><g id='b'><use href='#a'/></g>", "<linearGradient id='a' href='#b'/><linearGradient id='b' href='#a'/>", "<mask id='a'><rect mask='url(#a)'/></mask>", "<clipPath id='a'><rect clip-path='url(#a)'/></clipPath>", "<g id='a'/><rect fill='url(#a)'/>", "<g id='a'/><rect clip-path='url(#a)'/>", "<g id='a'/><rect mask='url(#a)'/>", "<linearGradient id='a'/><use href='#a'/>", "<path d='M 0 0 Q 1'/>", "<path d='L0 0'/>", "<path d='M0'/>", "<path d='M0 0 L'/>", "<path d='M0 0,'/>", "<path d='M0 0,,1 1'/>", "<path d='M0 0 Z 1 1'/>", "<path d='M0 0 A1 1 0 2 0 2 2'/>", "<path d='M0 0 A-1 1 0 0 1 2 2'/>", "<path d='M0 0 R1 1'/>", "<path d='M0 0 L1e7 1'/>", "<rect transform='matrix(1 0 0 1 0)'/>", "<rect transform='translate(1) garbage'/>", "<rect opacity='1.1'/>", "<rect width='-1'/>", "<circle r='-1'/>", "<polygon points='0 0 1'/>", "<stop offset='101%'/>", "<stop offset='1px'/>", "<rect fill='rgb(1%,2,3)'/>", "<rect fill='rgba(0,0,0,2)'/>", "<rect stroke-dasharray='1,,2'/>", "<!-- bad -- comment -->"}) CHECK(!valid(svg(body)));
    std::string nested; for (unsigned i = 0; i < 31; ++i) nested += "<g>"; for (unsigned i = 0; i < 31; ++i) nested += "</g>";
    CHECK(valid(svg(nested))); CHECK(!valid(svg("<g>" + nested + "</g>")));
    std::string elements; for (unsigned i = 0; i < 2047; ++i) elements += "<rect/>";
    CHECK(valid(svg(elements))); CHECK(!valid(svg(elements + "<rect/>")));
    std::string many_numbers = "M0 0"; for (unsigned i = 0; i < 8192; ++i) many_numbers += "L0 0";
    CHECK(!valid(svg("<path d='" + many_numbers + "'/>")));
    CHECK(!valid(svg("<path d='M0 0" + std::string(65536, ' ') + "'/>")));
    std::string explosion = "<g id='n0'><rect/></g>";
    for (unsigned i = 1; i < 14; ++i) explosion += "<g id='n" + std::to_string(i) + "'><use href='#n" + std::to_string(i - 1) + "'/><use href='#n" + std::to_string(i - 1) + "'/></g>";
    CHECK(!valid(svg(explosion)));
    std::string chain = "<g id='n0'/>";
    for (unsigned i = 1; i <= 17; ++i) chain += "<use id='n" + std::to_string(i) + "' href='#n" + std::to_string(i - 1) + "'/>";
    CHECK(!valid(svg(chain)));
    std::string dense = "M0 0"; for (unsigned i = 0; i < 2000; ++i) dense += "L0 0";
    std::string repeated = "<path id='dense' d='" + dense + "'/>"; for (unsigned i = 0; i < 20; ++i) repeated += "<use href='#dense'/>";
    CHECK(!valid(svg(repeated)));
    std::string padded_path = "M0 0" + std::string(60000, ' ');
    repeated = "<path id='dense' d='" + padded_path + "'/>"; for (unsigned i = 0; i < 5; ++i) repeated += "<use href='#dense'/>";
    CHECK(!valid(svg(repeated)));
    CHECK(valid(svg("<rect fill='rgba(255,0,0,50%)' opacity='50%' fill-rule='inherit' stroke='inherit' stroke-width='inherit'/>")));
    CHECK(valid(svg("<rect fill='lightsteelblue' transform='translate(1 2),scale(2) rotate(20 1 1) skewX(5)'/>")));
    CHECK(valid(svg("<rect fill='red\t\r\n'/>")));
    CHECK(valid("<?xml version='1.0' encoding='uTf-8'?>" + svg("")));
    CHECK(valid(svg("<rect x='1000000' y='-1000000'/>")));
    CHECK(valid(svg("<path d='" + std::string("M0 0") + std::string(65532, ' ') + "'/>")));
    std::string normalized_path = "M0 0"; for (unsigned i = 0; i < 65532; ++i) normalized_path += "\r\n";
    CHECK(valid(svg("<path d='" + normalized_path + "'/>")));
    CHECK(!valid(svg("<path d='" + normalized_path + "\r\n'/>")));
    CHECK(!valid(svg("<path d='M0 0" + std::string(33000, ' ') + "'/><path d='M0 0" + std::string(33000, ' ') + "'/>")));
    std::string exact_numbers = "M0 0"; for (unsigned i = 0; i < 8190; ++i) exact_numbers += "L0 0";
    CHECK(valid(svg("<path d='" + exact_numbers + "'/>")));
    CHECK(!valid(svg("<path d='" + exact_numbers + " H1'/>")));
    for (const char* body : {"<path d='M0 0 A1 1 0 1.0 0 2 2'/>", "<path d='M0 0 A1 1 0 +1 0 2 2'/>",
        "<path d='M0 0 A1 1 0 0e0 0 2 2'/>", "<rect transform=''/>", "<rect transform='skewX(90)'/>",
        "<rect transform=',translate(1)'/>", "<rect transform='translate(1),'/>", "<rect transform='translate(1),,scale(1)'/>",
        "<rect transform='scale(1000000) scale(2)'/>", "<rect x='1000001'/>", "<rect x='0e309'/>", "<rect x='0e-309'/>",
        "<rect opacity='1px'/>", "<rect stroke-dasharray='0,0'/>", "<use/>", "<polygon points='0 0'/>", "<!-- &amp; -->"}) CHECK(!valid(svg(body)));
    std::string dashes = "1"; for (unsigned i = 1; i < 64; ++i) dashes += " 1";
    CHECK(valid(svg("<rect stroke-dasharray='" + dashes + "'/>")));
    CHECK(!valid(svg("<rect stroke-dasharray='" + dashes + " 1'/>")));
    CHECK(!valid(svg("<rect x='" + std::string(65, '0') + "'/>")));
    CHECK(valid(svg("<rect x='" + std::string(64, '0') + "'/>")));
    std::string exact_source = svg("<!---->");
    exact_source = svg("<!--" + std::string(svg_max_encoded_bytes - exact_source.size(), ' ') + "-->");
    CHECK(exact_source.size() == svg_max_encoded_bytes && valid(exact_source));
    CHECK(valid(svg("<g transform='scale(1000)'><g transform='scale(1000)'><rect width='1' height='1'/></g></g>")));
    CHECK(!valid(svg("<g transform='scale(1000)'><g transform='scale(1001)'><rect width='1' height='1'/></g></g>")));
    CHECK(!valid(svg("<g id='a' transform='scale(1000)'><rect width='1' height='1'/></g><use href='#a' transform='scale(1001)'/>")));
    CHECK(valid(svg("<g transform='translate(500000)'><rect transform='translate(500000)'/></g>")));
    CHECK(!valid(svg("<g transform='translate(500000)'><rect transform='translate(500001)'/></g>")));
    CHECK(!valid(svg("", "width='10' height='10' viewBox='0 0 .000001 10'")));
    CHECK(valid(svg("", "width='10' height='10' viewBox='0 0 .00001 10'")));
    CHECK(!valid(std::string(svg_max_encoded_bytes + 1, ' ')));
    CHECK(!valid(svg("<title>" + std::string(1, '\0') + "</title>")));
    CHECK(!valid(svg("<title>\xc0\xaf</title>")));
    CHECK(!valid(svg("<title>\xed\xa0\x80</title>")));
    CHECK(!valid(svg("<title>\xf4\x90\x80\x80</title>")));
    CHECK(metadata.width == 0 && metadata.height == 0 && !error.empty());
    std::puts("PASS SVG validation: static grammar, fragment references, cycles, expansion, path syntax, XML/UTF-8 and source budgets");
}
