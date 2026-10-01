namespace GameAuthoringLab;
internal static class SpriteSortTests
{
    public static int Run()
    {
        int assertions=0;void Check(bool ok,string label){if(!ok)throw new InvalidOperationException("SORT: "+label);assertions++;}
        var random=new Random(702);var world=new World();var batch=new SpriteBatch();var layers=new List<int>();
        for(int i=0;i<1001;i++){var entity=world.Create("sprite",transform:new Transform2D(i,0));int layer=i switch{0=>int.MaxValue,1=>int.MinValue,_=>random.Next(-10,11)};layers.Add(layer);entity.Sprite=new(1,1,Layer:layer);}
        void Verify()
        {
            var expected=Enumerable.Range(0,layers.Count).OrderBy(i=>layers[i]).ToArray();world.ExtractSprites(batch);
            for(int i=0;i<expected.Length;i++)Check(batch.Draws[i].X==expected[i]&&batch.Sprites[i].X==expected[i],"stable layer and matching legacy/affine order");
        }
        Verify();for(int i=0;i<layers.Count;i++){layers[i]=-i;world.Entities[i].Sprite=new(1,1,Layer:-i);}Verify();
        for(int i=0;i<layers.Count;i++){layers[i]=7;world.Entities[i].Sprite=new(1,1,Layer:7);}Verify();
        for(int i=0;i<layers.Count;i++){layers[i]=random.Next(-4,5);world.Entities[i].Sprite=new(1,1,Layer:layers[i]);}Verify();
        for(int i=0;i<32;i++)world.ExtractSprites(batch);
        long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<100;i++)world.ExtractSprites(batch);long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
        Check(bytes==0,"mixed-layer warmed sort allocates zero bytes");
        for(int i=0;i<2000;i++){var entity=world.Create("grown",transform:new Transform2D(layers.Count,0));layers.Add(-100);entity.Sprite=new(1,1,Layer:-100);}Verify();
        // Empty/single after growth, then odd counts; no stale scratch entries enter output.
        foreach(var entity in world.Entities.ToArray())world.Destroy(entity);world.ExtractSprites(batch);Check(batch.Count==0,"empty after growth");
        var one=world.Create("one");one.Sprite=new(1,1);world.ExtractSprites(batch);Check(batch.Count==1,"single after growth");
        Console.WriteLine($"PASS stable merge sorting ({assertions} assertions; mixed-layer allocations={bytes})");return assertions;
    }
}
