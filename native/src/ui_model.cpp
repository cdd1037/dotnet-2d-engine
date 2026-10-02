#include "ui_model.h"
#include "ui_rml.h"
#include <RmlUi/Core/Elements/ElementFormControlInput.h>
#include <algorithm>
#include <charconv>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <limits>

// Contexts and views are confined to the engine thread. There are at most the
// current and candidate generic documents; no managed callback crosses this map.
static std::unordered_map<Rml::Context*, UiModelDocument*> model_documents;
bool gal_ui_model_can_interact(Rml::Element* element)
{
    for (auto* node = element; node; node = node->GetParentNode())
        if (!node->IsVisible() || node->HasAttribute("disabled")) return false;
    return true;
}
bool gal_ui_model_accept_attribute(Rml::DataView* view, Rml::Element* element,
    const Rml::String& name, const Rml::String& value)
{
    auto owner = model_documents.find(element->GetContext());
    return owner == model_documents.end() || owner->second->Attribute(view, element, name, value);
}
void gal_ui_model_release_view(Rml::DataView* view)
{
    for (auto& entry : model_documents) entry.second->ReleaseView(view);
}
bool UiModelDocument::Attribute(Rml::DataView* view, Rml::Element* element,
    const Rml::String& name, const Rml::String& value)
{
    auto entry = attribute_values.find(view);
    if (entry != attribute_values.end() && entry->second == value) return false;
    attribute_values[view] = value;
    before_attribute(element, name);
    return true;
}
bool UiModelDocument::HasKey(uint32_t s) const
{
    if (schema[s].kind == GAL_DATA_KEY) return true;
    if (schema[s].kind == GAL_DATA_ARRAY) return false; // Descendant rows do not identify their parent.
    for (auto child : members[s]) if (HasKey(child)) return true;
    return false;
}
bool UiModelDocument::Same(const Node& a, const Node& b) const
{
    if (std::memcmp(&a.value, &b.value, sizeof(a.value)) || a.children.size() != b.children.size()) return false;
    for (size_t i = 0; i < a.children.size(); ++i) if (!Same(a.children[i], b.children[i])) return false;
    return true;
}
bool UiModelDocument::IdentityChanged(const Node& a, const Node& b) const
{
    if (a.value.schema != b.value.schema || a.children.size() != b.children.size()) return true;
    auto kind = schema[a.value.schema].kind;
    if (kind == GAL_DATA_KEY) return a.value.key != b.value.key;
    // Unkeyed arrays have no stable identity proof; conservatively retire drafts
    // on any element change. Keyed records retain focus across scalar edits.
    if (kind == GAL_DATA_ARRAY && !HasKey(members[a.value.schema][0]) && !Same(a,b)) return true;
    for (size_t i = 0; i < a.children.size(); ++i) if (IdentityChanged(a.children[i], b.children[i])) return true;
    return false;
}

static bool bad(std::string& e, const char* s)
{
    e = s;
    return false;
}

static bool identifier(const char* p)
{
    size_t n = 0;
    while (n < 48 && p[n])
        n++;

    if (!n || n == 48 || !((p[0] >= 'a' && p[0] <= 'z') || (p[0] >= 'A' && p[0] <= 'Z')))
        return false;
    for (size_t i = 1; i < n; i++)
        if (!((p[i] >= 'a' && p[i] <= 'z') || (p[i] >= 'A' && p[i] <= 'Z') || (p[i] >= '0' && p[i] <= '9') || p[i] == '_'))
            return false;
    return true;
}

static bool zero_text(const char* p)
{
    for (size_t i = 0; i < 256; i++)
        if (p[i])
            return false;
    return true;
}

static bool key_text(const Rml::Variant& v, uint64_t& key)
{
    if (v.GetType() != Rml::Variant::STRING)
        return false;

    const auto text = v.Get<Rml::String>();
    auto parsed = std::from_chars(text.data(), text.data() + text.size(), key);
    return parsed.ec == std::errc{} && parsed.ptr == text.data() + text.size() && key != 0 && std::to_string(key) == text;
}

