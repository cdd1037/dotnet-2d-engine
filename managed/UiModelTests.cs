using System.Runtime.InteropServices;
using System.Text;
namespace GameAuthoringLab;
internal static unsafe class UiModelTests
{
    private sealed class Item(ulong id,string title){public ulong Id=id;public string Title=title;public bool Enabled=true;}
    private sealed class Bucket(string title){public string Title=title;public List<Item> Items=[];}
    private sealed class Model {public string Title="Nested <text> & symbols";public bool Visible=true;public double Number=4;public List<Item> Items=[new(ulong.MaxValue,"Rope"),new(9007199254740993,"Map")];}
    private static UiRecord<Model> Schema()=>new UiRecord<Model>().Text("title",m=>m.Title).Boolean("visible",m=>m.Visible).Number("number",m=>m.Number)
        .Array("items",m=>m.Items,new UiRecord<Item>().Key("id",i=>i.Id).Text("title",i=>i.Title).Boolean("enabled",i=>i.Enabled));
    private static UiCommands Commands()=>new UiCommands().Add("choose",1,UiValueKind.Key).Add("edit",2,UiValueKind.Text).Add("typed",3,UiValueKind.Boolean,UiValueKind.Number,UiValueKind.Text);
    private const string Rml="""
        <rml><head><title>Generic bridge</title><link type="text/rcss" href="model.rcss"/></head><body data-model="model">
        <main><h1 id="heading">{{state.title}}</h1><section data-if="state.visible" data-attrif-disabled="state.number &gt; 50"><input id="input" type="text" data-attr-value="state.title" data-event-change="edit(ev.value)" maxlength="64"/>
        <div data-for="item : state.items"><article><p>{{item.title}}</p><button id="choose" data-attrif-disabled="!item.enabled" data-event-click="choose(item.id)"><span>Choose</span></button></article></div>
        <button id="typed" data-event-click="typed(state.visible, state.number, state.title)">Typed</button></section></main></body></rml>
        """;
    private const string Css="body { font-family: Noto Sans CJK SC; font-size: 18px; color: #ffffff; background-color: #18202b; } main { margin: 20px; width: 600px; } h1 { font-size: 24px; } article { padding: 8px; } button { width: 180px; height: 40px; background-color: #336699; } input { width: 380px; height: 35px; }";
    private sealed class Fixture:IDisposable
    {public string Root=Path.Combine(Path.GetTempPath(),"gal-ui-model-test-"+Guid.NewGuid().ToString("N"));public AssetRoot Assets;public Fixture(){Directory.CreateDirectory(Root);Assets=new(Root);Write(Rml);}public void Write(string value){File.WriteAllText(Path.Combine(Root,"model.rml"),value);File.WriteAllText(Path.Combine(Root,"model.rcss"),Css);}public void Dispose()=>Directory.Delete(Root,true);}
    public static int RunContracts()
    {
        int count=0;void Check(bool value,string label){if(!value)throw new Exception("UI MODEL CONTRACT: "+label);count++;}
        void Reject<E>(Action action,string label)where E:Exception{try{action();}catch(E){count++;return;}throw new Exception("UI MODEL accepted: "+label);}
        Check(sizeof(ModelSchema)==64&&sizeof(ModelCommand)==72&&sizeof(ModelValue)==288&&sizeof(ModelSnapshot)==24&&sizeof(ModelArgument)==280&&sizeof(ModelEvent)==1144,"ABI sizes");
        Check(Marshal.OffsetOf<ModelValue>(nameof(ModelValue.Text))==32&&Marshal.OffsetOf<ModelEvent>(nameof(ModelEvent.Arguments))==24,"ABI offsets");
        using var engine=new EngineHost(true,8);var schema=Schema();using var ui=new UiModelSession<Model>(engine,schema,Commands());var model=new Model();
        Check(ui.Project(model)==13,"nested full projection");schema.Text("later",m=>"ignored");Check(ui.Project(model)==13,"schema frozen");
        Reject<InvalidOperationException>(()=>new UiSession(engine),"exclusive UI ownership");
        model.Items[1].Id=model.Items[0].Id;Reject<UiAuthoringException>(()=>ui.Project(model),"duplicate identity");model.Items[1].Id=9007199254740993;
        model.Items[1].Id=0;Reject<UiAuthoringException>(()=>ui.Project(model),"zero identity");model.Items[1].Id=9007199254740993;
        model.Number=double.NaN;Reject<UiAuthoringException>(()=>ui.Project(model),"nonfinite number");model.Number=4;
        model.Title="bad\ud800";Reject<UiAuthoringException>(()=>ui.Project(model),"invalid UTF16");model.Title=new string('你',86);Reject<UiAuthoringException>(()=>ui.Project(model),"UTF8 capacity");model.Title="ok";
        model.Items=Enumerable.Range(0,65).Select(i=>new Item((ulong)i+1,"x")).ToList();Reject<UiAuthoringException>(()=>ui.Project(model),"bounded array");
        var data=UiModelAuthoring.Validate(Encoding.UTF8.GetBytes(Rml),Encoding.UTF8.GetBytes(Css),["choose","edit","typed"]);Check(data.Rml.Contains("article"),"arbitrary nested elements and bindings");
        foreach(string bad in new[]{Rml.Replace("choose(item.id)","choose(item.id); edit('x')"),Rml.Replace("choose(item.id)","unknown(item.id)"),Rml.Replace("data-attr-value","data-value"),Rml.Replace("<main>","<main><script>x</script>"),Rml.Replace("<main>","<main><img src='https://x/y.png'/>"),Rml.Replace("<main>","<main><p data-rml='state.title'/>"),Rml.Replace("<main>","<main rmlui-inner-rml='x'>"),Rml.Replace("<main>","<main data-attr-rmlui-inner-rml='state.title'>")})Reject<UiAuthoringException>(()=>UiModelAuthoring.Validate(Encoding.UTF8.GetBytes(bad),Encoding.UTF8.GetBytes(Css),["choose","edit","typed"]),"resource/code/command rejection");
        Reject<UiAuthoringException>(()=>UiModelAuthoring.Validate(Encoding.UTF8.GetBytes(Rml),Encoding.UTF8.GetBytes("@import 'external.rcss';"),["choose","edit","typed"]),"external CSS");
        ui.Dispose();Reject<ArgumentException>(()=>new UiModelSession<Model>(engine,new UiRecord<Model>()),"empty schema before ownership");
        var recursive=new UiRecord<Model>();recursive.Record("self",m=>m,recursive);Reject<UiAuthoringException>(()=>new UiModelSession<Model>(engine,recursive),"cyclic schema bounded before ownership");
        UiModelSession<Model>? reentrant=null;
        using(var busy=reentrant=new UiModelSession<Model>(engine,new UiRecord<Model>().Text("title",m=>{reentrant!.Apply(m);return m.Title;})))Reject<InvalidOperationException>(()=>busy.Apply(new Model()),"projection reentry rejected");
        using(var busy=reentrant=new UiModelSession<Model>(engine,new UiRecord<Model>().Text("title",m=>{reentrant!.Dispose();return m.Title;}))){Reject<InvalidOperationException>(()=>busy.Apply(new Model()),"projection cannot dispose active owner");Check(!busy.IsDisposed,"reentrant disposal leaves owner alive");}
        using(var scalar=new UiModelSession<IReadOnlyList<string>>(engine,new UiRecord<IReadOnlyList<string>>().Array("words",m=>m,UiData.Text,4))){Check(scalar.Project(new[]{"one","two"})==4,"scalar array projection");Reject<InvalidOperationException>(()=>Task.Run(()=>scalar.Apply(new[]{"one"})).GetAwaiter().GetResult(),"owning thread");}
        using var after=new UiSession(engine);Check(!after.IsDisposed,"ownership after failed schema");
        count+=UiErgonomicsTests.RunContracts();
        Console.WriteLine($"UI MODEL CONTRACT PASS assertions={count}");return count;
    }
    public static int RunNative()
    {
        int count=0;void Check(bool value,string label){if(!value)throw new Exception("UI MODEL NATIVE: "+label);count++;}
        void Reject<E>(Action action,string label)where E:Exception{try{action();}catch(E){count++;return;}throw new Exception("UI MODEL NATIVE accepted: "+label);}
        using var fixture=new Fixture();using var engine=new EngineHost(false,16,legacyTone:false);using var ui=new UiModelSession<Model>(engine,Schema(),Commands());var model=new Model();
        void Render()=>engine.Draw(new Camera{Zoom=1},ReadOnlySpan<Sprite>.Empty);
        ui.LoadAsset(fixture.Assets,"model.rml");Reject<InvalidOperationException>(()=>ui.Apply(model),"pending apply rejected");Render();engine.PollInput();Check(ui.Status.Loaded&&!ui.Status.Pending,"staged publication");Check(ui.Apply(model),"first apply");Render();
        Check(ui.Probe(5,"heading").Contains("&lt;text&gt;"),"plain text escapes model markup");
        ui.Probe(2,"choose");var selected=ui.Poll();Check(selected.CommandId==1&&selected[0].Key==ulong.MaxValue&&ui.IsCurrent(selected),"pointer click exact uint64 key");
        ui.Probe(1,"choose",1);var second=ui.Poll();Check(second[0].Key==9007199254740993,"key above double precision");
        ui.Probe(1,"typed");var typed=ui.Poll();Check(typed.Count==3&&typed[0].Boolean&&typed[1].Number==4&&typed[2].Text==model.Title,"typed command packet");
        ui.Probe(6,"input",value:"Edited <safe>");var edit=ui.Poll();Check(edit.CommandId==2&&edit[0].Text=="Edited <safe>"&&model.Title!=edit[0].Text,"copied input draft leaves C# authoritative");
        Check(!ui.Apply(model),"unchanged skips native call");
        model.Title="Accepted";ui.Apply(model);Render();Check(!ui.IsCurrent(selected),"previously polled event stale");
        ui.Probe(3,"choose");model.Items.Reverse();ui.Apply(model);Render();ui.Probe(4,"choose");Check(ui.Poll().IsEmpty,"pointer-down reorder cannot select reused row");
        ui.Probe(2,"choose");Check(ui.Poll()[0].Key==9007199254740993,"fresh gesture uses reordered key");
        model.Items[0].Enabled=false;ui.Apply(model);Render();ui.Probe(1,"choose");Check(ui.Poll().IsEmpty,"disabled command rejected");model.Items[0].Enabled=true;ui.Apply(model);Render();
        ui.Probe(1,"choose");var revision=ui.Revision;model.Items[1].Id=model.Items[0].Id;Reject<UiAuthoringException>(()=>ui.Apply(model),"invalid snapshot projection");Check(ui.Revision==revision&&!ui.Poll().IsEmpty,"projection failure preserves revision/queue");model.Items[1].Id=ulong.MaxValue;
        for(int i=0;i<70;i++)ui.Probe(1,"choose");Check(ui.Status.Queued==64&&ui.Status.Overflow>=6,"bounded event overflow");while(!ui.Poll().IsEmpty){}
        fixture.Write(Rml.Replace("state.title","state.unknown"));Reject<UiAuthoringException>(()=>ui.LoadAsset(fixture.Assets,"model.rml"),"invalid binding preflight reload");Check(ui.Status.Loaded&&ui.Revision==revision,"failed reload retains live model");fixture.Write(Rml);
        ui.Probe(1,"choose");var old=ui.Poll();ui.LoadAsset(fixture.Assets,"model.rml");Render();ui.Apply(model);Render();Check(!ui.IsCurrent(old),"reload generation invalidation");
        for(int i=0;i<20;i++){model.Items.Reverse();ui.Apply(model);Render();ui.Probe(2,"choose");Check(ui.Poll()[0].Key==model.Items[0].Id,"repeated replacement and key routing");}
        ulong calls=ui.NativeApplyCalls;for(int i=0;i<200;i++)ui.Apply(model);long start=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1000;i++)ui.Apply(model);long allocations=GC.GetAllocatedBytesForCurrentThread()-start;Check(ui.NativeApplyCalls==calls&&allocations==0,"warmed unchanged no allocation or native mutation crossing");
        // Invalid native batches leave the accepted snapshot and queued events untouched.
        var definitions=new List<ModelSchema>();var projection=Schema().Compile("state",uint.MaxValue,definitions,1);var writer=new UiModelWriter();projection(model,writer);
        ModelSnapshot raw=new(){Size=(uint)sizeof(ModelSnapshot),Version=1,Generation=ui.Status.Generation,Revision=ui.Revision+1,Count=(uint)writer.Count};
        bool Rejected(ModelSnapshot value){fixed(ModelValue* data=writer.Values)return UiModelNative.Apply(engine.NativeContext,&value,data)!=0;}
        ui.Probe(1,"choose");var wrong=raw;wrong.Revision++;Check(Rejected(wrong),"native revision skip");wrong=raw;wrong.Version=2;Check(Rejected(wrong),"native snapshot version");wrong=raw;wrong.Count=2049;Check(Rejected(wrong),"native snapshot capacity");
        writer.Values[1].Schema=127;Check(Rejected(raw),"native field/schema mismatch");writer.Reset();projection(model,writer);writer.Values[1].Flags=1;Check(Rejected(raw),"native scalar payload type");writer.Reset();projection(model,writer);writer.Values[1].Text[0]=0xff;Check(Rejected(raw),"native invalid UTF8");Check(!ui.Poll().IsEmpty,"native rejection preserves queue");
        string font=Environment.GetEnvironmentVariable("GAL_UI_FONT")??"/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc";
        Check(UiModelNative.Open(engine.NativeContext,fixture.Assets.Resolve("model.rml"),font,fixture.Assets.Resolve("model.rcss"),null,1,null,0,null,0)!=0,"null generic schema cannot fall through to legacy profile");
        Check(ui.Revision==raw.Revision-1,"invalid open retains live model");
        // Exercise the native source gate independently of managed preflight.
        fixture.Write("<rml><head><title>File gate</title><link type='text/rcss' href='model.rcss'/></head><body data-model='model'><p>{{state.title}}</p></body></rml>");
        File.WriteAllText(Path.Combine(fixture.Root,"outside.rcss"),"body { color: #ff0000; }");File.WriteAllText(Path.Combine(fixture.Root,"model.rcss"),"@import \"outside.rcss\"; "+Css);
        var rawSchema=definitions.ToArray();fixed(ModelSchema* shape=rawSchema)Check(UiModelNative.Open(engine.NativeContext,fixture.Assets.Resolve("model.rml"),font,fixture.Assets.Resolve("model.rcss"),shape,(uint)rawSchema.Length,null,0,null,0)!=0,"native exact-source gate rejects extra stylesheet");Check(ui.Revision==raw.Revision-1,"denied file read retains live document");fixture.Write(Rml);

