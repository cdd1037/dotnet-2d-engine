using System.Runtime.InteropServices;
using System.Text;
namespace GameAuthoringLab;
internal sealed class InventoryModel
{
    public string Heading="Inventory / 物品",Name="Travel pack / 旅行包";
    public bool Enabled=true;
    public int Quantity=3;
    public List<UiListRow> Items=[new(10,"Rope / 绳索"),new(20,"Map <north> & compass / 地图",Selected:true),new(30,"Empty flask / 空瓶",Enabled:false)];
    public static UiBindings<InventoryModel> Bindings()=>new UiBindings<InventoryModel>().Text("heading",m=>m.Heading)
        .TextInput("name",m=>m.Name,101).Boolean("enabled",m=>m.Enabled,102).Number("quantity",m=>m.Quantity,103)
        .Action("refresh",104,m=>m.Enabled).List("items",m=>m.Items,105);
}
internal static unsafe class BoundUiTests
{
    public static int RunContracts()
    {
        int count=0;void Check(bool ok,string label){if(!ok)throw new Exception("BINDING CONTRACT: "+label);count++;}
        void Reject<T>(Action action,string label)where T:Exception{try{action();}catch(T){count++;return;}throw new Exception("BINDING CONTRACT accepted: "+label);}
        Check(sizeof(BoundTarget)==64&&sizeof(BoundValue)==288&&sizeof(BoundRow)==272&&sizeof(BoundSnapshot)==24&&sizeof(BoundAction)==304&&Marshal.OffsetOf<BoundAction>(nameof(BoundAction.Text))==48,"C ABI layouts");
        using var engine=new EngineHost(true,8);var builder=InventoryModel.Bindings();using var session=new BoundUiSession<InventoryModel>(engine,builder);var model=new InventoryModel();
        Check(session.Project(model)==3,"typed projections copy initial list");
        Reject<InvalidOperationException>(()=>new UiSession(engine),"session exclusive across profiles");
        Reject<ArgumentException>(()=>builder.Text("heading",m=>m.Name),"duplicate target");
        Reject<UiAuthoringException>(()=>new UiBindings<int>().Text("bad id",m=>""),"invalid target ID");
        Reject<UiAuthoringException>(()=>new UiBindings<int>().Text(new string('a',48),m=>""),"ID terminator capacity");
        Reject<ArgumentOutOfRangeException>(()=>new UiBindings<int>().Action("go",0),"zero action");
        var maximum=new UiBindings<int>();for(int i=0;i<32;i++)maximum.Text("t"+i,m=>"");
        Reject<ArgumentException>(()=>maximum.Text("extra",m=>""),"target limit");
        model.Items.Add(new(10,"duplicate"));Reject<ArgumentException>(()=>session.Project(model),"duplicate row IDs");model.Items.RemoveAt(3);
        model.Items.Add(new(0,"zero"));Reject<ArgumentException>(()=>session.Project(model),"zero row ID");model.Items.RemoveAt(3);
        model.Quantity=-1;Reject<ArgumentOutOfRangeException>(()=>session.Project(model),"number underflow");model.Quantity=101;Reject<ArgumentOutOfRangeException>(()=>session.Project(model),"number overflow");model.Quantity=3;
        model.Name=new string('a',65);Reject<UiAuthoringException>(()=>session.Project(model),"text scalar bound");model.Name="invalid\ud800";Reject<UiAuthoringException>(()=>session.Project(model),"invalid UTF16");model.Name="bad\ntext";Reject<UiAuthoringException>(()=>session.Project(model),"control character");model.Name="valid";
        model.Items[0]=new(10,new string('你',86));Reject<UiAuthoringException>(()=>session.Project(model),"row UTF8 capacity");model.Items[0]=new(10,new string('你',85));Check(session.Project(model)==3,"255-byte row accepted");
        model.Items=Enumerable.Range(1,65).Select(i=>new UiListRow((ulong)i,"Item")).ToList();Reject<ArgumentException>(()=>session.Project(model),"row capacity");model.Items.RemoveAt(64);Check(session.Project(model)==64,"64 rows accepted");
        var source=BoundUiAuthoring.ValidateAsset(new AssetRoot(),"ui/inventory.rml",session.Targets);Check(source.Stylesheet=="inventory.rcss","inventory reusable profile");
        byte[] rml=Encoding.UTF8.GetBytes(source.Rml),css=Encoding.UTF8.GetBytes(source.Rcss);
        void BadRml(string value)=>BoundUiAuthoring.Validate(Encoding.UTF8.GetBytes(value),css,session.Targets,"inventory.rml","inventory.rcss");
        Reject<UiAuthoringException>(()=>BadRml(source.Rml.Replace("<div id=\"items\"></div>","<div id=\"items\"><button id=\"nested\">x</button></div>",StringComparison.Ordinal)),"authored list children");
        Reject<UiAuthoringException>(()=>BadRml(source.Rml.Replace("id=\"refresh\"","id=\"refresh\" onclick=\"x\"",StringComparison.Ordinal)),"inline event");
        Reject<UiAuthoringException>(()=>BadRml(source.Rml.Replace("Travel pack / 旅行包","{{Name}}",StringComparison.Ordinal)),"implicit DSL");
        Reject<UiAuthoringException>(()=>BoundUiAuthoring.Validate(rml,Encoding.UTF8.GetBytes(source.Rcss+"\n#items { background-image: url(x); }"),session.Targets,"inventory.rml","inventory.rcss"),"external style resource");
        Reject<UiAuthoringException>(()=>session.LoadAsset(new AssetRoot(),"ui/roster.rml"),"schema mismatch before native open");
        session.Dispose();using(var roster=new BoundUiSession<List<UiListRow>>(engine,new UiBindings<List<UiListRow>>().Text("caption",_=>"Roster").List("members",m=>m,7).Action("add",8)))
            Check(BoundUiAuthoring.ValidateAsset(new AssetRoot(),"ui/roster.rml",roster.Targets).Stylesheet=="roster.rcss","different IDs/model use same API");
        Reject<ArgumentException>(()=>new BoundUiSession<int>(engine,new UiBindings<int>()),"empty schema before acquisition");
        using var afterFailure=new UiSession(engine);Check(!afterFailure.IsDisposed,"invalid constructor does not leak owner");
        Console.WriteLine($"BOUND UI CONTRACT PASS assertions={count}");return count;
    }
    public static int RunNative()
    {
        int count=0;void Check(bool ok,string label){if(!ok)throw new Exception("BOUND UI: "+label);count++;}
        void Reject<T>(Action action,string label)where T:Exception{try{action();}catch(T){count++;return;}throw new Exception("BOUND UI accepted: "+label);}
        using var engine=new EngineHost(false,16,legacyTone:false);using var ui=new BoundUiSession<InventoryModel>(engine,InventoryModel.Bindings());var model=new InventoryModel();var assets=new AssetRoot();
        void Render()=>engine.Draw(new Camera{Zoom=1},ReadOnlySpan<Sprite>.Empty);
        void Apply(){ui.Apply(model);Render();}
        ui.LoadAsset(assets,"ui/inventory.rml");Reject<InvalidOperationException>(()=>ui.Apply(model),"pending document cannot receive model");Render();engine.PollInput();
        Check(ui.State.Loaded==1&&ui.State.Pending==0,"generic document published");Check(ui.Apply(model)&&ui.Revision==1,"initial batched snapshot");Render();
        Check(ui.Probe(5,0).Text==model.Heading&&ui.Probe(5,1).Text==model.Name,"text and input projection");
        Check(ui.Probe(5,2).Boolean&&ui.Probe(5,3).Number==3,"boolean and integer projection");
        Check(ui.Probe(5,5,20).Text==model.Items[1].Text,"row punctuation preserved as plain text");
        string captures=Environment.GetEnvironmentVariable("GAL_BOUND_UI_CAPTURE_DIR")??"evidence/ui-binding/visual";Directory.CreateDirectory(captures);
        ui.Capture(Path.Combine(captures,"inventory.bmp"));Render();
        Check(!ui.Apply(model)&&ui.Revision==1,"unchanged batch skips revision and mutation");
        ui.Probe(2,5,10);var selected=ui.Poll();Check(selected.ActionId==105&&selected.RowId==10&&ui.IsCurrent(selected),"list pointer hit routes stable row ID");
        ui.Probe(1,5,30);Check(ui.Poll().IsEmpty,"disabled row cannot enqueue even direct dispatch");
        ui.Probe(2,4);var refresh=ui.Poll();Check(refresh.ActionId==104&&ui.IsCurrent(refresh),"button pointer action");
        ui.Probe(4,1,text:"Edited / 修改");var name=ui.Poll();Check(name.Text=="Edited / 修改"&&name.ActionId==101&&ui.IsCurrent(name),"typed text change copied");
        Check(!ui.Apply(model)&&ui.Probe(5,1).Text=="Edited / 修改","unchanged model leaves unaccepted draft intact");model.Name=name.Text;
        ui.Probe(4,2,boolean:false);var toggle=ui.Poll();Check(toggle.ActionId==102&&!toggle.Boolean,"typed boolean change");
        ui.Probe(4,3,number:42);var number=ui.Poll();Check(number.ActionId==103&&number.Number==42,"typed number change");model.Quantity=number.Number;
        Apply();Check(ui.Probe(5,1).Text==model.Name&&ui.Probe(5,3).Number==42,"caller accepts draft with explicit model update");
        Check(!ui.IsCurrent(selected)&&!ui.IsCurrent(refresh),"accepted model retires already-polled old revision");
        // Malformed direct C callers receive the same bounds checks before any live mutation.
        var rawValues=new BoundValue[6];for(int i=0;i<6;i++)rawValues[i]=new(){Size=(uint)sizeof(BoundValue),Target=(uint)i,Flags=1};
        var rawRows=new BoundRow[2];rawRows[0].Id=1;rawRows[1].Id=1;
        var snapshot=new BoundSnapshot{Size=(uint)sizeof(BoundSnapshot),Version=1,Generation=ui.State.Generation,Revision=ui.Revision+1,ValueCount=6};
        bool Rejected(BoundSnapshot value){fixed(BoundValue* values=rawValues)fixed(BoundRow* rows=rawRows)return BoundUiNative.Apply(engine.NativeContext,&value,values,rows)!=0;}
        snapshot.Version=99;Check(Rejected(snapshot),"native rejects snapshot version");snapshot.Version=1;
        snapshot.Revision++;Check(Rejected(snapshot),"native rejects revision gap");snapshot.Revision--;
        rawValues[3].Number=double.NaN;Check(Rejected(snapshot),"native rejects NaN before model mutation");rawValues[3].Number=0;
        rawValues[5].RowCount=2;snapshot.RowCount=2;Check(Rejected(snapshot),"native rejects duplicate row IDs");rawValues[5].RowCount=0;snapshot.RowCount=0;
        rawValues[0].Flags=8;Check(Rejected(snapshot),"native rejects unknown flags");rawValues[0].Flags=1;
        fixed(BoundValue* values=rawValues){values[0].Text[0]=0xc0;values[0].Text[1]=0x80;}Check(Rejected(snapshot),"native rejects malformed UTF8");rawValues[0]=new(){Size=(uint)sizeof(BoundValue),Flags=1};
        Check(ui.Probe(5,1).Text==model.Name&&ui.Revision+1==snapshot.Revision,"native rejection preserves live model and revision");
        BoundTarget* rawTargets=stackalloc BoundTarget[2];rawTargets[0]=new(){Size=(uint)sizeof(BoundTarget),Kind=1};rawTargets[1]=new(){Size=(uint)sizeof(BoundTarget),Kind=1};UiNative.Put(rawTargets[0].Id,48,"heading");new Span<byte>(rawTargets[1].Id,48).Fill((byte)'x');
        Check(BoundUiNative.Open(engine.NativeContext,assets.Resolve("ui/inventory.rml"),Environment.GetEnvironmentVariable("GAL_UI_FONT")??"/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc",rawTargets,2)!=0,"later unterminated target ID rejects before DOM lookup");
        Check(ui.Probe(5,1).Text==model.Name&&ui.State.Pending==0,"failed native registration retains published bindings");
        ui.Probe(1,5,10);uint revision=ui.Revision;model.Items.Add(new(10,"duplicate"));Reject<ArgumentException>(()=>ui.Apply(model),"duplicate rows transactional");
        Check(ui.Revision==revision&&ui.Poll().RowId==10&&ui.Probe(5,5,10).Text=="Rope / 绳索","invalid batch preserves live rows revision and queue");model.Items.RemoveAt(3);
        ui.Probe(1,5,10);model.Items.RemoveAt(0);Apply();Check(ui.Poll().IsEmpty,"remove clears pending stale event");
        Reject<InvalidOperationException>(()=>ui.Probe(1,5,10),"removed row absent");
        model.Items.Insert(0,new(40,"New item / 新物品"));Apply();ui.Probe(2,5,40);var added=ui.Poll();Check(added.RowId==40&&ui.IsCurrent(added),"inserted row pointer action");
        model.Items.Reverse();Apply();Check(!ui.IsCurrent(added),"reorder retires old revision");ui.Probe(1,5,40);Check(ui.Poll().RowId==40,"reordered row retains stable ID");
        model.Items[model.Items.FindIndex(r=>r.Id==40)]=new(40,"Disabled now",false);Apply();ui.Probe(1,5,40);Check(ui.Poll().IsEmpty,"row disable invalidates interaction");
        model.Enabled=false;Apply();Reject<InvalidOperationException>(()=>ui.Probe(2,4),"disabled button");model.Enabled=true;Apply();
        ui.Probe(6,5,20);model.Items=model.Items.Where(r=>r.Id!=20).ToList();Apply();ui.Probe(7,4);Check(ui.Poll().IsEmpty,"pointer release after removed pressed row cannot activate replacement");
        model.Items=[new(10,"Reinserted"),new(20,"Still stable")];Apply();ui.Probe(1,5,10);Check(ui.Poll().RowId==10,"removed ID can be reinserted under new revision");
        for(int i=0;i<65;i++)ui.Probe(1,5,10);Check(ui.State.Queued==64&&ui.State.Overflow==1,"bounded queue overflow explicit");
        int drained=0;while(!ui.Poll().IsEmpty)drained++;Check(drained==64,"queue drains exactly copied actions");
        for(int i=0;i<32;i++){model.Items=i%2==0?[]:[new(10,"Cycle")];Apply();}Check(ui.Probe(5,5,10).Text=="Cycle","repeated clear and repopulate lifecycle");
        ui.Capture(Path.Combine(captures,"mutated.bmp"));Render();
        ui.Probe(1,5,10);var beforeReload=ui.Poll();uint generation=ui.State.Generation;
        Reject<UiAuthoringException>(()=>ui.LoadAsset(assets,"ui/roster.rml"),"invalid reload");Check(ui.State.Generation==generation&&ui.IsCurrent(beforeReload),"failed reload preserves snapshot");
        ui.LoadAsset(assets,"ui/inventory.rml");Render();Check(ui.State.Generation!=generation&&!ui.IsCurrent(beforeReload),"reload invalidates action generation");
        Check(ui.Revision==0&&ui.Status.Revision==0&&ui.Poll().Revision==0,"published reload status starts at revision zero before Apply");
        Check(ui.Apply(model)&&ui.Revision==1,"new document resets revision and reapplies same model");Render();
        for(int i=0;i<100;i++)ui.Apply(model);long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1000;i++)ui.Apply(model);long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        Check(allocated==0,"unchanged warmed 1000 model batches allocate zero managed bytes");
        ui.Dispose();Reject<ObjectDisposedException>(()=>ui.Apply(model),"dispose invalidates binding API");
        using(var roster=new BoundUiSession<List<UiListRow>>(engine,new UiBindings<List<UiListRow>>().Text("caption",_=>"Party / 队伍").List("members",m=>m,7).Action("add",8))){
            roster.LoadAsset(assets,"ui/roster.rml");Render();roster.Apply([new(700,"Scout / 侦察员"),new(800,"Medic / 医疗员")]);Render();roster.Probe(2,1,800);var member=roster.Poll();
            Check(member.ActionId==7&&member.RowId==800&&roster.IsCurrent(member),"second schema/model routes through generic API");
            roster.Capture(Path.Combine(captures,"roster.bmp"));Render();ui.Dispose();Check(roster.State.Loaded==1,"old owner disposal cannot close new schema");
        }
        // Separate text bindings prove unrelated model updates preserve the active composition.
        string temp=Path.Combine(Path.GetTempPath(),"gal-bound-pair-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        try{
            File.WriteAllText(Path.Combine(temp,"pair.rml"),"<rml><head><title>Pair</title><link type=\"text/rcss\" href=\"pair.rcss\" /></head><body><input id=\"first\" type=\"text\" maxlength=\"64\" /><input id=\"second\" type=\"text\" maxlength=\"4\" /></body></rml>");
            File.WriteAllText(Path.Combine(temp,"pair.rcss"),"body { font-family: \"Noto Sans CJK SC\"; font-size: 16dp; } input { display: block; width: 200dp; height: 32dp; color: #ffffff; background-color: #172338; }");
            BoundUiSession<string[]>? nested=null;
            using var pair=nested=new BoundUiSession<string[]>(engine,new UiBindings<string[]>().TextInput("first",m=>m[0],1).TextInput("second",m=>m[1]=="fail"?throw new InvalidOperationException("projection failed"):m[1]=="nest"?(nested!.Apply(m)?"x":"y"):m[1],2));
            pair.LoadAsset(new AssetRoot(temp),"pair.rml");Render();string[] pairModel=["A","B"];pair.Apply(pairModel);Render();pair.Probe(3,0);Render();pair.Probe(8,0);engine.PollInput();Render();
            Check(pair.TextState.Composing,"first bound input accepts queued composition");
            pairModel[1]="C";pair.Apply(pairModel);Render();Check(pair.TextState.Composing,"changing unrelated text binding preserves composition");
            pairModel[1]="fail";Reject<InvalidOperationException>(()=>pair.Apply(pairModel),"projection failure before interop");Check(pair.TextState.Composing&&pair.Probe(5,1).Text=="C","projection failure preserves native snapshot");
            pairModel[1]="nest";Reject<InvalidOperationException>(()=>pair.Apply(pairModel),"projection cannot re-enter Apply");
            pairModel[1]="longer";Reject<InvalidOperationException>(()=>pair.Apply(pairModel),"authored smaller maxlength rejects oversized model");Check(pair.TextState.Composing&&pair.Probe(5,1).Text=="C","rejected text patch preserves composition and other field");pairModel[1]="C";
            pair.Probe(9,0);engine.PollInput();Render();var committed=pair.Poll();Check(committed.ActionId==1&&committed.Text.Contains('你')&&pair.IsCurrent(committed),"bound composition commits one typed action");Check(pair.Poll().IsEmpty,"one commit is one action");
            pair.Probe(8,0);engine.PollInput();Render();pairModel[0]="Updated";pair.Apply(pairModel);Render();Check(!pair.TextState.Composing&&pair.Probe(5,0).Text=="Updated","changed composing target cancels preedit before patch");
            pairModel[0]=string.Concat(Enumerable.Repeat("😀",63));pair.Apply(pairModel);Render();pair.Probe(10,0);engine.PollInput();Render();
            var overflow=pair.State;Check(pair.Poll().IsEmpty&&overflow.Overflow>0&&UiNative.Text(overflow.Diagnostic,512).Contains("255 UTF8",StringComparison.Ordinal),"over-byte-capacity committed draft reports explicit action diagnostic");
        }finally{Directory.Delete(temp,true);}
        using var late=new BoundUiSession<InventoryModel>(engine,InventoryModel.Bindings());late.LoadAsset(assets,"ui/inventory.rml");Render();late.Apply(model);Render();engine.Dispose();late.Dispose();Check(late.IsDisposed,"engine destruction invalidates document/list owner");
        Console.WriteLine($"BOUND UI NATIVE PASS assertions={count}; unchanged-batch allocations={allocated}");return count;
    }
}
internal static class BoundUiDemo
{
    public static int Run(int frames)
    {
        using var engine=new EngineHost(false,16,legacyTone:false);using var ui=new BoundUiSession<InventoryModel>(engine,InventoryModel.Bindings());
        var model=new InventoryModel();ulong nextId=100;ui.LoadAsset(new AssetRoot(),"ui/inventory.rml");engine.Draw(new Camera{Zoom=1},ReadOnlySpan<Sprite>.Empty);
        Console.WriteLine("Bound inventory: click a row to remove it; Refresh adds a stable-ID item; text/checkbox/range edit the C# model.");
        for(int frame=0;frames==0||frame<frames;frame++){
            var input=engine.PollInput();if(input.Quit!=0||input.KeyPressed(PhysicalKey.Escape))break;
            UiBindingAction action;while(!(action=ui.Poll()).IsEmpty){if(!ui.IsCurrent(action))continue;
                switch(action.ActionId){case 101:model.Name=action.Text;break;case 102:model.Enabled=action.Boolean;break;case 103:model.Quantity=action.Number;break;
                    case 104:if(model.Items.Count<64)model.Items.Add(new(nextId++,"New supply / 新补给"));break;
                    case 105:model.Items.RemoveAll(row=>row.Id==action.RowId);break;}
            }
            ui.Apply(model);engine.Draw(new Camera{Zoom=1},ReadOnlySpan<Sprite>.Empty);Thread.Sleep(1);
        }
        return 0;
    }
}