UiModelDocument::Definition::Definition(UiModelDocument& o, uint32_t s)
    : VariableDefinition(o.schema[s].kind == GAL_DATA_RECORD ? Rml::DataVariableType::Struct
                         : o.schema[s].kind == GAL_DATA_ARRAY ? Rml::DataVariableType::Array
                                                              : Rml::DataVariableType::Scalar),
      owner(o), schema(s)
{}

bool UiModelDocument::Definition::Get(void* p, Rml::Variant& v)
{
    const auto& n = static_cast<Node*>(p)->value;
    switch (owner.schema[schema].kind) {
    case GAL_DATA_TEXT:
        v = Rml::String(n.text);
        return true;
    case GAL_DATA_BOOL:
        v = bool(n.flags);
        return true;
    case GAL_DATA_NUMBER:
        v = n.number;
        return true;
    case GAL_DATA_KEY:
        v = std::to_string(n.key);
        return true;
    default:
        return false;
    }
}

int UiModelDocument::Definition::Size(void* p)
{
    return int(static_cast<Node*>(p)->children.size());
}

Rml::DataVariable UiModelDocument::Definition::Child(void* p, const Rml::DataAddressEntry& a)
{
    auto& n = *static_cast<Node*>(p);

    if (owner.schema[schema].kind == GAL_DATA_ARRAY) {
        if (a.name == "size")
            return Rml::MakeLiteralIntVariable(int(n.children.size()));

        if (a.index < 0 || size_t(a.index) >= n.children.size())
            return {};
        auto& child = n.children[size_t(a.index)];
        return {owner.definitions[child.value.schema].get(), &child};
    }
    for (size_t i = 0; i < owner.members[schema].size(); i++)
        if (a.name == owner.schema[owner.members[schema][i]].name)
            return {owner.definitions[n.children[i].value.schema].get(), &n.children[i]};
    return {};
}

UiModelDocument::Node UiModelDocument::Default(uint32_t s)
{
    Node n;
    n.value.schema = s;

    if (schema[s].kind == GAL_DATA_RECORD) {
        for (auto child : members[s])
            n.children.push_back(Default(child));
        n.value.children = uint32_t(n.children.size());
    }
    return n;
}

UiModelDocument::~UiModelDocument()
{
    if (document) {
        document->RemoveEventListener("mousedown", this, true);
        document->RemoveEventListener("mouseup", this, true);
        document->RemoveEventListener("keydown", this, true);
    }
    if (registered)
        context->RemoveDataModel("model");
    model_documents.erase(context);
}