        // A mouseup-bound command must also be invalidated across a positional reorder.
        fixture.Write(Rml.Replace("data-event-click=\"choose(item.id)\"","data-event-mouseup=\"choose(item.id)\""));ui.LoadAsset(fixture.Assets,"model.rml");Render();ui.Apply(model);Render();
        ui.Probe(3,"choose");model.Items.Reverse();ui.Apply(model);Render();ui.Probe(4,"choose");Check(ui.Poll().IsEmpty,"mouseup callback cannot bypass gesture invalidation");ui.Probe(2,"choose");Check(ui.Poll()[0].Key==model.Items[0].Id,"new gesture restores commands");
        ui.Probe(7,"input");ui.Probe(11,"input");engine.PollInput();
        for(int i=0;i<3;i++){ui.Probe(12,"input");engine.PollInput();var typedEdit=ui.Poll();Check(typedEdit.CommandId==2&&typedEdit[0].Text.EndsWith('x'),"continuous SDL edit packet");model.Title=typedEdit[0].Text;ui.Apply(model);Render();Check((ui.TextState.Flags&1)!=0,"accepted keystroke retains text focus");}
        ui.Probe(12,"input");ui.Probe(12,"input");engine.PollInput();var burstFirst=ui.Poll();var burstLast=ui.Poll();Check(burstFirst.CommandId==2&&burstLast.CommandId==2&&ui.IsCurrent(burstFirst)&&ui.IsCurrent(burstLast),"two text packets in one native input frame");model.Title=burstLast[0].Text;ui.Apply(model);Render();Check(model.Title.EndsWith("xx",StringComparison.Ordinal)&&(ui.TextState.Flags&1)!=0,"drain-before-apply retains final burst edit and focus");
        ui.Probe(6,"input",value:"Unaccepted draft");_=ui.Poll();ui.Probe(11,"input");var draftBefore=ui.TextState;model.Number++;ui.Apply(model);Render();var draftAfter=ui.TextState;
        Check(UiNative.Text(draftAfter.Value,512)=="Unaccepted draft"&&draftAfter.SelectionStart==draftBefore.SelectionStart&&(draftAfter.Flags&1)!=0,"unrelated scalar update preserves draft/caret/focus");
        ui.Probe(8,"input");engine.PollInput();var preedit=ui.TextState;Check((preedit.Flags&2)!=0,"generic queued preedit active");Check(ui.Poll().IsEmpty,"preedit is not an accepted change command");
        model.Number++;ui.Apply(model);Render();var afterUnrelated=ui.TextState;Check((afterUnrelated.Flags&2)!=0&&UiNative.Text(afterUnrelated.Value,512)==UiNative.Text(preedit.Value,512),"unrelated scalar update preserves active preedit");
        model.Title="Composition replacement";ui.Apply(model);Render();var replaced=ui.TextState;Check((replaced.Flags&2)==0&&(replaced.Flags&1)!=0&&UiNative.Text(replaced.Value,512)==model.Title,"external bound value cancels preedit and wins without dropping focus");
        ui.Probe(8,"input");engine.PollInput();model.Visible=false;ui.Apply(model);Render();Check((ui.TextState.Flags&3)==0,"hidden ancestor retires input focus/preedit");ui.Probe(12,"input");engine.PollInput();Check(ui.Poll().IsEmpty,"hidden input cannot accept queued text");model.Visible=true;ui.Apply(model);Render();
        ui.Probe(8,"input");engine.PollInput();model.Number=99;ui.Apply(model);Render();Check((ui.TextState.Flags&3)==0,"disabled ancestor retires input focus/preedit");model.Number=4;ui.Apply(model);Render();
        ui.Probe(8,"input");engine.PollInput();model.Items.Reverse();ui.Apply(model);Render();Check((ui.TextState.Flags&3)==0,"identity reorder cancels preedit and focus");

