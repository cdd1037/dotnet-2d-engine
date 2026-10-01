using System.Runtime.InteropServices;
namespace GameAuthoringLab;
internal static unsafe class UiTextTests
{
    public static int RunContracts()
    {
        int count=0;void Check(bool ok,string label){if(!ok)throw new Exception("UI OWNER: "+label);count++;}
        void Reject<T>(Action action,string label)where T:Exception{try{action();}catch(T){count++;return;}throw new Exception("UI OWNER accepted: "+label);}
        Check(sizeof(UiTextState)==832&&Marshal.OffsetOf<UiTextState>(nameof(UiTextState.Value))==64,"text-state C layout");
        using var engine=new EngineHost(true,8);using var first=new UiSession(engine);
        Reject<InvalidOperationException>(()=>new UiSession(engine),"duplicate settings owner");
        Reject<InvalidOperationException>(()=>new GameUiSession(engine),"cross-profile owner");
        first.Dispose();using var second=new GameUiSession(engine);first.Dispose();
        Reject<InvalidOperationException>(()=>new UiSession(engine),"old disposal cannot release newer owner");
        Exception? error=null;var thread=new Thread(()=>{try{second.Dispose();}catch(Exception e){error=e;}});thread.Start();thread.Join();
        Check(error is InvalidOperationException&&!second.IsDisposed,"wrong-thread disposal preserves owner");
        second.Dispose();using var failed=new UiSession(engine);
        Reject<InvalidOperationException>(()=>failed.LoadAsset(new AssetRoot(),"ui/settings.rml"),"headless UI open unsupported");
        failed.Dispose();Check(failed.IsDisposed,"failed unopened session releases ownership safely");
        using var late=new UiSession(engine);engine.Dispose();Check(late.IsDisposed,"engine invalidates UI owner");late.Dispose();
        Reject<ObjectDisposedException>(()=>{_=late.State;},"post-engine access");
        Console.WriteLine($"UI OWNER CONTRACT PASS assertions={count}");return count;
    }
    public static int RunComposition()
    {
        int count=0;void Check(bool ok,string label){if(!ok)throw new Exception("UI TEXT: "+label);count++;}
        void Reject<T>(Action action,string label)where T:Exception{try{action();}catch(T){count++;return;}throw new Exception("UI TEXT accepted: "+label);}
        using var engine=new EngineHost(false,16,legacyTone:false);using var ui=new UiSession(engine);var root=new AssetRoot();
        void Render()=>engine.Draw(new Camera{Zoom=1},ReadOnlySpan<Sprite>.Empty);
        ui.LoadAsset(root,"ui/settings.rml");Render();uint generation=ui.State.Generation;engine.PollInput();
        string Value(){var state=ui.TextState;return UiNative.Text(state.Value,512);}
        InputSnapshot Send(uint command){ui.Command(generation,command);var input=engine.PollInput();Render();return input;}
        void Focus(string text){ui.Set(generation,text,65,"Composition fixture");ui.Command(generation,3);ui.Command(generation,28);Render();while(ui.Poll().Action!=0){};}
        Focus("A");var state=ui.TextState;
        Check((state.Flags&1)!=0&&state.TextActive&&state.AreaWidth==1&&state.AreaHeight>0&&state.Failures==0,"focused text activates SDL and candidate geometry");
        Check((state.Flags&128)!=0,"SDL is told the application renders composition while OS owns candidates");
        Check(state.AreaX==(int)Math.Floor(state.CaretX)&&state.AreaY==(int)Math.Floor(state.CaretY),"1x candidate anchor agrees with Rml caret pixels");
        Focus("WWWW");int wideCaret=ui.TextState.AreaX;ui.Set(generation,"iiii",65,"Narrow glyph model");Render();
        Check(ui.TextState.AreaX<wideCaret&&ui.TextState.SelectionEnd==4,"same-bounds model replacement refreshes the actual caret after layout");
        Focus("A");var input=Send(19);state=ui.TextState;
        Check(state.Composing&&state.PreeditScalars==2&&Value()=="Ani","queued preedit enters Rml through upstream editor");
        Check((input.Consumed&InputConsumption.Text)!=0,"preedit consumed by UI");
        Send(20);state=ui.TextState;Check(Value()=="A你好"&&state.SelectionStart==2&&state.SelectionEnd==3,"multibyte preedit replacement and scalar selection");
        string captures=Environment.GetEnvironmentVariable("GAL_UI_TEXT_CAPTURE_DIR")??"evidence/ui-text/visual";Directory.CreateDirectory(captures);
        ui.Capture(Path.Combine(captures,"preedit.bmp"));Render();
        Send(21);Check(!ui.TextState.Composing&&Value()=="A","empty editing event restores original text before commit");
        input=Send(22);Check(Value()=="A你"&&!ui.TextState.Composing&&(input.Consumed&InputConsumption.Text)!=0,"committed text inserted once after empty preedit");
        int changes=0;UiAction action;while((action=ui.Poll()).Action!=0)if(action.Action==3)changes++;
        Check(changes==1,"preedit does not publish committed change events");
        Send(19);Send(22);Check(Value()=="A你你"&&!ui.TextState.Composing,"direct commit without empty-edit packet also replaces preedit");
        Focus("escape");Send(19);input=Send(29);Check(!ui.TextState.Composing&&Value()=="escape"&&input.KeyPressed(PhysicalKey.Escape,true)&&!input.KeyPressed(PhysicalKey.Escape),"composition Escape cancels without reaching gameplay");
        Focus("ABCD");ui.Command(generation,24);Send(19);Check(Value()=="AniD","preedit replaces selected original range");
        Send(21);Check(Value()=="ABCD"&&ui.TextState.SelectionStart==1&&ui.TextState.SelectionEnd==3,"cancel restores original selected text and selection");
        Send(20);Send(22);Check(Value()=="A你D","commit replaces original selection, not provisional characters");
        Focus("stable");Send(19);uint failures=ui.TextState.Failures;
        Send(25);Send(26);Send(27);Check(ui.TextState.Failures==failures+3&&Value()=="stableni"&&ui.TextState.Composing,"malformed/bounds editing events retain last accepted preedit");
        ui.Set(generation,"model",65,"External patch");Render();Check(Value()=="model"&&!ui.TextState.Composing,"explicit model replacement cancels composition");
        Send(19);Send(23);Check(!ui.TextState.Composing&&!ui.TextState.TextActive&&ui.State.KeyboardFocus==0,"blur cancels and deactivates text input");
        ui.Command(generation,3);ui.Command(generation,28);Render();Check(Value()=="model","refocus has no provisional residue");
        Send(20);Send(17);Check(!ui.TextState.Composing&&!ui.TextState.TextActive&&(ui.TextState.Flags&8)==0&&Value()=="model","window focus loss cancels preedit and stops SDL input");
        Send(19);Check(Value()=="model"&&!ui.TextState.Composing,"text arriving while unfocused is ignored");
        Send(18);Check(ui.TextState.TextActive,"focus restoration resumes existing field without old preedit");
        Send(19);Send(13);Check(ui.TextState.AreaX>=0&&ui.TextState.AreaX<384&&ui.TextState.AreaY<288,"resize updates/clamps candidate rectangle");
        Send(15);Check(!ui.TextState.TextActive&&!ui.TextState.Composing,"minimize cancels input composition");
        Send(17);Send(16);Check(!ui.TextState.TextActive&&(ui.TextState.Flags&8)==0,"restore does not fabricate window focus");
        Send(18);Send(14);Check(ui.TextState.TextActive,"focus regain refreshes restored text input area");
        Focus(new string('A',32));Send(19);Check(Value().Length==34,"preedit can temporarily exceed committed field length");Send(22);Check(Value()==new string('A',32),"commit respects Rml field maximum length");
        Focus("failed");Send(19);
        string badDirectory=Path.Combine(Path.GetTempPath(),"gal-ime-bad-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(badDirectory);
        try
        {
            string bad=Path.Combine(badDirectory,"settings.rml");File.WriteAllText(bad,File.ReadAllText(root.Resolve("ui/settings.rml")));
            File.WriteAllText(Path.Combine(badDirectory,"settings.rcss"),File.ReadAllText(root.Resolve("ui/settings.rcss"))+"\n#status { made-up-property: 1; }\n");
            Reject<UiAuthoringException>(()=>ui.Load(bad),"managed rejected reload");
            Check(UiNative.Open(engine.NativeContext,bad,Environment.GetEnvironmentVariable("GAL_UI_FONT")??"/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc")!=0,"native rejected reload");
            Check(ui.State.Generation==generation&&ui.TextState.Composing&&ui.TextState.TextActive&&Value()=="failedni","failed reload preserves live composition and generation");
            File.WriteAllText(bad,File.ReadAllText(root.Resolve("ui/settings.rml")).Replace("id=\"player-name\"","id=\"player-name\" autofocus=\"true\"",StringComparison.Ordinal));
            File.WriteAllText(Path.Combine(badDirectory,"settings.rcss"),File.ReadAllText(root.Resolve("ui/settings.rcss")));
            Native.Check(UiNative.Open(engine.NativeContext,bad,Environment.GetEnvironmentVariable("GAL_UI_FONT")??"/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc"),"native autofocus candidate");
            Check(ui.State.Pending==1&&ui.TextState.Composing&&Value()=="failedni","staging autofocus cannot take live text context");
            Render();generation=ui.State.Generation;Check(!ui.TextState.Composing&&!ui.TextState.TextActive,"publication retires old IME and focuses the document explicitly");
        }
        finally{Directory.Delete(badDirectory,true);}
        Focus("reload");Send(19);ui.LoadAsset(root,"ui/settings.rml");Render();generation=ui.State.Generation;
        Check(!ui.TextState.Composing&&!ui.TextState.TextActive&&ui.State.KeyboardFocus==0,"document replacement retires text context");
        Focus("new");Send(20);Check(Value()=="new你好","new document has no stale upstream composition offsets");
        ui.Capture(Path.Combine(captures,"new-context.bmp"));Render();
        ui.Command(generation,22);ui.LoadAsset(root,"ui/settings.rml");Render();generation=ui.State.Generation;
        Focus("fresh");engine.PollInput();Render();Check(Value()=="fresh","queued text from retired document cannot enter newly focused document");
        ui.Command(generation,22);ui.Dispose();
        using(var replacement=new UiSession(engine))
        {
            replacement.LoadAsset(root,"ui/settings.rml");Render();uint fresh=replacement.State.Generation;
            replacement.Set(fresh,"after-close",65,"Fresh owner");replacement.Command(fresh,3);replacement.Command(fresh,28);engine.PollInput();Render();
            var replacementState=replacement.TextState;Check(UiNative.Text(replacementState.Value,512)=="after-close","queued text from closed owner cannot enter replacement owner");
        }
        using var newer=new GameUiSession(engine);newer.LoadAsset(root,"ui/game.rml");Render();
        ui.Dispose();Check(newer.State.Loaded==1&&!newer.TextState.TextActive,"old wrapper cannot close new profile");
        Reject<InvalidOperationException>(()=>new UiSession(engine),"duplicate owner leaves game UI intact");
        var raw=new UiTextState{Size=(uint)sizeof(UiTextState),Version=99};Check(UiNative.TextState(engine.NativeContext,&raw)!=0,"text-state version rejected");
        newer.Dispose();using var late=new UiSession(engine);late.LoadAsset(root,"ui/settings.rml");Render();late.Command(late.State.Generation,3);Render();
        engine.Dispose();late.Dispose();Check(late.IsDisposed,"engine-first disposal while text active is safe");
        Console.WriteLine($"UI TEXT PASS assertions={count}; queued SDL preedit/commit, no real OS IME/candidate-window acceptance");return count;
    }
}
