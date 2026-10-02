using System.Numerics;
using GameAuthoringLab;

int assertions=0;
void Check(bool ok,string label){if(!ok)throw new Exception(label);assertions++;}
void Reject<T>(Action action,string label) where T:Exception
{try{action();}catch(T){assertions++;return;}throw new Exception("Accepted: "+label);}
var assets=new AssetRoot(Path.Combine(AppContext.BaseDirectory,"assets"));
var loaded=TileMapAsset.LoadAsset(assets,"level.tilemap.json");
Check(loaded.Map.Width==4&&loaded.Map.Layers.Length==1,"public source-generated tile loader");
string written=TileMapAsset.Write(loaded.Source,assets.FilePath("level.tilemap.json"),assets);
Check(TileMapAsset.Load(written,assets.FilePath("level.tilemap.json"),assets).Map.Height==4,"public tile authoring round trip");
loaded.Source.Layers[0].Cells[12]=0;
Check(loaded.Map.Layers[0].Cell(12)==1,"runtime grid is independent of mutable authoring DTO");
using var engine=EngineHost.Create(headless:true,maxSprites:32);
using var physics=engine.OpenPhysics();
using var map=new TileMapInstance(engine,loaded,new(0,0));
var scale=new PhysicsScale(32);var collision=map.AttachCollision(physics,scale);
Check(collision.Rectangles.Length==1,"tile collision uses existing bounded merged rectangles");
using var bodies=new PhysicsScope(physics);
var body=bodies.CreateBody(new(PhysicsBodyType.Dynamic,1.5f,.5f,FixedRotation:true));
body.AddShape(PhysicsShapeDefinition.Circle(.3f,category:2));
var goal=bodies.CreateBody(new(PhysicsBodyType.Static,1.5f,2.5f));
goal.AddShape(PhysicsShapeDefinition.Box(.5f,.5f,category:4,mask:2,sensor:true));
var initial=body.State;bool entered=false;PhysicsEventView copiedEvent=default;
for(int i=0;i<120;i++)
{
    Check(physics.Step().Dropped==0,"bounded real solver events");
    foreach(var e in physics.Events)if(e.Type==PhysicsEventType.SensorBegin){entered=true;copiedEvent=e;}
}
Check(entered&&copiedEvent.Type==PhysicsEventType.SensorBegin,"public typed sensor events");
Check(initial.Y==.5f&&Math.Abs(body.State.Y-2.7f)<.03f&&Math.Abs(body.State.Vy)<.05f,"copied pose survives solver movement to tile floor");
for(int i=0;i<128;i++){physics.Step();_ =body.State;}
long allocationStart=GC.GetAllocatedBytesForCurrentThread();
for(int i=0;i<1000;i++){physics.Step();_ =body.State;}
Check(GC.GetAllocatedBytesForCurrentThread()==allocationStart,"warmed public physics views and copied event buffer allocate zero bytes");
var hit=physics.RayCast(2.5f,0,0,4,mask:1);
Check(hit.Hit&&collision.TryGetRectangle(hit.Shape,out var rectangle)&&rectangle.Y==3,"public ray resolves source grid rectangle");
Span<ulong> query=stackalloc ulong[8];Check(physics.QueryAabb(0,2.9f,4,4,query,mask:1)==1,"bounded public broad-phase query");

using var timing=new TimingScope();
var clip=new FrameClip(["red","blue"],.125);
var animation=timing.Own(new FramePlayer(clip));
var tween=timing.Own(Tween.Vector(Vector2.Zero,new(32,16),.25));
var timer=timing.Own(new EngineTimer(.125,domain:ClockDomain.RealTime));
var paused=TimingStep.FromReal(.125,paused:true);
animation.Advance(paused);tween.Advance(paused);timer.Advance(paused);
Check(animation.AssetKey=="red"&&tween.Value==Vector2.Zero&&timer.TicksDue==1,"explicit game/real-time clocks");
var tick=TimingStep.FromReal(.125);animation.Advance(tick);tween.Advance(tick);
Check(animation.AssetKey=="blue"&&tween.Value==new Vector2(16,8),"public frame clip and typed tween advance");
using var textures=new TextureBank(engine,loaded.Catalog);
var world=new World();var actor=world.Create("Animated body");
actor.LocalTransform=new(scale.ToPixels(body.State.X),scale.ToPixels(body.State.Y));
actor.Sprite=new(8,8,AssetKey:animation.AssetKey,Layer:2);
textures.Sync(world);var batch=new SpriteBatch(8){RegionResolver=textures.ResolveRegion};world.ExtractSprites(batch);
map.AppendSprites(batch,new(0,0,128,128));Check(batch.Count==5,"public animation resource ID plus map extraction");
for(int i=0;i<3;i++)engine.Draw(new Camera{Zoom=1},batch);
Check(engine.GetStats().Frames==3,"headless draw submission through packaged native library");