bool UiModelDocument::Configure(const gal_ui_data_schema* input, uint32_t n, const gal_ui_command* cmd,
                               uint32_t nc, std::string& e)
{
    if (!input || !n || n > 128 || nc > 32 || (nc && !cmd))
        return bad(e, "generic UI requires 1..128 schema nodes and 0..32 commands");

    schema.assign(input, input + n);
    members.resize(n);

    for (uint32_t i = 0; i < n; i++) {
        const auto& s = schema[i];

        if (s.reserved || s.kind < 1 || s.kind > 6 || !identifier(s.name))
            return bad(e, "invalid generic schema kind/name/reserved");

        if (i == 0) {
            if (s.kind != GAL_DATA_RECORD || s.parent != UINT32_MAX || std::strcmp(s.name, "state"))
                return bad(e, "generic root must be record state");
        } else {
            if (s.parent >= i || schema[s.parent].kind < GAL_DATA_RECORD)
                return bad(e, "schema parent must be an earlier record/array");
            uint32_t depth = 1, p = s.parent;
            while (p != UINT32_MAX) {
                if (++depth > 16)
                    return bad(e, "schema depth exceeds 16");
                p = schema[p].parent;
            }
            for (auto sibling : members[s.parent])
                if (!std::strcmp(schema[sibling].name, s.name))
                    return bad(e, "duplicate record member");
            members[s.parent].push_back(i);
        }
        if ((s.kind == GAL_DATA_ARRAY && (s.limit < 1 || s.limit > 64)) || (s.kind != GAL_DATA_ARRAY && s.limit))
            return bad(e, "invalid array schema limit");
    }

    for (uint32_t i = 0; i < n; i++) {
        if (schema[i].kind == GAL_DATA_ARRAY && members[i].size() != 1)
            return bad(e, "array schema requires exactly one element definition");

        if (schema[i].kind == GAL_DATA_RECORD && members[i].empty())
            return bad(e, "record schema requires at least one member");
        definitions.push_back(std::make_unique<Definition>(*this, i));
    }

    for (uint32_t i = 0; i < nc; i++) {
        if (!cmd[i].id || cmd[i].count > 4 || !identifier(cmd[i].name))
            return bad(e, "invalid typed command registration");
        for (uint32_t a = 0; a < 4; a++)
            if (a < cmd[i].count ? (cmd[i].kinds[a] < 1 || cmd[i].kinds[a] > 4) : cmd[i].kinds[a] != 0)
                return bad(e, "command arguments must be scalar types");
        for (uint32_t j = 0; j < i; j++)
            if (cmd[i].id == cmd[j].id || !std::strcmp(cmd[i].name, cmd[j].name))
                return bad(e, "duplicate command ID/name");
        commands.push_back(cmd[i]);
    }

    root = Default(0);
    auto constructor = context->CreateDataModel("model");

    if (!constructor)
        return bad(e, "could not create generic data model");
    registered = true;
    model_documents[context] = this;
    handle = constructor.GetModelHandle();

    // Bind the root's stable address; snapshot commits replace its contents in place.
    if (!constructor.BindCustomDataVariable("state", {definitions[0].get(), &root}))
        return bad(e, "could not bind generic root");

    for (uint32_t i = 0; i < nc; i++)
        if (!constructor.BindEventCallback(commands[i].name,
                [this, i](Rml::DataModelHandle, Rml::Event& event, const Rml::VariantList& args) {
                    Event(i, event, args);
                }))
            return bad(e, "could not bind typed command");

    return true;
}

void UiModelDocument::Attach(Rml::ElementDocument* d)
{
    document = d;
    document->AddEventListener("mousedown", this, true);
    document->AddEventListener("mouseup", this, true);
    document->AddEventListener("keydown", this, true);
}

void UiModelDocument::Publish(uint32_t g)
{
    generation = g;
    revision = 0;
    ready = false;
    first = count = overflow = 0;
    pressed = suppress_events = false;
    diagnostic[0] = 0;
}

bool UiModelDocument::ReadNode(uint32_t s, const gal_ui_data_value* input, uint32_t n, uint32_t& cursor,
                              Node& out, std::string& e)
{
    if (cursor >= n)
        return bad(e, "snapshot ended before schema");

    const auto& v = input[cursor++];
    const auto kind = schema[s].kind;

    if (v.schema != s || v.reserved || v.flags > 1 || !std::isfinite(v.number))
        return bad(e, "invalid snapshot schema/reserved/flags/number");
    if (kind != GAL_DATA_BOOL && v.flags)
        return bad(e, "flags only valid on bool");
    if (kind != GAL_DATA_NUMBER && v.number != 0)
        return bad(e, "number only valid on number");
    if (kind != GAL_DATA_KEY && v.key)
        return bad(e, "key only valid on key");
    if (kind != GAL_DATA_TEXT && !zero_text(v.text))
        return bad(e, "text only valid on text");
    if (kind < GAL_DATA_RECORD && v.children)
        return bad(e, "scalar cannot have children");
    if (kind == GAL_DATA_NUMBER && std::abs(v.number) > 9007199254740991.0)
        return bad(e, "number outside exact integer envelope");
    if (kind == GAL_DATA_TEXT && !ui_valid_utf8(v.text, sizeof(v.text), 255))
        return bad(e, "invalid snapshot UTF8/text limit");

    if (kind == GAL_DATA_KEY) {
        if (!v.key || std::find(staged_keys.begin(), staged_keys.end(), v.key) != staged_keys.end())
            return bad(e, "keys must be nonzero and unique across snapshot");
        staged_keys.push_back(v.key);
    }
    out.value = v;

    if (kind == GAL_DATA_RECORD) {
        if (v.children != members[s].size())
            return bad(e, "record shape mismatch");
        out.children.resize(v.children);
        for (uint32_t i = 0; i < v.children; i++)
            if (!ReadNode(members[s][i], input, n, cursor, out.children[i], e))
                return false;
    } else if (kind == GAL_DATA_ARRAY) {
        if (v.children > schema[s].limit)
            return bad(e, "array exceeds registered limit");
        out.children.resize(v.children);
        for (auto& child : out.children)
            if (!ReadNode(members[s][0], input, n, cursor, child, e))
                return false;
    }
    return true;
}

