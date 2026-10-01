using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
namespace GameAuthoringLab;
internal static unsafe class MaterialTests
{
    public static int Run()
    {
        int n=0;void Check(bool ok,string label){if(!ok)throw new Exception("MATERIAL: "+label);n++;}
        void Reject<T>(Action action,string label)where T:Exception{try{action();}catch(T){n++;return;}throw new Exception("MATERIAL accepted: "+label);}
        Check(sizeof(MaterialDescriptor)==16&&sizeof(MaterialParameters)==32&&sizeof(MaterialDraw)==136,"material ABI sizes");
        Check(Marshal.OffsetOf<MaterialDraw>(nameof(MaterialDraw.Sprite))==8&&Marshal.OffsetOf<MaterialDraw>(nameof(MaterialDraw.Material))==96&&Marshal.OffsetOf<MaterialDraw>(nameof(MaterialDraw.Parameters))==104,"material ABI offsets");
        foreach(float bad in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity})
            Reject<ArgumentOutOfRangeException>(()=>new MaterialParameters(new(bad,0,0,0)),"nonfinite parameters");
        var signed=new MaterialParameters(new(-1,2,3,4),new(5,6,7,8));Check(signed.First.X==-1&&signed.Second.W==8,"finite parameters are shader-defined, not colors");
        var assets=new AssetRoot();var source=MaterialAsset.Load(assets,"materials/tint.material.json");
        Check(source.Info.Profile==MaterialAsset.Profile&&source.Code.Length>20,"source-generated authored material manifest");
        string folder=Path.Combine(Path.GetTempPath(),"gal-material-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            var root=new AssetRoot(folder);File.WriteAllBytes(Path.Combine(folder,"test.spv"),source.Code);
            string manifest="{\"version\":1,\"profile\":\"sprite-fragment-v1\",\"fragment\":\"test.spv\",\"sha256\":\""+source.Info.Sha256+"\",\"parameterBytes\":32}";
            void Write(string json)=>File.WriteAllText(Path.Combine(folder,"test.material.json"),json);
            foreach(string bad in new[]{manifest.Replace("\"version\":1","\"version\":2"),manifest.Replace("sprite-fragment-v1","unknown"),manifest.Replace("\"parameterBytes\":32","\"parameterBytes\":16"),manifest.Replace("test.spv","../test.spv"),manifest.Replace(source.Info.Sha256,"bad"),manifest.Replace("\"version\":1","\"version\":1,\"version\":1"),manifest.Replace("\"version\":1","\"version\":1,\"extra\":0"),manifest.Replace("\"test.spv\"","null")})
            {Write(bad);Reject<AssetException>(()=>MaterialAsset.Load(root,"test.material.json"),"invalid authored manifest");}
            Write(new string(' ',16385));Reject<AssetException>(()=>MaterialAsset.Load(root,"test.material.json"),"bounded manifest bytes");
            Write(manifest);File.WriteAllBytes(Path.Combine(folder,"test.spv"),new byte[MaterialAsset.MaximumShaderBytes+1]);
            Reject<AssetException>(()=>MaterialAsset.Load(root,"test.material.json"),"bounded shader bytes");File.WriteAllBytes(Path.Combine(folder,"test.spv"),source.Code);

            using var engine=EngineHost.Create(true,8);var cache=engine.Materials;using var first=cache.Acquire(root,"test.material.json");ulong handle=first.Handle;
            using var second=cache.Acquire(root,"test.material.json");Check(first.Handle==second.Handle&&cache.Count==1&&cache.Loads==1,"same manifest shares pipeline lifetime");
            Check(first.Info.FragmentPath=="test.spv"&&first.Info.Sha256==source.Info.Sha256,"lease exposes logical fragment metadata");
            Check(Task.Run(()=>{try{_ = first.Handle;return false;}catch(InvalidOperationException){return true;}}).GetAwaiter().GetResult(),"material owner thread");
            var camera=new Camera{Zoom=1};var sprite=SpriteDrawV2.Create(new(){M11=1,M22=1,Width=32,Height=32,R=1,G=1,B=1,A=1});
            MaterialDraw[] draws=[MaterialDraw.Create(sprite,handle,new(Vector4.One)),MaterialDraw.Create(sprite)];
            engine.Draw(camera,draws);Check(engine.GetStats().Sprites==2,"mixed custom/default headless frame");
            var badDraw=draws[0];badDraw.Version=9;Reject<InvalidOperationException>(()=>engine.Draw(camera,new[]{draws[0],badDraw}),"late invalid draw header");
            Reject<InvalidOperationException>(()=>engine.Draw(camera,new[]{MaterialDraw.Create(sprite,0,new(Vector4.One))}),"default material rejects nonzero parameters");
            Reject<ArgumentException>(()=>engine.Draw(camera,draws,new[]{FramebufferClip.Disabled,FramebufferClip.Disabled,FramebufferClip.Disabled}),"bad clip cardinality");
            Reject<ArgumentException>(()=>engine.Draw(camera,draws,new[]{default(FramebufferClip)}),"bad clip header");
            engine.Draw(camera,draws);Check(engine.GetStats().Frames==2,"recovery after failed material submission");
            var cachedCount=cache.Count;Write(manifest.Replace(source.Info.Sha256,new string('0',64)));using(var retained=cache.Acquire(root,"test.material.json"))Check(retained.Handle==handle,"live cache snapshot ignores on-disk edits");
            File.WriteAllText(Path.Combine(folder,"bad.material.json"),manifest.Replace(source.Info.Sha256,new string('0',64)));
            Reject<AssetException>(()=>cache.Acquire(root,"bad.material.json"),"hash mismatch load");Check(cache.Count==cachedCount&&first.Handle==handle,"failed load retains prior pipeline");
            byte[] broken=(byte[])source.Code.Clone();broken[0]=0;File.WriteAllBytes(Path.Combine(folder,"broken.spv"),broken);
            File.WriteAllText(Path.Combine(folder,"broken.material.json"),manifest.Replace("test.spv","broken.spv").Replace(source.Info.Sha256,Convert.ToHexStringLower(SHA256.HashData(broken))));
            Reject<AssetException>(()=>cache.Acquire(root,"broken.material.json"),"matching hash does not replace native shader header validation");Check(cache.Count==1,"failed native material creation publishes no cache entry");
            second.Dispose();Check(cache.Count==1&&cache.Releases==0,"retained lease keeps material alive");first.Dispose();first.Dispose();Check(cache.Count==0&&cache.Releases==1,"last lease releases once");
            Reject<ObjectDisposedException>(()=>_ = first.Handle,"disposed lease access");
            Reject<InvalidOperationException>(()=>engine.Draw(camera,draws),"stale material in queued managed record");
            Reject<AssetException>(()=>cache.Acquire(root,"test.material.json"),"last release makes subsequent load observe changed source");
            Write(manifest);using var reloaded=cache.Acquire(root,"test.material.json");Check(reloaded.Handle!=handle&&cache.Loads==2,"reload issues new handle");
            draws[0]=MaterialDraw.Create(sprite,reloaded.Handle,new(Vector4.One));
            for(int i=0;i<128;i++)engine.Draw(camera,draws);long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1000;i++)engine.Draw(camera,draws);
            Check(GC.GetAllocatedBytesForCurrentThread()==before,"warmed material submissions allocate no managed bytes");
            engine.Dispose();Reject<ObjectDisposedException>(()=>_ = reloaded.Handle,"engine-first disposal invalidates material lease");reloaded.Dispose();Check(cache.Releases==2,"engine owns remaining pipeline cleanup");
            using var next=EngineHost.Create(true,8);Reject<InvalidOperationException>(()=>next.Draw(camera,draws),"handle from previous context rejected");
        }
        finally{Directory.Delete(folder,true);}
        Console.WriteLine($"MATERIAL CONTRACT PASS assertions={n}");return n;
    }
}