        ui.Dispose();count+=RunExamples(engine);count+=RunUnkeyedParent(engine);count+=UiErgonomicsTests.RunNative(engine);
        using var late=new UiModelSession<Model>(engine,Schema(),Commands());fixture.Write(Rml);late.LoadAsset(fixture.Assets,"model.rml");Render();late.Apply(model);Render();
        engine.Dispose();Check(late.IsDisposed,"engine-first invalidates owner");ui.Dispose();Console.WriteLine($"UI MODEL NATIVE PASS assertions={count} unchanged_bytes={allocations}");return count;
    }
    private static int RunExamples(EngineHost engine)
    {
        int count=0;void Check(bool value,string label){if(!value)throw new Exception("UI MODEL EXAMPLE: "+label);count++;}
        void Render()=>engine.Draw(new Camera{Zoom=1},ReadOnlySpan<Sprite>.Empty);
        var assets=new AssetRoot();string captures=Environment.GetEnvironmentVariable("GAL_MODEL_UI_CAPTURE_DIR")??"evidence/ui-model/visual";Directory.CreateDirectory(captures);
        using(var ui=new UiModelSession<UiModelExamples.InventoryModel>(engine,UiModelExamples.InventorySchema(),UiModelExamples.InventoryCommands())){
            var model=UiModelExamples.Inventory();ui.LoadAsset(assets,UiModelExamples.InventoryAsset,UiModelExamples.InventoryImages);Render();ui.Apply(model);Render();ui.Capture(Path.Combine(captures,"inventory.bmp"));Render();
            ui.Probe(2,"equip",1);var equip=ui.Poll();Check(equip.CommandId==UiModelExamples.Equip&&equip[0].Key==model.Items[1].Id,"inventory second nested action");Check(ui.IsCurrent(equip)&&UiModelExamples.Handle(model,equip),"C# owns equipment change");ui.Apply(model);Render();
            ui.Probe(2,"drop");var drop=ui.Poll();Check(drop.CommandId==UiModelExamples.Drop&&UiModelExamples.Handle(model,drop),"inventory two independent commands");ui.Apply(model);Render();Check(model.Items.Count==2,"inventory remove");
            model.Items.Reverse();ui.Apply(model);Render();ui.Probe(2,"equip");Check(ui.Poll()[0].Key==model.Items[0].Id,"inventory card reorder key");
        }
        using(var ui=new UiModelSession<UiModelExamples.DialogueModel>(engine,UiModelExamples.DialogueSchema(),UiModelExamples.DialogueCommands())){
            var model=UiModelExamples.Dialogue();ui.LoadAsset(assets,UiModelExamples.DialogueAsset);Render();ui.Apply(model);Render();ui.Capture(Path.Combine(captures,"dialogue.bmp"));Render();
            ui.Probe(2,"choice");var choice=ui.Poll();Check(choice.CommandId==UiModelExamples.Choose&&choice[1].Text==model.Choices[0].Text,"dialogue key and text arguments");UiModelExamples.Handle(model,choice);ui.Apply(model);Render();Check(!model.Conversation.Message.ShowAside,"dialogue conditional authored subtree");ui.Probe(1,"choice",2);Check(ui.Poll().IsEmpty,"unavailable dialogue choice");
        }
        using(var ui=new UiModelSession<UiModelExamples.SettingsModel>(engine,UiModelExamples.SettingsSchema(),UiModelExamples.SettingsCommands())){
            var model=UiModelExamples.Settings();ui.LoadAsset(assets,UiModelExamples.SettingsAsset);Render();ui.Apply(model);Render();ui.Capture(Path.Combine(captures,"settings.bmp"));Render();
            ui.Probe(6,"player-name",value:"New explorer");var rename=ui.Poll();Check(rename.CommandId==UiModelExamples.Rename&&rename[0].Text=="New explorer","settings text draft");UiModelExamples.Handle(model,rename);ui.Apply(model);Render();
            ui.Probe(6,"volume",value:"37");var volume=ui.Poll();Check(volume.CommandId==UiModelExamples.SetVolume&&volume[0].Number==37,"settings numeric command");UiModelExamples.Handle(model,volume);ui.Apply(model);Render();
            ui.Probe(10,"option-toggle",3);Render();ui.Probe(2,"option-toggle",3);var option=ui.Poll();Check(option.CommandId==UiModelExamples.SetOption&&option[0].Key==model.Groups[1].Options[1].Id&&!option[1].Boolean,"nested array checkbox key and bool");UiModelExamples.Handle(model,option);ui.Apply(model);Render();
            model.Groups.Reverse();model.Groups[0].Options.Reverse();ui.Apply(model);Render();ui.Probe(10,"option-toggle");Render();ui.Probe(2,"option-toggle");Check(ui.Poll()[0].Key==model.Groups[0].Options[0].Id,"outer and inner reorder");
            model.Groups[0].Options.Clear();ui.Apply(model);Render();ui.Probe(10,"option-toggle");Render();ui.Probe(2,"option-toggle");Check(ui.Poll()[0].Key==model.Groups[1].Options[0].Id,"nested clear preserves remaining identity");
            model.Groups.Clear();ui.Apply(model);Render();Check(ui.Poll().IsEmpty,"empty nested arrays");
        }
        Console.WriteLine($"UI MODEL THREE EXAMPLES PASS assertions={count} captures={captures}");return count;
    }

    private static int RunUnkeyedParent(EngineHost engine)
    {
        using var fixture=new Fixture();fixture.Write("<rml><head><title>Nested identity</title><link type='text/rcss' href='model.rcss'/></head><body data-model='model'><div data-for='group : state.groups'><input id='draft' type='text' data-attr-value='group.title'/></div></body></rml>");
        var schema=new UiRecord<List<Bucket>>().Array("groups",m=>m,new UiRecord<Bucket>().Text("title",m=>m.Title).Array("items",m=>m.Items,new UiRecord<Item>().Key("id",m=>m.Id)));
        using var ui=new UiModelSession<List<Bucket>>(engine,schema);var model=new List<Bucket>{new("one"),new("two")};
        void Render()=>engine.Draw(new Camera{Zoom=1},ReadOnlySpan<Sprite>.Empty);
        ui.LoadAsset(fixture.Assets,"model.rml");Render();ui.Apply(model);Render();ui.Probe(8,"draft");engine.PollInput();if((ui.TextState.Flags&2)==0)throw new Exception("Nested identity preedit did not start");
        model.Reverse();ui.Apply(model);Render();if((ui.TextState.Flags&3)!=0)throw new Exception("Nested child-key schema incorrectly identified an unkeyed parent");
        return 2;
    }

}