bool UiModelDocument::Stage(const gal_ui_data_snapshot& s, const gal_ui_data_value* values, std::string& e)
{
    if (s.size != sizeof(s) || s.version != 1 || s.reserved || s.generation != generation ||
        revision == UINT32_MAX || s.revision != revision + 1 || !values || !s.count || s.count > 2048)
        return bad(e, "invalid or stale generic snapshot");

    staged_keys.clear();
    uint32_t cursor = 0;

    // Copy and validate the complete snapshot before any live model mutation.
    if (!ReadNode(0, values, s.count, cursor, staged, e))
        return false;
    if (cursor != s.count)
        return bad(e, "snapshot contains trailing values");
    retargeted = IdentityChanged(root, staged);
    pending_revision = s.revision;
    return true;
}

void UiModelDocument::Commit()
{
    ready = false;
    // A snapshot change during a press invalidates every command from that gesture.
    if (pressed)
        suppress_events = true;
    if (retargeted) attribute_values.clear();
    std::swap(root, staged);
    keys.swap(staged_keys);
    revision = pending_revision;
    first = count = 0;
    handle.DirtyVariable("state");
}

void UiModelDocument::ProcessEvent(Rml::Event& e)
{
    if (e.GetId() == Rml::EventId::Mousedown) {
        pressed = true;
        suppress_events = false;
    } else if (e.GetId() == Rml::EventId::Mouseup)
        pressed = false;
    else if (e.GetId() == Rml::EventId::Keydown && !pressed)
        suppress_events = false;
}

void UiModelDocument::Drop(const char* why)
{
    if (overflow != UINT32_MAX)
        overflow++;
    std::snprintf(diagnostic, sizeof(diagnostic), "%s", why);
}

void UiModelDocument::Event(uint32_t c, Rml::Event& event, const Rml::VariantList& args)
{
    if (!generation || !revision)
        return;
    if (!ready || suppress_events || !allow_event(event))
        return;

    if (!gal_ui_model_can_interact(event.GetCurrentElement())) return;

    const auto& command = commands[c];
    if (args.size() != command.count) {
        Drop("command argument count mismatch");
        return;
    }
    gal_ui_event packet{};
    packet.size = sizeof(packet);
    packet.generation = generation;
    packet.revision = revision;
    packet.command = command.id;
    packet.count = command.count;

    for (uint32_t i = 0; i < command.count; i++) {
        auto& a = packet.arguments[i];
        a.kind = command.kinds[i];
        const auto& value = args[i];

        if (a.kind == GAL_DATA_TEXT) {
            if (value.GetType() != Rml::Variant::STRING) {
                Drop("command text type mismatch");
                return;
            }
            auto text = value.Get<Rml::String>();
            if (!ui_valid_utf8(text.c_str(), 256, 255) || text.size() > 255) {
                Drop("command text exceeds UTF8 limit");
                return;
            }
            std::memcpy(a.text, text.data(), text.size());
        } else if (a.kind == GAL_DATA_KEY) {
            if (!key_text(value, a.key) || std::find(keys.begin(), keys.end(), a.key) == keys.end()) {
                Drop("command key must be an exact current snapshot key");
                return;
            }
        } else if (a.kind == GAL_DATA_BOOL) {
            if (value.GetType() != Rml::Variant::BOOL) {
                Drop("command bool type mismatch");
                return;
            }
            a.number = value.Get<bool>() ? 1 : 0;
        } else {
            if (value.GetType() != Rml::Variant::FLOAT && value.GetType() != Rml::Variant::DOUBLE &&
                value.GetType() != Rml::Variant::INT && value.GetType() != Rml::Variant::INT64) {
                Drop("command number type mismatch");
                return;
            }
            a.number = value.Get<double>();
            if (!std::isfinite(a.number) || std::abs(a.number) > 9007199254740991.0) {
                Drop("command number out of range");
                return;
            }
        }
    }
    if (count == events.size()) {
        Drop("generic UI event queue overflow (64)");
        return;
    }
    events[(first + count) % events.size()] = packet;
    count++;
}

