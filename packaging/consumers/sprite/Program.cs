using GameAuthoringLab;
var assets=new AssetRoot(Path.Combine(AppContext.BaseDirectory,"assets"));
var scene=AuthoredScene.LoadAsset(assets,"sample.scene.json");
using var engine=EngineHost.Create(maxSprites:32);
using var textures=new TextureBank(engine,scene.Catalog);
textures.Sync(scene.World);
var batch=new SpriteBatch(2){RegionResolver=textures.ResolveRegion};
scene.World.ExtractSprites(batch);
if(batch.Count!=1||textures.LoadedCount!=1)throw new Exception("Authored scene/texture resource did not load.");
for(int i=0;i<3;i++){engine.PollInputFrame();engine.Draw(new Camera{Zoom=1},batch);}
var stats=engine.GetStats();if(stats.Frames!=3||stats.DrawCalls==0)throw new Exception("No native frame submission.");
try{assets.Resolve("missing.bmp");throw new Exception("Missing resource accepted.");}catch(AssetException e)when(e.Code=="ASSET_MISSING"){}
Console.WriteLine($"PACKAGE SPRITE PASS frames={stats.Frames} draws={stats.DrawCalls} sprites={batch.Count}");
