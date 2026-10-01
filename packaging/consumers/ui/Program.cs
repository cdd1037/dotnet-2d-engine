using GameAuthoringLab;
var assets=new AssetRoot(Path.Combine(AppContext.BaseDirectory,"assets"));
using var engine=EngineHost.Create(maxSprites:32);
using var ui=new BoundUiSession<Bag>(engine,new UiBindings<Bag>().Text("caption",m=>m.Title).List("members",m=>m.Items,7).Action("add",8));
ui.LoadAsset(assets,"ui/roster.rml");
void Draw()=>engine.Draw(new Camera{Zoom=1},ReadOnlySpan<Sprite>.Empty);
Draw();var bag=new Bag("Package consumer / 独立使用",[new(700,"Scout / 侦察员"),new(800,"Medic / 医疗员",Selected:true)]);
if(!ui.Apply(bag)||ui.Apply(bag))throw new Exception("Dirty batch contract failed.");Draw();
var status=ui.Status;if(!status.Loaded||status.Revision!=1||status.Overflow!=0)throw new Exception("UI did not publish.");
var old=new UiBindingAction(status.Generation,status.Revision,1,7,UiBindingKind.List,700,"Scout",0,false);
if(!ui.IsCurrent(old))throw new Exception("Initial row identity is invalid.");
bag.Items.RemoveAt(0);bag.Items.Add(new(900,"New entry / 新成员"));ui.Apply(bag);Draw();
if(ui.IsCurrent(old)||ui.Revision!=2)throw new Exception("Removed row retained its revision.");
ui.Capture(Path.Combine(Environment.CurrentDirectory,"package-ui.bmp"));Draw();
ui.LoadAsset(assets,"ui/roster.rml");Draw();
if(ui.Status.Revision!=0||ui.Revision!=0||ui.Poll().Revision!=0)throw new Exception("Reload status has an obsolete revision.");
ui.Apply(bag);Draw();ui.Dispose();
using(var replacement=new BoundUiSession<Bag>(engine,new UiBindings<Bag>().Text("caption",m=>m.Title).List("members",m=>m.Items,7).Action("add",8))){
 replacement.LoadAsset(assets,"ui/roster.rml");Draw();replacement.Apply(bag);Draw();ui.Dispose();if(!replacement.Status.Loaded)throw new Exception("Obsolete owner closed replacement.");
}
Console.WriteLine("PACKAGE UI PASS schema=typed rows=2 replacement=safe");
sealed record Bag(string Title,List<UiListRow> Items);