bool UiModelDocument::Poll(gal_ui_event& v, std::string& e)
{
    if (v.size != sizeof(v))
        return bad(e, "invalid generic event size");
    v = {};
    v.size = sizeof(v);

    if (count) {
        v = events[first];
        first = (first + 1) % events.size();
        count--;
    }
    return true;
}

static Rml::Element* find_id(Rml::Element* e, const char* id, uint32_t& occurrence)
{
    if (e->GetId() == id) {
        if (!occurrence)
            return e;
        occurrence--;
    }
    for (int i = 0; i < e->GetNumChildren(); i++)
        if (auto* found = find_id(e->GetChild(i), id, occurrence))
            return found;
    return nullptr;
}

bool UiModelDocument::Test(uint32_t command, const char* id, uint32_t occurrence, gal_ui_event& v, std::string& e)
{
    if (v.size != sizeof(v) || v.generation != generation || v.revision != revision ||
        !id || std::strlen(id) > 128 || occurrence > 2048)
        return bad(e, "invalid/stale generic UI probe");

    context->Update();
    auto* element = find_id(document, id, occurrence);
    if (!element)
        return bad(e, "generic probe element not found");

    if (command == 5) {
        const auto text = element->GetInnerRML();
        if (text.size() > 255)
            return bad(e, "probe text exceeds 255 bytes");
        v.arguments[0] = {};
        std::memcpy(v.arguments[0].text, text.data(), text.size());
        return true;
    }
    if (command == 11) {
        auto* input = dynamic_cast<Rml::ElementFormControlInput*>(element);
        if (!input) return bad(e, "selection probe requires input");
        const int length = int(Rml::StringUtilities::LengthUTF8(input->GetValue()));
        input->SetSelectionRange(length, length);
        return true;
    }
    if (command == 10) {
        element->ScrollIntoView(false);
        return true;
    }
    if (command == 7) {
        element->Focus();
        return true;
    }
    if (command == 6) {
        auto* input = dynamic_cast<Rml::ElementFormControlInput*>(element);
        if (!input || !ui_valid_utf8(v.arguments[0].text, 256, 255))
            return bad(e, "probe requires input and valid text");
        input->SetValue(v.arguments[0].text);
        Rml::Dictionary parameters;
        parameters["value"] = Rml::String(v.arguments[0].text);
        element->DispatchEvent("change", parameters);
        return true;
    }
    if (command == 1) {
        element->DispatchEvent("click", {});
        return true;
    }
    if (command < 2 || command > 4)
        return bad(e, "unknown generic probe");

    const auto offset = element->GetAbsoluteOffset(Rml::BoxArea::Border);
    const auto size = element->GetBox().GetSize(Rml::BoxArea::Border);
    context->ProcessMouseMove(int(offset.x + size.x / 2), int(offset.y + size.y / 2), 0);
    if (command == 2 || command == 3)
        context->ProcessMouseButtonDown(0, 0);
    if (command == 2 || command == 4)
        context->ProcessMouseButtonUp(0, 0);
    return true;
}