using var audio=engine.OpenAudio(offline:true);
using var sounds=new AudioScope(audio);
var pcm=sounds.LoadClip(assets,"pcm.wav");var voice=sounds.CreateVoice(pcm);
float[] samples=new float[512];
// Keep this tiny oracle local: copied package consumers do not reference test sources.
bool SamplesMatch(float left,float right,float tolerance=1e-5f)
{
    for(int i=0;i<samples.Length;i++)
        if(!float.IsFinite(samples[i])||!(Math.Abs(samples[i]-(i%2==0?left:right))<=tolerance))return false;
    return true;
}
void Mix(){Array.Fill(samples,float.NaN);audio.Mix(samples);}
voice.Play(-1);Mix();
Check(SamplesMatch(.25f,-.25f),"packaged offline mixer produces actual stereo PCM");
var snapshot=voice.State;voice.Pause();Mix();
Check(voice.State.Paused&&voice.State.Position==snapshot.Position&&SamplesMatch(0,0,0)&&snapshot.Playing,"voice pause and copied immutable state");
voice.Resume();voice.SetGain(.5f);audio.SetGain(AudioGroup.Sfx,.5f);audio.SetGain(AudioGroup.Master,.5f);Mix();
Check(SamplesMatch(.03125f,-.03125f),"voice and group gains");
voice.Stop();var stream=sounds.OpenStream(assets,"pcm.wav");stream.Play(-1);Mix();
Check(stream.State.Streaming&&stream.State.Position>0,"public reusable file stream");stream.Stop();

Reject<ArgumentNullException>(()=>new AudioScope(null!),"null audio owner");
Reject<ArgumentNullException>(()=>audio.LoadClip(null!,"pcm.wav"),"null audio root");
Reject<ArgumentNullException>(()=>audio.CreateVoice(null!),"null clip");
Reject<ArgumentNullException>(()=>new PhysicsScope(null!),"null physics owner");
Reject<ArgumentNullException>(()=>new FramePlayer(null!),"null frame clip");
Reject<ArgumentNullException>(()=>timing.Own<FramePlayer>(null!),"null timing operation");
Reject<ArgumentNullException>(()=>TileMapAsset.LoadAsset(null!,"level.tilemap.json"),"null tile root");
Reject<ArgumentNullException>(()=>new TileMapInstance(engine,null!,new(0,0)),"null loaded map");
Reject<TileMapException>(()=>TileMapAsset.LoadAsset(assets,"missing.tilemap.json"),"missing tilemap");
Reject<AssetException>(()=>audio.LoadClip(assets,"missing.wav"),"missing audio");

sounds.Dispose();Check(audio.State.Clips==0&&audio.State.Voices==0,"scope releases voices before clips");
Reject<ObjectDisposedException>(()=>voice.Play(),"disposed voice");
timing.Dispose();Reject<ObjectDisposedException>(()=>animation.Advance(tick),"disposed frame player");
Check(animation.IsDisposed&&tween.IsDisposed&&timer.IsDisposed,"timing scope owns all playbacks");
bodies.Dispose();map.Dispose();textures.Dispose();physics.Step();
Check(physics.State is {Bodies:0,Shapes:0,RetiredShapes:0}&&engine.Textures.Count==0,"public owners release complete level");
Reject<ObjectDisposedException>(()=>_ =body.State,"disposed body");
physics.Dispose();
Check(copiedEvent.Type==PhysicsEventType.SensorBegin&&initial.Y==.5f,"copied events and poses survive world disposal");
Reject<ObjectDisposedException>(()=>new PhysicsScope(physics),"closed physics owner");
audio.Dispose();Reject<ObjectDisposedException>(()=>new AudioScope(audio),"closed audio owner");
Console.WriteLine($"PACKAGE MODULES PASS assertions={assertions} frames=3 PCM=verified bodies=0 textures=0");
